# Spike S2 — Word hiển thị font toán và dấu ∫ thế nào?

> **Trạng thái:** chờ chạy trên Windows + Microsoft 365 (Word).
> **Mục đích:** dựng *Native Capability Matrix* đầu tiên, từ đó quyết định khi nào chế độ `Auto` phải chuyển sang backend TeX ([01 §3.1](../01-feasibility.md#31-quy-tắc-của-chế-độ-auto)).

## Chuẩn bị

1. Cài các font cần so sánh cho Windows: XITS Math, Latin Modern Math, STIX Two Math. Cambria Math có sẵn.
2. Sinh tài liệu thử:

   ```bash
   cd mathtypex
   dotnet run --project src/MathTypeX.Cli -- docx tests/corpus/integrals.tex --out out/S2-integrals.docx
   dotnet run --project src/MathTypeX.Cli -- docx tests/corpus/typography.tex --out out/S2-typography.docx --sizing TeX
   dotnet run --project src/MathTypeX.Cli -- docx tests/corpus/typography.tex --out out/S2-eqarr.docx --sizing TeX --align eqarr --fonts "Cambria Math"
   ```

3. Mở từng file trong Word, ghi kết quả vào bảng bên dưới (chụp màn hình nếu được, lưu vào `docs/spikes/S2/`).

## Câu hỏi và kết quả

| # | Câu hỏi | Cách xem | Kết quả |
|---|---|---|---|
| 1 | Word có dùng đúng font ghi trong `w:rFonts` của từng run không (XITS, LM, STIX)? | So sánh hình dạng chữ x, α giữa các dòng font khác nhau | |
| 2 | Ký tự ∫ có dùng font ghi trong `m:ctrlPr` của `m:naryPr` không? | Mục "Thử trộn font" ở cuối `S2-integrals.docx` | |
| 3 | Inline: ∫ có ở cỡ nhỏ (text style), cận có đặt bên cạnh không? | Dòng `… · TeX · inline` | |
| 4 | Display: ∫ có ở cỡ display, cân đối với phân số không? | Dòng `… · TeX · display` | |
| 5 | `Grow`: ∫ có kéo dãn theo chiều cao phân số lồng (§51 ca 4) không? Font nào làm đẹp, font nào xấu? | So `TeX · display` với `Grow · display` | |
| 6 | Bấm vào equation rồi chuyển *Linear* ↔ *Professional* có giữ đúng cấu trúc không? | Thao tác trong Word | |
| 7 | `aligned`: bản ma trận (`S2-typography`) hay bản `m:eqArr` + `&` (`S2-eqarr`) căn dấu `=` đúng hơn? | Mục "Đa dòng và trường hợp" | |
| 8 | Font cài per-user (không phải cho mọi người dùng) có hiện trong Word không? | Cài một font "Install for me only" rồi sinh lại tài liệu | |
| 9 | Lưu `.docx` với tuỳ chọn *Embed fonts in the file*: font toán có được nhúng không? | Mở trên máy khác không có font đó | |
| 10 | Mở tài liệu, lưu lại, rồi chạy `mtx` đọc ngược (VS-4): OMML Word ghi lại khác gì OMML ban đầu? | Để dành cho VS-4 | |

## Kết luận và quyết định

*(Điền sau khi chạy: cập nhật `assets/capabilities/word-m365.json` và quyết định mặc định cho `AlignmentStrategy`.)*
