# 11 — Vertical slice đầu tiên

## 16. Đề xuất VS-1: "LaTeX → AST → OMML → Word Equation thật" (chạy headless)

### 16.1 Vì sao chọn slice này trước

| Lý do | Giải thích |
|---|---|
| Đi xuyên các tầng quan trọng nhất | Tokenizer → Parser → AST → Normalizer → OmmlWriter → `.docx`. Đây là "xương sống" mà mọi tính năng khác dựa vào |
| Build và test được trên **mọi** máy CI | Không cần Office, không cần Windows để chạy unit test và golden test |
| Demo được trên Word thật | Mở file `.docx` sinh ra trong Word là thấy ngay equation gốc |
| Kiêm luôn công cụ đo cho spike S2 | Cùng file `.docx` đó là ma trận font × cấu trúc, dùng để dựng Native Capability Matrix và trả lời rủi ro lớn nhất (R1) |
| Không phụ thuộc vào các quyết định chưa chốt | Không đụng tới D2 (mô hình tiến trình), WebView2 hay IME, nên chạy song song được với S1/S3/S4 |

### 16.2 Phạm vi

**Trong phạm vi:**

- **Tokenizer** đầy đủ theo bảng catcode rút gọn ([03](03-math-ast-and-parser.md)), có `SourceSpan`; ký pháp `^^` bị báo Diagnostic.
- **Parser (tập con):**
  - Ký tự: identifier, number, toán tử và quan hệ cơ bản; group `{}`; `^` và `_` (có lỗi double script).
  - Phân số và căn: `\frac`, `\dfrac`, `\tfrac`, `\sqrt[n]{}`.
  - Chữ Hy Lạp (thường, hoa, các dạng `\var…`).
  - Large operator: `\int \iint \iiint \oint \sum \prod`, có giới hạn và `\limits`/`\nolimits`.
  - Spacing: `\, \; \quad \qquad`.
  - Delimiter: `\left … \right` với `( [ \{ | .`.
  - Kiểu chữ: `\mathrm \mathbf \mathbb \mathcal`.
  - Hàm: `\sin \cos \tan \ln \log \exp \lim`.
