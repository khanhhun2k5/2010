namespace MathTypeX.Editing.Tests;

public class ComposerTests
{
    private static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Theory]
    [InlineData("$x^2$", "x^2", false)]
    [InlineData("$$x^2$$", "x^2", true)]
    [InlineData(@"\(x^2\)", "x^2", false)]
    [InlineData(@"\[x^2\]", "x^2", true)]
    [InlineData("  x^2  ", "x^2", null)]
    [InlineData(@"100\$", @"100\$", null)]
    public void StripDelimiters(string input, string latex, bool? display)
    {
        Assert.Equal((latex, display), EquationComposer.StripDelimiters(input));
    }

    [Fact]
    public void AcceptanceFormulaComposesToFlatOpcWithNaryAndFont()
    {
        var outcome = EquationComposer.Compose(@"\int_0^1 \frac{x^2}{1+x^2}\,dx", new ComposeOptions { MathFont = "XITS Math", FontSizePt = 13 });
        Assert.Null(outcome.BlockingMessage);
        var result = Assert.IsType<EditResult>(outcome.Result);
        Assert.False(result.Cancelled);
        Assert.Equal(@"\int_{0}^{1}\frac{x^{2}}{1+x^{2}}\,dx", result.NormalizedLatex);

        var pkg = XDocument.Parse(result.FlatOpc);
        Assert.Single(pkg.Descendants(M + "oMath"));
        Assert.Single(pkg.Descendants(M + "nary"));
        Assert.All(pkg.Descendants(W + "rFonts"), f => Assert.Equal("XITS Math", f.Attribute(W + "ascii")!.Value));
        Assert.All(pkg.Descendants(W + "sz"), s => Assert.Equal("26", s.Attribute(W + "val")!.Value));
    }

    [Fact]
    public void DisplayDelimiterProducesOMathPara()
    {
        var result = EquationComposer.Compose(@"\[E(X)=\mu.\]", new ComposeOptions()).Result!;
        Assert.True(result.Display);
        Assert.Single(XDocument.Parse(result.FlatOpc).Descendants(M + "oMathPara"));
    }

    [Fact]
    public void ErrorsBlockInsertionWithVietnameseMessage()
    {
        var outcome = EquationComposer.Compose(@"\frac{a}{b", new ComposeOptions());
        Assert.Null(outcome.Result);
        Assert.Equal("Bạn đang thiếu dấu } để kết thúc mẫu số.", outcome.BlockingMessage);
    }

    [Theory]
    [InlineData(@"\frac{}{}")]
    [InlineData(@"\frac{a}{}")]
    [InlineData(@"\sqrt{}")]
    [InlineData(@"x^{}")]
    [InlineData(@"\hat{}")]
    public void EmptySlotsBlockInsertionUnlessForced(string latex)
    {
        Assert.Contains("ô trống", EquationComposer.Compose(latex, new ComposeOptions()).BlockingMessage);
        Assert.NotNull(EquationComposer.Compose(latex, new ComposeOptions(), allowEmptySlots: true).Result);
    }

    [Fact]
    public void EmptyInputIsBlocked()
    {
        Assert.Equal("Chưa nhập công thức.", EquationComposer.Compose("   ", new ComposeOptions()).BlockingMessage);
    }

    [Fact]
    public void TextFontFollowsTheDocument()
    {
        var result = EquationComposer.Compose(@"x \text{ với mọi } y", new ComposeOptions { TextFont = "Arial" }).Result!;
        var textRun = XDocument.Parse(result.FlatOpc).Descendants(M + "r").Single(r => r.Element(M + "rPr")?.Element(M + "nor") is not null);
        Assert.Equal("Arial", textRun.Descendants(W + "rFonts").Single().Attribute(W + "ascii")!.Value);
    }
}
