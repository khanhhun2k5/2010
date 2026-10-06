using MathTypeX.Ast;
using MathTypeX.Parsing;

namespace MathTypeX.Editing.Catalog;

/// <summary>
/// Catalog lệnh cho autocomplete, Command Palette và Visual Formula Library (§7–§9).
/// Gồm các mẫu có placeholder viết tay (tên/mô tả tiếng Việt) và mọi ký hiệu trong bảng của parser (sinh tự động).
/// </summary>
public static class CommandCatalog
{
    private static readonly Lazy<IReadOnlyList<CatalogEntry>> LazyAll = new(Build);

    public static IReadOnlyList<CatalogEntry> All => LazyAll.Value;

    public static CatalogEntry? FindByTrigger(string trigger) =>
        All.FirstOrDefault(e => e.Trigger == trigger && e.Id == "cmd:" + trigger)
        ?? All.FirstOrDefault(e => e.Trigger == trigger);

    private static CatalogEntry T(string id, string trigger, string snippet, Category category, string vi, string en,
        string kwVi = "", string kwEn = "", string syntax = "", string example = "", string[]? args = null, string descVi = "", string descEn = "") => new()
        {
            Id = id,
            Trigger = trigger,
            Snippet = snippet,
            Category = category,
            NameVi = vi,
            NameEn = en,
            KeywordsVi = kwVi,
            KeywordsEn = kwEn,
            SyntaxVi = syntax,
            Example = example,
            ArgumentsVi = args ?? Array.Empty<string>(),
            DescriptionVi = descVi,
            DescriptionEn = descEn,
        };

