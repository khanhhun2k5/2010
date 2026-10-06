namespace MathTypeX.Documents.Tests;

/// <summary>
/// Word ghi lại OMML theo cách riêng; khoá chuẩn hoá phải không đổi. Các biến đổi dưới đây mô phỏng những gì Word
/// có thể làm (giả định — S1 sẽ xác nhận bằng golden file thật).
/// </summary>
public class WordLikeOmmlTests
{
    private static readonly XNamespace M = OmmlWriter.M;
    private static readonly XNamespace W = OmmlWriter.W;

    private static XElement Generate(string latex, bool display = false) =>
        OmmlWriter.Write(LatexParser.Parse(latex), new OmmlOptions { Display = display }).Element;

    /// <summary>Tách mỗi m:r thành một run cho từng ký tự.</summary>
    private static XElement SplitRuns(XElement root)
    {
        var copy = new XElement(root);
        foreach (var run in copy.Descendants(M + "r").ToArray())
        {
            string text = run.Element(M + "t")!.Value;
            if (text.Length <= 1) continue;
            var pieces = text.Select(c =>
            {
                var r = new XElement(run);
                r.Element(M + "t")!.Value = c.ToString();
                return r;
            }).ToArray();
            run.ReplaceWith(pieces);
        }
        return copy;
    }

    /// <summary>Thêm w:i, rsid, ctrlPr với font khác, bỏ xml:space — những thứ không mang nghĩa toán.</summary>
    private static XElement Decorate(XElement root)
    {
        var copy = new XElement(root);
        foreach (var rPr in copy.Descendants(W + "rPr").ToArray())
        {
            // Word đặt w:i cho run toán; run chữ thường (m:nor) giữ nguyên định dạng thật của chữ.
            bool normalText = rPr.Parent?.Element(M + "rPr")?.Element(M + "nor") is not null;
            if (!normalText) rPr.Add(new XElement(W + "i"));
            rPr.SetAttributeValue(W + "rsidR", "00AB12CD");
        }
        foreach (var ctrl in copy.Descendants(M + "ctrlPr").ToArray())
            ctrl.ReplaceWith(new XElement(M + "ctrlPr", new XElement(W + "rPr", new XElement(W + "rFonts", new XAttribute(W + "ascii", "Cambria Math")), new XElement(W + "i"))));
        foreach (var t in copy.Descendants(M + "t")) t.Attribute(XNamespace.Xml + "space")?.Remove();
        foreach (var e in copy.Descendants(M + "e").Where(e => !e.HasElements).ToArray())
            e.Add(new XElement(M + "ctrlPr"));
        return copy;
    }

    [Theory]
    [InlineData(@"\int_0^1 \frac{x^2}{1+x^2}\,dx")]
    [InlineData(@"X\sim N(\mu,\sigma^2)")]
    [InlineData(@"\lim_{x\to 0} \frac{\sin x}{x} = 1")]
    [InlineData(@"\begin{pmatrix} a & b \\ c & d \end{pmatrix}")]
    [InlineData(@"\mathbb{R}^n \alpha_1 + \Gamma")]
    [InlineData(@"\text{với mọi } x")]
    public void KeySurvivesWordStyleRewrites(string latex)
    {
        foreach (bool display in new[] { false, true })
        {
            var original = Generate(latex, display);
            string key = EquationIdentity.KeyOf(original);
            Assert.StartsWith(EquationIdentity.Prefix, key);
            Assert.Equal(key, EquationIdentity.KeyOf(SplitRuns(original)));
            Assert.Equal(key, EquationIdentity.KeyOf(Decorate(original)));
            Assert.Equal(key, EquationIdentity.KeyOf(Decorate(SplitRuns(original))));
        }
    }

    [Fact]
    public void KeyDiffersForDifferentContentButNotForFont()
    {
        string a = EquationIdentity.KeyOf(Generate(@"x^2"));
        string b = EquationIdentity.KeyOf(Generate(@"x^3"));
        string c = EquationIdentity.KeyOf(OmmlWriter.Write(LatexParser.Parse(@"x^2"), new OmmlOptions { MathFont = "XITS Math" }).Element);
        Assert.NotEqual(a, b);
        Assert.Equal(a, c);
        Assert.Equal(a, EquationIdentity.KeyOf(Generate(@"x^{2}", display: true)));
    }

