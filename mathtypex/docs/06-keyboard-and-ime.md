# 06 — Phím tắt và bộ gõ tiếng Việt (IME)

## 11. Cách xử lý keyboard shortcut và Vietnamese IME

### 11.1 Hai lớp phím tắt

| Lớp | Phạm vi | Cơ chế | Ví dụ |
|---|---|---|---|
| **L1: trong cửa sổ Office** | Khi con trỏ đang ở trong tài liệu Word/PowerPoint | Hook bàn phím **cục bộ theo thread UI của Office**: `SetWindowsHookEx(WH_KEYBOARD, proc, IntPtr.Zero, GetCurrentThreadId())`. **Không** dùng hook toàn hệ thống | `Alt+M`, `Ctrl+Shift+M`, `Ctrl+Alt+M` |
| **L2: trong editor** | Khi editor đang có focus | `InputBindings` của WPF, keymap JSON | `Ctrl+Enter`, `Tab`, `Ctrl+Space`, `Ctrl+Shift+P`, `Alt+↑/↓` … |

Phím ở L2 **không thể xung đột với Word**, vì lúc đó Word không có focus. Chỉ các phím ở L1 cần cơ chế phát hiện xung đột.

### 11.2 Chi tiết hook ở L1

1. Delegate của hook được giữ trong một trường static, để GC không thu hồi nó.
2. Chỉ xử lý khi:
   - đây là sự kiện nhấn phím (bit 31 của `lParam` bằng 0);
   - tổ hợp modifier khớp (`GetKeyState` cho Ctrl/Shift; bit 29 cho Alt);
   - **cửa sổ đang có focus là vùng soạn tài liệu**: class `_WwG` với Word, `paneClassDC` với PowerPoint (⚠ xác minh bằng Spy++ trong S3);
   - Office không ở trạng thái modal (không có hộp thoại nào đang mở).
3. Khi khớp: trả về 1 để nuốt phím, **post** công việc sang hàng đợi thực thi bất đồng bộ, và **không bao giờ làm việc nặng bên trong hook**.
4. **Vấn đề KeyTips khi nhả Alt:** khi `Alt+M` bị nuốt, Office có thể vẫn thấy cặp "Alt xuống / Alt lên" mà không có phím nào ở giữa, và hiện KeyTips ra.
   - Cách xử lý: gửi một "menu-mask key" chưa gán (`VK 0xE8`) trước khi Alt được nhả. Đây là kỹ thuật AutoHotkey dùng với `MenuMaskKey`.
   - Thêm vào đó, focus đã chuyển sang editor nên sự kiện Alt-up thường đi tới editor, không tới Word. S3 kiểm chứng cả hai.
5. Gỡ hook khi add-in tắt, và tự kiểm tra sức khoẻ của hook theo chu kỳ.

**Đã cân nhắc và loại bỏ** cách gán phím qua `Application.KeyBindings` với một macro VBA đặt trong template `.dotm`. Cách này cần macro, nên vướng chính sách bảo mật macro, và nhiều tổ chức đã tắt hẳn VBA. Tuy vậy `Application.FindKey` vẫn **được dùng để phát hiện xung đột**.

### 11.3 Xung đột của các phím mặc định đề xuất

⚠ Các xung đột dưới đây đúng với Word/PowerPoint giao diện tiếng Anh. Với giao diện tiếng Việt và các phiên bản khác, cần xác minh trong S3.

| Phím | Word (mặc định) | PowerPoint (mặc định) | Đề xuất |
|---|---|---|---|
| `Alt+M` | **KeyTip mở tab Mailings** | ⚠ cần xác minh | Vẫn dùng làm mặc định (đúng yêu cầu), nhưng ở lần chạy đầu hiển thị: *"Alt+M đang mở tab Mailings. [Giữ Alt+M cho MathTypeX] [Chọn phím khác]"*. Tab Mailings vẫn mở được qua `Alt`, rồi `M` gõ tách rời ⚠ |
| `Ctrl+Shift+M` | Bỏ thụt lề trái (Remove indent) | ⚠ | Dùng mặc định kèm cảnh báo, hoặc dùng phím thay thế |
| `Ctrl+Alt+M` | **Chèn comment** | **Chèn comment** ⚠ | Không nên ghi đè; đề xuất phím khác cho thao tác inline/display |
| `Alt+=` | Chèn equation của Word | Chèn equation | **Không đụng tới** |
| `Ctrl+Shift+P`, `Ctrl+Space`, `Ctrl+Enter` | Có chức năng trong Word | — | Chỉ dùng ở L2 (trong editor), nên không xung đột |

**Bộ phát hiện xung đột (Keyboard settings):**

