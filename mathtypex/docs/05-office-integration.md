# 05 — Tích hợp Word · Tích hợp PowerPoint

## 9. Cách tích hợp Word

### 9.1 Vòng đời add-in

- **VSTO add-in**, file manifest ký số, đăng ký per-user tại `HKCU\Software\Microsoft\Office\Word\Addins\MathTypeX.Word` với `LoadBehavior=3`.
- `ThisAddIn_Startup` chỉ làm bốn việc: đăng ký Ribbon (Ribbon XML), cài hook bàn phím, chụp `SynchronizationContext`, và khởi động `MathTypeX.Editor.exe` ở nền với độ ưu tiên thấp. Mục tiêu: **dưới 50 ms**.
- **Ribbon:** tab "MathTypeX" gồm Insert Equation, Convert Selection, Scan Document, Font & Typography, Settings. Context menu gồm *Edit as LaTeX*, *Copy as LaTeX/MathML/Word Equation/SVG*, *Paste as Math*.
  - Vị trí trong menu chuột phải: `ContextMenuText`. Riêng menu chuột phải của equation dùng `idMso` nào thì ⚠ cần xác minh.

### 9.2 Bảng thao tác và API

| Thao tác | API / cách làm | Ghi chú |
|---|---|---|
| Chụp context khi nhấn `Alt+M` | `Selection.Range`, `Selection.Font.Size/Name`, `Selection.Information(wdWithInTable)`, `Selection.StoryType`, `Document.CompatibilityMode` | Chỉ đọc |
| Toạ độ con trỏ | `ActiveWindow.GetPoint(out l, out t, out w, out h, Selection.Range)` | Đơn vị pixel màn hình; editor tự quy đổi theo DPI của từng màn hình |
| Chèn equation native | `range.InsertXML(flatOpc)`, Flat OPC tối giản (chỉ `document.xml`, **không có `styles.xml`** để không làm bẩn style) | ⚠ S1: đoạn văn cuối có được gộp vào đoạn hiện tại không, có giữ định dạng ký tự xung quanh không |
| Chèn (đường dự phòng) | `doc.OMaths.Add(range)` → `range.Text = unicodeMath` → `OMath.BuildUp()` | Chỉ dùng khi InsertXML thất bại |
| Tìm equation tại con trỏ | `Selection.OMaths(1)`. Nếu con trỏ nằm sát mép equation thì thử `Range(start-1, end+1).OMaths` | ⚠ S1 |
| Đọc OMML | `oMath.Range.WordOpenXML`, rồi trích `m:oMath` / `m:oMathPara` | Có thể chậm (vài chục ms); chỉ gọi khi cần |
| Thay thế equation | `oMath.Range.InsertXML(newFlatOpc)` | Trong `UndoRecord` |
| Inline ↔ Display | Tự quản lý đoạn văn (§9.3) và `OMath.Type` | |
| Undo | `Application.UndoRecord.StartCustomRecord("MathTypeX: …")` / `EndCustomRecord()` trong `finally` | Mỗi thao tác chỉ tạo **một** mục Undo |
| Thiết lập toán của tài liệu | `Document.OMathFontName`, `OMathIntSubSupLim`, `OMathNarySupSubLim`, `OMathSmallFrac`… | Chỉ đổi khi người dùng chọn "Áp dụng cho tài liệu" |
| Ảnh vector | Tự dựng DrawingML trong Flat OPC: `a:blip` (PNG fallback) + phần mở rộng `asvg:svgBlip`, kích thước theo EMU chính xác, `wp:docPr` có `descr` (alt text). Đường dự phòng: `InlineShapes.AddPicture` | Kiểm soát được kích thước, alt text và baseline cùng một lúc |
| Baseline của ảnh inline | `w:position` của run, đơn vị nửa point, bằng −depth (lấy từ metrics TeX) | Qua `Font.Position` của COM thì chỉ chỉnh được theo point ⚠, nên ưu tiên đặt qua XML |
| Metadata | `Document.CustomXMLParts` (namespace riêng) | Xem [07](07-equation-metadata.md) |
| Field, bookmark (Phase 3) | `Fields.Add(range, wdFieldEmpty, "SEQ Equation \\* ARABIC \\s 1")`, `Bookmarks.Add` | |
| Batch | `Application.ScreenUpdating = false`, `Options.Pagination = false` trong lúc chạy, khôi phục trong `finally` | |
| Xem trước giống hệt Word (tuỳ chọn) | Chèn vào tài liệu ẩn rồi lấy `Range.EnhMetaFileBits` | Gọi theo yêu cầu, không chạy mỗi lần gõ phím |

