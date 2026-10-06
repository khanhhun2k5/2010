# 12 — Tiến độ triển khai và cách chạy

## Trạng thái các slice

| Slice | Nội dung | Trạng thái | Kiểm chứng |
|---|---|---|---|
| **VS-1** | Core headless: tokenizer → parser (có error recovery) → AST → normalizer → OMML; LaTeX chuẩn hoá; CLI `mtx`; sinh `.docx` | ✅ Xong | 282 test tự động (Linux CI), OOXML hợp lệ theo OpenXmlValidator, xem thử bằng LibreOffice |
| **VS-2** | Word COM add-in (net48, không VSTO): hook `Alt+M` cục bộ, chụp ngữ cảnh và toạ độ con trỏ; `MathTypeX.Editor.exe` (.NET 10 WPF) nổi dưới con trỏ, kiểm tra cú pháp khi gõ; chèn bằng `InsertXML` trong một mục Undo; gói cài đặt Windows | 🟡 Code xong, **chờ kiểm tra trên Word** | Compile trên Linux; 15 test cho giao thức pipe và composer; kịch bản kiểm tra: [spikes/VS2-word-checklist.md](spikes/VS2-word-checklist.md) |
| **VS-3** | Preview MathML Core trong WebView2 (vẽ cả khi đang lỗi: □ cho ô thiếu, lệnh sai tô đỏ); bộ đọc OpenType tự viết (TTC, `name`, `cmap` 4/12, `MATH`: hằng số và biến thể ∫); quét font có cache; font selector lấy từ font đã cài, tooltip mô tả ∫; `mtx fonts`, `mtx mathml`, `mtx preview` (trang so sánh font) | ✅ Phần lõi đã kiểm chứng trên Chromium headless; 🟡 WebView2 trong editor chờ kiểm tra trên Windows | 43 test MathML (gồm bất biến D9), 17 test font (font tổng hợp + Latin Modern/TeX Gyre/STIX thật); ảnh chụp Chromium |
| **VS-4** | Sửa lại công thức: OMML → LaTeX (mọi Word Equation, kể cả do Word tạo; bỏ qua phần bị xoá khi Track Changes), khoá chuẩn hoá `k1:` (SHA-256 của LaTeX chuẩn hoá, không phụ thuộc cách Word ghi lại XML), kho CustomXMLPart trong tài liệu + kho cục bộ; add-in đọc equation tại con trỏ, mở editor với source cũ, thay tại chỗ trong một mục Undo, khoá tính từ OMML Word thực sự lưu | ✅ Lõi · 🟡 Word chờ kiểm tra | 90 test: round-trip "dựng lại ra đúng OMML" trên corpus × 4 bộ tuỳ chọn, khoá bền trước các biến đổi kiểu Word, chuyển ngược công thức kiểu Word gallery, kho hỏng coi như rỗng |
| **VS-6/7** | Catalog lệnh/mẫu (24 nhóm, tên Việt–Anh, từ khoá, cú pháp cho người mới); gợi ý khi gõ `\fra` (tìm mờ, gập dấu tiếng Việt, ưu tiên lệnh hay dùng — thống kê chỉ lưu Id, cục bộ); snippet `$1`/`${1:gợi ý}`/`$0` với Tab/Shift+Tab, snippet lồng nhau; Tab nhảy tới ô `{}` rỗng; ô rỗng hiện □ trong preview và chặn chèn (Ctrl+Shift+Enter để vẫn chèn); Command Palette Ctrl+Shift+P; dòng trợ giúp Beginner mode (F1) | ✅ Lõi · 🟡 UI chờ kiểm tra trên Windows | 69 test Editing (tìm kiếm, snippet, thứ tự Tab §10, phát hiện tiền tố, trợ giúp ngữ cảnh, ô trống); kịch bản A1–A12 trong checklist |
| VS-8 | Chuyển LaTeX trong vùng chọn (`$…$`, `\(…\)`, `$$…$$`, `\[…\]`) | ⏳ Tiếp theo | |

## Cài thử trên Windows

Tải artifact **MathTypeX-win-x64** của workflow CI (hoặc chạy `tools/package.sh`), giải nén, đóng Word, chạy `install.cmd`. Chi tiết và các bước kiểm tra: [spikes/VS2-word-checklist.md](spikes/VS2-word-checklist.md).

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
