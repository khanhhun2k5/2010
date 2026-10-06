# 10 — Các rủi ro kỹ thuật lớn nhất

## 15. Risk register

Thang đo: **K** = khả năng xảy ra, **T** = tác động (Thấp / Trung bình / Cao).

| ID | Rủi ro | K | T | Giảm thiểu | Spike |
|---|---|---|---|---|---|
| R1 | **Word không tôn trọng việc trộn font toán trong một equation** (font theo từng slot, font riêng cho ∫), hoặc hành vi thay đổi theo build | Cao | Cao | Capability Matrix sinh từ thực nghiệm, `Auto` chuyển sang Exact, cảnh báo minh bạch. Đặt kỳ vọng: hứa "WYSIWYG tuyệt đối" ở Exact, còn Native là "tốt nhất có thể và được báo trước" | S2 |
| R2 | **PowerPoint không có API chèn OMML.** Đường vòng qua clipboard có thể mong manh và làm phiền clipboard của người dùng | Cao | Cao | Ưu tiên P1 (presentation tạm) có lưu/khôi phục clipboard và loại khỏi Clipboard History. Mặc định "một equation một shape". Nếu S5 thất bại thì dùng SVG làm mặc định | S5 |
| R3 | **Bộ gõ tiếng Việt dạng hook** biến dạng lệnh LaTeX; hook LL của mình bị AV/EDR nghi ngờ hoặc bị bộ gõ chen lên trước | Cao | Cao | Bốn lớp L0–L3; editor là tiến trình riêng nên loại trừ được; EXE ký số; Shield chỉ chạy khi editor có focus; có policy để tắt | S6 |
| R4 | **Office tự vô hiệu hoá add-in** vì khởi động chậm hoặc crash, hoặc Group Policy chặn VSTO | TB | Cao | Add-in mỏng (dưới 50 ms), mọi thứ nặng nằm ngoài tiến trình, `try/catch` ở mọi điểm vào từ Office, đo thời gian khởi động trong CI; tài liệu triển khai cho doanh nghiệp | S3 |
| R5 | **Ràng buộc .NET Framework 4.8 in-proc**; nếu ai đó nạp .NET Core in-proc thì xung đột runtime với add-in khác | Chắc chắn (là ràng buộc) | TB | Kiến trúc hai tiến trình; core dùng netstandard2.0; quy tắc không bao giờ nạp .NET Core vào tiến trình Office | — |
| R6 | **Môi trường TeX đa dạng**: TeX Live và MiKTeX, phiên bản khác nhau, thiếu package, cache `luaotfload`; LuaLaTeX chậm (0,3–1,5 s) | Cao | TB | Phát hiện và chẩn đoán bản cài, template có version, format dựng sẵn, render nền có cache, preview MathML thay thế trong lúc chờ. Cân nhắc đóng gói TeX tối giản (câu hỏi mở Q3) | S7 |
| R7 | **Office hỗ trợ SVG không đầy đủ** (Office 2016 không có SVG; tập con SVG; dark mode) | TB | TB | Glyph chuyển thành path, tránh các tính năng SVG hiếm, luôn có EMF/PNG fallback, cảnh báo dark mode | S7, S11 |
| R8 | **Mất metadata** qua `.doc`, Google Docs, LibreOffice, copy giữa tài liệu | TB | TB | Bốn tầng lưu trữ (L1–L4), chuyển ngược OMML→LaTeX, cảnh báo khi lưu `.doc` | S1 |
| R9 | **Tài liệu lớn**: chi phí mỗi lệnh gọi COM, vị trí text lệch Range (field, hidden text, bảng) | TB | Cao | Đọc text một lần, kiểm tra từng ứng viên, xử lý từ cuối lên, tắt ScreenUpdating và Pagination, benchmark với tài liệu 300 trang | S1 |
| R10 | **Xung đột phím tắt** và khác biệt theo ngôn ngữ giao diện (KeyTips) | Cao | TB | Wizard ở lần chạy đầu, `FindKey`, bảng tĩnh cộng UI Automation, đề xuất phím trống | S3 |
| R11 | **Kỳ vọng typography lệch** (∫ "phải co giãn như TeX" trong khi TeX không co giãn ∫) | TB | TB | Ba chế độ TeX/Grow/Scale có preview so sánh; nêu rõ trong tài liệu và trong UI | S8 |
| R12 | **License font**: Cambria Math độc quyền; quyền nhúng (`fsType`) khi chuyển glyph thành path; font toán OTF không nhúng được vào `.docx` | TB | TB | Chỉ đóng gói font OFL/GFL; đọc `fsType` để cảnh báo; preset "An toàn khi chia sẻ"; lệnh "Chuyển sang vector để chia sẻ" | S2 |
| R13 | **Preview khác với kết quả cuối** (MathML Core và Word có thuật toán layout khác nhau) | Cao | TB | Ghi rõ "preview gần đúng" cho Native; chức năng tuỳ chọn "Word-exact preview" qua `EnhMetaFileBits`; với Exact thì preview chính là SVG thật | S4 |
| R14 | **Undo:** `InsertXML` có thể sinh nhiều bước; PowerPoint không gộp được | TB | TB | `UndoRecord` cho Word; ghi rõ giới hạn của PowerPoint | S1, S5 |
| R15 | **Office cập nhật hằng tháng** (Click-to-Run) làm đổi hành vi | TB | TB | Integration test và test ảnh hằng đêm trên Current Channel lẫn Monthly Enterprise; Capability Matrix theo dải build | — |
| R16 | **Bảo mật TeX** (bypass bằng `^^`, macro bomb, file độc hại chứa metadata) | Thấp | Cao | TeX chỉ nhận đầu ra của printer, sandbox nhiều lớp, mở tài liệu không kích hoạt TeX, fuzz MacroExpander | S7 |
| R17 | **Phạm vi quá lớn** so với nguồn lực (56 mục yêu cầu) | Cao | Cao | Vertical slice; Phase 1 chỉ có Native và Word; spike trước khi cam kết; cắt bớt tính năng trước khi cắt chất lượng typography | — |
| R18 | **Kiểm thử Office trên CI**: Microsoft không hỗ trợ tự động hoá Office trên server; cần VM có license | Chắc chắn | TB | Runner Windows tự host với phiên đăng nhập tương tác; test có `Trait("Requires","Office")`; phần lớn logic test được không cần Office (headless) | — |

## Ba rủi ro cần xử lý sớm nhất (tuần 1–2)

1. **R1 / S2:** quyết định Native có đạt chất lượng mong muốn với XITS / LM / STIX hay không. Kết quả này ảnh hưởng tới cách định vị sản phẩm.
2. **R3 / S6:** người dùng mục tiêu là người Việt; nếu gõ `\cos` thành `\có` thì sản phẩm hỏng ngay ở trải nghiệm đầu tiên.
3. **R4 + D2 / S3:** mô hình tiến trình là nền móng; đổi về sau rất đắt.
