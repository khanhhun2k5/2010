# 12 — Tiến độ triển khai và cách chạy

## Trạng thái các slice

| Slice | Nội dung | Trạng thái | Kiểm chứng |
|---|---|---|---|
| **VS-1** | Core headless: tokenizer → parser (có error recovery) → AST → normalizer → OMML; LaTeX chuẩn hoá; CLI `mtx`; sinh `.docx` | ✅ Xong | 282 test tự động (Linux CI), OOXML hợp lệ theo OpenXmlValidator, xem thử bằng LibreOffice |
| **VS-2** | Word COM add-in (net48, không VSTO): hook `Alt+M` cục bộ, chụp ngữ cảnh và toạ độ con trỏ; `MathTypeX.Editor.exe` (.NET 10 WPF) nổi dưới con trỏ, kiểm tra cú pháp khi gõ; chèn bằng `InsertXML` trong một mục Undo; gói cài đặt Windows | 🟡 Code xong, **chờ kiểm tra trên Word** | Compile trên Linux; 15 test cho giao thức pipe và composer; kịch bản kiểm tra: [spikes/VS2-word-checklist.md](spikes/VS2-word-checklist.md) |
| **VS-3** | Preview MathML Core trong WebView2 (vẽ cả khi đang lỗi: □ cho ô thiếu, lệnh sai tô đỏ); bộ đọc OpenType tự viết (TTC, `name`, `cmap` 4/12, `MATH`: hằng số và biến thể ∫); quét font có cache; font selector lấy từ font đã cài, tooltip mô tả ∫; `mtx fonts`, `mtx mathml`, `mtx preview` (trang so sánh font) | ✅ Phần lõi đã kiểm chứng trên Chromium headless; 🟡 WebView2 trong editor chờ kiểm tra trên Windows | 43 test MathML (gồm bất biến D9), 17 test font (font tổng hợp + Latin Modern/TeX Gyre/STIX thật); ảnh chụp Chromium |
| **VS-4** | Sửa lại công thức: OMML → LaTeX (mọi Word Equation, kể cả do Word tạo; bỏ qua phần bị xoá khi Track Changes), khoá chuẩn hoá `k1:` (SHA-256 của LaTeX chuẩn hoá, không phụ thuộc cách Word ghi lại XML), kho CustomXMLPart trong tài liệu + kho cục bộ; add-in đọc equation tại con trỏ, mở editor với source cũ, thay tại chỗ trong một mục Undo, khoá tính từ OMML Word thực sự lưu | ✅ Lõi · 🟡 Word chờ kiểm tra | 90 test: round-trip "dựng lại ra đúng OMML" trên corpus × 4 bộ tuỳ chọn, khoá bền trước các biến đổi kiểu Word, chuyển ngược công thức kiểu Word gallery, kho hỏng coi như rỗng |
| **VS-6/7** | Catalog lệnh/mẫu (24 nhóm, tên Việt–Anh, từ khoá, cú pháp cho người mới); gợi ý khi gõ `\fra` (tìm mờ, gập dấu tiếng Việt, ưu tiên lệnh hay dùng — thống kê chỉ lưu Id, cục bộ); snippet `$1`/`${1:gợi ý}`/`$0` với Tab/Shift+Tab, snippet lồng nhau; Tab nhảy tới ô `{}` rỗng; ô rỗng hiện □ trong preview và chặn chèn (Ctrl+Shift+Enter để vẫn chèn); Command Palette Ctrl+Shift+P; dòng trợ giúp Beginner mode (F1) | ✅ Lõi · 🟡 UI chờ kiểm tra trên Windows | 69 test Editing (tìm kiếm, snippet, thứ tự Tab §10, phát hiện tiền tố, trợ giúp ngữ cảnh, ô trống); kịch bản A1–A12 trong checklist |
| **VS-8** | Chuyển LaTeX trong vùng chọn: thư viện `MathTypeX.Scanner` (máy trạng thái, quy tắc `$` kiểu Pandoc, `\$`, field/ô bảng/ảnh của Word là ranh giới, chấm điểm độ tin cậy kèm lý do); planner tách đoạn cho display, đưa dấu câu đứng sau `$$…$$` vào công thức, bỏ `\label`/`\tag`, sửa ký tự AutoCorrect của Word (’ – NBSP); add-in: nút **Chuyển LaTeX** (không chọn gì thì lấy đoạn chứa con trỏ), một mục Undo, bỏ qua vùng code / chữ ẩn / style "MTX Ignore" / equation có sẵn, kiểm tra lại văn bản trước khi thay, lưu metadata kèm văn bản gốc (cho Revert ở VS-9); `Alt+M` trên đoạn chữ đang chọn mở editor với đoạn đó; thiết lập dùng chung editor ↔ add-in; `mtx scan`, `mtx convert` | ✅ Lõi · 🟡 Word chờ kiểm tra | 253 test Scanner (gồm 136 tổ hợp công thức × delimiter × 9 ngữ cảnh, fuzz 3.000 chuỗi, 10.000 công thức < 5 s), 38 test planner/thiết lập; `mtx convert tests/corpus/convert-sample.txt` → .docx hợp lệ; kịch bản C1–C10 |
| **VS-9** | Chuyển cả tài liệu: quét thân bài, chú thích, header/footer (bỏ trùng khi liên kết section), hộp văn bản; hộp duyệt trong editor (tích sẵn mục chắc chắn, lý do cho mục chưa chắc/bị khoá, ngữ cảnh, xem trước, lỗi cú pháp; Space/Del/Ctrl+A/Enter/Esc); "luôn bỏ qua" vào ignore list; không mở được editor thì hỏi bằng MessageBox; tài liệu bị sửa trong lúc duyệt thì ánh xạ lại mục đã chọn; một mục Undo, tiến độ trên thanh trạng thái, giữ Esc để dừng; **Trả về LaTeX** (đúng văn bản gốc nếu công thức do Convert tạo); phát hiện dấu hết đoạn thừa của `InsertXML` bằng so ký tự lân cận thay vì đếm đoạn | ✅ Lõi · 🟡 Word chờ kiểm tra | 46 test planner/thiết lập (lọc trước khi tách đoạn, ngữ cảnh, ánh xạ lại sau khi sửa, ignore list), test giao thức `review`, test Flat OPC chữ thường; kịch bản D1–D9 |
| VS-10 | Settings, keymap, IME | ⏳ Tiếp theo | |

