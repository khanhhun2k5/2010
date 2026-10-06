namespace MathTypeX.Scanner.Tests;

/// <summary>
/// Scanner (docs/05 §9.4): quy tắc $ kiểu Pandoc, escape, ký tự đặc biệt của Word, chấm điểm độ tin cậy.
/// Ký hiệu kỳ vọng: "I:latex" là inline, "D:latex" là display; nhiều công thức ngăn bằng " ¦ ".
/// </summary>
public class ScannerTests
{
    private static string Describe(IReadOnlyList<MathCandidate> found) =>
        string.Join(" ¦ ", found.Select(c => (c.Display ? "D:" : "I:") + c.Latex));

    [Theory]
    [InlineData("$x$", "I:x")]
    [InlineData("Cho $x^2+1$ là một đa thức.", "I:x^2+1")]
    [InlineData("$a$ và $b$", "I:a ¦ I:b")]
    [InlineData("$a$$b$", "I:a ¦ I:b")]
    [InlineData("$x$.", "I:x")]
    [InlineData("($x$)", "I:x")]
    [InlineData("$x$, $y$; $z$!", "I:x ¦ I:y ¦ I:z")]
    [InlineData("$n$th", "I:n")]
    [InlineData("$n$-th", "I:n")]
    [InlineData("$5$", "I:5")]
    [InlineData("$\\$5$", "I:\\$5")]
    [InlineData("$a\\$b$", "I:a\\$b")]
    [InlineData("$a\vb$", "I:a\vb")]
    [InlineData("$\\{x\\}$", "I:\\{x\\}")]
    [InlineData("$$E=mc^2$$", "D:E=mc^2")]
    [InlineData("Ta có $$ E(X)=\\mu. $$", "D: E(X)=\\mu. ")]
    [InlineData("$$a\rb$$", "D:a\rb")]
    [InlineData("$$a\\$$$", "D:a\\$")]
    [InlineData("\\(a+b\\)", "I:a+b")]
    [InlineData("\\( a \\)", "I: a ")]
    [InlineData("\\[a+b\\]", "D:a+b")]
    [InlineData("\\[a\rb\\]", "D:a\rb")]
    [InlineData("\\[ \\begin{pmatrix}1\\\\2\\end{pmatrix} \\]", "D: \\begin{pmatrix}1\\\\2\\end{pmatrix} ")]
    [InlineData("\\begin{equation}a=b\\end{equation}", "D:a=b")]
    [InlineData("\\begin{equation*}a=b\\end{equation*}", "D:a=b")]
    [InlineData("\\begin{displaymath}a\\end{displaymath}", "D:a")]
    [InlineData("\\begin{math}x\\end{math}", "I:x")]
    [InlineData("\\begin{align*}a&=b\\\\c&=d\\end{align*}", "D:\\begin{align*}a&=b\\\\c&=d\\end{align*}")]
    [InlineData("\\begin{gather}a\\\\b\\end{gather}", "D:\\begin{gather}a\\\\b\\end{gather}")]
    [InlineData("\\begin{align}a\r&=b\\end{align}", "D:\\begin{align}a\r&=b\\end{align}")]
    [InlineData("Với $X\\sim N(\\mu,\\sigma^2)$, ta có $$E(X)=\\mu.$$", "I:X\\sim N(\\mu,\\sigma^2) ¦ D:E(X)=\\mu.")]
    [InlineData("$a$ \\(b\\) $$c$$ \\[d\\]", "I:a ¦ I:b ¦ D:c ¦ D:d")]
    [InlineData("\u0013 PAGE $x$ \u0014 1 \u0015 $y$", "I:y")]
    [InlineData("\u0013 A \u0013 B $x$ \u0015 $z$ \u0015 $y$", "I:y")]
    [InlineData("$x$\a$y$", "I:x ¦ I:y")]
    [InlineData("a$b$c", "I:b")]
    [InlineData("$x$$$y$$", "I:x ¦ D:y")]
    [InlineData("\\\\ $x$", "I:x")]
    [InlineData("$\\text{giá $5}$", "I:\\text{giá $5}")] // "$" sau khoảng trắng không đóng được
    [InlineData("Giá $20 và $30, còn $x$ thì không.", "I:x")]
    [InlineData("$a $b$", "I:b")]
    [InlineData("$\\text{a $b$ c}$", "I:\\text{a $b$ c}")]
    public void FindsFormulas(string text, string expected)
    {
        Assert.Equal(expected, Describe(LatexScanner.Scan(text)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Không có công thức nào.")]
    [InlineData("Giá $5 và $")]
    [InlineData("$20,000 và $30,000")]
    [InlineData("Từ $20 đến $30")]
    [InlineData("US$5 or $6")]
    [InlineData("\\$5 and \\$6")]
    [InlineData("\\$x\\$")]
    [InlineData("$ x$")]
    [InlineData("$x $")]
    [InlineData("$x$5")]
    [InlineData("$ $")]
    [InlineData("$")]
    [InlineData("$$")]
    [InlineData("$$ $$")]
    [InlineData("$$x$")]
    [InlineData("$$x")]
    [InlineData("$a\rb$")]
    [InlineData("$a\n\nb$")]
    [InlineData("$a\ab$")]
    [InlineData("$a\fb$")]
    [InlineData("$a\u0013b\u0015c$")]
    [InlineData("$a\u0001b$")]
    [InlineData("\\(a\rb\\)")]
    [InlineData("\\(a")]
    [InlineData("\\[a")]
    [InlineData("\\(\\)")]
    [InlineData("\\[ \\]")]
    [InlineData("\\\\(a\\\\)")]
    [InlineData("\\\\[2pt] x")]
    [InlineData("a \\\\[4pt] b \\\\[4pt] c")]
    [InlineData("\\begin{matrix}a\\end{matrix}")]
    [InlineData("\\begin{itemize}\\item x\\end{itemize}")]
    [InlineData("\\begin{equation}a=b")]
    [InlineData("\\begin{equation}\\end{equation}")]
    [InlineData("\\begin{equation}a\ab\\end{equation}")]
    [InlineData("\\begin{math}a\rb\\end{math}")]
    [InlineData("\\begin")]
    [InlineData("\\begin{")]
    [InlineData("\u0013 HYPERLINK \"http://x.com/$a$\" \u0014 link \u0015")]
    [InlineData("$100 tiền vé + $50 phụ thu")]
    [InlineData("chi phí: $1.5 triệu, $2 triệu")]
    [InlineData("$\\frac{a}{$")] // nhóm { chưa đóng: $ bên trong nhóm không đóng công thức
    public void IgnoresNonFormulas(string text)
    {
        Assert.Empty(LatexScanner.Scan(text));
    }

    [Theory]
    [InlineData("$x$", MathDelimiter.Dollar, 0, 3)]
    [InlineData("ab $x^2$ cd", MathDelimiter.Dollar, 3, 5)]
    [InlineData("ab $$x$$ cd", MathDelimiter.DoubleDollar, 3, 5)]
    [InlineData("ab \\(x\\) cd", MathDelimiter.Paren, 3, 5)]
    [InlineData("ab \\[x\\] cd", MathDelimiter.Bracket, 3, 5)]
    [InlineData("ab \\begin{math}x\\end{math} cd", MathDelimiter.Environment, 3, 23)]
    public void ReportsExactSpansIncludingDelimiters(string text, MathDelimiter kind, int start, int length)
    {
        var c = Assert.Single(LatexScanner.Scan(text));
        Assert.Equal((kind, start, length), (c.Delimiter, c.Start, c.Length));
    }

    [Theory]
    [InlineData("$x$", true)]
    [InlineData("$\\alpha$", true)]
    [InlineData("$x^2$", true)]
    [InlineData("$a+b$", true)]
    [InlineData("$5$", true)]
    [InlineData("$$x$$", true)]
    [InlineData("\\(x\\)", true)]
    [InlineData("\\[x\\]", true)]
    [InlineData("$HOME$", false)]
    [InlineData("$PATH_INFO$", false)]
    [InlineData("$C:\\Users\\a$", false)]
    [InlineData("echo $HOME/bin:$PATH", false)]
    [InlineData("$/usr/local/bin$", false)]
    [InlineData("$%APPDATA%$", false)]
    [InlineData("${HOME}$", false)]
    [InlineData("$and the price is$", false)]
    [InlineData("$tiền vé là$", false)]
    [InlineData("$\\frac{a}$", false)]
    [InlineData("$\\fracc{a}{b}$", false)]
    public void ConfidenceDecidesWhatIsRecommended(string text, bool recommended)
    {
        var c = Assert.Single(LatexScanner.Scan(text));
        Assert.True(c.Recommended == recommended, $"{text}: điểm {c.Confidence} ({string.Join(", ", c.Reasons)})");
        if (!recommended) Assert.NotEmpty(c.Reasons);
    }

    [Fact]
    public void ThresholdIsConfigurable()
    {
        var strict = new ScannerOptions { MinConfidence = 90 };
        Assert.False(LatexScanner.Scan("$x+y$", strict).Single().Recommended);
        Assert.True(LatexScanner.Scan("$\\alpha^2$", strict).Single().Recommended);
    }

    [Fact]
    public void DelimitersCanBeDisabled()
    {
        const string text = "$a$ $$b$$ \\(c\\) \\[d\\] \\begin{equation}e\\end{equation}";
        Assert.Equal("D:b ¦ I:c ¦ D:d ¦ D:e", Describe(LatexScanner.Scan(text, new ScannerOptions { SingleDollar = false })));
        Assert.Equal("I:a ¦ I:c ¦ D:d ¦ D:e", Describe(LatexScanner.Scan(text, new ScannerOptions { DoubleDollar = false })));
        Assert.Equal("I:a ¦ D:b ¦ D:e", Describe(LatexScanner.Scan(text, new ScannerOptions { Parentheses = false, Brackets = false })));
        Assert.Equal("I:a ¦ D:b ¦ I:c ¦ D:d", Describe(LatexScanner.Scan(text, new ScannerOptions { Environments = false })));
    }

    [Fact]
    public void DisplayCannotSpanTooManyParagraphs()
    {
        string far = "$$a" + string.Concat(Enumerable.Repeat("\rđoạn văn", 20)) + "$$";
        Assert.Empty(LatexScanner.Scan(far));
        Assert.Single(LatexScanner.Scan(far, new ScannerOptions { MaxDisplayParagraphs = 30 }));
    }

    [Fact]
    public void InlineHasALengthLimit()
    {
        string longText = "$" + new string('x', 2000) + "$";
        Assert.Empty(LatexScanner.Scan(longText));
    }

    // ── Ca sinh tổ hợp: mỗi công thức × mỗi delimiter × mỗi ngữ cảnh xung quanh ──

    public static readonly string[] Formulas =
    {
        "x", "x^2", "a_1", "x_{n+1}", "\\alpha", "\\frac{a}{b}", "\\sqrt{2}", "\\sqrt[3]{x}", "a+b=c", "e^{i\\pi}+1=0",
        "\\int_0^1 f(x)\\,dx", "\\sum_{k=1}^{n} k", "\\lim_{x\\to 0}\\frac{\\sin x}{x}", "\\mathbb{R}", "\\vec{v}",
        "f(x)=\\begin{cases}1&x>0\\\\0&x\\le 0\\end{cases}", "\\begin{pmatrix}a&b\\\\c&d\\end{pmatrix}", "\\left(\\frac{1}{2}\\right)",
        "\\text{nếu } x>0", "\\{1,2,3\\}", "|x|", "\\|v\\|", "x \\in A", "A \\cup B", "\\overline{AB}", "\\widehat{ABC}",
        "\\binom{n}{k}", "\\log_2 8 = 3", "\\cos^2 x + \\sin^2 x = 1", "3{,}14", "\\$", "a\\,b", "n!", "\\infty",
    };

    public static readonly (string Open, string Close, bool Display)[] Delimiters =
    {
        ("$", "$", false), ("$$", "$$", true), ("\\(", "\\)", false), ("\\[", "\\]", true),
    };

    public static readonly (string Before, string After)[] Contexts =
    {
        ("", ""), ("Ta có ", " là đúng."), ("(", ")"), ("Xét ", ", suy ra"), ("\v", "\v"), ("Câu 1. ", "\r"),
        ("\u0013 PAGE \u0014 3 \u0015 ", ""), ("giá \\$5 và ", " đồng"), ("\t", "\t"),
    };

    public static TheoryData<string, int> FormulaDelimiterPairs()
    {
        var data = new TheoryData<string, int>();
        foreach (var f in Formulas)
        {
            for (int d = 0; d < Delimiters.Length; d++) data.Add(f, d);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(FormulaDelimiterPairs))]
    public void FindsEachFormulaInEveryContext(string formula, int delimiter)
    {
        var (open, close, display) = Delimiters[delimiter];
        foreach (var (before, after) in Contexts)
        {
            string text = before + open + formula + close + after;
            var c = Assert.Single(LatexScanner.Scan(text));
            Assert.Equal(formula, c.Latex);
            Assert.Equal(display, c.Display);
            Assert.Equal(before.Length, c.Start);
            Assert.Equal(open.Length + formula.Length + close.Length, c.Length);
            Assert.True(c.Recommended, $"{text}: điểm {c.Confidence} ({string.Join(", ", c.Reasons)})");
        }
    }

    // ── Bất biến trên dữ liệu ngẫu nhiên ─────────────────────────────────

    [Fact]
    public void NeverThrowsAndCandidatesAreWellFormed()
    {
        const string alphabet = "$$$\\\\()[]{}ax1 ^_\r\v\a\u0013\u0014\u0015\u00A0beginequation";
        var random = new Random(20251006);
        for (int round = 0; round < 3000; round++)
        {
            int length = random.Next(0, 60);
            var chars = new char[length];
            for (int k = 0; k < length; k++) chars[k] = alphabet[random.Next(alphabet.Length)];
            string text = new(chars);

            var found = LatexScanner.Scan(text);
            int previousEnd = 0;
            foreach (var c in found)
            {
                Assert.True(c.Start >= previousEnd, text);
                Assert.True(c.End <= text.Length, text);
                Assert.Contains(c.Latex, text.Substring(c.Start, c.Length));
                Assert.InRange(c.Confidence, 0, 100);
                previousEnd = c.End;
            }
        }
    }

    [Fact]
    public void DeepNestingAndLongInputAreFast()
    {
        string text = string.Concat(Enumerable.Repeat("Cho $x_{i}^{2}$ và \\(\\frac{a}{b}\\); ", 5000));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var found = LatexScanner.Scan(text);
        watch.Stop();
        Assert.Equal(10000, found.Count);
        Assert.True(watch.ElapsedMilliseconds < 5000, $"{watch.ElapsedMilliseconds} ms");
    }
}