    private static IReadOnlyList<CatalogEntry> Build()
    {
        var list = new List<CatalogEntry>
        {
            // ── Phân số, luỹ thừa, căn ──
            T("cmd:frac", "frac", @"\frac{$1}{$2}$0", Category.Fraction, "Phân số", "Fraction", "chia tu so mau so", "divide",
                @"\frac{tử số}{mẫu số}", @"\frac{x+1}{x-1}", new[] { "tử số", "mẫu số" }),
            T("cmd:dfrac", "dfrac", @"\dfrac{$1}{$2}$0", Category.Fraction, "Phân số cỡ lớn (display)", "Display-style fraction", "phan so lon",
                syntax: @"\dfrac{tử số}{mẫu số}", example: @"\dfrac{1}{2}", args: new[] { "tử số", "mẫu số" }),
            T("cmd:tfrac", "tfrac", @"\tfrac{$1}{$2}$0", Category.Fraction, "Phân số cỡ nhỏ", "Text-style fraction", "phan so nho",
                syntax: @"\tfrac{tử số}{mẫu số}", example: @"\tfrac{1}{2}", args: new[] { "tử số", "mẫu số" }),
            T("tpl:power", "^", @"^{$1}$0", Category.Power, "Luỹ thừa (số mũ)", "Superscript / power", "mu so mu binh phuong lap phuong", "exponent",
                @"x^{số mũ}", "x^{2}", new[] { "số mũ" }),
            T("tpl:subscript", "_", @"_{$1}$0", Category.Power, "Chỉ số dưới", "Subscript", "chi so duoi", "index", @"x_{chỉ số}", "x_{n}", new[] { "chỉ số dưới" }),
            T("cmd:sqrt", "sqrt", @"\sqrt{$1}$0", Category.Root, "Căn bậc hai", "Square root", "can can bac hai", "root radical",
                @"\sqrt{biểu thức}", @"\sqrt{x^2+1}", new[] { "biểu thức dưới căn" }),
            T("tpl:nthroot", "sqrt", @"\sqrt[$1]{$2}$0", Category.Root, "Căn bậc n", "n-th root", "can bac n can bac ba", "cube root",
                @"\sqrt[bậc]{biểu thức}", @"\sqrt[3]{x}", new[] { "bậc của căn", "biểu thức dưới căn" }),

            // ── Tích phân ──
            T("cmd:int", "int", @"\int $1\,d$2$0", Category.Integral, "Nguyên hàm (tích phân bất định)", "Indefinite integral", "tich phan nguyen ham", "antiderivative",
                @"\int f(x)\,dx", @"\int x^2\,dx", new[] { "hàm dưới dấu tích phân", "biến lấy tích phân" }),
            T("tpl:defint", "int", @"\int_{$1}^{$2} $3\,d$4$0", Category.Integral, "Tích phân xác định", "Definite integral", "tich phan can", "integral bounds",
                @"\int_{cận dưới}^{cận trên} f(x)\,dx", @"\int_0^1 \frac{x^2}{1+x^2}\,dx", new[] { "cận dưới", "cận trên", "hàm dưới dấu tích phân", "biến" }),
            T("cmd:iint", "iint", @"\iint_{$1} $2\,d$3\,d$4$0", Category.Integral, "Tích phân hai lớp (kép)", "Double integral", "tich phan kep hai lop", "",
                @"\iint_{D} f\,dx\,dy", @"\iint_D f(x,y)\,dx\,dy", new[] { "miền", "hàm", "biến 1", "biến 2" }),
            T("cmd:iiint", "iiint", @"\iiint_{$1} $2\,dV$0", Category.Integral, "Tích phân ba lớp", "Triple integral", "tich phan ba lop boi ba", "",
                @"\iiint_{V} f\,dV", @"\iiint_V f\,dV", new[] { "miền", "hàm" }),
            T("cmd:oint", "oint", @"\oint_{$1} $2$0", Category.Integral, "Tích phân đường (kín)", "Contour integral", "tich phan duong vong kin", "line integral",
                @"\oint_{C} …", @"\oint_C \vec F\cdot d\vec r", new[] { "đường cong", "biểu thức" }),

            // ── Tổng, tích, giới hạn ──
            T("cmd:sum", "sum", @"\sum_{$1}^{$2} $0", Category.SumProduct, "Tổng (sigma)", "Summation", "tong sigma", "sum series",
                @"\sum_{i=1}^{n} aᵢ", @"\sum_{n=1}^{\infty} \frac{1}{n^2}", new[] { "chỉ số bắt đầu", "chỉ số kết thúc" }),
            T("cmd:prod", "prod", @"\prod_{$1}^{$2} $0", Category.SumProduct, "Tích (pi)", "Product", "tich pi", "product",
                @"\prod_{k=1}^{n} …", @"\prod_{k=1}^{n} k", new[] { "chỉ số bắt đầu", "chỉ số kết thúc" }),
            T("cmd:lim", "lim", @"\lim_{$1 \to $2} $0", Category.Limit, "Giới hạn", "Limit", "gioi han tien toi", "limit",
                @"\lim_{x \to a} f(x)", @"\lim_{x\to 0} \frac{\sin x}{x}", new[] { "biến", "giá trị tiến tới" }),
            T("tpl:liminf", "lim", @"\lim_{$1 \to +\infty} $0", Category.Limit, "Giới hạn tại vô cực", "Limit at infinity", "gioi han vo cuc", "",
                example: @"\lim_{n\to+\infty} \left(1+\frac{1}{n}\right)^n", args: new[] { "biến" }),

            // ── Đạo hàm ──
            T("tpl:derivative", "frac", @"\frac{d$1}{d$2}$0", Category.Calculus, "Đạo hàm (ký hiệu Leibniz)", "Derivative (Leibniz)", "dao ham", "derivative",
                @"\frac{dy}{dx}", @"\frac{dy}{dx}", new[] { "hàm", "biến" }),
            T("tpl:partial", "frac", @"\frac{\partial $1}{\partial $2}$0", Category.Calculus, "Đạo hàm riêng", "Partial derivative", "dao ham rieng", "partial",
                @"\frac{\partial f}{\partial x}", @"\frac{\partial f}{\partial x}", new[] { "hàm", "biến" }),
            T("tpl:prime", "'", @"$1'($2)$0", Category.Calculus, "Đạo hàm (phẩy)", "Derivative (prime)", "dao ham phay", "prime", "f'(x)", "f'(x)", new[] { "hàm", "biến" }),

            // ── Ngoặc ──
            T("tpl:paren", "left", @"\left( $1 \right)$0", Category.Basic, "Ngoặc tròn co giãn", "Auto-sized parentheses", "ngoac tron", "parentheses brackets",
                @"\left( … \right)", @"\left(\frac{a}{b}\right)", new[] { "nội dung" }),
            T("tpl:bracket", "left", @"\left[ $1 \right]$0", Category.Basic, "Ngoặc vuông co giãn", "Auto-sized brackets", "ngoac vuong", "", @"\left[ … \right]", @"\left[\frac{a}{b}\right]", new[] { "nội dung" }),
            T("tpl:brace", "left", @"\left\{ $1 \right\}$0", Category.Basic, "Ngoặc nhọn co giãn", "Auto-sized braces", "ngoac nhon tap hop", "set braces", @"\left\{ … \right\}", @"\left\{ x \middle| x>0 \right\}", new[] { "nội dung" }),
            T("tpl:abs", "left", @"\left| $1 \right|$0", Category.Basic, "Giá trị tuyệt đối", "Absolute value", "tri tuyet doi", "abs modulus", @"\left| … \right|", @"\left|x-1\right|", new[] { "biểu thức" }),
            T("tpl:norm", "left", @"\left\| $1 \right\|$0", Category.Vector, "Chuẩn (độ dài vectơ)", "Norm", "chuan do dai", "norm", @"\left\| … \right\|", @"\left\|\vec{v}\right\|", new[] { "vectơ" }),

            // ── Ma trận, hệ ──
            T("tpl:pmatrix2", "begin", "\\begin{pmatrix} $1 & $2 \\\\ $3 & $4 \\end{pmatrix}$0", Category.Matrix, "Ma trận 2×2 (ngoặc tròn)", "2×2 matrix", "ma tran", "matrix pmatrix",
                example: @"\begin{pmatrix}a&b\\c&d\end{pmatrix}"),
            T("tpl:pmatrix3", "begin", "\\begin{pmatrix} $1 & $2 & $3 \\\\ $4 & $5 & $6 \\\\ $7 & $8 & $9 \\end{pmatrix}$0", Category.Matrix, "Ma trận 3×3 (ngoặc tròn)", "3×3 matrix", "ma tran 3x3", "matrix",
                example: @"\begin{pmatrix}1&0&0\\0&1&0\\0&0&1\end{pmatrix}"),
            T("tpl:bmatrix2", "begin", "\\begin{bmatrix} $1 & $2 \\\\ $3 & $4 \\end{bmatrix}$0", Category.Matrix, "Ma trận 2×2 (ngoặc vuông)", "2×2 bracket matrix", "ma tran ngoac vuong", "bmatrix",
                example: @"\begin{bmatrix}a&b\\c&d\end{bmatrix}"),
            T("tpl:vmatrix2", "begin", "\\begin{vmatrix} $1 & $2 \\\\ $3 & $4 \\end{vmatrix}$0", Category.LinearAlgebra, "Định thức 2×2", "2×2 determinant", "dinh thuc", "determinant",
                example: @"\begin{vmatrix}a&b\\c&d\end{vmatrix}"),
            T("tpl:cases", "begin", "\\begin{cases} $1 & \\text{nếu } $2 \\\\ $3 & \\text{nếu } $4 \\end{cases}$0", Category.Basic, "Hàm nhiều nhánh (cases)", "Piecewise function", "ham nhieu nhanh truong hop", "cases piecewise",
                example: @"\begin{cases} x & \text{nếu } x\ge 0 \\ -x & \text{nếu } x<0 \end{cases}"),
            T("tpl:system", "begin", "\\begin{cases} $1 \\\\ $2 \\end{cases}$0", Category.Basic, "Hệ phương trình", "System of equations", "he phuong trinh", "system equations",
                example: @"\begin{cases} x+y=3 \\ x-y=1 \end{cases}"),
            T("tpl:aligned", "begin", "\\begin{aligned} $1 &= $2 \\\\ &= $3 \\end{aligned}$0", Category.Basic, "Biến đổi nhiều dòng (căn dấu =)", "Aligned equations", "nhieu dong bien doi can dau bang", "align multiline",
                example: @"\begin{aligned} f(x) &=x^2+2x+1\\ &=(x+1)^2 \end{aligned}"),

            // ── Vectơ, dấu mũ, hình học ──
            T("cmd:vec", "vec", @"\vec{$1}$0", Category.Vector, "Vectơ (một chữ)", "Vector", "vecto", "vector arrow", @"\vec{v}", @"\vec{v}", new[] { "tên vectơ" }),
            T("cmd:overrightarrow", "overrightarrow", @"\overrightarrow{$1}$0", Category.Vector, "Vectơ AB", "Vector AB (long arrow)", "vecto ab", "vector",
                @"\overrightarrow{AB}", @"\overrightarrow{AB}", new[] { "điểm đầu và điểm cuối" }),
            T("tpl:angle", "widehat", @"\widehat{$1}$0", Category.Geometry, "Góc (ký hiệu ABC có mũ)", "Angle (hat notation)", "goc", "angle", @"\widehat{ABC}", @"\widehat{ABC}", new[] { "ba đỉnh" }),
            T("tpl:arc", "overset", @"\overset{\frown}{$1}$0", Category.Geometry, "Cung", "Arc", "cung tron", "arc", @"\overset{\frown}{AB}", @"\overset{\frown}{AB}", new[] { "tên cung" }),
            T("cmd:overline", "overline", @"\overline{$1}$0", Category.Accents, "Gạch trên (số phức liên hợp, số tuần hoàn)", "Overline", "gach tren lien hop", "conjugate bar",
                @"\overline{z}", @"\overline{z}", new[] { "biểu thức" }),
            T("cmd:hat", "hat", @"\hat{$1}$0", Category.Accents, "Dấu mũ", "Hat accent", "mu", "hat", @"\hat{x}", @"\hat{x}", new[] { "ký hiệu" }),
            T("cmd:bar", "bar", @"\bar{$1}$0", Category.Accents, "Gạch ngang ngắn (trung bình)", "Bar accent (mean)", "trung binh gach", "mean", @"\bar{x}", @"\bar{x}", new[] { "ký hiệu" }),
            T("cmd:tilde", "tilde", @"\tilde{$1}$0", Category.Accents, "Dấu ngã", "Tilde accent", "nga", "tilde", @"\tilde{x}", @"\tilde{x}", new[] { "ký hiệu" }),
            T("cmd:dot", "dot", @"\dot{$1}$0", Category.Accents, "Chấm trên (đạo hàm theo thời gian)", "Dot accent", "cham tren", "dot", @"\dot{x}", @"\dot{x}", new[] { "ký hiệu" }),
            T("cmd:underbrace", "underbrace", @"\underbrace{$1}_{$2}$0", Category.Accents, "Ngoặc dưới có chú thích", "Underbrace", "ngoac duoi chu thich", "underbrace",
                @"\underbrace{…}_{chú thích}", @"\underbrace{1+\cdots+1}_{n}", new[] { "biểu thức", "chú thích" }),
            T("cmd:overbrace", "overbrace", @"\overbrace{$1}^{$2}$0", Category.Accents, "Ngoặc trên có chú thích", "Overbrace", "ngoac tren chu thich", "overbrace",
                @"\overbrace{…}^{chú thích}", @"\overbrace{a+b}^{n}", new[] { "biểu thức", "chú thích" }),

            // ── Tổ hợp, xác suất, thống kê ──
            T("cmd:binom", "binom", @"\binom{$1}{$2}$0", Category.Probability, "Tổ hợp (nhị thức)", "Binomial coefficient", "to hop nhi thuc", "choose binomial",
                @"\binom{n}{k}", @"\binom{n}{k}", new[] { "n", "k" }),
            T("tpl:combination", "C", @"C_{$1}^{$2}$0", Category.Probability, "Tổ hợp (ký hiệu SGK)", "Combination (VN notation)", "to hop c n k", "combination", @"C_{n}^{k}", @"C_{n}^{k}", new[] { "n", "k" }),
            T("tpl:arrangement", "A", @"A_{$1}^{$2}$0", Category.Probability, "Chỉnh hợp", "Arrangement", "chinh hop", "permutation arrangement", @"A_{n}^{k}", @"A_{n}^{k}", new[] { "n", "k" }),
            T("tpl:prob", "P", @"P\left( $1 \right)$0", Category.Probability, "Xác suất", "Probability", "xac suat", "probability", @"P\left(A\right)", @"P\left(A\right)", new[] { "biến cố" }),
            T("tpl:condprob", "P", @"P\left( $1 \mid $2 \right)$0", Category.Probability, "Xác suất có điều kiện", "Conditional probability", "xac suat dieu kien", "conditional",
                @"P\left(A \mid B\right)", @"P(A\mid B)=\frac{P(B\mid A)P(A)}{P(B)}", new[] { "biến cố", "điều kiện" }),
            T("tpl:normal", "N", @"$1 \sim N\left( $2, $3^2 \right)$0", Category.Statistics, "Phân phối chuẩn", "Normal distribution", "phan phoi chuan", "normal gaussian",
                example: @"X\sim N(\mu,\sigma^2)"),
            T("tpl:mean", "bar", @"\bar{x} = \frac{1}{n}\sum_{i=1}^{n} x_i$0", Category.Statistics, "Trung bình mẫu", "Sample mean", "trung binh mau", "mean average",
                example: @"\bar{x} = \frac{1}{n}\sum_{i=1}^{n} x_i"),

            // ── Logarit, lượng giác ──
            T("tpl:logbase", "log", @"\log_{$1} $2$0", Category.Logarithm, "Logarit cơ số a", "Logarithm base a", "logarit co so", "logarithm base", @"\log_{a} b", @"\log_2 8", new[] { "cơ số", "biểu thức" }),

            // ── Tập hợp ──
            T("tpl:setbuilder", "left", @"\left\{ $1 \middle| $2 \right\}$0", Category.SetTheory, "Tập hợp theo tính chất", "Set-builder notation", "tap hop tinh chat", "set builder",
                example: @"\left\{ x\in\mathbb{R} \middle| x>0 \right\}", args: new[] { "phần tử", "tính chất" }),
            T("tpl:reals", "mathbb", @"\mathbb{R}$0", Category.SetTheory, "Tập số thực ℝ", "Real numbers", "tap so thuc", "reals", example: @"\mathbb{R}"),
            T("tpl:naturals", "mathbb", @"\mathbb{N}$0", Category.SetTheory, "Tập số tự nhiên ℕ", "Natural numbers", "tap so tu nhien", "naturals", example: @"\mathbb{N}"),
            T("tpl:integers", "mathbb", @"\mathbb{Z}$0", Category.SetTheory, "Tập số nguyên ℤ", "Integers", "tap so nguyen", "integers", example: @"\mathbb{Z}"),
            T("tpl:rationals", "mathbb", @"\mathbb{Q}$0", Category.SetTheory, "Tập số hữu tỉ ℚ", "Rational numbers", "tap so huu ti", "rationals", example: @"\mathbb{Q}"),
            T("tpl:complexes", "mathbb", @"\mathbb{C}$0", Category.SetTheory, "Tập số phức ℂ", "Complex numbers", "tap so phuc", "complex", example: @"\mathbb{C}"),
            T("cmd:mathbb", "mathbb", @"\mathbb{$1}$0", Category.SetTheory, "Chữ nét đôi (tập số)", "Blackboard bold", "net doi", "blackboard", @"\mathbb{R}", @"\mathbb{R}", new[] { "chữ cái" }),
            T("cmd:mathcal", "mathcal", @"\mathcal{$1}$0", Category.SetTheory, "Chữ hoa nghiêng (họ tập hợp)", "Calligraphic", "chu hoa nghieng", "calligraphic", @"\mathcal{F}", @"\mathcal{F}", new[] { "chữ cái" }),
            T("cmd:mathfrak", "mathfrak", @"\mathfrak{$1}$0", Category.SetTheory, "Chữ Fraktur", "Fraktur", "fraktur", "", @"\mathfrak{g}", @"\mathfrak{g}", new[] { "chữ cái" }),
            T("cmd:mathbf", "mathbf", @"\mathbf{$1}$0", Category.Vector, "Chữ đậm (vectơ, ma trận)", "Bold", "chu dam", "bold", @"\mathbf{x}", @"\mathbf{x}", new[] { "ký hiệu" }),
            T("cmd:mathrm", "mathrm", @"\mathrm{$1}$0", Category.Text, "Chữ đứng (đơn vị, vi phân d)", "Upright", "chu dung don vi", "roman upright", @"\mathrm{d}x", @"\mathrm{d}x", new[] { "chữ" }),
            T("cmd:text", "text", @"\text{$1}$0", Category.Text, "Chữ thường trong công thức", "Text in math", "chu van ban tieng viet", "text words", @"\text{với mọi}", @"\text{với mọi } x", new[] { "chữ" }),
            T("cmd:operatorname", "operatorname", @"\operatorname{$1}$0", Category.Text, "Tên toán tử tự đặt", "Custom operator name", "ten toan tu", "operator", @"\operatorname{rank}", @"\operatorname{rank} A", new[] { "tên" }),
            T("cmd:boxed", "boxed", @"\boxed{$1}$0", Category.Basic, "Đóng khung (kết quả)", "Boxed", "dong khung ket qua", "box result", @"\boxed{…}", @"\boxed{x=2}", new[] { "nội dung" }),
            T("cmd:xrightarrow", "xrightarrow", @"\xrightarrow{$1}$0", Category.Arrows, "Mũi tên có chữ", "Arrow with label", "mui ten co chu", "arrow label", @"\xrightarrow{…}", @"\xrightarrow{t\to0}", new[] { "chữ trên mũi tên" }),
        };

        // ── Ký hiệu sinh tự động từ bảng của parser ──
        var known = new HashSet<string>(list.Select(e => e.Trigger), StringComparer.Ordinal);
        foreach (var name in LatexSymbols.CommandNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (known.Contains(name) || !LatexSymbols.TryGetCommand(name, out var info)) continue;
            var (vi, en, category, keywords) = Describe(name, info);
            list.Add(new CatalogEntry
            {
                Id = "cmd:" + name,
                Trigger = name,
                Snippet = info.Kind == SymbolKind.Function ? "\\" + name + " $0" : "\\" + name + "$0",
                Category = category,
                NameVi = vi,
                NameEn = en,
                KeywordsVi = keywords,
                Example = info.Kind == SymbolKind.Function ? "\\" + name + " x" : "\\" + name,
                Symbol = info.Kind == SymbolKind.Space ? null : info.Text,
            });
        }

        // Dấu mũ chưa có mục riêng.
        foreach (var accent in LatexSymbols.Accents.Keys.Where(a => !known.Contains(a) && list.All(e => e.Trigger != a)))
        {
            list.Add(T("cmd:" + accent, accent, "\\" + accent + "{$1}$0", Category.Accents, "Dấu mũ " + accent, accent + " accent",
                syntax: "\\" + accent + "{…}", example: "\\" + accent + "{x}", args: new[] { "ký hiệu" }));
        }
        return list;
    }

