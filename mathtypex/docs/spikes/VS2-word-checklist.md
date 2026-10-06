# Kịch bản kiểm tra VS-2…VS-9 trên Windows + Word (kiêm spike S1, S3)

> Tôi (Claude) không chạy được Word trong môi trường build. Các bước dưới đây cần anh/chị chạy trên máy Windows có Microsoft 365. Ghi kết quả vào cột cuối, gửi lại kèm file `%LOCALAPPDATA%\MathTypeX\logs\word-addin.log` nếu có lỗi.

## Cài đặt

1. Tải artifact **MathTypeX-win-x64** từ GitHub Actions (workflow "MathTypeX", nhánh `claude/youthful-ritchie-g5z8e4`, job `package-windows`). Hoặc tự build: `pwsh tools/package.ps1 -Zip` (Windows) / `tools/package.sh` (Linux/WSL) → `out/MathTypeX-win-x64.zip`.
2. Giải nén, **đóng Word**, chạy `install.cmd`.
3. Mở Word. Trên ribbon phải có tab **MathTypeX**.
   - Nếu không thấy tab: vào *File → Options → Add-ins → Manage: COM Add-ins → Go…* xem MathTypeX có bị tắt không, và đọc nhật ký.

## Chuỗi acceptance §54 (phần VS-2 làm được)

| # | Thao tác | Kỳ vọng | Kết quả |
|---|---|---|---|
| 1 | Gõ `Ta có ` trong một đoạn văn | | |
| 2 | Nhấn `Alt+M` | Editor hiện ngay dưới con trỏ (lần đầu có thể chậm 1–2 giây vì editor đang khởi động); tab Mailings **không** bị mở | |
| 3 | Gõ `\int_0^1 \frac{x^2}{1+x^2}\,dx` | Dòng trạng thái hiện `✓ \int_{0}^{1}…` | |
| 5 | Chọn font **XITS Math** (nếu đã cài) | | |
| 6 | Nhấn `Enter` | Editor đóng, focus về Word | |
| 7 | | Công thức xuất hiện **inline, là Word Equation gốc** ngay sau "Ta có ", con trỏ đứng sau công thức | |
| — | `Ctrl+Z` một lần | Công thức biến mất (một bước Undo duy nhất) | |
| — | Gõ `\frac{a}{b` rồi Enter | Không chèn; hiện "Bạn đang thiếu dấu } để kết thúc mẫu số." | |
| — | Đoạn trống → `Alt+M` → `Ctrl+Alt+M` → `E(X)=\mu.` → Enter | Display equation canh giữa, đứng riêng một đoạn | |
| — | `Esc` | Editor đóng, không chèn gì | |

### Sửa lại công thức (VS-4, §54 bước 8–13)

| # | Thao tác | Kỳ vọng | Kết quả |
|---|---|---|---|
| 8 | Đặt con trỏ vào bên trong công thức vừa chèn | | |
| 9 | `Alt+M` | Editor mở, ô LaTeX chứa **đúng source cũ** `\int_0^1 \frac{x^2}{1+x^2}\,dx` (không phải bản chuyển ngược), font XITS Math được chọn sẵn | |
| 10 | | Như trên | |
| 11 | Sửa `x^2` thành `x^3` | Preview cập nhật | |
| 12 | `Enter` | | |
| 13 | | Công thức trong Word được **thay tại chỗ**, `Ctrl+Z` một lần trả về công thức cũ | |
| — | Lưu tài liệu, đóng Word, mở lại, đặt con trỏ vào công thức, `Alt+M` | Source vẫn là bản đã gõ (metadata nằm trong tài liệu) | |
| — | Copy công thức sang **tài liệu mới** trên cùng máy, `Alt+M` | Source vẫn đúng (kho cục bộ `%LOCALAPPDATA%\MathTypeX\equations.jsonl`) | |
| — | Chèn equation bằng Word (`Alt+=`, gõ `a^2+b^2=c^2`), rồi `Alt+M` trên nó | Editor mở với LaTeX chuyển ngược `a^{2}+b^{2}=c^{2}` và dòng thông báo "chưa có source LaTeX lưu kèm" | |
| — | Sửa trực tiếp một công thức MathTypeX bằng công cụ Equation của Word, rồi `Alt+M` | Editor hiện bản chuyển ngược của nội dung **mới** | |

### Gõ nhanh: gợi ý lệnh, ô trống, Tab, Command Palette (VS-6/7)

