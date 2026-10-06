namespace MathTypeX.Parser.Tests;

public class FunctionTests
{
    [Fact]
    public void SinCapturesSingleArgument()
    {
        var nodes = H.Nodes(@"\sin x + 1");
        var fn = H.As<FunctionApply>(nodes[0]);
        Assert.Equal("x", H.Text(fn.Argument!));
        Assert.Equal(3, nodes.Count);
    }

    [Fact]
    public void SinSquared()
    {
        var fn = H.As<FunctionApply>(H.Single(@"\sin^2 x"));
        Assert.Equal("2", H.Text(fn.Upper!));
        Assert.Equal("x", H.Text(fn.Argument!));
    }

    [Fact]
    public void LimitTakesFractionAndStopsAtRelation()
    {
        var nodes = H.Nodes(@"\lim_{x\to 0} \frac{\sin x}{x} = 1");
        var fn = H.As<FunctionApply>(nodes[0]);
        Assert.True(fn.LimitsByDefault);
        Assert.IsType<Fraction>(fn.Argument);
        Assert.Equal("x→0", H.Text(fn.Lower!));
    }

    [Fact]
    public void FunctionCallInParentheses()
    {
        var fn = H.As<FunctionApply>(H.Single(@"\max(a,b)"));
        Assert.Equal(5, H.As<Row>(fn.Argument!).Children.Count);
    }

    [Fact]
    public void AbsoluteValueArgument()
    {
        var fn = H.As<FunctionApply>(H.Single(@"\ln|x|"));
        Assert.Equal(3, H.As<Row>(fn.Argument!).Children.Count);
    }

    [Fact]
    public void ProductOfTwoFunctions()
    {
        var nodes = H.Nodes(@"\sin x \cos y");
        Assert.Equal(2, nodes.Count);
        Assert.Equal("x", H.Text(H.As<FunctionApply>(nodes[0]).Argument!));
        Assert.Equal("y", H.Text(H.As<FunctionApply>(nodes[1]).Argument!));
    }

    [Fact]
    public void NestedFunction()
    {
        var fn = H.As<FunctionApply>(H.Single(@"\sin\cos x"));
        Assert.IsType<FunctionApply>(fn.Argument);
    }

    [Fact]
    public void OperatorNameIsCustomFunction()
    {
        var fn = H.As<FunctionApply>(H.Single(@"\operatorname{rank} A"));
        Assert.Equal(("rank", false), (fn.Name, fn.IsBuiltin));
    }
}