## Cài thử trên Windows

1. Mở tab **Actions** của repo → workflow **MathTypeX** → lần chạy mới nhất có dấu ✅ → mục *Artifacts* → tải **MathTypeX-win-x64** (một tệp .zip).
2. Giải nén ra một thư mục cố định, **đóng Word**, nhấp đúp `install.cmd` (không cần quyền admin). Đọc `README.txt` trong gói nếu cần.
3. Mở Word → tab **MathTypeX** → `Alt+M`. Kịch bản kiểm tra đầy đủ: [spikes/VS2-word-checklist.md](spikes/VS2-word-checklist.md).
4. Kiểm tra nhanh không cần Word: `editor\MathTypeX.Editor.exe --self-test "%TEMP%\mtx.txt"` → báo cáo kết thúc bằng `RESULT PASS`.

Tự đóng gói: `pwsh tools/package.ps1 -Zip` (Windows, giống CI) hoặc `tools/package.sh` (Linux/WSL) → `out/package/` và `out/MathTypeX-win-x64.zip`.

### CI (`.github/workflows/mathtypex.yml`)

| Job | Runner | Việc |
|---|---|---|
| `test-linux` | ubuntu-latest | restore → build Release → toàn bộ test → sinh tài liệu demo (`docx`, `preview`, `convert`; kiểm tra tệp khác rỗng) → artifact `mathtypex-demo-docx` |
| `build-windows` | windows-latest | restore → build Release (add-in net48, editor WPF) → toàn bộ test trên Windows |
| `package-windows` | windows-latest, sau hai job trên | `tools/package.ps1` (kiểm tra theo `tools/package-manifest.txt`) → `tools/smoke-test.ps1` trên Windows PowerShell 5.1/.NET Framework 4.8: nạp mọi DLL của add-in, đối chiếu callback ribbon, chạy lõi trên .NET Framework, `MathTypeX.Editor.exe --self-test`, `install.ps1` → kiểm tra dữ liệu đăng ký HKCU đúng như mscoree đọc (64/32-bit) → `uninstall.ps1` gỡ sạch → vì runner chạy quyền cao (COM bỏ qua HKCU), cài thêm `install.ps1 -AllUsers` và tạo add-in qua COM thật ở tiến trình 64-bit và 32-bit như Word → gỡ sạch → artifact **MathTypeX-win-x64** |