### 9.3 Inline, display và đoạn văn

- **Inline:** `m:oMath` nằm trong đoạn văn hiện tại.
  - Cỡ chữ: lấy `Selection.Font.Size` tại con trỏ (chế độ *Follow surrounding text*), hoặc cố định, hoặc theo tỉ lệ (§49). Cỡ được ghi `w:sz` cho từng `m:r`.
- **Display:** Word chỉ coi một equation là display khi nó **đứng riêng trong một đoạn**.
  - Chèn display khi con trỏ đang ở giữa đoạn: tách thành ba phần. *Phần trước* giữ ở đoạn cũ; *equation* nằm trong đoạn mới, cùng style với đoạn cũ hoặc theo style tuỳ chọn "MTX Display"; *phần sau* (nếu có) sang đoạn mới.
  - Ví dụ ở §54, `Với $X\sim N(\mu,\sigma^2)$, ta có $$E(X)=\mu.$$` sẽ thành:
    - đoạn 1: `Với [inline], ta có`
    - đoạn 2: `[display: E(X)=μ.]`
  - Dấu câu nằm *bên trong* `$$…$$` được giữ trong equation, đúng như LaTeX.
- **Kiểm tra trước khi chèn:**

| Tình huống | Cách xử lý |
|---|---|
| `Document.CompatibilityMode < 12` (tài liệu kiểu Word 2003) | Word không cho chèn equation. App đề nghị *Convert document* (thao tác do người dùng chủ động) |
| Định dạng `.doc` | Khi lưu, Word **chuyển mọi equation thành ảnh**, nên app cảnh báo và đề nghị lưu `.docx` |
| `TrackRevisions` đang bật | Tôn trọng: phần chèn sẽ hiện thành revision. App không tự tắt tính năng này |
| Story không phải main text (footnote, header, text box) | Vẫn hỗ trợ. ⚠ S1 kiểm tra InsertXML trong từng loại story |

### 9.4 Convert LaTeX trong vùng chọn và trong tài liệu

1. **Đọc text** của story bằng `range.TextRetrievalMode.IncludeHiddenText = true` và `IncludeFieldCodes = true`, để vị trí ký tự khớp với vị trí Range.
   - Duyệt mọi story: `Document.StoryRanges` cộng `NextStoryRange` (main, footnote, endnote, header/footer, text frame); duyệt thêm text trong shape.
2. **Chạy scanner** (thư viện `MathTypeX.Scanner`, state machine, không dùng regex). Ứng viên có dạng `{start, end, delimiter, latex, confidence, reasons}`.
3. **Quy tắc chống convert nhầm (§31):**

| Quy tắc | Chi tiết |
|---|---|
| Mở/đóng `$` kiểu Pandoc | `$` mở phải có ký tự khác khoảng trắng ngay bên phải; `$` đóng phải có ký tự khác khoảng trắng ngay bên trái **và không đứng ngay trước một chữ số**. Nhờ vậy `$20,000 và $30,000` không bị coi là toán |
| Ký tự escape | `\$` không phải delimiter |
| Bỏ qua theo style | Bỏ qua đoạn có style hoặc character style là code ("Code", "HTML Code", "Source Code", danh sách cấu hình được) hoặc dùng font monospace (Consolas, Courier New…) |
| Bỏ qua theo vùng | Bỏ qua bên trong equation có sẵn, field code, hyperlink, và vùng người dùng đánh dấu *Ignore* (character style "MTX Ignore" hoặc ignore list theo nội dung) |
| Giảm độ tin cậy | Nội dung chỉ là số (`$5$`), đường dẫn (`C:\…`, `$HOME`), giống tiền tệ (`$`+số+đơn vị), hoặc parse ra lỗi |
| Tăng độ tin cậy | Có `\cmd`, `^`, `_`, `=`, chữ Hy Lạp… |
| Kết quả | Ứng viên có độ tin cậy dưới ngưỡng **mặc định không được tích** trong hộp *Scan Document* |

