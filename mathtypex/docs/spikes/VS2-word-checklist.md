# Kịch bản kiểm tra VS-2 trên Windows + Word (kiêm spike S1, S3)

> Tôi (Claude) không chạy được Word trong môi trường build. Các bước dưới đây cần anh/chị chạy trên máy Windows có Microsoft 365. Ghi kết quả vào cột cuối, gửi lại kèm file `%LOCALAPPDATA%\MathTypeX\logs\word-addin.log` nếu có lỗi.

## Cài đặt

1. Tải artifact **MathTypeX-win-x64** từ GitHub Actions (workflow "MathTypeX", nhánh `claude/youthful-ritchie-g5z8e4`). Hoặc tự build: chạy `tools/package.sh` (Linux/WSL) để có `out/MathTypeX-win-x64.zip`.
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

Các bước 8–13 (sửa lại công thức đã chèn) thuộc VS-4.

## Câu hỏi của spike S1 / S3 cần quan sát thêm

| Câu hỏi | Cách xem | Kết quả |
|---|---|---|
| S1: `InsertXML` có sinh thêm một đoạn trống không? | Xem nhật ký: dòng `Đã chèn: paragraphs A→B` (B nên bằng A) | |
| S1: Chèn trong ô bảng, footnote, header có được không? | Thử `Alt+M` ở các vị trí đó | |
| S1: Bật Track Changes rồi chèn | Phần chèn hiện thành revision | |
| S3: Editor có hiện đúng cạnh con trỏ trên màn hình thứ hai / màn hình DPI 150% không? | Kéo Word sang màn hình khác | |
| S3: Thả phím Alt sau `Alt+M` có làm hiện KeyTips (chữ cái trên ribbon) không? | Quan sát | |
| S3: Bộ gõ UniKey/EVKey đang bật Telex: gõ `\cos`, `\infty` trong editor có bị biến dạng không? | Quan sát (lớp L0 chỉ chặn được bộ gõ TSF; với UniKey/EVKey cần lớp L1/L2 ở VS-10) | |

## Gỡ cài đặt

Chạy `uninstall.cmd`.
