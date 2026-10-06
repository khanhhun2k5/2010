# 01 — Feasibility · Word native · Phần bắt buộc TeX/vector

Ký hiệu được dùng thống nhất trong mọi tài liệu:

- ✅ làm được bằng API hoặc định dạng **đã có tài liệu chính thức**;
- 🟡 làm được nhưng cần workaround, hoặc cần **spike** để xác minh (S1…S11, xem [09-roadmap](09-roadmap.md#spikes));
- 🔴 không làm được theo cách được mô tả, kèm phương án thay thế.

Nguyên tắc: hành vi nào của Office **không có trong tài liệu chính thức** đều được gắn 🟡/⚠ và quy về một spike, kể cả khi tôi khá tự tin về nó.

---

## 1. Phân tích feasibility từng yêu cầu

| § | Yêu cầu | Đánh giá | Ghi chú kỹ thuật / hạn chế | Phase |
|---|---|---|---|---|
| 1 | `Alt+M` mở editor ngay tại con trỏ | 🟡 | Lấy vị trí con trỏ: Word dùng `Window.GetPoint(…, Range)`; PowerPoint dùng `TextRange.BoundLeft/BoundTop` kết hợp `DocumentWindow.PointsToScreenPixelsX/Y` ✅. Bắt phím bằng hook bàn phím cục bộ theo thread của Office (S3). `Alt+M` **trùng KeyTip tab Mailings** của Word (UI tiếng Anh), xem [06](06-keyboard-and-ime.md). | 1 |
| 1, 20 | `Enter` để chèn; mở lại để sửa | ✅ / 🟡 | Chèn OMML bằng `Range.InsertXML` ✅. Định danh lại công thức sau khi lưu hoặc copy: S1. | 1 |
| 1, 13 | Ba trạng thái VI / EN / MATH | 🟡 | Trạng thái thuộc về **editor**, không đổi layout bàn phím của HĐH. Khó nhất là các bộ gõ dạng hook (UniKey, EVKey, OpenKey): S6. | 1–2 |
| 2A | Quét font OpenType MATH đã cài | ✅ | Tự đọc table directory, tìm thẻ `MATH`. Phải hỗ trợ `.ttc`, vì **Cambria Math nằm trong `cambria.ttc`**. | 1 |
| 2B | `mathptmx`, `newtx`, `mathdesign`, `fourier`, `kpfonts`… | 🔴 native · ✅ Exact | Đây là font Type1 dùng TFM/VF 8-bit, không có bảng MATH, nên Word không dùng được. Chỉ chạy được qua backend TeX (pdfLaTeX). Ở chế độ Native chỉ có thể đưa ra "font tương đương gần nhất" (TeX Gyre Termes Math, STIX Two Math…), và **phải cảnh báo** người dùng. | 2 |
| 2 | Hai backend cộng chế độ `Auto` | ✅ | `Auto` dựa trên *Native Capability Matrix* đo bằng thực nghiệm (S2). | 1 (Native), 2 (Exact) |
| 3 | Chọn font riêng cho từng lớp ký hiệu | 🟡 native · ✅ Exact | OMML cho phép đặt `w:rFonts` cho từng run. Tuy nhiên Word có **tôn trọng nhiều font khác nhau trong cùng một equation** hay không, nhất là với ký tự n-ary và delimiter co giãn, thì **không có tài liệu** (S2). Ở Exact, `unicode-math` với `\setmathfont{…}[range=…]` làm đúng việc này. | 2 |
| 4 | Integral và large operator đúng typography | ✅ / 🟡 | Native: `m:nary` + display/inline + `m:grow`; chất lượng phụ thuộc MathVariants của font. Chỉnh *scale*, *khoảng cách sub/sup*, *spacing*: 🔴 với native, phải sang Exact (dùng các tham số `\Umath…` của LuaTeX). Xem lưu ý "TeX không kéo dãn ∫" ở [04 §8](04-fonts-and-integrals.md#8-chiến-lược-dấu-tích-phân-đúng-kiểu-latex). | 1–2 |
| 5 | `$…$`, `\(…\)`, `$$…$$`, `\[…\]` và các environment gõ trực tiếp trong Word | ✅ | Scanner viết riêng (không dùng regex) kết hợp thao tác Range. `$$…$$` nằm giữa câu thì **phải tách đoạn**, vì display equation của Word phải đứng riêng một đoạn. | 1 |
| 6 | Parser thật, có error recovery | ✅ | Tự viết, không phụ thuộc Office ([03](03-math-ast-and-parser.md)). | 1 |
| 7 | Autocomplete fuzzy | ✅ | Completion của AvalonEdit cộng bộ chấm điểm fuzzy; dưới 5 ms với khoảng 3.000 mục. | 1 |
| 8 | Command palette tìm bằng tiếng Việt/Anh | ✅ | Có gập dấu tiếng Việt: gõ "tich phan" vẫn khớp "tích phân". | 1 |
| 9 | Thư viện công thức trực quan | ✅ | Catalog JSON, preview bằng MathML. | 1–2 |
| 10 | Placeholder và điều hướng bằng Tab | ✅ | Snippet theo cú pháp TextMate (`${1:tử số}`); AvalonEdit đã có sẵn hạ tầng snippet. | 1 |
| 11 | Keyboard-first, tự cấu hình phím, phát hiện xung đột | 🟡 | Word có `Application.FindKey(…).Command` để biết lệnh đang gán cho một phím ✅. KeyTips của ribbon và phím tắt PowerPoint thì không có API, phải dùng bảng tĩnh theo phiên bản/ngôn ngữ, cộng thử nghiệm qua UI Automation. | 1 |
| 12 | Inline/Display, giữ đúng baseline | ✅ native · 🟡 vector | OMML: Word tự canh baseline. SVG/EMF: hạ ảnh xuống bằng `w:position` (đơn vị nửa point) theo *depth* đo được từ TeX. | 1 / 2 |
| 14 | Smart replacements | ✅ | Thay thế có "trì hoãn" để `<=>` không bị cắt thành `\le>`. | 2 |
| 15 | Snippet engine | ✅ | Import/export JSON. "Sync" nghĩa là dùng một thư mục đồng bộ do người dùng chọn (OneDrive…); không có server riêng. | 2 |
| 16 | Live preview không làm đơ UI | ✅ | MathML Core trong WebView2 (vài ms). TeX chạy nền và có cache. | 1 / 2 |
| 17 | Font preview, chế độ Compare | ✅ | Cùng pipeline MathML, đặt `font-family` theo từng font. | 1–2 |
| 18 | Typography presets | ✅ | Lưu dạng JSON. | 1–2 |
| 19 | Font fallback, không hiện ô vuông | ✅ | Biết font nào có glyph nào nhờ bảng `cmap`. Ở Native, gán `w:rFonts` **tường minh** cho run cần fallback, không để Word tự chọn font. | 2 |
| 20 | Sửa lại công thức nhờ metadata | ✅ / 🟡 | CustomXMLPart cộng khoá hash đã chuẩn hoá; chuyển ngược OMML→LaTeX làm lưới an toàn ([07](07-equation-metadata.md)). | 1 |
| 21 | Copy dạng LaTeX / UnicodeMath / MathML / OMML / SVG / PNG / text | ✅ / 🟡 | Text, MathML, SVG, PNG ✅. Đặt "Word Equation" lên clipboard (RTF có math, hoặc MathML) cần spike S10. | 2 |
| 22 | Paste as Math | ✅ | | 2 |
| 23 | Đánh số công thức, `\label` / `\ref` | 🟡 | Field SEQ / STYLEREF / REF và bookmark ✅. Nhưng display equation phải đứng riêng một đoạn, nên muốn đánh số phải dùng bảng 3 cột hoặc cơ chế `#` của Word 365 (S9). | 3 |
| 24 | `align` / `aligned` / `gather` / `cases` / `split` | ✅ / 🟡 | Dùng `m:eqArr`. Cách Word biểu diễn điểm canh `&` phải lấy từ golden file sinh bởi Word thật (S1). | 1 (bản không đánh số) |
| 25 | Matrix GUI | ✅ | | 2 |
| 26 | Beginner mode | ✅ | Thông báo lỗi theo **vai trò của đối số**, ví dụ "mẫu số". | 1 |
| 27 | Recently / Frequently / Favorites | ✅ | Thống kê lưu cục bộ. | 2–3 |
| 28 | PowerPoint | 🟡 / 🔴 | **Không có API COM hay Office.js nào để chèn OMML vào PowerPoint**, nên phải đi qua presentation tạm kết hợp clipboard (S5). SVG làm được ✅ qua `Shapes.AddPicture` (M365/2019+). Ảnh không thể nằm *bên trong* một dòng text, nên chỉ có thể "gần như inline". | 2 |
| 29 | Word với tài liệu hàng trăm trang | 🟡 | Đọc text một lần, kiểm tra lại từng ứng viên, xử lý từ cuối lên đầu, gói trong một UndoRecord, tắt ScreenUpdating trong lúc chạy. | 1 |
| 30 | Batch convert có transaction | ✅ | `Application.UndoRecord` (Word 2010+), cộng metadata cho phép "Revert về LaTeX". | 1 (cơ bản) / 3 |
| 31 | Không convert nhầm (`$100`…) | 🟡 | Quy tắc kiểu Pandoc, chấm điểm độ tin cậy, bỏ qua các đoạn có style code. Không thể đúng 100%, nên mục có độ tin cậy thấp **mặc định không được tích**. | 1 |
| 32 | `\newcommand`, Macros Manager | ✅ | Bộ mở rộng macro của chính app, không gọi TeX; có giới hạn độ sâu và số token. | 1 (cơ bản) / 3 |
| 33 | Bảo mật khi chạy TeX | ✅ | Nhiều lớp: TeX chỉ nhận LaTeX do serializer sinh ra từ AST đã qua whitelist; tiến trình chạy trong sandbox. | 2 |
| 34 | VSTO/COM chạy trên .NET 8 | 🔴 nếu làm nguyên văn | **VSTO chỉ chạy trên .NET Framework 4.x.** Nạp .NET 8 in-proc qua COM hosting không được Microsoft hỗ trợ cho Office, và có xung đột runtime giữa các add-in. Thêm nữa, .NET 8 hết hỗ trợ ngày 10/11/2026. Đề xuất dùng .NET 10 LTS cho các tiến trình riêng ([02](02-stack-and-architecture.md)). | 1 |
| 35–37 | AST trung tâm; sinh OMML theo AST; hỗ trợ UnicodeMath | ✅ | | 1 / 3 |
| 38 | Hiệu năng | ✅ / 🟡 | Native và preview đạt mục tiêu. Exact (LuaLaTeX) mất 0,3–1,5 giây cho một lần chạy lạnh, nên phải chạy bất đồng bộ, có cache và dùng format dựng sẵn. | 1 / 2 |
| 39 | Font cache | ✅ | | 1 |
| 40–41 | UI tối giản; settings có tìm kiếm | ✅ | | 1 |
| 42 | Dark mode | ✅ UI · 🟡 trong tài liệu | OMML dùng màu "Automatic" nên đổi theo dark mode của Word. **Ảnh SVG/EMF thì không đổi** (S11). | 1 |
| 43 | Accessibility | ✅ native · 🟡 ảnh | Trình đọc màn hình đọc được OMML. Ảnh cần alt text: LaTeX, hoặc câu đọc sinh ra từ AST. | 1–2 |
| 44 | Undo/Redo | ✅ Word · 🟡 PowerPoint | PowerPoint không có cơ chế gộp undo tương đương `UndoRecord`. | 1 / 2 |
| 45 | Báo lỗi thân thiện | ✅ | Ánh xạ log TeX về dòng/cột trên source **của người dùng** nhờ source map. | 1 / 2 |
| 46 | Chạy offline; telemetry chỉ khi người dùng đồng ý | ✅ | Phải chủ động tắt tính năng *tự cài package* của MiKTeX, vì nó tải từ mạng một cách ngầm định. | 1 |
| 47–49 | Data model, style profile, cỡ chữ | ✅ | | 1–3 |
| 50–51 | Bộ test typography và test tích phân | ✅ | So sánh hình ảnh cần runner Windows có Office bản quyền. | 1 |
| 53 | Danh sách "không được làm" | ✅ | Thiết kế tuân thủ toàn bộ. PNG chỉ xuất hiện dưới dạng *fallback bắt buộc* kèm theo SVG (định dạng OOXML yêu cầu), không bao giờ thay thế vector. | — |

---

## 2. Những yêu cầu Word hỗ trợ native

### 2.1 Khả năng có sẵn và cơ chế tương ứng

| Khả năng | Cơ chế trong Word | Mức chắc chắn |
|---|---|---|
| Chèn equation gốc | `Range.InsertXML(flatOpc)`, trong đó Flat OPC chứa `m:oMath` / `m:oMathPara` | ✅ API có tài liệu; ⚠ cách gộp vào đoạn văn hiện tại cần S1 |
| Đọc OMML của một equation | `OMath.Range.WordOpenXML` | ✅ |
| Duyệt các equation | `Document.OMaths`, `Range.OMaths`, `Selection.OMaths` | ✅ |
| Chuyển inline ↔ display | `OMath.Type` (`wdOMathInline` / `wdOMathDisplay`) cộng việc tự quản lý đoạn văn | ✅ |
| Build-up từ UnicodeMath | `OMaths.Add(range)` + `OMath.BuildUp()` | ✅ (chỉ làm đường fallback) |
| Font toán và thiết lập toán của tài liệu | `Document.OMathFontName`, `OMathIntSubSupLim`, `OMathNarySupSubLim`, `OMathSmallFrac`, `OMathDispDefJc`, `OMathLeftMargin`/`RightMargin`, `OMathWrap`, `OMathBreakBin`/`BreakSub` (tương ứng `m:mathPr`) | ✅ |
| N-ary (∫ ∑ ∏…) | `m:nary`: `m:chr`, `m:limLoc` (`undOvr`/`subSup`), **`m:grow`**, `m:subHide`, `m:supHide` | ✅ (theo schema ECMA-376) |
| Delimiter co giãn | `m:d`: `begChr` / `sepChr` / `endChr`, `m:grow`, `m:shp` | ✅ |
| Phân số, căn, chỉ số trên/dưới, chỉ số đứng trước | `m:f` (`bar` / `noBar` / `lin` / `skw`), `m:rad`, `m:sSub` / `m:sSup` / `m:sSubSup`, `m:sPre` | ✅ |
| Ma trận, mảng phương trình | `m:m` (`m:mcs` / `m:mcJc` cho căn cột), `m:eqArr` | ✅ (⚠ cách biểu diễn `&` cần S1) |
| Dấu mũ, gạch trên/dưới, ngoặc nhọn, mũi tên có chữ | `m:acc`, `m:bar`, `m:groupChr`, `m:limLow`, `m:limUpp` | ✅ |
| Hàm (sin, lim…) | `m:func` (`m:fName` cộng đối số) | ✅ |
| `\boxed`, `\cancel`, `\phantom` | `m:borderBox` (có `strikeBLTR`…), `m:phant` (`zeroWid` / `zeroAsc` / `zeroDesc`) | ✅ |
| Chữ thường trong công thức (có dấu tiếng Việt) | `m:r` với `m:nor` và `w:rFonts` của font văn bản | ✅ |
| Kiểu chữ toán (𝐱, ℝ, 𝓕, 𝔤) | Ký tự Unicode Mathematical Alphanumeric, hoặc `m:scr` / `m:sty` | ✅ |
| Gộp undo | `Application.UndoRecord.StartCustomRecord` / `EndCustomRecord` (Word 2010+) | ✅ |
| Toạ độ con trỏ trên màn hình | `Window.GetPoint(out l, out t, out w, out h, range)` | ✅ |
| Phát hiện xung đột phím | `Application.FindKey(BuildKeyCode(…)).Command` | ✅ |
| Đánh số, tham chiếu chéo | Field `SEQ`, `STYLEREF`, `REF`; `Bookmarks` | ✅ |
| Lưu metadata trong tài liệu | `Document.CustomXMLParts`; Content Control (`Appearance = Hidden` từ Word 2013+) | ✅ |
| Chèn SVG / EMF | `InlineShapes.AddPicture` (SVG từ Office 2019/M365), hoặc tự dựng DrawingML trong Flat OPC | ✅ |
| Xem trước đúng như Word hiển thị | `Range.EnhMetaFileBits` trả về EMF của một vùng văn bản | ✅ (có thể dùng cho "Word-exact preview") |
| Accessibility | Trình đọc màn hình đọc được equation gốc | ✅ |

### 2.2 Những gì Word *không* làm được native, hoặc chỉ làm xấp xỉ

| Nhu cầu | Tình trạng trong Word | Hướng xử lý |
|---|---|---|
| Font TeX truyền thống (`mathptmx`, `mathdesign`…) | Không có | Exact (pdfLaTeX) |
| Chỉnh khoảng cách sub/sup, khe giới hạn | Chỉ phụ thuộc MathConstants của font | Exact (`\Umath…`) |
| Phóng to riêng dấu ∫ | Không có thuộc tính | Exact (`\Umathoperatorsize`) |
| `\big` `\Big` `\bigg` (cỡ delimiter cố định) | `m:d` chỉ có chế độ co giãn hoặc không | Xấp xỉ (cảnh báo) hoặc Exact |
| `\!` (khoảng trắng âm), `\hspace{…}` chính xác | Không có khoảng trắng âm | Bỏ qua hoặc xấp xỉ, kèm cảnh báo; hoặc Exact |
| `\displaystyle` bên trong inline, `\dfrac` khi inline | Không ép được style theo từng phần tử | Exact (nếu có), hoặc cảnh báo |
| `array` có đường kẻ, `\hline`, `\cline` | `m:m` không có đường kẻ | Exact |
| Phân biệt `\mathcal` và `\mathscr` trong cùng một font | Unicode dùng chung code point; OMML không bật được stylistic set | Dùng hai font khác nhau cho hai lớp, hoặc Exact |
| Trộn nhiều font toán trong một equation | Chưa có tài liệu (S2) | Theo Capability Matrix |
| Sơ đồ (`tikz-cd`, `amscd`, `xymatrix`) | Không có | Exact (whitelist, Phase 3) |

---

## 3. Những yêu cầu bắt buộc cần TeX/vector backend

1. **Package font truyền thống:** `mathptmx`, `newtxmath`, `newpxmath`, `mathdesign`, `fourier`, `kpfonts`, `mathpazo`, `fouriernc`, `ebgaramond-maths`… Chạy bằng pdfLaTeX.
2. **Trộn font vượt quá khả năng native** đã ghi nhận trong Capability Matrix (ví dụ: ∫ dùng Latin Modern Math còn phần thân dùng XITS Math, nếu S2 cho thấy Word không giữ được).
3. **Tham số typography do người dùng chỉnh:** khoảng cách sub/sup, vị trí và khe của giới hạn, cỡ operator, spacing giữa operator và các atom khác.
4. **Cấu trúc OMML không biểu diễn được:** delimiter cỡ cố định, `array` có đường kẻ, `\displaystyle` cục bộ, khoảng trắng âm, `\genfrac` đầy đủ, `\smash` theo từng phần.
5. **Yêu cầu "giống hệt file PDF LaTeX"** của luận văn hoặc giáo trình.
6. **PowerPoint**, khi spike S5 cho thấy không thể chèn equation gốc một cách ổn định.
7. **Sơ đồ và ký hiệu đặc biệt** (Phase 3, có whitelist).

### 3.1 Quy tắc của chế độ `Auto`

```
NativeFidelityReport report = CapabilityMatrix.Evaluate(ast, typographyProfile, officeBuild);
// Mỗi feature được xếp loại: Supported | Approximate | Unsupported

if (report.Has(Unsupported))
    backend = TexAvailable ? Exact : Native;                  // có warning rõ ràng, không im lặng
else if (report.Has(Approximate))
    backend = settings.ApproximationPolicy == PreferExact && TexAvailable ? Exact : Native;
else
    backend = Native;
```

- Mọi quyết định đều đi kèm **danh sách lý do** hiển thị nhẹ nhàng trong editor, ví dụ: "Font tích phân Latin Modern Math không giữ được ở chế độ Word Equation → đã dùng Exact".
- Chế độ `Native` do người dùng ép thì **không bao giờ âm thầm sai**: các feature bị xếp `Approximate` hoặc `Unsupported` được tô vàng trong preview, và phải xác nhận trước khi chèn.
- `CapabilityMatrix` là **dữ liệu** (`assets/capabilities/word-*.json`) sinh ra từ các test thực nghiệm (S2) chạy trên từng dải build của Office, không phải logic viết cứng.