    private static readonly Dictionary<string, (string Vi, string Keywords)> VietnameseNames = new()
    {
        ["le"] = ("nhỏ hơn hoặc bằng", "be hon hoac bang"), ["leq"] = ("nhỏ hơn hoặc bằng", ""), ["ge"] = ("lớn hơn hoặc bằng", "lon hon hoac bang"),
        ["geq"] = ("lớn hơn hoặc bằng", ""), ["ne"] = ("khác (không bằng)", "khong bang"), ["neq"] = ("khác (không bằng)", ""),
        ["approx"] = ("xấp xỉ", "gan bang"), ["equiv"] = ("đồng nhất / đồng dư", "dong du"), ["sim"] = ("tương tự / phân phối", "dong dang"),
        ["cong"] = ("bằng nhau (hình học)", "bang nhau tam giac"), ["parallel"] = ("song song", ""), ["perp"] = ("vuông góc", ""),
        ["in"] = ("thuộc", "phan tu"), ["notin"] = ("không thuộc", ""), ["subset"] = ("tập con", ""), ["subseteq"] = ("tập con hoặc bằng", ""),
        ["supset"] = ("chứa", ""), ["cup"] = ("hợp", "hop tap hop"), ["cap"] = ("giao", "giao tap hop"), ["setminus"] = ("hiệu tập hợp", ""),
        ["emptyset"] = ("tập rỗng", ""), ["varnothing"] = ("tập rỗng", ""), ["infty"] = ("vô cùng", "vo cuc"), ["forall"] = ("với mọi", ""),
        ["exists"] = ("tồn tại", ""), ["nexists"] = ("không tồn tại", ""), ["neg"] = ("phủ định", ""), ["land"] = ("và (hội)", ""),
        ["lor"] = ("hoặc (tuyển)", ""), ["wedge"] = ("và (hội)", ""), ["vee"] = ("hoặc (tuyển)", ""), ["Rightarrow"] = ("suy ra", "keo theo"),
        ["implies"] = ("suy ra", "keo theo"), ["Leftrightarrow"] = ("tương đương", "khi va chi khi"), ["iff"] = ("khi và chỉ khi", "tuong duong"),
        ["to"] = ("tiến tới / ánh xạ", "mui ten"), ["rightarrow"] = ("mũi tên phải", ""), ["leftarrow"] = ("mũi tên trái", ""),
        ["mapsto"] = ("ánh xạ thành", ""), ["pm"] = ("cộng trừ", ""), ["mp"] = ("trừ cộng", ""), ["times"] = ("nhân (dấu ×)", "tich co huong"),
        ["cdot"] = ("nhân (dấu chấm)", "tich vo huong"), ["div"] = ("chia", ""), ["circ"] = ("hợp thành (hàm hợp)", ""), ["partial"] = ("đạo hàm riêng ∂", ""),
        ["nabla"] = ("nabla ∇", "gradient"), ["angle"] = ("góc", ""), ["triangle"] = ("tam giác", ""), ["degree"] = ("độ", ""),
        ["cdots"] = ("ba chấm giữa", ""), ["ldots"] = ("ba chấm dưới", ""), ["vdots"] = ("ba chấm dọc", ""), ["ddots"] = ("ba chấm chéo", ""),
        ["sin"] = ("sin", "luong giac"), ["cos"] = ("cos", "luong giac"), ["tan"] = ("tan", "luong giac tang"), ["cot"] = ("cot", "luong giac"),
        ["arcsin"] = ("arcsin", "luong giac nguoc"), ["arccos"] = ("arccos", "luong giac nguoc"), ["arctan"] = ("arctan", "luong giac nguoc"),
        ["ln"] = ("logarit tự nhiên", "logarit nepe"), ["log"] = ("logarit", ""), ["exp"] = ("hàm mũ", ""), ["max"] = ("lớn nhất", "gia tri lon nhat"),
        ["min"] = ("nhỏ nhất", "gia tri nho nhat"), ["det"] = ("định thức", ""), ["gcd"] = ("ước chung lớn nhất", "ucln"), ["sup"] = ("cận trên", ""),
        ["inf"] = ("cận dưới", ""), ["mid"] = ("chia hết / điều kiện", "chia het"), ["nmid"] = ("không chia hết", ""),
        ["therefore"] = ("do đó", ""), ["because"] = ("vì", ""), ["propto"] = ("tỉ lệ thuận", ""), ["ll"] = ("nhỏ hơn nhiều", ""), ["gg"] = ("lớn hơn nhiều", ""),
        ["quad"] = ("khoảng trắng lớn", ""), ["qquad"] = ("khoảng trắng rất lớn", ""),
    };

