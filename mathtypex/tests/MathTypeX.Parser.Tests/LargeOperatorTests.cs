namespace MathTypeX.Parser.Tests;

/// <summary>Bắt phần thân của ∫ ∑ ∏ (docs/03 §6.4) — nền tảng cho m:nary và chế độ Grow.</summary>
public class LargeOperatorTests
{
    private static LargeOperator Op(string latex) => H.As<LargeOperator>(H.Nodes(latex)[0]);

    [Fact]
    public void IntegralFoldsLimitsAndCapturesUpToDifferential()
    {
        var op = Op(@"\int_0^1 \frac{x^2}{1+x^2}\,dx");
        Assert.Equal(NaryKind.Integral, op.Kind);
        Assert.Equal("0", H.Text(op.Lower!));
        Assert.Equal("1", H.Text(op.Upper!));
        var operand = H.As<Row>(op.Operand!);
        Assert.IsType<Fraction>(operand.Children[0]);
        Assert.IsType<Space>(operand.Children[1]);
        var d = H.As<Identifier>(operand.Children[2]);
        Assert.Equal(IdentifierRole.Differential, d.Role);
        Assert.Equal("x", H.Text(operand.Children[3]));
    }

    [Fact]
    public void TextAfterDifferentialStaysOutside()
    {
        var nodes = H.Nodes(@"\int_a^b f(x)\,\mathrm{d}x = F(b)-F(a)");
        var op = H.As<LargeOperator>(nodes[0]);
        var operand = H.As<Row>(op.Operand!);
        Assert.Equal(IdentifierRole.Differential, H.As<Identifier>(operand.Children[^2]).Role);
        Assert.Equal(MathVariant.Normal, H.As<Identifier>(operand.Children[^2]).Variant);
        Assert.Equal("=", H.As<Operator>(nodes[1]).Text);
    }

    [Fact]
    public void DoubleIntegralNeedsTwoDifferentials()
    {
        var op = Op(@"\iint_D f(x,y)\,dx\,dy");
        var operand = H.As<Row>(op.Operand!);
        Assert.Equal(2, operand.Children.OfType<Identifier>().Count(i => i.Role == IdentifierRole.Differential));
    }

    [Fact]
    public void NestedIntegralsNestCorrectly()
    {
        // ∫∫ f dx dy  ⇒  ∫( ∫ f dx ) dy
        var outer = Op(@"\int_0^1\int_0^2 f\,dx\,dy");
        var operand = H.As<Row>(outer.Operand!);
        var inner = H.As<LargeOperator>(operand.Children[0]);
        var innerOperand = H.As<Row>(inner.Operand!);
        Assert.Equal("x", H.Text(innerOperand.Children[^1]));
        Assert.Equal("y", H.Text(operand.Children[^1]));
    }

    [Fact]
    public void SumStopsAtTopLevelBinaryAndRelation()
    {
        var nodes = H.Nodes(@"\sum_{i=1}^n a_i b_i + c = 0");
        var op = H.As<LargeOperator>(nodes[0]);
        Assert.Equal(2, H.As<Row>(op.Operand!).Children.Count);
        Assert.Equal("+", H.As<Operator>(nodes[1]).Text);
    }

    [Fact]
    public void SumKeepsParenthesizedExpression()
    {
        var op = Op(@"\prod_{k=1}^{n}(1+x_k)");
        Assert.Equal(5, H.As<Row>(op.Operand!).Children.Count);
    }

    [Fact]
    public void IntegralSignTypedDirectlyIsStillAStructure()
    {
        var op = Op("∫_0^1 x dx");
        Assert.Equal(NaryKind.Integral, op.Kind);
        Assert.NotNull(op.Operand);
    }

    [Fact]
    public void LimitsCommand()
    {
        Assert.Equal(LimitPlacement.Limits, Op(@"\int\limits_0^1 x\,dx").Limits);
        Assert.Equal(LimitPlacement.NoLimits, Op(@"\sum\nolimits_i a_i").Limits);
    }

    [Fact]
    public void MisplacedLimitsIsWarned()
    {
        Assert.Contains(H.Parse(@"x\limits").Diagnostics, d => d.Code == DiagnosticCode.MisplacedLimits);
    }

    [Fact]
    public void IntegralInsideParenthesesStopsAtClosingParen()
    {
        var nodes = H.Nodes(@"\left( \int f\,dx \right)");
        var fenced = H.As<Fenced>(nodes[0]);
        var op = H.As<LargeOperator>(H.As<Row>(fenced.Parts[0]).Children[0]);
        Assert.NotNull(op.Operand);
    }
}