1. Với Word: gọi `Application.FindKey(BuildKeyCode(...)).Command` để lấy tên lệnh đang gán cho phím (bao gồm cả các tuỳ biến của người dùng).
2. Với KeyTips của ribbon: dùng bảng tĩnh theo phiên bản và ngôn ngữ giao diện. Thêm thử nghiệm đọc thuộc tính `AccessKey` của các tab ribbon qua **UI Automation** ⚠.
3. Với PowerPoint: dùng bảng tĩnh.
4. Với phím dành riêng của hệ điều hành và phím chuyển chế độ của bộ gõ (UniKey/EVKey thường dùng `Ctrl+Shift` hoặc `Alt+Z`): cảnh báo.
5. Khi người dùng muốn đổi phím, app đề xuất **phím trống đầu tiên** trong danh sách ứng viên, đã được kiểm bằng `FindKey`.

### 11.4 Mở editor đúng chỗ và trả focus về Office

1. Add-in gọi `AllowSetForegroundWindow(editorPid)` (lúc này Word đang ở foreground nên có quyền cấp), rồi gửi RPC `OpenEditor(ctx)`.
2. Editor là một *tool window* top-level (`WS_EX_TOOLWINDOW`, không hiện trên taskbar).
   - Mặc định đặt **ngay dưới hình chữ nhật của con trỏ**; lật lên trên nếu sát mép dưới màn hình; luôn nằm trong vùng làm việc của màn hình chứa con trỏ.
   - **Không** đặt quan hệ owner xuyên tiến trình, để tránh hàng đợi input của hai tiến trình bị gắn vào nhau (một bên treo thì bên kia cũng treo). Thay vào đó, editor theo dõi cửa sổ Word bằng `SetWinEventHook` (out-of-context) với các sự kiện vị trí và kích hoạt.
3. Đóng editor (`Esc`, hoặc chèn xong): gọi `SetForegroundWindow(wordHwnd)`. Selection của Word vẫn còn nguyên.
4. Mặc định editor tự ẩn khi người dùng chuyển sang ứng dụng khác (có tuỳ chọn ghim lại). Nội dung đang soạn được giữ làm bản nháp.

### 11.5 Ý nghĩa của phím Enter

| Phím | Hành vi |
|---|---|
| `Enter` | Chèn equation, nếu popup autocomplete đang đóng **và** con trỏ không ở trong một environment nhiều dòng |
| `Enter` trong `align`/`matrix`… | Xuống dòng (`\\` + newline) |
| `Ctrl+Enter` | **Luôn** chèn, đúng như §11 |
| `Shift+Enter` | Luôn xuống dòng trong source |

Cách này thoả mãn cả §11 (`Ctrl+Enter`) lẫn chuỗi acceptance ở §54 (nhấn `Enter` để chèn).

### 11.6 Trạng thái `VI | EN | MATH`

- Trạng thái thuộc **editor**, hiển thị ở thanh trạng thái của editor (`VI | EN | MATH`).
- App **không đổi layout bàn phím của hệ điều hành** và **không tự bật/tắt bộ gõ**.

| Trạng thái | Khi nào | Bộ gõ tiếng Việt |
|---|---|---|
| **MATH** | Mặc định; con trỏ nằm ngoài `\text{}` | Bị chặn (§11.7) |
| **VI** | Con trỏ nằm trong `\text{…}` (tự nhận ra từ AST) và đang chọn VI | Được phép hoạt động: gõ "với mọi", "khi và chỉ khi" bình thường |
| **EN** | Con trỏ nằm trong `\text{…}` và đang chọn EN | Bị chặn giống MATH |

- Phím chuyển (cấu hình được, chỉ có hiệu lực trong editor): ví dụ `Ctrl+T` để bọc hoặc bỏ bọc `\text{}` và vào/ra chế độ text; `Ctrl+Shift+L` để đổi giữa VI và EN.

### 11.7 Bộ gõ tiếng Việt: phân loại và giải pháp

**Ba loại bộ gõ trên Windows:**

| Loại | Ví dụ | Cách hoạt động |
|---|---|---|
| (a) Bộ gõ dạng **hook** | UniKey, EVKey, OpenKey | Cài `WH_KEYBOARD_LL` toàn hệ thống; nuốt phím thật, rồi gửi `SendInput` (Backspace + ký tự Unicode) |
| (b) Bộ gõ **TSF** | Bàn phím "Vietnamese Telex" / "Vietnamese Number Key-based" có sẵn trong Windows 10/11 ⚠ tên chính xác | Đi qua Text Services Framework |
| (c) **Layout** Vietnamese (`0000042A`) | — | Không phải bộ gõ: hàng phím số sinh ra chữ có dấu, không phải chữ số |

**Ví dụ lỗi thật với Telex** (s = sắc, f = huyền, r = hỏi, x = ngã, j = nặng):

