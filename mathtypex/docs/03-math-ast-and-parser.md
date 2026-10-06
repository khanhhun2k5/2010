# 03 — Kiến trúc Math AST (kèm thiết kế parser)

## 6. Kiến trúc Math AST

### 6.1 Nguyên tắc

1. **Thiên về trình bày, có kèm gợi ý ngữ nghĩa.** Cách tiếp cận giống MathML Presentation, với các gợi ý như "đây là vi phân `d`" hay "đây là integrand". Lý do: cả OMML lẫn TeX đều là mô hình trình bày; còn ngữ nghĩa thì chỉ cần ở mức đủ để dựng đúng (ví dụ phạm vi của ∫).
2. **Bất biến (immutable)**, mỗi node có `NodeId` ổn định và `SourceSpan` (vị trí trong source gốc). Span phục vụ ba việc: tô lỗi, đồng bộ con trỏ giữa source và preview, và source map cho log TeX.
3. **Không chứa font.** AST chỉ giữ `MathVariant` theo ngữ nghĩa (bold, script, double-struck…). Việc chọn font cụ thể thuộc `TypographyResolver`, cho ra `StyleMap: NodeId → ResolvedStyle`.
4. **Độc lập với renderer.** Không có trường nào riêng cho OMML hay TeX. Renderer là các visitor.
5. **Có version** (`AstVersion`), serialize được sang JSON, và **round-trip được** qua LaTeX chuẩn hoá: `parse(print(ast)) ≅ ast`.
6. **Parser không bao giờ throw.** Mọi lỗi đều trở thành `Diagnostic` cộng node `Error`/`Placeholder`.

### 6.2 Phân lớp node (phác thảo C#, chưa phải code cuối cùng)

```csharp
public abstract record MathNode { public NodeId Id { get; init; } public SourceSpan Span { get; init; } }

// ── Khung ───────────────────────────────────────────────────────────────
record MathDocument(MathNode Body, DisplayMode Mode, ImmutableArray<Diagnostic> Diagnostics, int AstVersion);
record Row(ImmutableArray<MathNode> Children) : MathNode;
record Group(Row Content) : MathNode;                         // {…} tường minh ⇒ atom Ord (ảnh hưởng spacing)

// ── Atom ───────────────────────────────────────────────────────────────
record Identifier(string Text, MathVariant Variant, IdentifierRole Role) : MathNode;
        // Role: Variable | Constant(e,i,π) | Differential(d) | FunctionName | Unit
record Number(string Text) : MathNode;                        // "3,14" khi bật dấu phẩy thập phân
record Operator(string Text, AtomClass Class, OperatorFlags Flags) : MathNode;
        // Class: Ord|Op|Bin|Rel|Open|Close|Punct|Inner ; Flags: Stretchy|LargeOp|Symmetric|Accent|Fence
record TextRun(string Text, TextStyle Style) : MathNode;      // \text{với mọi}: có dấu tiếng Việt
record Space(SpaceKind Kind, Length Width) : MathNode;        // \, \: \; \quad \qquad \! \hspace{}

// ── Cấu trúc ───────────────────────────────────────────────────────────
record Fraction(MathNode Num, MathNode Den, FractionKind Kind, StyleOverride Style, Length? Rule) : MathNode;
        // Kind: Bar|NoBar(\binom)|Linear|Skewed ; Style: Auto|Display(\dfrac)|Text(\tfrac)
record Radical(MathNode Radicand, MathNode? Index) : MathNode;
record Scripts(MathNode Base, MathNode? Sub, MathNode? Sup, MathNode? PreSub, MathNode? PreSup) : MathNode;
record UnderOver(MathNode Base, MathNode? Under, MathNode? Over, bool AccentUnder, bool AccentOver) : MathNode;
record LargeOperator(string Symbol, NaryKind Kind, MathNode? Lower, MathNode? Upper,
                     LimitPlacement Limits, NarySizing Sizing, MathNode? Operand) : MathNode;
        // Kind: Integral|DoubleIntegral|TripleIntegral|ContourIntegral|Sum|Product|Coproduct|BigCup|…
        // Limits: Auto|Limits|NoLimits ; Sizing: TexStyle|Grow|VariantStep(n)|Scale(k)
record FunctionApply(MathNode Name, MathNode? Argument, LimitPlacement Limits) : MathNode; // \sin x, \lim_{x\to0}
record Fenced(Delimiter Open, Delimiter Close, ImmutableArray<Delimiter> Middles,
              MathNode Content, DelimiterSizing Sizing) : MathNode;
        // Sizing: Auto(\left\right) | Fixed(Big1..4: \big \Big \bigg \Bigg) | Natural
record Accent(MathNode Base, string AccentChar, bool Stretchy) : MathNode;    // \hat \bar \vec \widehat
record Bar(MathNode Base, VerticalPosition Position) : MathNode;              // \overline \underline
record GroupChar(MathNode Base, string Char, VerticalPosition Position, MathNode? Label) : MathNode;
        // \overbrace{}^{…} \underbrace{}_{…} \xrightarrow{…} \overrightarrow
record Table(TableKind Kind, ImmutableArray<TableRow> Rows, ColumnSpec Columns,
             Delimiter? Open, Delimiter? Close) : MathNode;
        // Kind: Matrix|PMatrix|BMatrix|VMatrix|VVMatrix|BBMatrix|SmallMatrix|Array|Cases|DCases|RCases|Substack
record Alignment(AlignmentKind Kind, ImmutableArray<AlignedRow> Rows) : MathNode;
        // Kind: Align|AlignStar|Aligned|Gather|GatherStar|Gathered|Split|Multline|Equation|EquationStar
record Phantom(MathNode Content, PhantomKind Kind) : MathNode;               // \phantom \hphantom \vphantom \smash
record Boxed(MathNode Content, BoxKind Kind) : MathNode;                     // \boxed \cancel \bcancel \xcancel
record StyleChange(MathStyle Style, MathNode Content) : MathNode;            // \displaystyle … (phạm vi tới cuối group)
record Colored(ColorSpec Color, MathNode Content) : MathNode;

// ── Soạn thảo & lỗi ────────────────────────────────────────────────────
record Placeholder(int TabIndex, string? Hint) : MathNode;                   // □ trong preview
record ErrorNode(string RawSource, DiagnosticCode Code) : MathNode;
record UnknownCommand(string Name, ImmutableArray<MathNode> Args) : MathNode;

// ── Thuộc tính của dòng ────────────────────────────────────────────────
record AlignedRow(ImmutableArray<MathNode> Cells, RowNumbering Numbering, string? Tag, string? Label);
record TableRow(ImmutableArray<MathNode> Cells, Length? ExtraSpace);
```