    private static (string Vi, string En, Category Category, string Keywords) Describe(string name, SymbolInfo info)
    {
        VietnameseNames.TryGetValue(name, out var vn);
        string en = name;
        string symbol = info.Kind == SymbolKind.Space ? "" : " " + info.Text;
        switch (info.Kind)
        {
            case SymbolKind.Identifier when info.Text.Length == 1 && (MathAlphabets.IsGreekLower(info.Text[0]) || MathAlphabets.IsGreekUpper(info.Text[0])):
                return ("chữ Hy Lạp " + name + symbol, "Greek letter " + name, Category.Greek, "chu hy lap greek");
            case SymbolKind.LargeOperator:
                return ((vn.Vi ?? name) + symbol, "Large operator " + name, info.Nary.IsIntegral() ? Category.Integral : Category.SumProduct, "toan tu lon");
            case SymbolKind.Function:
                bool trig = name.Contains("sin") || name.Contains("cos") || name.Contains("tan") || name.Contains("cot") || name is "sec" or "csc";
                bool log = name is "ln" or "log" or "lg" or "exp";
                return ((vn.Vi ?? name) + " (hàm)", "Function " + name, trig ? Category.Trigonometry : log ? Category.Logarithm : name is "lim" or "liminf" or "limsup" ? Category.Limit : Category.Calculus, "ham " + (vn.Keywords ?? ""));
            case SymbolKind.Space:
                return (vn.Vi ?? "khoảng trắng " + name, "Space " + name, Category.Basic, "khoang trang");
            case SymbolKind.Operator when info.Class == AtomClass.Rel:
                bool arrow = name.Contains("arrow") || name is "to" or "gets" or "mapsto" or "implies" or "impliedby" or "iff" or "leadsto";
                return ((vn.Vi ?? name) + symbol, "Relation " + name, arrow ? Category.Arrows : name is "in" or "notin" or "ni" or "subset" or "supset" or "subseteq" or "supseteq" ? Category.SetTheory : Category.Relations, vn.Keywords ?? "quan he");
            case SymbolKind.Operator:
                return ((vn.Vi ?? name) + symbol, "Operator " + name, name is "cup" or "cap" or "setminus" ? Category.SetTheory : name is "land" or "lor" or "wedge" or "vee" ? Category.Logic : Category.Basic, vn.Keywords ?? "phep toan");
            default:
                return ((vn.Vi ?? name) + symbol, "Symbol " + name, name is "forall" or "exists" or "nexists" or "neg" or "lnot" ? Category.Logic : name is "emptyset" or "varnothing" ? Category.SetTheory : name is "angle" or "triangle" or "degree" or "perp" or "parallel" ? Category.Geometry : Category.Basic, vn.Keywords ?? "ky hieu");
        }
    }
}
