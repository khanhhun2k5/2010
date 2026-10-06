namespace MathTypeX.Parser.Tests;

public class ParserTests
{
    [Fact]
    public void IdentifiersNumbersAndOperators()
    {
        var n = H.Nodes("x+3.14=-y");
        Assert.Equal("x", H.As<Identifier>(n[0]).Text);
        Assert.Equal(AtomClass.Bin, H.As<Operator>(n[1]).Class);
        Assert.Equal("3.14", H.As<Number>(n[2]).Text);
        Assert.Equal(AtomClass.Rel, H.As<Operator>(n[3]).Class);
        var minus = H.As<Operator>(n[4]);
        Assert.Equal("−", minus.Text);
        Assert.Equal(AtomClass.Ord, minus.Class); // dấu trừ một ngôi sau quan hệ
    }

    [Fact]
    public void DecimalCommaOption()
    {
        Assert.Equal(3, H.Parse("3,14").Body.Children.Count);
        var n = H.Parse("3,14", decimalComma: true).Body.Children;
        Assert.Equal("3,14", H.As<Number>(Assert.Single(n)).Text);
        Assert.Equal("3,14", H.As<Number>(H.Single("3{,}14")).Text);
    }

    [Fact]
    public void FractionTakesSingleTokenArguments()
    {
        var f = H.As<Fraction>(H.Single(@"\frac12"));
        Assert.Equal("1", H.As<Number>(f.Numerator).Text);
        Assert.Equal("2", H.As<Number>(f.Denominator).Text);
    }

    [Fact]
    public void FractionStyles()
    {
        Assert.Equal(MathStyleOverride.Display, H.As<Fraction>(H.Single(@"\dfrac{a}{b}")).Style);
        Assert.Equal(MathStyleOverride.Text, H.As<Fraction>(H.Single(@"\tfrac{a}{b}")).Style);
    }

    [Fact]
    public void ScriptsFollowTeXSemantics()
    {
        var s = H.As<Scripts>(H.Nodes("x^23")[0]);
        Assert.Equal("2", H.As<Number>(s.Sup!).Text);
        Assert.Equal("3", H.As<Number>(H.Nodes("x^23")[1]).Text);

        var both = H.As<Scripts>(H.Single("x_i^2"));
        Assert.Equal("i", H.Text(both.Sub!));
        Assert.Equal("2", H.Text(both.Sup!));
    }

    [Fact]
    public void DoubleSuperscriptIsReportedAndRecovered()
    {
        var doc = H.Parse("x^2^3");
        Assert.Contains(doc.Diagnostics, d => d.Code == DiagnosticCode.DoubleSuperscript);
        var s = H.As<Scripts>(Assert.Single(doc.Body.Children));
        Assert.IsType<Group>(s.Base);
    }

    [Fact]
    public void Primes()
    {
        var s = H.As<Scripts>(H.Single("f''(x)".Substring(0, 3)));
        Assert.Equal("″", H.As<Operator>(s.Sup!).Text);
        var withPower = H.As<Scripts>(H.Single("f'^2"));
        Assert.Equal("′2", H.Text(withPower.Sup!));
    }

    [Fact]
    public void RadicalWithIndex()
    {
        var r = H.As<Radical>(H.Single(@"\sqrt[3]{x+1}"));
        Assert.Equal("3", H.Text(r.Index!));
        Assert.Equal("x+1", H.Text(r.Radicand));
    }

    [Theory]
    [InlineData(@"\epsilon", "ϵ")]
    [InlineData(@"\varepsilon", "ε")]
    [InlineData(@"\phi", "ϕ")]
    [InlineData(@"\varphi", "φ")]
    [InlineData(@"\Gamma", "Γ")]
    [InlineData(@"\vartheta", "ϑ")]
    public void GreekLettersMatchTeX(string latex, string expected) =>
        Assert.Equal(expected, H.As<Identifier>(H.Single(latex)).Text);

    [Fact]
    public void VariantsMapToBaseLetterAndVariant()
    {
        var r = H.As<Identifier>(H.Single(@"\mathbb{R}"));
        Assert.Equal(("R", MathVariant.DoubleStruck), (r.Text, r.Variant));
        var typed = H.As<Identifier>(H.Single("ℝ"));
        Assert.Equal(("R", MathVariant.DoubleStruck), (typed.Text, typed.Variant));
        Assert.Equal(MathVariant.Calligraphic, H.As<Identifier>(H.Single(@"\mathcal{F}")).Variant);
        Assert.Equal(MathVariant.Fraktur, H.As<Identifier>(H.Single(@"\mathfrak{g}")).Variant);
        Assert.Equal(MathVariant.Bold, H.As<Identifier>(H.Single(@"\mathbf{x}")).Variant);
        Assert.Equal(MathVariant.BoldItalic, H.As<Identifier>(H.Single(@"\boldsymbol{\alpha}")).Variant);
        Assert.Equal(MathVariant.Bold, H.As<Identifier>(H.Single(@"\boldsymbol{\Gamma}")).Variant);
    }