**`MathVariant`** gồm: `Normal`, `Italic`, `Bold`, `BoldItalic`, `DoubleStruck`, `Script`, `BoldScript`, `Calligraphic`, `Fraktur`, `BoldFraktur`, `SansSerif` (×4 biến thể), `Monospace`.

`Calligraphic` và `Script` được giữ là **hai giá trị riêng** dù dùng chung code point Unicode, vì chúng có thể được gán hai font khác nhau ([04](04-fonts-and-integrals.md)).

### 6.3 Bảng ánh xạ node → các định dạng đích

| Node | LaTeX nguồn | OMML (Native) | MathML Core (preview) | TeX (chuẩn hoá) |
|---|---|---|---|---|
| `Identifier` | `x`, `\alpha`, `\mathbb{R}` | `m:r` (`m:sty`; ký tự Unicode math) | `<mi>` | `x`, `\alpha`, `\mathbb{R}` |
| `Number` | `3.14` | `m:r` (`m:sty p`) | `<mn>` | `3.14` (hoặc `3{,}14`) |
| `Operator` | `+ = \le \to` | `m:r` | `<mo>` | `+ = \le \to` |
| `TextRun` | `\text{với mọi}` | `m:r` + `m:nor` + `w:rFonts` (font văn bản) | `<mtext>` | `\text{với mọi}` |
| `Fraction` | `\frac \dfrac \tfrac \binom` | `m:f` (`m:type`); `\binom` dùng `m:d` + `m:f noBar` | `<mfrac>` | `\frac{…}{…}` |
| `Radical` | `\sqrt[n]{…}` | `m:rad` (`m:degHide`) | `<msqrt>` / `<mroot>` | `\sqrt[n]{…}` |
| `Scripts` | `x^2_i`, `{}^{14}_6C` | `m:sSub` / `m:sSup` / `m:sSubSup` / `m:sPre` | `<msub>` / `<msup>` / `<msubsup>` / `<mmultiscripts>` | `x_{i}^{2}` |
| `LargeOperator` | `\int_0^1 … dx`, `\sum`, `\oint` | `m:nary` (`m:chr`, `m:limLoc`, `m:grow`) với `m:e` = Operand | `<munderover>` / `<msubsup>` + `<mo largeop>` | `\int_{0}^{1}` |
| `FunctionApply` | `\sin x`, `\lim_{x\to 0}` | `m:func`; `\lim` dùng `m:fName` + `m:limLow` | `<mi>sin</mi><mo>⁡</mo>…` | `\sin x` |
| `Fenced` | `\left( … \right)`, `\bigl[` | `m:d` (`begChr` / `endChr` / `sepChr`, `m:grow`) | `<mrow><mo stretchy>` | `\left( … \right)` |
| `Accent` | `\hat \bar \vec \tilde \dot \ddot \widehat` | `m:acc` (`m:chr` U+0302 / U+0304 / U+20D7 …) | `<mover accent>` | `\hat{…}` |
| `Bar` | `\overline{AB}` | `m:bar` (`m:pos`) | `<mover><mo>‾</mo>` | `\overline{…}` |
| `GroupChar` | `\overbrace`, `\underbrace`, `\xrightarrow` | `m:groupChr` (+ `m:limUpp` / `m:limLow` cho nhãn) | `<mover>` / `<munder>` | như nguồn |
| `Table` | `matrix`, `pmatrix`, `cases`… | `m:m` (`m:mcs`), bọc trong `m:d` nếu có ngoặc; `cases` dùng `m:d` + `m:eqArr` | `<mtable>` | `\begin{pmatrix}…` |
| `Alignment` | `align*`, `aligned`, `gather` | `m:eqArr` (⚠ cách biểu diễn `&`: S1) | `<mtable>` có căn cột | `\begin{aligned}…` |
| `Phantom` | `\phantom`, `\vphantom` | `m:phant` (`m:show`, `m:zeroWid`…) | `<mphantom>` / `<mpadded>` | như nguồn |
| `Boxed` | `\boxed`, `\cancel` | `m:borderBox` (`m:strikeBLTR`…) | `<menclose>`* / CSS | như nguồn |
| `Space` | `\, \: \; \quad` | U+2009 / U+205F / U+2004 / U+2003 trong `m:t` (xấp xỉ) | `<mspace width>` | như nguồn |
| `Placeholder` | `□` (do snippet sinh) | **Không cho chèn**: hỏi xoá placeholder hay điền nội dung | ô viền chấm | `\square` (chỉ trong preview) |