4. **Áp dụng:**
   - Sắp ứng viên đã chọn **từ cuối lên đầu**.
   - Trong `UndoRecord`, với mỗi ứng viên: kiểm tra `doc.Range(s,e).Text == expected` (nếu khác thì bỏ qua và ghi log), rồi gọi `InsertXML`.
   - Metadata ghi lại `SourceSpan` gốc (delimiter và text), để lệnh **Revert to LaTeX text** hoạt động được cả sau khi lưu và mở lại tài liệu.
5. **Tài liệu lớn:**
   - Thanh tiến trình và nút Huỷ (huỷ giữa chừng vẫn đóng `UndoRecord` sạch sẽ).
   - Với hàng nghìn công thức, tuỳ chọn chia thành nhiều UndoRecord, mỗi cái khoảng 200 công thức.
   - Đo hiệu năng trên tài liệu mẫu 300 trang có 1.500 công thức (test hiệu năng trong CI).

### 9.5 Copy / Paste

| Lệnh | Định dạng clipboard |
|---|---|
| Copy as LaTeX | `CF_UNICODETEXT`: source gốc hoặc bản chuẩn hoá (setting) |
| Copy as MathML | `"MathML"`, `"MathML Presentation"`, `"application/mathml+xml"`, kèm text |
| Copy as Word Equation | Hai phương án: (a) RTF có các control word toán (`\mmath`…, đặc tả RTF 1.9.1); (b) MathML. ⚠ S10 chọn phương án Word/PowerPoint nhận ổn định |
| Copy as SVG / PNG | `"image/svg+xml"` ⚠ S10, `CF_ENHMETAFILE`, PNG |
| Paste as Math | Đọc text, MathML hoặc OMML trên clipboard → AST → chèn |

### 9.6 Zoom, high DPI, dark mode

- **OMML:** Word tự lo zoom và DPI. Equation dùng màu "Automatic", nên tự đổi theo chế độ tối của trang (⚠ S11 xác nhận).
- **SVG/EMF:** vector nên không vỡ nét khi zoom.
  - **Không tự đổi màu trong dark mode của Word.** App cảnh báo; nếu đoạn văn dùng màu chữ tường minh thì render đúng màu đó.
- **Editor:** dùng Per-Monitor DPI v2; vị trí con trỏ được quy đổi theo DPI của màn hình chứa con trỏ.
- **Preview:** nền giống màu giấy, kể cả khi UI đang ở theme tối, để người dùng thấy đúng như sẽ in hoặc hiển thị trong tài liệu.

---

## 10. Cách tích hợp PowerPoint

### 10.1 Thực tế API

| Nhu cầu | PowerPoint COM | Kết luận |
|---|---|---|
| Chèn OMML | **Không có API** (Office.js cũng không có) | Đi đường vòng (§10.2), cần S5 |
| Đọc OMML của text | Không có | Không đọc được OMML |
| Metadata | `Shape.Tags.Add(name, value)`, lưu cùng file, đi theo shape khi copy ⚠ | ✅, giống cách IguanaTex (add-in LaTeX mã nguồn mở cho PowerPoint) đang dùng |
| Ảnh vector | `Shapes.AddPicture` (SVG từ M365/2019; EMF cho mọi bản) | ✅ |
| Toạ độ con trỏ | `TextRange.BoundLeft/BoundTop/BoundHeight` cộng `ActiveWindow.PointsToScreenPixelsX/Y` | ✅ |
| Tìm vùng toán trong text | `TextRange2.MathZones` ⚠ | S5 |
| Gộp undo | Không có `UndoRecord`; chỉ có `Application.StartNewUndoEntry()` để **bắt đầu** một mục mới | 🟡: một thao tác có thể sinh nhiều bước Undo |
| Phím tắt | Không có `KeyBindings`/`FindKey` | Bảng xung đột tĩnh ([06](06-keyboard-and-ime.md)) |