## Build và test

Yêu cầu: .NET SDK 10.0.

```bash
cd mathtypex
dotnet build MathTypeX.slnx
dotnet test --solution MathTypeX.slnx
```

Golden snapshot OMML nằm ở `tests/golden/omml/`. Khi thay đổi OMML có chủ đích, chạy lại với `MTX_UPDATE_GOLDEN=1`, rồi xem diff trước khi commit.

## Dùng thử CLI `mtx`

```bash
cd mathtypex
dotnet run --project src/MathTypeX.Cli -- omml  "\int_0^1 \frac{x^2}{1+x^2}\,dx"
dotnet run --project src/MathTypeX.Cli -- parse "\frac{a}{b"      # → "Bạn đang thiếu dấu } để kết thúc mẫu số."
dotnet run --project src/MathTypeX.Cli -- latex "\leq \rightarrow \frac12"
dotnet run --project src/MathTypeX.Cli -- docx tests/corpus/integrals.tex --out out/integrals-demo.docx
dotnet run --project src/MathTypeX.Cli -- fonts                    # font OpenType MATH đã cài + đặc điểm ∫
dotnet run --project src/MathTypeX.Cli -- scan 'Giá $20 và $30, còn $x^2$ thì là toán'   # ứng viên + độ tin cậy
dotnet run --project src/MathTypeX.Cli -- convert tests/corpus/convert-sample.txt --out out/convert-demo.docx
dotnet run --project src/MathTypeX.Cli -- preview tests/corpus/integrals.tex --display --out out/integrals.html \
    --fonts "Cambria Math,XITS Math,Latin Modern Math,STIX Two Math"  # mở bằng Edge/Chrome: so sánh font
```

Mở `out/integrals-demo.docx` bằng Word. Mọi công thức trong đó là Word Equation gốc: click vào là sửa được bằng công cụ Equation của Word.

## Những gì VS-1 đã làm được

- **Parser không bao giờ throw.** Đã fuzz 5.000 input ngẫu nhiên và thử lồng ngoặc 50.000 tầng. Lỗi được báo bằng thông điệp tiếng Việt theo vai trò của đối số, kèm gợi ý sửa.
- **∫ luôn là cấu trúc `m:nary`.** Phần thân được bắt tới vi phân `dx`; ∬ cần hai vi phân; ∫∫ lồng nhau đúng. Có bất biến D9 trên toàn corpus: không có ký hiệu ∫ ∑ ∏ nào nằm trong `m:t`.
- **Chữ Hy Lạp đúng như TeX:** `\epsilon` là ϵ, `\varepsilon` là ε, `\phi` là ϕ, `\varphi` là φ.
- `\mathbb`, `\mathcal`, `\mathfrak`, `\mathbf`, `\mathrm{d}`; hàm (`\sin`, `\lim`, `\operatorname`); `\left…\middle…\right`; ma trận, `cases`, `aligned`, `array`; `\text{tiếng Việt}` (chuẩn hoá NFC); dấu phẩy thập phân (`3{,}14` hoặc tuỳ chọn).
- **Tuỳ chọn typography cho OMML:** font toán, font riêng cho ∫, cỡ ∫ `TeX`/`Grow`, `d` đứng hoặc nghiêng, cỡ chữ, cách biểu diễn `aligned` (`matrix` hoặc `eqarr`).

## Những gì chưa kiểm chứng được (cần Word thật)

- Font và cỡ ∫ hiển thị trong Word ra sao, và `m:grow` có kéo dãn ∫ không → spike S2: [docs/spikes/S2-word-math-fonts.md](spikes/S2-word-math-fonts.md).
- `aligned` nên dùng ma trận hay `m:eqArr` + `&` → S1.
- **LibreOffice không phải Word.** Bộ nhập OMML của LibreOffice hiện "¿" ở ô trống của `aligned`, và hiểu `|` thành phép "or" (∨). Đây là lỗi phía LibreOffice; dùng LibreOffice chỉ để bắt lỗi cấu trúc thô.