\* `menclose` không thuộc MathML Core; preview dùng CSS thay thế.

### 6.4 Bắt phạm vi của large operator (operand capture)

LaTeX không có khái niệm "integrand": trong `\int_0^1 f(x)\,dx`, phần thân của ∫ không bị giới hạn bởi cặp ngoặc nào. OMML `m:nary` lại cần `m:e`, và `m:grow` cũng co giãn theo `m:e`. Vì vậy pass `CaptureNaryOperands` chạy trong `Normalizer`:

```
Với mỗi LargeOperator L trong một Row, xét các phần tử anh em phía sau theo thứ tự:
  1. Gặp vi phân: d<var>, \mathrm{d}<var>, \,d<var>, \partial<var> (khi L là tích phân)
     ⇒ lấy luôn vi phân đó vào Operand rồi DỪNG (đánh dấu Identifier "d" có Role=Differential).
  2. Gặp Rel (=, <, \le, \to…), Punct ở cấp cao nhất (dấu phẩy, chấm phẩy),
     \quad, \text{…} đứng một mình, hoặc hết Row ⇒ DỪNG.
  3. Với ∑ / ∏: gặp Bin (+, −) ở cấp cao nhất ⇒ DỪNG (theo ngữ nghĩa TeX: ∑ aᵢ + b ≠ ∑(aᵢ + b)).
  4. Với tích phân lồng nhau (\int\int, \iint … dx dy) ⇒ Operand là toàn bộ phần còn lại
     tới các vi phân cuối cùng.
Nếu Operand rỗng ⇒ Operand = null; OMML vẫn sinh m:e rỗng hợp lệ và kèm Diagnostic thông tin.
```

