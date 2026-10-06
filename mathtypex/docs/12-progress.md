# 12 — Tiến độ triển khai và cách chạy

## Trạng thái các slice

| Slice | Nội dung | Trạng thái | Kiểm chứng |
|---|---|---|---|
| **VS-1** | Core headless: tokenizer → parser (có error recovery) → AST → normalizer → OMML; LaTeX chuẩn hoá; CLI `mtx`; sinh `.docx` | ✅ Xong | 282 test tự động (Linux CI), OOXML hợp lệ theo OpenXmlValidator, xem thử bằng LibreOffice |
| VS-2 | Word add-in: `Alt+M` → editor → chèn equation gốc | ⏳ Tiếp theo | Cần anh/chị chạy trên Windows + Word |

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