| # | Thao tác | Kỳ vọng | Kết quả |
|---|---|---|---|
| A1 | Trong editor gõ `\fra` | Danh sách gợi ý hiện ngay dưới `\fra`, `\frac` đứng đầu; dòng dưới danh sách có cú pháp `\frac{tử số}{mẫu số}` | |
| A2 | `Enter` (hoặc `Tab`) | Thành `\frac{}{}`, con trỏ nằm trong ô tử số; preview hiện hai ô □; dòng trợ giúp: "Phân số · đang nhập: tử số …" | |
| A3 | Gõ `x+1`, `Tab`, gõ `x-1`, `Tab` | Con trỏ sang mẫu số rồi ra sau `}`; dòng trợ giúp đổi theo | |
| A4 | Xoá hết, gõ `\int` rồi chọn mẫu **Tích phân xác định** bằng `↓`, `Enter` | `Tab` đi lần lượt: cận dưới → cận trên → hàm → biến → ra ngoài (§10) | |
| A5 | Trong ô tử số của `\frac` gõ `\sqrt` + `Enter` | Snippet lồng: `Tab` đi hết ô của `\sqrt`, `Tab` tiếp theo sang mẫu số của `\frac` | |
| A6 | Gõ `\alpha` đầy đủ rồi `Enter` | Chèn luôn vào Word (không phải nhấn `Enter` hai lần) | |
| A7 | Gõ `\frac{1}{}` rồi `Enter` | Không chèn, báo "Còn ô trống □ chưa điền (Ctrl+Shift+Enter để vẫn chèn)"; `Ctrl+Shift+Enter` thì chèn | |
| A8 | `Ctrl+Shift+P`, gõ `tich phan` (không dấu) | Danh sách có các mẫu tích phân; `Enter` chèn mẫu tại vị trí con trỏ trong ô LaTeX, `Esc` đóng palette và trả focus về ô LaTeX | |
| A9 | `Ctrl+Shift+P`, gõ `phân số` bằng UniKey/EVKey | Gõ tiếng Việt được trong ô tìm (ô LaTeX thì vẫn tắt bộ gõ) | |
| A10 | Nhấp đúp một mục trong danh sách gợi ý | Được chèn như khi nhấn `Enter` | |
| A11 | `F1` | Bật/tắt dòng trợ giúp người mới; lần mở sau vẫn giữ lựa chọn | |
| A12 | `Ctrl+Z` sau khi chấp nhận gợi ý | Trả về `\fra` (Undo của ô soạn còn nguyên) | |

### Chuyển LaTeX trong vùng chọn (VS-8, §54 phần 2)

Dán đoạn sau vào Word (mỗi dòng một đoạn), hoặc mở `tests/corpus/convert-sample.txt` rồi chép vào:

```
Với $X\sim N(\mu,\sigma^2)$, ta có $$E(X)=\mu.$$ Do đó kỳ vọng bằng μ.
Giá vé $20 và $30, biến $HOME$, còn \(\alpha+\beta\) là toán.
```

| # | Thao tác | Kỳ vọng | Kết quả |
|---|---|---|---|
| C1 | Chọn cả hai đoạn, tab **MathTypeX → Chuyển LaTeX** (hoặc `Alt`, `Y`/`MX`, `C`) | Thanh trạng thái: "đã chuyển 3/4 công thức"; hộp thông báo liệt kê `$HOME$` — giống tên biến môi trường | |
| C2 | | Đoạn 1 tách thành: "Với [inline], ta có" / [display `E(X)=μ.`] / "Do đó kỳ vọng bằng μ." | |
| C3 | | `$20 và $30` giữ nguyên là chữ; `\(\alpha+\beta\)` thành equation inline | |
| C4 | `Ctrl+Z` **một lần** | Toàn bộ văn bản trở lại như trước khi chuyển (kể cả các đoạn đã tách) | |
| C5 | Không chọn gì, đặt con trỏ trong đoạn 2, Chuyển LaTeX | Chỉ đoạn 2 được chuyển | |
| C6 | Đặt `$x^2$` trong một đoạn font Consolas, Chuyển LaTeX | Không chuyển, lý do "dùng font code" | |
| C7 | Chuyển trong ô bảng, footnote, header | Hoạt động như ở thân bài (ghi lại nếu lỗi) | |
| C8 | Sau khi chuyển, đặt con trỏ vào `E(X)=μ.` → `Alt+M` | Editor mở với `E(X)=\mu.` (metadata đã lưu) | |
| C9 | Bôi đen chữ `\frac{a}{b}` (văn bản thường) → `Alt+M` | Editor mở sẵn `\frac{a}{b}`; `Enter` thay đoạn chữ bằng equation | |
| C10 | Bật Track Changes rồi Chuyển LaTeX | Phần thay hiện thành revision; `Ctrl+Z` vẫn một lần | |