Các luật này được test bằng bộ ca §51 và ca thực tế lấy từ đề thi Việt Nam. Người dùng có thể ép phạm vi bằng một group tường minh: `\int_0^1 {f(x)\,dx}`.

### 6.5 Các pass của Normalizer (theo thứ tự)

1. **Infix → prefix:** `\over`, `\atop`, `\choose` trở thành `Fraction`.
2. **Kiểu chữ:** `\mathbf{…}`, `\boldsymbol`, `\bf` (có phạm vi) được gán thành `MathVariant` trên các `Identifier`/`Number`.
3. **Gộp số:** gộp chữ số liên tiếp; dấu thập phân theo setting (`.`, hoặc `,` cho người dùng Việt Nam).
4. **Nhận diện hàm:** `\sin`, `\operatorname{…}`, và các macro `\DeclareMathOperator` trở thành `FunctionApply`; đối số là atom kế tiếp hoặc một `Fenced`.
5. **Đổi lớp Bin → Ord** theo quy tắc TeX (Appendix G, quy tắc 5–6): Bin đứng đầu, hoặc đứng sau Rel/Open/Bin/Punct/Op, thì thành Ord.
6. **Bắt operand cho LargeOperator** (§6.4) và phát hiện vi phân.
7. **Dấu phẩy trên (prime):** `f'` thành `Scripts(f, sup: ′)`; `f''` dùng ″.
8. **Chuẩn hoá ký hiệu:** `\le`/`\leq` → `≤`, `\to`/`\rightarrow` → `→`, `\ne`/`\neq` → `≠`.
9. **Làm phẳng** các `Row` lồng nhau; giữ các `Group` có ảnh hưởng tới lớp atom.
10. **Gợi ý ngữ nghĩa:** hằng số `e`, `i`, `π` và vi phân `d`, phục vụ setting "chữ đứng hay nghiêng" ([04](04-fonts-and-integrals.md)).

### 6.6 LaTeX chuẩn hoá (`LatexPrinter`)

- Luôn có ngoặc nhọn tường minh: `\frac{1}{2}`, `x^{2}`. Ký hiệu được viết theo tên ngắn chuẩn: `\le`, `\to`.
- **Idempotent:** `print(parse(print(parse(x)))) == print(parse(x))`. Tính chất này được kiểm bằng property-based test.
- Dùng làm: đầu vào của TexRenderer (lớp bảo mật số 1), trường `NormalizedLatex` trong metadata, và chức năng "Copy as LaTeX (normalized)".

---

## 6bis. Thiết kế LaTeX parser

### Tokenizer

TeX có cơ chế "catcode"; ở đây dùng một phiên bản rút gọn, với **bảng catcode cố định**, vì người dùng không được đổi catcode.

