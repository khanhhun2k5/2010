namespace MathTypeX.Parser.Tests;

public class PrinterTests
{
    [Theory]
    [InlineData(@"\frac12", @"\frac{1}{2}")]
    [InlineData(@"x^2_i", @"x_{i}^{2}")]
    [InlineData(@"\int_0^1 \frac{x^2}{1+x^2}\,dx", @"\int_{0}^{1}\frac{x^{2}}{1+x^{2}}\,dx")]
    [InlineData(@"\leq \neq \rightarrow", @"\le\ne\to")]
    [InlineData(@"\sin x", @"\sin x")]
    [InlineData(@"\lim_{x\to0}\frac{\sin x}{x}", @"\lim_{x\to0}\frac{\sin x}{x}")]
    [InlineData(@"\mathrm{d}x", @"\mathrm{d}x")]
    [InlineData(@"\mathbb{R}^n", @"\mathbb{R}^{n}")]
    [InlineData(@"\left( a \right)", @"\left(a\right)")]
    [InlineData(@"\binom nk", @"\binom{n}{k}")]
    [InlineData(@"f'(x)", @"f'(x)")]
    [InlineData(@"f''^2", @"f''^{2}")]
    [InlineData(@"\text{với mọi } x", @"\text{với mọi }x")]
    [InlineData(@"a\not= b", @"a\ne b")]
    [InlineData(@"\alpha\beta", @"\alpha\beta")]
    [InlineData(@"\sqrt[3]{x}", @"\sqrt[3]{x}")]
    [InlineData(@"\vec{v}\cdot\hat{n}", @"\vec{v}\cdot\hat{n}")]
    [InlineData(@"\overline{AB}", @"\overline{AB}")]
    [InlineData(@"a\perp b", @"a\perp b")]
    [InlineData(@"\top", @"\top")]
    public void NormalizedForm(string input, string expected) => Assert.Equal(expected, H.Norm(input));

    public static TheoryData<string> Corpus => new()
    {
        @"\alpha \beta \gamma \delta \epsilon \varepsilon \phi \varphi",
        @"\sum_{n=1}^{\infty} \frac{1}{n^2} = \frac{\pi^2}{6}",
        @"\prod_{k=1}^{n} k = n!",
        @"\int_0^1 x\,dx",
        @"\iint_D f(x,y)\,dx\,dy",
        @"\oint_C \vec F\cdot d\vec r",
        @"\left(\frac{a}{b}\right)",
        @"\left[\frac{\frac ab}{\frac cd}\right]",
        @"\sqrt{\frac{a}{b}}",
        @"\begin{pmatrix}a&b\\c&d\end{pmatrix}",
        @"\hat{x} \bar{x} \vec{x} \overline{AB}",
        @"\mathbb{R} \mathcal{F} \mathfrak{g} \mathbf{x} \mathrm{d}x",
        @"\begin{cases} x & x\ge0 \\ -x & x<0 \end{cases}",
        @"\begin{aligned} f(x) &= x^2+2x+1\\ &=(x+1)^2 \end{aligned}",
        @"X\sim N(\mu,\sigma^2)",
        @"E(X)=\mu.",
        @"\lim_{n\to\infty}\left(1+\frac1n\right)^n = e",
        @"P(A\mid B)=\frac{P(B\mid A)P(A)}{P(B)}",
        @"\overbrace{1+\cdots+1}^{n} \underbrace{x}_{k}",
        @"\xrightarrow[a]{f} \overset{!}{=} \underset{x}{\max}",
        @"\boxed{x} \cancel{y} \phantom{z}",
        @"\{x\in\mathbb{N} : x>2\}",
        @"\left\{ x \middle| x>0 \right\}",
        @"\bigl( x \bigr)",
        @"{n \choose k} {a \atop b}",
        @"x \quad y \qquad z \; w \: v \! u ~ t",
        @"\operatorname{rank} A \operatorname*{arg\,max}_x f",
        @"3{,}14 + 2.5",
        @"\int\limits_0^1 f \sum\nolimits_i a_i",
        @"E = mc^2 \tag{1}",
        @"\frac{a}{b",
        @"\left( x",
        @"\alpah",
    };

    [Theory]
    [MemberData(nameof(Corpus))]
    public void PrinterIsIdempotent(string input)
    {
        string once = H.Norm(input);
        string twice = H.Norm(once);
        Assert.Equal(once, twice);
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void NormalizedFormParsesWithoutNewErrors(string input)
    {
        var first = H.Parse(input);
        var second = H.Parse(LatexPrinter.Print(first));
        Assert.True(second.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error)
                    <= first.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error));
    }
}