### Chuyển cả tài liệu và Trả về LaTeX (VS-9)

Chuẩn bị một tài liệu có: vài đoạn chứa `$…$`/`$$…$$` ở thân bài, một footnote có `$x^2$`, header có `$\alpha$`, một hộp văn bản có `\(a+b\)`, một đoạn font Consolas có `$PATH$`, và dòng "Giá $20 và $30".

| # | Thao tác | Kỳ vọng | Kết quả |
|---|---|---|---|
| D1 | **MathTypeX → Chuyển cả tài liệu** (`Alt`, `Y`/`MX`, `D`) | Hộp duyệt mở trước cửa sổ Word; liệt kê công thức ở Thân bài, Chú thích cuối trang, Header, Hộp văn bản; mục Consolas bị khoá (đỏ, "dùng font code"); `$20` không xuất hiện | |
| D2 | `↑`/`↓` qua các mục | Ngữ cảnh "…ta có ⟦$$…$$⟧ nên…" và preview đổi theo; mục lỗi cú pháp hiện "Lỗi: …" | |
| D3 | `Space` trên một mục, `Ctrl+Shift+A`, `Ctrl+A` | Tích/bỏ tích; nút "Chuyển N công thức" cập nhật số | |
| D4 | `Del` trên một mục rồi `Enter` | Mục đó bị gạch ngang, không chuyển; lần quét sau không còn xuất hiện (`%APPDATA%\MathTypeX\settings.json` → `IgnoredSources`) | |
| D5 | Sau khi chuyển | Mọi công thức đã chọn thành Word Equation ở đúng story; display tách đoạn như C2; `Ctrl+Z` **một lần** hoàn tác tất cả | |
| D6 | Tài liệu dài (vài trăm công thức), giữ `Esc` giữa chừng | Dừng, thông báo "đã dừng theo yêu cầu"; phần đã chuyển vẫn hoàn tác được bằng một lần `Ctrl+Z` | |
| D7 | Mở hộp duyệt, sửa tài liệu (thêm một dòng ở đầu), rồi `Enter` trong hộp duyệt | Vẫn chuyển đúng các công thức đã chọn | |
| D8 | Đóng editor (Task Manager) rồi Chuyển cả tài liệu | Nếu không khởi động lại được editor: MessageBox hỏi "Chuyển N mục chắc chắn?" | |
| D9 | Chọn một đoạn có công thức đã chuyển → **Trả về LaTeX** | Công thức thành đúng văn bản gốc (`$$E(X)=\mu.$$`); đoạn đã tách **không** tự gộp lại (giới hạn đã biết); `Ctrl+Z` một lần | |

## Câu hỏi của spike S1 / S3 cần quan sát thêm

| Câu hỏi | Cách xem | Kết quả |
|---|---|---|
| S1: `InsertXML` có sinh thêm một đoạn trống không? | Xem nhật ký: dòng "InsertXML sinh thêm một đoạn…" (không có dòng này là tốt nhất) | |
| S1: `Range.InsertParagraphAfter` trên Range rỗng có chèn dấu hết đoạn đúng tại vị trí đó không? | C2: chữ trước/sau display nằm đúng đoạn | |
| S1: `InsertXML` một đoạn chữ thay cho equation (Trả về LaTeX) có xoá hẳn vùng toán không? | D9: văn bản trả về không còn nằm trong khung equation | |
| S1: `StoryRanges` + `NextStoryRange` có lặp lại header liên kết section trước không? | D1: mỗi công thức header chỉ xuất hiện một lần | |
| S1: Chèn trong ô bảng, footnote, header có được không? | Thử `Alt+M` ở các vị trí đó | |
| S1: Bật Track Changes rồi chèn | Phần chèn hiện thành revision | |
| S3: Editor có hiện đúng cạnh con trỏ trên màn hình thứ hai / màn hình DPI 150% không? | Kéo Word sang màn hình khác | |
| S3: Thả phím Alt sau `Alt+M` có làm hiện KeyTips (chữ cái trên ribbon) không? | Quan sát | |
| S3: Bộ gõ UniKey/EVKey đang bật Telex: gõ `\cos`, `\infty` trong editor có bị biến dạng không? | Quan sát (lớp L0 chỉ chặn được bộ gõ TSF; với UniKey/EVKey cần lớp L1/L2 ở VS-10) | |

## Gỡ cài đặt

Chạy `uninstall.cmd`.
