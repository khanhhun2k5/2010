namespace MathTypeX.Documents.Tests;

/// <summary>LaTeX → OMML → LaTeX phải cho cùng dạng chuẩn hoá (docs/07 §12.5).</summary>
public class RoundTripTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MathTypeX.slnx"))) dir = dir.Parent;
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }

    public static TheoryData<string> Formulas()
    {
        var data = new TheoryData<string>();
        foreach (var file in new[] { "typography.tex", "integrals.tex" })
            foreach (var line in File.ReadAllLines(RepoFile("tests", "corpus", file)))
                if (line.Trim().Length > 0 && !line.TrimStart().StartsWith('%')) data.Add(line.Trim());
        foreach (var extra in new[]
                 {
                     @"\lim_{x\to 0} \frac{\sin x}{x} = 1", @"\sin^2 x + \cos^2 x = 1", @"\log_2 8 = 3",
                     @"\operatorname{rank} A", @"\binom{n}{k}", @"\left\{ x \middle| x>0 \right\}", @"\left. F \right|_0^1",
                     @"\sqrt[3]{x+1}", @"f'(x) + f''(x)", @"x_i^2", @"{}^{14}_{6}C", @"\overbrace{a+b}^{n}", @"\underbrace{x}_{k}",
                     @"\xrightarrow[a]{f}", @"\overset{!}{=}", @"\underset{x}{\arg}", @"\boxed{x} \cancel{y}", @"\phantom{x}",
                     @"\begin{bmatrix} 1 & 2 \\ 3 & 4 \end{bmatrix}", @"\begin{vmatrix} a & b \\ c & d \end{vmatrix}",
                     @"\begin{rcases} a \\ b \end{rcases}", @"\int\limits_0^1 f\,dx", @"\sum\nolimits_i a_i", @"\iint_D f(x,y)\,dx\,dy",
                     @"\oint_C \vec F\cdot d\vec r", @"\mathbb{R}^n \mathcal{F} \mathfrak{g} \mathbf{x} \boldsymbol{\alpha}",
                     @"3{,}14", @"\text{với mọi } x \in \mathbb{N}", @"a \le b \ne c \to d \Rightarrow e", @"\alpha\beta\Gamma\varepsilon\varphi",
                     @"x \quad y \qquad z \; w \: v", @"\hat{x} \tilde{y} \bar{z} \dot{a} \ddot{b} \widehat{AB}", @"\max(a,b) \ln|x|",
                     @"P(A\mid B)=\frac{P(B\mid A)P(A)}{P(B)}", @"\lim_{n\to\infty}\left(1+\frac{1}{n}\right)^n = e",
                 })
            data.Add(extra);
        return data;
    }

    private static readonly OmmlOptions[] Variants =
    {
        new() { Display = false },
        new() { Display = true },
        new() { Display = true, MathFont = "XITS Math", IntegralFont = "Latin Modern Math", NarySizing = NarySizing.Grow, FontSizePt = 13 },
        new() { Display = true, Alignment = AlignmentStrategy.EquationArray },
    };

    /// <summary>
    /// Tiêu chí trung thực: dựng lại từ LaTeX chuyển ngược phải cho ra đúng OMML ban đầu (cùng hiển thị trong Word).
    /// Không đòi giống chuỗi LaTeX: ví dụ \nolimits thừa ở chế độ inline không ảnh hưởng gì nên không khôi phục được.
    /// </summary>
    [Theory]
    [MemberData(nameof(Formulas))]
    public void ReverseConversionReproducesTheSameEquation(string latex)
    {
        var doc = LatexParser.Parse(latex);
        Assert.False(doc.HasErrors, string.Join("\n", doc.Diagnostics));
        foreach (var options in Variants)
        {
            var omml = OmmlWriter.Write(doc, options).Element;
            var reverse = OmmlToLatex.Convert(omml);
            var rebuilt = OmmlWriter.Write(LatexParser.Parse(reverse.Latex), options).Element;
            Assert.True(XNode.DeepEquals(omml, rebuilt),
                $"options={options}\nreverse: {reverse.Latex}\nexpected:\n{omml}\nactual:\n{rebuilt}");
            Assert.Equal(options.Display, reverse.Display);
        }
    }

    [Theory]
    [InlineData(@"x \quad y \qquad z", @"x\quad y\qquad z")]
    [InlineData(@"3{,}14", @"3{,}14")]
    [InlineData(@"\underset{x}{\arg}", @"\underset{x}{\arg}")]
    [InlineData(@"\hat{AB}", @"\widehat{AB}")]
    [InlineData(@"\vec{AB} + \vec{v}", @"\overrightarrow{AB}+\vec{v}")]
    public void ReverseKeepsTypicalNotation(string latex, string expected)
    {
        var omml = OmmlWriter.Write(LatexParser.Parse(latex)).Element;
        Assert.Equal(expected, OmmlToLatex.Convert(omml).NormalizedLatex);
    }

    [Fact]
    public void ReverseReportsFontsAndGrow()
    {
        var omml = OmmlWriter.Write(LatexParser.Parse(@"\int_0^1 x\,dx"), new OmmlOptions { MathFont = "XITS Math", NarySizing = NarySizing.Grow }).Element;
        var r = OmmlToLatex.Convert(omml);
        Assert.Equal("XITS Math", r.PrimaryFont);
        Assert.True(r.Grow);
    }

    [Fact]
    public void ReverseLatexIsReadable()
    {
        var omml = OmmlWriter.Write(LatexParser.Parse(@"\alpha \le \beta")).Element;
        Assert.Equal(@"\alpha\le\beta", OmmlToLatex.Convert(omml).Latex);
    }
}
