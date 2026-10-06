MathTypeX — gõ LaTeX thành Word Equation (bản thử nghiệm cho Windows)
=====================================================================

YÊU CẦU
  - Windows 10/11 64-bit, Microsoft 365 / Word 2016 trở lên (bản desktop, 32 hoặc 64-bit).
  - Microsoft Edge WebView2 Runtime (thường có sẵn trên Windows 11/10 đã cập nhật) — chỉ cần cho khung xem trước.
  - Không cần quyền quản trị, không cần cài .NET riêng (editor đã kèm runtime).

CÀI ĐẶT
  1. Giải nén toàn bộ thư mục này ra một chỗ cố định (ví dụ Downloads\MathTypeX).
  2. Đóng Word.
  3. Nhấp đúp install.cmd. Script chép add-in và editor vào %LOCALAPPDATA%\MathTypeX
     và đăng ký add-in cho người dùng hiện tại (HKCU, cho cả Office 32-bit và 64-bit).
     Nếu Windows SmartScreen cảnh báo, chọn "More info" → "Run anyway" (bản chưa ký số).
  4. Mở Word: trên ribbon có tab "MathTypeX".

DÙNG THỬ
  - Alt+M trong văn bản: mở editor ngay dưới con trỏ. Gõ \int_0^1 \frac{x^2}{1+x^2}\,dx rồi Enter
    → chèn Word Equation gốc. Alt+M trên công thức đã chèn: sửa lại từ đúng LaTeX cũ.
  - Ribbon MathTypeX → "Chuyển LaTeX": đổi $…$, $$…$$, \(…\), \[…\] trong vùng chọn thành equation.
  - Ribbon MathTypeX → "Chuyển cả tài liệu": quét toàn tài liệu, duyệt từng mục rồi chuyển.
  - Ribbon MathTypeX → "Trả về LaTeX": đổi equation đang chọn về lại văn bản LaTeX.
  - Mỗi thao tác chỉ là một bước Undo (Ctrl+Z).
  Kịch bản kiểm tra chi tiết: docs/spikes/VS2-word-checklist.md trong mã nguồn.

KIỂM TRA NHANH KHÔNG CẦN WORD
  editor\MathTypeX.Editor.exe --self-test "%TEMP%\mtx-self-test.txt"
  rồi mở tệp báo cáo: mọi dòng phải là PASS/INFO, dòng cuối "RESULT PASS".

KHI CÓ LỖI
  - Ribbon MathTypeX → "Nhật ký", hoặc mở %LOCALAPPDATA%\MathTypeX\logs\word-addin.log.
  - Word tắt add-in (File → Options → Add-ins → COM Add-ins không còn tích MathTypeX):
    tích lại, hoặc chạy lại install.cmd.

GỠ CÀI ĐẶT
  Đóng Word, nhấp đúp uninstall.cmd. Thiết lập và nhật ký được giữ lại ở
  %APPDATA%\MathTypeX và %LOCALAPPDATA%\MathTypeX.

QUYỀN RIÊNG TƯ
  MathTypeX chạy hoàn toàn offline: không gửi nội dung tài liệu, LaTeX, tên tệp hay clipboard đi đâu.

---------------------------------------------------------------------
English (short): extract, close Word, run install.cmd, open Word → "MathTypeX" tab, press Alt+M.
Self-test without Word: editor\MathTypeX.Editor.exe --self-test report.txt. Uninstall: uninstall.cmd.
License: MIT (see LICENSE).