| Gõ | Có thể thành | Lý do |
|---|---|---|
| `\cos` | `\có` | `s` là dấu sắc |
| `\infty` | `\ìnty` | `f` là dấu huyền |
| `\dots` | `\dót` | |
| `\left` | `\lèt` | |

Mức độ còn tuỳ tuỳ chọn *kiểm tra chính tả* / *khôi phục phím với từ sai* của từng bộ gõ, nên S6 sẽ đo.

**Giải pháp nhiều lớp:**

| Lớp | Bật mặc định? | Cách làm | Đối phó loại |
|---|---|---|---|
| **L0: Tắt TSF cho ô Math** | Có | `InputMethod.IsInputMethodEnabled = false` trên ô soạn khi ở MATH/EN; bật lại khi ở VI. Chỉ tác động lên editor, không đụng tới hệ điều hành | (b) |
| **L0': Đọc phím theo layout US** | Có, khi layout hiện hành là Vietnamese | Ở MATH, editor dịch phím vật lý bằng `ToUnicodeEx` với HKL en-US (cờ không làm thay đổi trạng thái dead-key). Layout của hệ điều hành giữ nguyên | (c) |
| **L1: Sửa lỗi sau khi gõ** | Có | Tokenizer phát hiện lệnh chứa chữ có dấu (`\có`). Từ đó sinh mọi "chuỗi gõ gốc" có thể có theo Telex, VNI và VIQR, rồi so với từ điển lệnh. Nếu chỉ có một lệnh khớp thì tự sửa (có thể Undo) hoặc hiện quick-fix "`\có` → `\cos`" | (a), (b), không xâm lấn |
| **L2: Math Input Shield** | Tự bật khi phát hiện (a) đang chạy; tắt được | Chỉ khi **editor đang có focus và ở MATH/EN**, `MathTypeX.Editor.exe` cài một `WH_KEYBOARD_LL`. Hook cài sau nên nằm **đầu chuỗi hook**, tức là được gọi trước hook của bộ gõ. Nó nuốt các **phím vật lý in ra ký tự** (sự kiện có cờ `LLKHF_INJECTED` thì bỏ qua) và đưa thẳng ký tự vào bộ đệm của editor. Phím điều hướng và tổ hợp Ctrl/Alt đi qua bình thường. Gỡ hook ngay khi editor mất focus hoặc đóng | (a) |
| **L3: Hướng dẫn loại trừ** | Lần chạy đầu | Phát hiện tiến trình UniKey/EVKey/OpenKey, rồi hướng dẫn thêm `MathTypeX.Editor.exe` vào danh sách loại trừ hoặc "tự chuyển chế độ theo ứng dụng" của bộ gõ đó (⚠ tính năng có hay không tuỳ bộ gõ và phiên bản). Làm được điều này **chính là nhờ editor là một tiến trình riêng** | (a) |
| Không làm | — | Đổi layout của hệ điều hành; giả lập phím tắt bật/tắt bộ gõ (chỉ để dạng *experimental*, mặc định tắt) | — |

**Các ràng buộc an toàn của L2:**

- Callback của hook phải trả về dưới 1 ms. Hook chạy trên một thread riêng có message loop riêng, vì Windows âm thầm gỡ hook nào vượt `LowLevelHooksTimeout`.
- **Watchdog:** nếu sau khi editor đã nuốt phím mà vẫn thấy chuỗi Backspace + `VK_PACKET` do tiến trình khác gửi vào, tức là bộ gõ đã cài lại hook và đứng trước, thì editor cài lại hook của mình và hiện một mẹo nhỏ cho người dùng.
- **Accessibility:** không bao giờ chặn input có cờ injected, để On-Screen Keyboard và phần mềm hỗ trợ tiếp cận vẫn hoạt động.
- **Phần mềm diệt virus / EDR:** hook LL là dấu hiệu thường gặp của keylogger. Vì vậy hook chỉ được cài trong EXE **có ký số**, chỉ trong lúc editor có focus, được ghi rõ trong tài liệu, và có thể tắt bằng policy (registry HKLM) cho môi trường doanh nghiệp.

### 11.8 Ma trận test cho S6

| Chiều | Giá trị |
|---|---|
| Bộ gõ | UniKey 4.x (Telex, VNI; Unicode dựng sẵn và Unicode tổ hợp), EVKey, OpenKey, Windows Vietnamese Telex, layout Vietnamese |
| Trạng thái editor | MATH, VI, EN |
| Ca gõ | `\cos`, `\infty`, `\dots`, `\left(`, `\exists`, `\notin`, `\sqrt`, `\frac`, `\text{với mọi}`, các chữ số `0–9` |
| Tiêu chí đạt | Ở MATH, source nhận được đúng 100% ký tự vật lý đã gõ. Ở VI, chữ tiếng Việt đúng chuẩn Unicode NFC |
