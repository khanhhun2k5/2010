namespace MathTypeX.Parser.Tests;

public class EnvironmentTests
{
    [Fact]
    public void PMatrix()
    {
        var t = H.As<Table>(H.Single(@"\begin{pmatrix} a & b \\ c & d \end{pmatrix}"));
        Assert.Equal(TableKind.PMatrix, t.Kind);
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal(2, t.ColumnCount);
        Assert.Equal("d", H.Text(t.Rows[1].Cells[1]));
    }

    [Fact]
    public void TrailingRowSeparatorIsIgnored()
    {
        var t = H.As<Table>(H.Single(@"\begin{matrix} 1 & 2 \\ 3 & 4 \\ \end{matrix}"));
        Assert.Equal(2, t.Rows.Count);
    }

    [Fact]
    public void CasesAreLeftAligned()
    {
        var t = H.As<Table>(H.Single(@"\begin{cases} x & \text{nếu } x\ge 0 \\ -x & \text{nếu } x<0 \end{cases}"));
        Assert.Equal(TableKind.Cases, t.Kind);
        Assert.All(t.Columns, c => Assert.Equal(ColumnAlign.Left, c));
    }

    [Fact]
    public void AlignedSplitsCellsAtAmpersand()
    {
        var al = H.As<Alignment>(H.Single(@"\begin{aligned} f(x) &= x^2+2x+1 \\ &= (x+1)^2 \end{aligned}"));
        Assert.Equal(AlignmentKind.Aligned, al.Kind);
        Assert.Equal(2, al.Rows.Count);
        Assert.Equal(2, al.Rows[0].Cells.Count);
        Assert.True(AstWalker.IsEmpty(al.Rows[1].Cells[0]));
    }

    [Fact]
    public void AlignRowsKeepTagsAndLabels()
    {
        var al = H.As<Alignment>(H.Single(@"\begin{align} a &= b \label{eq:a} \\ c &= d \nonumber \end{align}"));
        Assert.Equal("eq:a", al.Rows[0].Label);
        Assert.True(al.Rows[1].NoNumber);
    }

    [Fact]
    public void MissingEndIsReported()
    {
        var doc = H.Parse(@"\begin{bmatrix} 1 & 2");
        var d = Assert.Single(doc.Diagnostics, x => x.Code == DiagnosticCode.MissingEnvironmentEnd);
        Assert.Equal("\\end{bmatrix}", d.Fixes[0].Replacement);
    }

    [Fact]
    public void MismatchedEnd()
    {
        Assert.Contains(H.Parse(@"\begin{pmatrix} 1 \end{bmatrix}").Diagnostics, x => x.Code == DiagnosticCode.MismatchedEnvironmentEnd);
    }

    [Fact]
    public void ArrayColumnSpec()
    {
        var t = H.As<Table>(H.Single(@"\begin{array}{lcr} a & b & c \end{array}"));
        Assert.Equal(new[] { ColumnAlign.Left, ColumnAlign.Center, ColumnAlign.Right }, t.Columns);
    }
}
