# MathTypeX (tên tạm) — LaTeX4Office

Add-in cho Microsoft Word và PowerPoint trên Windows. Người dùng gõ công thức nhanh như gõ LaTeX, kể cả khi chưa biết LaTeX. Kết quả được chèn thành **Word Equation gốc (OMML)**, hoặc thành **hình vector dựng bằng TeX** khi cần typography đúng kiểu LaTeX. Source LaTeX luôn được giữ lại để sửa sau.

> **Trạng thái:** đang thiết kế, theo *Nhiệm vụ đầu tiên* (§57). Chưa có code sản phẩm.
> Các tài liệu dưới đây là **đề xuất để review**, chưa phải spec đã chốt. Phần còn lại của tài liệu
> chuyển giao (PRD, Test strategy chi tiết…) sẽ được viết sau khi các quyết định ở cuối file này được xác nhận.

---

## Đọc theo thứ tự

| # | Tài liệu | Trả lời mục §57 |
|---|---|---|
| 0 | README (file này): hiểu biết về yêu cầu, các điều chỉnh đề xuất, quyết định chính, câu hỏi mở | — |
| 1 | [docs/01-feasibility.md](docs/01-feasibility.md) | 1. Feasibility · 2. Word native · 3. Phần bắt buộc TeX/vector |
| 2 | [docs/02-stack-and-architecture.md](docs/02-stack-and-architecture.md) | 4. Stack · 5. Kiến trúc tổng thể |
| 3 | [docs/03-math-ast-and-parser.md](docs/03-math-ast-and-parser.md) | 6. Math AST (kèm thiết kế parser) |
| 4 | [docs/04-fonts-and-integrals.md](docs/04-fonts-and-integrals.md) | 7. Chiến lược font · 8. Dấu tích phân |
| 5 | [docs/05-office-integration.md](docs/05-office-integration.md) | 9. Word · 10. PowerPoint |
| 6 | [docs/06-keyboard-and-ime.md](docs/06-keyboard-and-ime.md) | 11. Phím tắt & bộ gõ tiếng Việt |
| 7 | [docs/07-equation-metadata.md](docs/07-equation-metadata.md) | 12. Lưu source LaTeX & data model |
| 8 | [docs/08-repository-structure.md](docs/08-repository-structure.md) | 13. Cấu trúc repository |
| 9 | [docs/09-roadmap.md](docs/09-roadmap.md) | 14. Roadmap theo tuần (+ danh sách spike) |
| 10 | [docs/10-risks.md](docs/10-risks.md) | 15. Rủi ro kỹ thuật |
| 11 | [docs/11-first-vertical-slice.md](docs/11-first-vertical-slice.md) | 16. Vertical slice đầu tiên |

Quy ước trong toàn bộ tài liệu:

- ✅ làm được bằng API hoặc định dạng **đã có tài liệu chính thức**;
- 🟡 làm được nhưng cần workaround hoặc **spike** để xác minh;
- 🔴 không làm được theo cách được mô tả, kèm phương án thay thế.