    [Fact]
    public void InnerVariantWins()
    {
        var id = H.As<Identifier>(H.Single(@"\mathbf{\mathcal{F}}"));
        Assert.Equal(MathVariant.Calligraphic, id.Variant);
    }

    [Fact]
    public void NotCombinesIntoNegatedRelation()
    {
        Assert.Equal("≠", H.As<Operator>(H.Nodes(@"a\not=b")[1]).Text);
        Assert.Equal("∉", H.As<Operator>(H.Nodes(@"x\not\in A")[1]).Text);
    }

    [Fact]
    public void LeftRightWithMiddle()
    {
        var f = H.As<Fenced>(H.Single(@"\left\{ x \middle| x>0 \right\}"));
        Assert.Equal(("{", "}"), (f.Open, f.Close));
        Assert.Equal(2, f.Parts.Count);
        Assert.Equal("|", Assert.Single(f.Separators));
    }

    [Fact]
    public void LeftDotIsInvisibleDelimiter()
    {
        var s = H.As<Scripts>(H.Single(@"\left. F(x) \right|_0^1"));
        var f = H.As<Fenced>(s.Base);
        Assert.Equal(("", "|"), (f.Open, f.Close));
    }

    [Fact]
    public void BigDelimiters()
    {
        var op = H.As<Operator>(H.Nodes(@"\bigl( x \bigr)")[0]);
        Assert.Equal((DelimiterSize.Big, AtomClass.Open), (op.Size, op.Class));
    }

    [Fact]
    public void BinomIsFencedNoBarFraction()
    {
        var f = H.As<Fenced>(H.Single(@"\binom{n}{k}"));
        Assert.Equal(FractionKind.NoBar, H.As<Fraction>(f.Parts[0]).Kind);
        var choose = H.As<Group>(H.Single(@"{n \choose k}"));
        Assert.IsType<Fenced>(Assert.Single(choose.Content.Children));
    }

    [Fact]
    public void OverInfix()
    {
        var g = H.As<Group>(H.Single(@"{a+b \over c}"));
        var f = H.As<Fraction>(Assert.Single(g.Content.Children));
        Assert.Equal("a+b", H.Text(f.Numerator));
    }

    [Fact]
    public void TextKeepsVietnameseAndNormalizesToNfc()
    {
        // "với" gõ ở dạng tổ hợp (NFD): v + o + U+031B + U+0300
        string nfd = "vo\u031B\u0301i m\u1ECDi";
        var n = H.Nodes(@"\text{" + nfd + "} x");
        Assert.Equal("với mọi".Normalize(System.Text.NormalizationForm.FormC), H.As<TextRun>(n[0]).Text);
        Assert.IsType<Identifier>(n[1]);
    }

    [Fact]
    public void TextWithNestedMath()
    {
        // Các mảnh chữ và toán được trải phẳng vào dòng ngoài.
        var n = H.Nodes(@"\text{khi $x>0$ thì}");
        Assert.Equal(5, n.Count);
        Assert.Equal("khi ", H.As<TextRun>(n[0]).Text);
        Assert.IsType<Identifier>(n[1]);
        Assert.Equal(" thì", H.As<TextRun>(n[4]).Text);
    }

    [Fact]
    public void OverbraceTakesLabelFromSuperscript()
    {
        var gc = H.As<GroupChar>(H.Single(@"\overbrace{a+b}^{n}"));
        Assert.Equal("n", H.Text(gc.Label!));
        var arrow = H.As<GroupChar>(H.Single(@"\xrightarrow[t]{f}"));
        Assert.Equal(("→", "f", "t"), (arrow.Char, H.Text(arrow.Base), H.Text(arrow.Label!)));
    }

    [Fact]
    public void EmptyInputIsEmptyRow()
    {
        var doc = H.Parse("");
        Assert.Empty(doc.Body.Children);
        Assert.Empty(doc.Diagnostics);
    }

    [Fact]
    public void TopLevelTagWrapsIntoEquation()
    {
        var al = H.As<Alignment>(H.Single(@"E=mc^2 \tag{1}\label{eq:energy}"));
        Assert.Equal(AlignmentKind.Equation, al.Kind);
        Assert.Equal(("1", "eq:energy"), (al.Rows[0].Tag, al.Rows[0].Label));
    }
}
