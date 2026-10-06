namespace MathTypeX.Render.Omml.Tests;

/// <summary>Ánh xạ AST → OMML theo từng node (docs/03 §6.3).</summary>
public class StructureTests
{
    [Fact]
    public void InlineRootIsOMathAndDisplayRootIsOMathPara()
    {
        Assert.Equal(O.M + "oMath", O.X("x").Name);
        var para = O.X("x", display: true);
        Assert.Equal(O.M + "oMathPara", para.Name);
        Assert.Equal("centerGroup", para.One("jc").Val());
    }

    [Fact]
    public void IntegralBecomesNaryWithOperandAndDifferential()
    {
        var x = O.X(@"\int_0^1 \frac{x^2}{1+x^2}\,dx");
        var nary = x.One("nary");
        Assert.Equal("∫", nary.Element(O.M + "naryPr")!.Element(O.M + "chr").Val());
        Assert.Equal("subSup", nary.Element(O.M + "naryPr")!.Element(O.M + "limLoc").Val());
        Assert.Equal("0", nary.Element(O.M + "sub")!.Text());
        Assert.Equal("1", nary.Element(O.M + "sup")!.Text());
        var e = nary.Element(O.M + "e")!;
        Assert.NotNull(e.Element(O.M + "f"));
        Assert.EndsWith("dx", e.Text());
        Assert.Null(nary.Descendants(O.M + "grow").FirstOrDefault()); // mặc định TeX: không co giãn
    }

    [Fact]
    public void GrowSizingSetsMGrow()
    {
        var x = O.Write(@"\int_0^1 f\,dx", new OmmlOptions { NarySizing = NarySizing.Grow }).Element;
        Assert.Equal("1", x.One("grow").Val());
    }

    [Fact]
    public void IntegralFontGoesToNaryControlProperties()
    {
        var x = O.Write(@"\int f\,dx", new OmmlOptions { MathFont = "XITS Math", IntegralFont = "Latin Modern Math" }).Element;
        var fonts = x.One("naryPr").Element(O.M + "ctrlPr")!.Descendants(O.W + "rFonts").Single();
        Assert.Equal("Latin Modern Math", fonts.Attribute(O.W + "ascii")!.Value);
        Assert.All(x.Descendants(O.M + "r"), r => Assert.Equal("XITS Math", r.Descendants(O.W + "rFonts").Single().Attribute(O.W + "ascii")!.Value));
    }

    [Fact]
    public void MissingLimitsAreHidden()
    {
        var pr = O.X(@"\int x\,dx").One("naryPr");
        Assert.Equal("1", pr.Element(O.M + "subHide").Val());
        Assert.Equal("1", pr.Element(O.M + "supHide").Val());
    }

    [Theory]
    [InlineData(@"\sum_{i=1}^n i", false, "subSup")]
    [InlineData(@"\sum_{i=1}^n i", true, "undOvr")]
    [InlineData(@"\int_0^1 x\,dx", true, "subSup")]
    [InlineData(@"\int\limits_0^1 x\,dx", true, "undOvr")]
    [InlineData(@"\sum\nolimits_i a_i", true, "subSup")]
    public void LimitPlacementFollowsTeX(string latex, bool display, string expected) =>
        Assert.Equal(expected, O.X(latex, display).One("limLoc").Val());

    [Fact]
    public void FractionAndBinomial()
    {
        Assert.Null(O.X(@"\frac{a}{b}").One("fPr").Element(O.M + "type"));
        var binom = O.X(@"\binom{n}{k}");
        Assert.Equal("(", binom.One("begChr").Val());
        Assert.Equal("noBar", binom.One("type").Val());
    }

    [Fact]
    public void RadicalHidesDegreeOnlyWithoutIndex()
    {
        Assert.Equal("1", O.X(@"\sqrt{x}").One("degHide").Val());
        var cube = O.X(@"\sqrt[3]{x}");
        Assert.Empty(cube.Descendants(O.M + "degHide"));
        Assert.Equal("3", cube.One("deg").Text());
    }

    [Fact]
    public void ScriptsChooseTheRightElement()
    {
        Assert.NotNull(O.X("x^2").Descendants(O.M + "sSup").SingleOrDefault());
        Assert.NotNull(O.X("x_i").Descendants(O.M + "sSub").SingleOrDefault());
        Assert.NotNull(O.X("x_i^2").Descendants(O.M + "sSubSup").SingleOrDefault());
    }

    [Fact]
    public void FencedWithMiddleUsesSeparator()
    {
        var d = O.X(@"\left\{ x \middle| x>0 \right\}").One("d");
        Assert.Equal(("{", "|", "}"), (d.Descendants(O.M + "begChr").Single().Val(), d.Descendants(O.M + "sepChr").Single().Val(), d.Descendants(O.M + "endChr").Single().Val()));
        Assert.Equal(2, d.Elements(O.M + "e").Count());
    }