### 10.2 Chèn equation native vào PowerPoint (sẽ quyết sau S5)

| Phương án | Cách làm | Rủi ro |
|---|---|---|
| **P1 (ưu tiên thử)** | Dùng Open XML SDK sinh một `.pptx` tạm, có shape chứa equation (`mc:AlternateContent` / `a14:m` bọc `m:oMathPara`). Mở ẩn bằng `Presentations.Open(…, WithWindow:=msoFalse)`, gọi `shape.Copy()`, rồi `Slide.Shapes.Paste()` (hoặc `TextRange.Paste()` để chèn vào text đang soạn) | Phải dùng clipboard: chụp và khôi phục clipboard của người dùng, đánh dấu `ExcludeClipboardContentFromMonitorProcessing` để không lọt vào Clipboard History. Có thể mất các định dạng delayed-render |
| P2 | Đưa MathML lên clipboard rồi `TextRange.Paste` | ⚠ PowerPoint có chuyển MathML thành equation không: chưa rõ |
| P3 | `ExecuteMso("EquationInsertNew")`, sau đó đưa UnicodeMath vào | Phụ thuộc giao diện người dùng, mong manh. Không ưu tiên |

**Quyết định UX để giảm rủi ro:**

- Mặc định trong PowerPoint, **mỗi equation native nằm trong một text box riêng** (một shape là một equation). Nhờ vậy Tags gắn đúng 1-1, sửa lại rất đơn giản.
- Chèn *vào bên trong* một text box đang có chữ là tuỳ chọn nâng cao. Với tuỳ chọn này, việc định danh lại công thức để sửa phụ thuộc kết quả S5.

**Nếu S5 thất bại:** PowerPoint dùng **Exact (SVG) làm mặc định**. Native vẫn dùng được khi người dùng tự dán từ Word.

### 10.3 Equation vector trong PowerPoint

- `Shapes.AddPicture(svg)`, đặt `LockAspectRatio = msoTrue`. Ảnh là vector thật (glyph đã chuyển thành path), nên phóng to không vỡ nét. Kèm EMF fallback cho Office 2016.
- **"Gần như inline"** khi đang soạn trong một text box:
  - Ảnh được đặt tại vị trí con trỏ, tính bằng `BoundLeft/BoundTop`.
  - Đáy ảnh canh theo baseline: `BoundTop + ascent` của font trong đoạn văn, trừ đi depth.
  - Text không tự chừa chỗ cho ảnh. App có thể chèn khoảng trắng có độ rộng tương ứng (tuỳ chọn), và nói rõ giới hạn này cho người dùng.
- **Display:** canh giữa theo chiều ngang của text box hoặc slide, đặt dưới đoạn văn hiện tại.
- **Metadata:** `Tags("MTX_ID")`, `Tags("MTX_DATA")` (JSON, nén gzip và mã hoá base64 nếu dài). **Alt text** là LaTeX hoặc câu đọc sinh từ AST, cho accessibility.
- **Sửa lại:** chọn shape rồi nhấn `Alt+M`, add-in đọc Tags và mở editor. Khi thay thế, giữ nguyên vị trí, thứ tự lớp (z-order), hiệu ứng animation (⚠ cần kiểm tra cách giữ animation khi đổi ảnh), và tỉ lệ theo chiều cao chữ.

### 10.4 Cỡ chữ trên slide

- Chế độ "Theo chữ": lấy `TextRange.Font.Size` tại con trỏ.
- SVG được render theo cỡ đó: 1 pt trên slide bằng 1 pt của TeX sau quy đổi bp. Nhờ vậy equation và text có cỡ khớp nhau, kể cả khi trình chiếu hay xuất PDF.