    [Fact]
    public void ExtractsEquationFromWordOpenXml()
    {
        string pkg = FlatOpc.ForMath(Generate(@"\sqrt{2}"));
        var eq = OmmlExtractor.FirstEquation(pkg);
        Assert.NotNull(eq);
        Assert.Equal(@"\sqrt{2}", OmmlToLatex.Convert(eq!).NormalizedLatex);
    }

    /// <summary>OMML viết theo kiểu Word tự sinh (công thức Pythagore và nghiệm bậc hai trong Equation gallery).</summary>
    [Fact]
    public void ReverseConvertsWordAuthoredEquations()
    {
        const string ns = "xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\" xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";
        const string rpr = "<w:rPr><w:rFonts w:ascii=\"Cambria Math\" w:hAnsi=\"Cambria Math\"/></w:rPr>";
        const string ctrl = "<m:ctrlPr><w:rPr><w:rFonts w:ascii=\"Cambria Math\" w:hAnsi=\"Cambria Math\"/><w:i/></w:rPr></m:ctrlPr>";
        string pythagoras = $"<m:oMathPara {ns}><m:oMath>"
            + $"<m:sSup><m:sSupPr>{ctrl}</m:sSupPr><m:e><m:r>{rpr}<m:t>a</m:t></m:r></m:e><m:sup><m:r>{rpr}<m:t>2</m:t></m:r></m:sup></m:sSup>"
            + $"<m:r>{rpr}<m:t>+</m:t></m:r>"
            + $"<m:sSup><m:sSupPr>{ctrl}</m:sSupPr><m:e><m:r>{rpr}<m:t>b</m:t></m:r></m:e><m:sup><m:r>{rpr}<m:t>2</m:t></m:r></m:sup></m:sSup>"
            + $"<m:r>{rpr}<m:t>=</m:t></m:r>"
            + $"<m:sSup><m:sSupPr>{ctrl}</m:sSupPr><m:e><m:r>{rpr}<m:t>c</m:t></m:r></m:e><m:sup><m:r>{rpr}<m:t>2</m:t></m:r></m:sup></m:sSup>"
            + "</m:oMath></m:oMathPara>";
        var p = OmmlToLatex.Convert(XElement.Parse(pythagoras));
        Assert.Equal("a^{2}+b^{2}=c^{2}", p.NormalizedLatex);
        Assert.True(p.Display);

        string quadratic = $"<m:oMath {ns}><m:r>{rpr}<m:t>x=</m:t></m:r><m:f><m:fPr>{ctrl}</m:fPr><m:num>"
            + $"<m:r>{rpr}<m:t>−b±</m:t></m:r><m:rad><m:radPr><m:degHide m:val=\"1\"/>{ctrl}</m:radPr><m:deg/><m:e>"
            + $"<m:sSup><m:sSupPr>{ctrl}</m:sSupPr><m:e><m:r>{rpr}<m:t>b</m:t></m:r></m:e><m:sup><m:r>{rpr}<m:t>2</m:t></m:r></m:sup></m:sSup>"
            + $"<m:r>{rpr}<m:t>−4ac</m:t></m:r></m:e></m:rad></m:num><m:den><m:r>{rpr}<m:t>2a</m:t></m:r></m:den></m:f></m:oMath>";
        Assert.Equal(@"x=\frac{-b\pm\sqrt{b^{2}-4ac}}{2a}", OmmlToLatex.Convert(XElement.Parse(quadratic)).NormalizedLatex);
    }

    [Fact]
    public void TrackedDeletionsInsideEquationsAreIgnored()
    {
        string xml = "<m:oMath xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\" xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
            + "<m:r><m:t>x+</m:t></m:r><w:del w:id=\"1\" w:author=\"A\"><m:r><m:t>1</m:t></m:r></w:del><w:ins w:id=\"2\" w:author=\"A\"><m:r><m:t>2</m:t></m:r></w:ins></m:oMath>";
        Assert.Equal("x+2", OmmlToLatex.Convert(XElement.Parse(xml)).NormalizedLatex);
    }
}