Mọi hành vi của Office mà tôi chưa kiểm chứng được bằng tài liệu đều được gắn ⚠ và quy về một spike `S1…S11` (xem [09-roadmap](docs/09-roadmap.md#spikes)).

---

## Hiểu biết về yêu cầu

**Anh/chị đã nêu rõ:**

- Đối tượng: sinh viên, giáo viên, giảng viên và người soạn đề thi Toán ở Việt Nam, làm việc trong Word/PowerPoint trên Windows.
- Thành công tức là chuỗi acceptance ở §54 chạy trơn tru: `Alt+M` → gõ LaTeX → preview → chọn font → `Enter` → equation gốc trong Word → mở lại → sửa → cập nhật. Thêm vào đó là lệnh *Convert LaTeX* trên văn bản có `$…$` và `$$…$$`.
- Ưu tiên typography đúng hơn giải pháp nhanh nhưng sai (§56). Native khi có thể, vector khi native không đủ. Không bao giờ mất source.
- Làm theo từng vertical slice nhỏ, mỗi slice build được, chạy được, có test và có demo.

**Các giả định của tôi (cần anh/chị xác nhận, xem phần Câu hỏi mở):**

| # | Giả định |
|---|---|
| A1 | Mục tiêu chính là Microsoft 365 Apps và Office 2021/2024 trên Windows 10 22H2+/11, cả bản 64-bit lẫn 32-bit. Office 2016/2019 chỉ hỗ trợ ở mức "best effort" (Office 2016 không có SVG, nên dùng EMF). |
| A2 | Nhiều người dùng **không cài TeX**. Vì vậy backend Native phải tự đứng vững được, còn backend TeX là tùy chọn (Phase 2). |
| A3 | Đội 2 developer: một người lo Office/Windows, một người lo core/typography. Roadmap tính theo giả định này. |
| A4 | Cài đặt theo từng người dùng (per-user), không cần quyền admin, chạy offline hoàn toàn. |

---

## Những điểm tôi đề xuất điều chỉnh trong yêu cầu

Đây là những chỗ mà làm đúng nguyên văn yêu cầu sẽ dẫn tới sai kỹ thuật hoặc sai typography:

1. **TeX không kéo dãn dấu ∫ theo nội dung.** Trong LaTeX chuẩn, `\int` chỉ có hai cỡ (text và display). Phân số cao bao nhiêu thì dấu ∫ vẫn giữ cỡ display. Cảm giác "∫ thấp, lệch" trong Word thường đến từ việc dùng chế độ inline hoặc dùng ký tự ∫ như chữ thường. Đề xuất: có ba chế độ cỡ `TeX` / `Grow` / `Scale`, mặc định là `TeX` (xem [04 §8](docs/04-fonts-and-integrals.md#8-chiến-lược-dấu-tích-phân-đúng-kiểu-latex)).
2. **VSTO không chạy được trên .NET 8.** Add-in in-proc buộc phải dùng .NET Framework 4.8. Ngoài ra **.NET 8 hết hỗ trợ ngày 10/11/2026**, nên các tiến trình riêng nên dùng **.NET 10 LTS**.
3. **Các phím mặc định đề xuất bị trùng với Word.** `Alt+M` trùng KeyTip tab *Mailings* (Word giao diện tiếng Anh). `Ctrl+Shift+M` đang là *bỏ thụt lề*. `Ctrl+Alt+M` đang là *chèn comment*. Vẫn giữ được các phím này, nhưng phải có cơ chế phát hiện xung đột và đề xuất phím thay thế ngay từ lần chạy đầu.
4. **Display equation trong Word phải đứng riêng một đoạn.** Với câu `ta có $$E(X)=\mu.$$`, khi convert phải tách đoạn văn. Đây là hành vi của Word chứ không phải lỗi của app.
5. **Không có API nào để chèn OMML vào PowerPoint**, cả COM lẫn Office.js. Equation gốc trong PowerPoint phải đi đường vòng (spike S5). Hình vector SVG thì làm được.
6. **Gửi file `.docx` cho người khác:** font toán dạng OpenType/CFF (XITS, Latin Modern Math, STIX…) nhiều khả năng **không nhúng được** vào file, nên máy người nhận sẽ hiển thị khác. Cambria Math là lựa chọn an toàn khi chia sẻ. App cần cảnh báo việc này (S2).
7. **Dark mode:** equation gốc (OMML) đổi màu theo chế độ tối của Word, còn ảnh SVG/EMF thì không.
8. **Dấu phẩy thập phân kiểu Việt Nam:** gõ `3,14` theo chuẩn TeX sẽ ra "3, 14" (có khoảng trắng). Cần setting *"Dấu thập phân là dấu phẩy"*.
9. **Các package `mathptmx`, `mathdesign`, `fourier`, `kpfonts`, `newtxmath`** là font Type1 8-bit. Cách ổn định nhất là chạy chúng qua **pdfLaTeX** chứ không qua LuaLaTeX/XeLaTeX. Với OpenType MATH thì dùng LuaLaTeX kèm `unicode-math`.
10. Không nên đặt namespace gốc là `Math.*` vì nó **xung đột với `System.Math`** của C#. Đề xuất dùng `MathTypeX.*`.

---

## Quyết định kiến trúc chính (đề xuất)

| ID | Quyết định | Lý do ngắn |
|---|---|---|
| D1 | Add-in **VSTO (.NET Framework 4.8)** giữ ở mức thật mỏng; MVP không dùng Office.js | Office.js không hook được phím ở mức sâu, không biết toạ độ con trỏ, không chạy được TeX, không đọc được danh sách font đã cài |
| D2 | Ba tiến trình: add-in in-proc → `MathTypeX.Editor.exe` (.NET 10, WPF) → `MathTypeX.TexWorker.exe` (sandbox) | Dùng được .NET hiện đại; crash không kéo Word theo; add-in tải nhanh nên không bị Office tự vô hiệu hoá; bộ gõ tiếng Việt loại trừ được theo tên tiến trình |
| D3 | **AST là trung tâm.** Thư viện core target `netstandard2.0` + `net10.0` | Dùng chung cho add-in (net48), editor, CLI và test |
| D4 | Preview bằng **MathML Core trong WebView2** | Chromium dựng công thức từ bảng OpenType MATH của *chính font đã cài*; nhanh (vài ms); Office 365 vốn đã kèm WebView2 |
| D5 | Native: **tự sinh OMML từ AST**, chèn bằng `Range.InsertXML` | Kiểm soát hoàn toàn cấu trúc (`m:nary`, `m:d`, `m:m`…); không phụ thuộc AutoCorrect hay build-up của Word |
| D6 | Exact: **LuaLaTeX + `unicode-math`** (font OTF, `range=` cho từng lớp ký hiệu), **pdfLaTeX** (package cũ) → `dvisvgm` (glyph chuyển thành path) → SVG, cộng EMF tự chuyển đổi | Đúng typography TeX, giữ vector |
| D7 | Chế độ `Auto` chọn backend dựa trên **Native Capability Matrix đo bằng thực nghiệm** | Không đoán xem Word làm được gì |
| D8 | Metadata lưu trong **CustomXMLPart**, khoá là **hash của OMML→AST đã chuẩn hoá**. Thêm kho cục bộ, và chuyển ngược OMML→LaTeX làm lưới an toàn | Không chèn "marker ẩn" vào công thức; copy/paste trong cùng tài liệu vẫn sửa lại được |
| D9 | ∫ luôn được dựng như **cấu trúc n-ary**, không bao giờ là một ký tự text | Đáp ứng §4 và §53 |
| D10 | Bộ gõ tiếng Việt: **không đổi layout của HĐH**. Tắt TSF IME cho ô Math; sửa lỗi Telex/VNI ngay trong tokenizer; có "Math Input Shield" mà người dùng kiểm soát được | §13 |

---

## Câu hỏi mở (cần anh/chị quyết định trước khi viết spec chi tiết)

1. **Phiên bản Office:** chỉ hỗ trợ M365/2021/2024, hay phải hỗ trợ đầy đủ cả 2016/2019? Câu trả lời quyết định SVG hay EMF là mặc định, và lượng test cần chạy.
2. **Mô hình tiến trình:** anh/chị có đồng ý tách editor thành `.exe` riêng (D2) không? Phương án thay thế là chạy toàn bộ in-proc trên .NET Framework 4.8.
3. **TeX:** yêu cầu người dùng tự cài MiKTeX/TeX Live, hay đóng gói kèm một bản TeX tối giản (thêm khoảng vài trăm MB)?
4. **Cỡ ∫ mặc định:** chọn `TeX` (hai cỡ, đúng LaTeX) hay `Grow` (kéo dãn theo nội dung)?
5. **Mô hình phát hành:** thương mại/đóng hay mã nguồn mở? Điều này ảnh hưởng tới việc chọn license và tới việc ký số.
6. **Nguồn lực thực tế** (số người, thời gian) để hiệu chỉnh roadmap.