| Loại | Ký tự / mẫu |
|---|---|
| Escape | `\`, tạo control word `\[a-zA-Z]+` hoặc control symbol `\,` `\\` `\{` `\$` … |
| Mở / đóng group | `{` `}` |
| Math shift | `$` (chỉ có nghĩa với scanner tài liệu và với chế độ text) |
| Alignment | `&` |
| Superscript / subscript | `^` `_` |
| Letter | chữ cái và chữ Unicode (bao gồm tiếng Việt khi ở chế độ text) |
| Other | chữ số, dấu câu, toán tử Unicode (ví dụ ≤ gõ thẳng) |
| Space | khoảng trắng và xuống dòng; bị bỏ qua trong chế độ math |
| Comment | `%` tới hết dòng |
| Parameter | `#` (chỉ dùng trong định nghĩa macro) |
| Active | `~` (khoảng trắng không ngắt) |

- **Ký pháp `^^`**: không được hỗ trợ, báo Diagnostic. Đây cũng là một lớp bảo mật.
- Mỗi token mang `SourceSpan`. Tokenizer chạy **tăng dần theo dòng** để highlight; còn parse toàn bộ một công thức (dưới 2 KB) chỉ mất dưới 1 ms nên không cần parse tăng dần.

### MacroExpander

- Hỗ trợ `\newcommand`, `\renewcommand`, `\providecommand` (`[n]` tham số, có tham số tuỳ chọn mặc định) và `\DeclareMathOperator`. **Không** hỗ trợ `\def`, `\let`, `\catcode`, `\csname`.
- Giới hạn độ sâu mở rộng 64, tối đa 100.000 token sau mở rộng, có timeout. Nhờ vậy `\newcommand{\a}{\a}` sinh lỗi "macro đệ quy" chứ không làm treo app.
- Macro lấy từ profile (Macros Manager) và từ preamble của chính công thức (nếu có). Mỗi token sau mở rộng giữ con trỏ *origin* về vị trí gọi macro, để vẫn tô lỗi đúng chỗ.

### Parser

Parser đệ quy xuống, **chạy theo bảng lệnh**:

```text
\frac      : args = [Math "tử số|numerator", Math "mẫu số|denominator"]  → Fraction(Bar)
\sqrt      : args = [Opt Math "chỉ số căn|index", Math "biểu thức dưới căn|radicand"] → Radical
\int       : nary(Integral)    ;  \sum : nary(Sum) ; \lim : func(limits=Limits)
\left      : delim             → mở Fenced (đẩy vào stack) ; \middle : delim ; \right : delim → đóng
\text      : args = [Text "nội dung chữ|text"] → TextRun   (chuyển sang chế độ Text)
\begin{…}  : EnvTable["pmatrix"] = { body: Table(PMatrix), rowSep: \\, colSep: & }
```

- Mỗi đối số có **vai trò (ArgRole)**, gồm tên tiếng Việt và tiếng Anh. Thông báo lỗi Beginner được sinh từ vai trò này.
- Bảng lệnh, bảng environment và catalog autocomplete **dùng chung một nguồn dữ liệu** (`assets/catalog/commands.json`), có sinh code lúc build.
- Ngữ nghĩa script theo TeX: `^`/`_` gắn vào atom ngay trước; `x^2_3` hợp lệ; `x^2^3` báo lỗi "double superscript" và vẫn dựng tiếp.
- Các chế độ parse: `Math`, `Text` (bên trong `\text{}`, cho phép tiếng Việt, `$…$` lồng sẽ quay lại `Math`), `AlignmentCell`, `EnvironmentBody`.

### Error recovery (không bao giờ crash)

| Tình huống | Cách xử lý của parser | Thông báo ở Beginner mode |
|---|---|---|
| `\frac{a}{b` (thiếu `}`) | Tự đóng group ảo ở cuối đối số hoặc cuối input; tạo Diagnostic kèm *fix-it* "chèn `}` tại vị trí X" | "Bạn đang thiếu dấu `}` để kết thúc **mẫu số**." (tô đúng vị trí) |
| `\frac{a}` (thiếu đối số) | Tạo `Placeholder` | "`\frac` cần 2 phần: tử số và mẫu số. Bạn mới nhập tử số." |
| `\left(` mà không có `\right` | Tự thêm `\right.` ở cuối group | "Ngoặc mở `\left(` chưa có `\right)` tương ứng." |
| `}` thừa | Bỏ qua, ghi Diagnostic | "Có một dấu `}` thừa." |
| Lệnh không biết (`\alpah`) | Tạo `UnknownCommand`, gợi ý bằng fuzzy match | "Không có lệnh `\alpah`. Có phải bạn muốn `\alpha` (α)?" |
| Lệnh bị bộ gõ biến dạng (`\có`) | Ánh xạ ngược Telex/VNI để tìm lệnh khớp ([06](06-keyboard-and-ime.md)) | "Bộ gõ tiếng Việt đã đổi `\cos` thành `\có`. [Sửa]" |
| `&` nằm ngoài môi trường căn | Diagnostic; coi như ký tự thường | "Dấu `&` chỉ dùng trong ma trận hoặc `align`." |
| `x^2^3` | Diagnostic; gộp thành group | "Hai số mũ liên tiếp; hãy dùng `x^{2^3}` hoặc `{x^2}^3`." |
| `\end{pmatrix}` thiếu hoặc lệch tên | Tự đóng environment; Diagnostic | "Thiếu `\end{pmatrix}`." |

Đảm bảo chất lượng:

- **Fuzz test** (SharpFuzz kết hợp generator FsCheck) trên CI, với bất biến: không có exception, thời gian parse dưới 50 ms với input dài 64 KB.
- Diagnostic có cấu trúc `{Code, Severity, Span, ArgRole?, FixIts[], MessageKey}`. Thông điệp được localize vi/en và tách khỏi parser.
