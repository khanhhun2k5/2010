namespace MathTypeX.Render.MathMl.Tests;

public class MathMlTests
{
    private static readonly XNamespace N = MathMlWriter.Ns;

    private static XElement X(string latex, bool display = false, bool grow = false) =>
        MathMlWriter.Write(LatexParser.Parse(latex).Body, new MathMlOptions { Display = display, GrowLargeOperators = grow });

    [Fact]
    public void RootCarriesDisplayMode()
    {
        Assert.Equal("inline", X("x").Attribute("display")!.Value);
        Assert.Equal("block", X("x", display: true).Attribute("display")!.Value);
    }

    [Fact]
    public void IntegralIsLargeOperatorWithSideLimitsAndOperand()
    {
        var x = X(@"\int_0^1 \frac{x^2}{1+x^2}\,dx", display: true);
        var subsup = x.Descendants(N + "msubsup").Single();
        var mo = subsup.Elements().First();
        Assert.Equal(("mo", "∫", "true", "mtx-int"), (mo.Name.LocalName, mo.Value, mo.Attribute("largeop")!.Value, mo.Attribute("class")!.Value));
        Assert.NotNull(x.Descendants(N + "mfrac").SingleOrDefault());
        Assert.Null(mo.Attribute("stretchy"));
    }

    [Fact]
    public void GrowMakesTheIntegralStretchy()
    {
        var mo = X(@"\int_0^1 f\,dx", grow: true).Descendants(N + "mo").First();
        Assert.Equal("true", mo.Attribute("stretchy")!.Value);
    }

    [Fact]
    public void SumUsesUnderOverOnlyInDisplay()
    {
        Assert.Single(X(@"\sum_{i=1}^n i", display: true).Descendants(N + "munderover"));
        Assert.Single(X(@"\sum_{i=1}^n i").Descendants(N + "msubsup"));
    }

    [Fact]
    public void FunctionGetsApplicationOperator()
    {
        var x = X(@"\sin x");
        Assert.Equal("sin", x.Descendants(N + "mi").First().Value);
        Assert.Contains(x.Descendants(N + "mo"), mo => mo.Value == "⁡");
    }

    [Fact]
    public void VariantsUseUnicodeMathAlphanumerics()
    {
        Assert.Equal("ℝ", X(@"\mathbb{R}").Descendants(N + "mi").Single().Value);
        Assert.Equal("𝐱", X(@"\mathbf{x}").Descendants(N + "mi").Single().Value);
        var d = X(@"\mathrm{d}").Descendants(N + "mi").Single();
        Assert.Equal("normal", d.Attribute("mathvariant")!.Value);
        Assert.Equal("normal", X(@"\Gamma").Descendants(N + "mi").Single().Attribute("mathvariant")!.Value);
    }

    [Fact]
    public void FencesAreStretchy()
    {
        var mos = X(@"\left( \frac{a}{b} \right)").Descendants(N + "mo").ToArray();
        Assert.All(mos, mo => Assert.Equal("true", mo.Attribute("stretchy")!.Value));
        Assert.Equal(new[] { "(", ")" }, mos.Select(m => m.Value));
    }

    [Fact]
    public void MatricesAndCases()
    {
        var p = X(@"\begin{pmatrix} a & b \\ c & d \end{pmatrix}");
        Assert.Equal(2, p.Descendants(N + "mtr").Count());
        Assert.Equal(4, p.Descendants(N + "mtd").Count());
        var cases = X(@"\begin{cases} x & x\ge0 \\ -x & x<0 \end{cases}");
        Assert.All(cases.Descendants(N + "mtd"), td => Assert.Contains("text-align:left", td.Attribute("style")!.Value));
    }

    [Fact]
    public void PlaceholdersAreVisible()
    {
        Assert.Single(X(@"\frac{a}{}").Descendants(N + "mrow"), r => !r.HasElements); // mẫu số rỗng
        Assert.Contains(X(@"\frac{a}").Descendants(N + "mi"), mi => mi.Attribute("class")?.Value == "mtx-ph");
    }

    [Fact]
    public void TextRunIsMtextAndKeepsEdgeSpaces()
    {
        Assert.Equal("v\u1EDBi m\u1ECDi\u00A0", X("\\text{v\u1EDBi m\u1ECDi } x").Descendants(N + "mtext").Single().Value);
    }

    [Fact]
    public void FunctionArgumentGetsThinSpaceUnlessFenced()
    {
        Assert.Single(X(@"\sin x").Descendants(N + "mspace"));
        Assert.Empty(X(@"\sin(x)").Descendants(N + "mspace"));
        Assert.Empty(X(@"\ln|x|").Descendants(N + "mspace"));
    }

    [Fact]
    public void ErrorsAreMarked()
    {
        Assert.Contains(X(@"\alpah").Descendants(N + "mtext"), t => t.Attribute("class")?.Value == "mtx-err");
    }

    public static TheoryData<string> Corpus()
    {
        var data = new TheoryData<string>();
        foreach (var file in new[] { "typography.tex", "integrals.tex" })
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MathTypeX.slnx"))) dir = dir.Parent;
            foreach (var line in File.ReadAllLines(Path.Combine(dir!.FullName, "tests", "corpus", file)))
                if (line.Trim().Length > 0 && !line.TrimStart().StartsWith('%')) data.Add(line.Trim());
        }
        return data;
    }

    /// <summary>Bất biến D9 cho preview: ∫ ∑ ∏ luôn là &lt;mo largeop&gt;.</summary>
    [Theory]
    [MemberData(nameof(Corpus))]
    public void LargeOperatorsAreAlwaysLargeOp(string latex)
    {
        foreach (bool display in new[] { false, true })
        {
            var x = X(latex, display);
            foreach (var el in x.DescendantNodes().OfType<XText>())
            {
                if (el.Value.IndexOfAny("∫∬∭∮∑∏".ToCharArray()) < 0) continue;
                var mo = el.Parent!;
                Assert.Equal("mo", mo.Name.LocalName);
                Assert.Equal("true", mo.Attribute("largeop")?.Value);
            }
        }
    }

    [Fact]
    public void PreviewPageContainsRenderFunction()
    {
        string html = PreviewPage.LivePage();
        Assert.Contains("function mtxRender", html);
        Assert.Contains("--mtx-int", html);
    }
}