    [Fact]
    public void InvisibleLeftDelimiterIsEmptyString()
    {
        Assert.Equal("", O.X(@"\left. F \right|_0^1").One("begChr").Val());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LimitFunctionsUseLimLow(bool display)
    {
        var x = O.X(@"\lim_{x\to 0} \frac{\sin x}{x}", display);
        var func = x.Descendants(O.M + "func").First();
        Assert.NotNull(func.Element(O.M + "fName")!.Element(O.M + "limLow"));
        Assert.Equal("lim", func.Element(O.M + "fName")!.Descendants(O.M + "t").First().Value);
    }

    [Fact]
    public void PowerOfFunctionUsesScriptInsideFunctionName()
    {
        var func = O.X(@"\sin^2 x").Descendants(O.M + "func").Single();
        Assert.NotNull(func.Element(O.M + "fName")!.Element(O.M + "sSup"));
        Assert.NotNull(O.X(@"\lim\nolimits_{n} a_n").Descendants(O.M + "fName").Single().Element(O.M + "sSub"));
    }

    [Fact]
    public void FunctionNameIsUpright()
    {
        var r = O.X(@"\sin x").Descendants(O.M + "fName").Single().Element(O.M + "r")!;
        Assert.Equal("p", r.Element(O.M + "rPr")!.Element(O.M + "sty").Val());
    }

    [Fact]
    public void MathAlphabetsAndUprightGreek()
    {
        Assert.Equal("ℝ", O.X(@"\mathbb{R}").Text());
        Assert.Equal("ℱ", O.X(@"\mathcal{F}").Text());
        Assert.Equal("𝔤", O.X(@"\mathfrak{g}").Text());
        Assert.Equal("b", O.X(@"\mathbf{x}").One("sty").Val());
        Assert.Equal("p", O.X(@"\Gamma").One("sty").Val());
        Assert.Equal("p", O.X(@"\mathrm{d}").One("sty").Val());
        Assert.Empty(O.X(@"\alpha").Descendants(O.M + "sty")); // Hy Lạp thường: Word tự nghiêng
    }

    [Fact]
    public void UprightDifferentialOption()
    {
        var x = O.Write(@"\int f\,dx", new OmmlOptions { UprightDifferential = true }).Element;
        Assert.Contains(x.Descendants(O.M + "r"), r => r.Element(O.M + "t")!.Value == "d" && r.Descendants(O.M + "sty").Single().Val() == "p");
    }

    [Fact]
    public void TextRunUsesNormalTextAndTextFont()
    {
        var r = O.X(@"\text{với mọi } x").Descendants(O.M + "r").First();
        Assert.NotNull(r.Element(O.M + "rPr")!.Element(O.M + "nor"));
        Assert.Equal("Times New Roman", r.Descendants(O.W + "rFonts").Single().Attribute(O.W + "ascii")!.Value);
        Assert.Equal("với mọi ", r.Element(O.M + "t")!.Value);
    }

    [Fact]
    public void MinusIsTrueMinusSign()
    {
        Assert.Equal("a−b", O.X("a-b").Text());
    }

    [Fact]
    public void AdjacentAtomsShareOneRun()
    {
        var x = O.X("1+x");
        Assert.Single(x.Elements(O.M + "r"));
    }

    [Fact]
    public void PMatrixIsMatrixInsideParentheses()
    {
        var x = O.X(@"\begin{pmatrix} a & b \\ c & d \end{pmatrix}");
        var d = x.Elements(O.M + "d").Single();
        Assert.Equal("(", d.One("begChr").Val());
        var m = d.One("m");
        Assert.Equal(2, m.Elements(O.M + "mr").Count());
        Assert.All(m.Elements(O.M + "mr"), mr => Assert.Equal(2, mr.Elements(O.M + "e").Count()));
        Assert.Equal(2, m.Descendants(O.M + "mc").Count());
    }

    [Fact]
    public void CasesHaveLeftBraceOnlyAndLeftAlignedColumns()
    {
        var x = O.X(@"\begin{cases} x & x\ge0 \\ -x & x<0 \end{cases}");
        Assert.Equal(("{", ""), (x.One("begChr").Val(), x.One("endChr").Val()));
        Assert.All(x.Descendants(O.M + "mcJc"), j => Assert.Equal("left", j.Val()));
    }

    [Fact]
    public void AlignedAsMatrixAlternatesRightLeft()
    {
        var x = O.X(@"\begin{aligned} f(x) &= x^2 \\ &= y \end{aligned}", display: true);
        var jc = x.Descendants(O.M + "mcJc").Select(j => j.Val()).ToArray();
        Assert.Equal(new[] { "right", "left" }, jc);
        Assert.Equal("0", x.One("cGp").Val());
    }

    [Fact]
    public void AlignedAsEquationArrayKeepsAmpersand()
    {
        var x = O.Write(@"\begin{aligned} f(x) &= x^2 \\ &= y \end{aligned}", new OmmlOptions { Alignment = AlignmentStrategy.EquationArray }).Element;
        var arr = x.One("eqArr");
        Assert.Equal(2, arr.Elements(O.M + "e").Count());
        Assert.All(arr.Elements(O.M + "e"), e => Assert.Contains("&", e.Text()));
    }

    [Fact]
    public void AccentsAndBars()
    {
        Assert.Equal("⃗", O.X(@"\vec{x}").One("chr").Val());
        Assert.Equal("top", O.X(@"\overline{AB}").One("pos").Val());
        var brace = O.X(@"\overbrace{a+b}^{n}");
        Assert.NotNull(brace.Descendants(O.M + "limUpp").SingleOrDefault());
        Assert.Equal("⏞", brace.One("chr").Val());
    }

    [Fact]
    public void FontSizeIsWrittenInHalfPoints()
    {
        var x = O.Write("x", new OmmlOptions { FontSizePt = 13 }).Element;
        Assert.Equal("26", x.Descendants(O.W + "sz").First().Attribute(O.W + "val")!.Value);
    }

    [Fact]
    public void ApproximationsAreReported()
    {
        Assert.Contains(@"\big", O.Write(@"\bigl( x \bigr)").Approximations);
        Assert.Contains(@"\!", O.Write(@"a\!b").Approximations);
    }

    [Fact]
    public void FlatOpcWrapsMathInAParagraph()
    {
        string pkg = FlatOpc.ForMath(O.X("x^2"));
        Assert.StartsWith("<?xml", pkg);
        var doc = XDocument.Parse(pkg);
        Assert.Single(doc.Descendants(O.W + "p"));
        Assert.Single(doc.Descendants(O.M + "oMath"));
        Assert.Contains("mso-application", pkg);
    }
}
