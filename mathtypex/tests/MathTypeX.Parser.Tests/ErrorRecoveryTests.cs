namespace MathTypeX.Parser.Tests;

/// <summary>Parser không bao giờ throw; lỗi có vị trí, vai trò đối số và gợi ý sửa.</summary>
public class ErrorRecoveryTests
{
    private static Diagnostic One(string latex, DiagnosticCode code)
    {
        var doc = H.Parse(latex);
        return Assert.Single(doc.Diagnostics, d => d.Code == code);
    }

    [Fact]
    public void MissingCloseBraceNamesTheDenominator()
    {
        var d = One(@"\frac{a}{b", DiagnosticCode.MissingCloseBrace);
        Assert.Equal(ArgRole.Denominator, d.Role);
        Assert.Equal(10, d.Span.Start);
        Assert.Equal("Bạn đang thiếu dấu } để kết thúc mẫu số.", DiagnosticFormatter.Format(d, UiLanguage.Vi));
        Assert.Equal("}", Assert.Single(d.Fixes).Replacement);
    }

    [Fact]
    public void MissingArgumentBecomesPlaceholder()
    {
        var doc = H.Parse(@"\frac{a}");
        var d = Assert.Single(doc.Diagnostics);
        Assert.Equal(DiagnosticCode.MissingArgument, d.Code);
        Assert.Equal("\\frac cần mẫu số nhưng bạn chưa nhập.", DiagnosticFormatter.Format(d, UiLanguage.Vi));
        var f = H.As<Fraction>(Assert.Single(doc.Body.Children));
        Assert.IsType<Placeholder>(f.Denominator);
    }

    [Fact]
    public void UnmatchedLeftGetsVirtualRight()
    {
        var doc = H.Parse(@"\left( a+b");
        var d = Assert.Single(doc.Diagnostics);
        Assert.Equal(DiagnosticCode.UnmatchedLeft, d.Code);
        Assert.Equal("\\right.", d.Fixes[0].Replacement);
        Assert.IsType<Fenced>(Assert.Single(doc.Body.Children));
    }

    [Fact]
    public void UnknownCommandSuggestsClosestNames()
    {
        var d = One(@"\alpah+1", DiagnosticCode.UnknownCommand);
        Assert.Contains("alpha", d.Arguments);
        Assert.Equal("Không có lệnh \\alpah. Có phải bạn muốn \\alpha?", DiagnosticFormatter.Format(d, UiLanguage.Vi));
    }

    [Fact]
    public void ExtraCloseBraceIsSkipped()
    {
        var doc = H.Parse("a}+b");
        Assert.Contains(doc.Diagnostics, d => d.Code == DiagnosticCode.ExtraCloseBrace);
        Assert.Equal(3, doc.Body.Children.Count);
    }

    [Fact]
    public void AlignmentOutsideEnvironment()
    {
        One("a & b", DiagnosticCode.MisplacedAlignment);
    }

    [Fact]
    public void GroupInsideFracStopsAtEndOfInputWithRole()
    {
        var doc = H.Parse(@"\frac{a{b}");
        var d = Assert.Single(doc.Diagnostics, x => x.Code == DiagnosticCode.MissingCloseBrace);
        Assert.Equal(ArgRole.Numerator, d.Role);
        Assert.Contains(doc.Diagnostics, x => x.Code == DiagnosticCode.MissingArgument && x.Role == ArgRole.Denominator);
    }

    [Fact]
    public void MissingBraceInsideMatrixCellDoesNotEatTheRest()
    {
        var doc = H.Parse(@"\begin{pmatrix} \frac{a}{b & c \\ d & e \end{pmatrix}");
        Assert.Contains(doc.Diagnostics, x => x.Code == DiagnosticCode.MissingCloseBrace && x.Role == ArgRole.Denominator);
        var t = H.As<Table>(Assert.Single(doc.Body.Children));
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal(2, t.Rows[0].Cells.Count);
    }

    [Fact]
    public void DeepNestingDoesNotOverflowTheStack()
    {
        string deep = new string('{', 50_000) + "x" + new string('}', 50_000);
        var doc = H.Parse(deep);
        Assert.Contains(doc.Diagnostics, d => d.Code == DiagnosticCode.NestingTooDeep);
    }

    [Fact]
    public void RandomInputNeverThrows()
    {
        var rng = new Random(20261006);
        string alphabet = "\\{}[]()^_&$#%~'xy12 +-=.,|" + "αℝ∫𝔤\uD800";
        string[] words = { "\\frac", "\\sqrt", "\\left", "\\right", "\\begin{pmatrix}", "\\end{pmatrix}", "\\\\", "\\int", "\\sum",
                           "\\text{", "\\mathbb", "\\lim", "\\sin", "\\over", "\\middle", "\\not", "\\limits", "\\tag{", "\\big" };
        for (int i = 0; i < 5000; i++)
        {
            var sb = new System.Text.StringBuilder();
            int len = rng.Next(0, 40);
            for (int k = 0; k < len; k++)
            {
                if (rng.Next(4) == 0) sb.Append(words[rng.Next(words.Length)]);
                else sb.Append(alphabet[rng.Next(alphabet.Length)]);
            }
            string input = sb.ToString();
            var doc = LatexParser.Parse(input);
            _ = LatexPrinter.Print(doc);
        }
    }
}