- **Error recovery:** thiếu `}`, thiếu đối số (sinh Placeholder), `\left` không có `\right`, lệnh không biết (gợi ý bằng fuzzy match). Thông điệp vi/en dựa trên ArgRole.
- **Normalizer:** gộp số, nhận diện hàm, đổi lớp Bin→Ord, **bắt operand của n-ary cùng vi phân** ([03 §6.4](03-math-ast-and-parser.md#64-bắt-phạm-vi-của-large-operator-operand-capture)), chuẩn hoá ký hiệu.
- **AST:** serialize JSON có `AstVersion`; `LatexPrinter` idempotent.
- **OmmlWriter (tập con):**
  - `m:oMath` (inline) và `m:oMathPara` (display).
  - Các cấu trúc: `m:f`, `m:rad`, `m:sSub`/`m:sSup`/`m:sSubSup`, `m:nary` (`m:chr`, `m:limLoc`, `m:grow`), `m:d`, `m:func`, `m:limLow`, `m:r` (`m:sty`, ký tự Unicode math).
  - Một font toán cho cả công thức (`w:rFonts`); tham số dòng lệnh cho phép thử font riêng cho slot ∫ (phục vụ S2).
- **CLI `mtx`:**

  ```
  mtx parse   "<latex>"                    # in AST (JSON) + diagnostics
  mtx latex   "<latex>"                    # in LaTeX chuẩn hoá
  mtx omml    "<latex>" [--display]        # in OMML
  mtx docx    tests/corpus/integrals.tex --fonts "Cambria Math,XITS Math,Latin Modern Math,STIX Two Math"
              --modes inline,display --sizing TeX,Grow --out out/integrals-demo.docx
  ```

  Lệnh `mtx docx` dùng Open XML SDK để sinh tài liệu: mỗi dòng gồm nhãn (font / mode / sizing), equation, và LaTeX gốc. Ngoài ra có một bảng thử trộn font (∫ bằng LM, thân công thức bằng XITS).

**Ngoài phạm vi** (để dành cho các slice sau): UI, add-in Word, preview, IPC, font scanner, typography theo slot đầy đủ, matrix, `cases`, `align`, accent, metadata.

### 16.3 Project được tạo

```
src/MathTypeX.Core   src/MathTypeX.Ast   src/MathTypeX.Parser   src/MathTypeX.Render.Omml   src/MathTypeX.Cli
tests/MathTypeX.Parser.Tests   tests/MathTypeX.Render.Omml.Tests   tests/MathTypeX.Architecture.Tests
tests/corpus/{typography.tex, integrals.tex}   schemas/ooxml/*.xsd
```

`tests/corpus/integrals.tex` gồm đúng các ca ở §51:

```latex
\int x\,dx
\int_0^1 x\,dx
\int_0^1 \frac{x}{1+x^2}\,dx
\int_0^1 \frac{\frac{x^2+1}{x+1}}{\sqrt{1-x^4}}\,dx
```

`tests/corpus/typography.tex` gồm bộ §50: Greek, Operators, Delimiters, Roots. Riêng Matrix, Accent và Font thuộc slice sau, nên được đánh dấu `skip` kèm lý do.

### 16.4 Test

| Loại | Nội dung | Số lượng (ước tính) |
|---|---|---|
| Unit: tokenizer | Mọi loại token, span, comment, control symbol, `^^` | ~40 |
| Unit: parser | Mọi lệnh trong phạm vi, ngữ nghĩa script, lớp atom, các ca recovery (không throw, đúng Diagnostic, đúng vị trí fix-it) | ~120 |
| Unit: normalizer | Bắt operand của ∫/∑ (có `dx`, có `\mathrm{d}x`, ∫ lồng nhau, ∑ gặp `+`, không có vi phân), nhận diện hàm, Bin→Ord | ~40 |
| Property | `LatexPrinter` idempotent; parse không bao giờ throw (FsCheck, 10.000 ca) | 2 |
| Golden OMML | Snapshot bằng Verify cho từng ca trong `integrals.tex` và `typography.tex` × inline/display | ~30 |
| Hợp lệ schema | **Mọi** OMML sinh ra (golden và property) được kiểm bằng XSD ECMA-376 `shared-math.xsd` | toàn bộ |
| Bất biến typography | Không có U+222B…U+2233 nằm trong `m:r` ngoài `m:nary` (∫ không bao giờ là chữ thường) | 1 (chạy trên toàn corpus) |
| Architecture | `Ast` không phụ thuộc gì; `Render.Omml` không tham chiếu Office | 2 |
| Fuzz smoke | SharpFuzz chạy 5 phút trong CI hằng đêm | — |

### 16.5 Kịch bản demo

1. `dotnet test` cho kết quả xanh trên Linux và Windows.
2. Chạy `mtx omml "\int_0^1 \frac{x^2}{1+x^2}\,dx"` để xem OMML có `m:nary` (∫, `subSup`) chứa `m:e`, bên trong có `m:f` và vi phân `dx`.
3. Chạy `mtx parse "\frac{a}{b"` để thấy Diagnostic: *"Bạn đang thiếu dấu } để kết thúc mẫu số."*, kèm span và fix-it.
4. Chạy `mtx docx … --out integrals-demo.docx` rồi mở file trong Word (Windows):
   - Mọi equation là **Word Equation gốc**: click vào sửa được bằng công cụ Equation của Word, chuyển qua lại *Linear*/*Professional* được.
   - Thấy rõ khác biệt giữa các font (Cambria, XITS, LM, STIX), giữa inline và display, giữa `TeX` và `Grow`.
   - Kết quả quan sát được ghi vào báo cáo `docs/spikes/S2-word-math-fonts.md` và file `assets/capabilities/word-<build>.json` đầu tiên.

### 16.6 Definition of Done

- [ ] Build sạch, không có warning, với `netstandard2.0` và `net10.0`.
- [ ] Toàn bộ test ở §16.4 xanh trên CI.
- [ ] `integrals-demo.docx` mở được trong Word M365 mà không báo lỗi "unreadable content".
- [ ] Báo cáo S2 (bản đầu) cộng Capability Matrix v0 đã được commit.
- [ ] ADR D1–D10 đã được ghi vào `docs/adr/`.

### 16.7 Slice tiếp theo: VS-2 "Alt+M trong Word" (tuần 3)

- Add-in VSTO mỏng: hook `Alt+M`, chụp context, lấy vị trí con trỏ.
- `MathTypeX.Editor.exe` tối giản: một ô `TextBox`, `Enter` để chèn, `Esc` để đóng. Giao tiếp qua StreamJsonRpc.
- Chèn bằng `InsertXML` trong `UndoRecord`, rồi trả focus về Word.
- **Demo:** gõ "Ta có", nhấn `Alt+M`, gõ `\int_0^1 \frac{x^2}{1+x^2}\,dx`, nhấn `Enter`, và equation inline xuất hiện ngay sau "Ta có". Nhấn `Ctrl+Z` một lần thì equation biến mất.
- Slice này phụ thuộc kết quả S1 và S3 ở tuần 1.
