namespace MathTypeX.Render.Omml.Tests;

/// <summary>Luật phân lớp (docs/02 §5.6): AST độc lập; renderer không phụ thuộc parser hay Office.</summary>
public class ArchitectureTests
{
    private static string[] Refs(Type t) => t.Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

    [Fact]
    public void AstDependsOnlyOnCore()
    {
        var refs = Refs(typeof(MathNode)).Where(r => r.StartsWith("MathTypeX", StringComparison.Ordinal)).ToArray();
        Assert.Equal(new[] { "MathTypeX.Core" }, refs);
    }

    [Fact]
    public void OmmlRendererDoesNotDependOnParserOrOffice()
    {
        var refs = Refs(typeof(OmmlWriter));
        Assert.DoesNotContain("MathTypeX.Parser", refs);
        Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.Office", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("DocumentFormat", StringComparison.Ordinal));
    }

    [Fact]
    public void ParserDoesNotDependOnRenderers()
    {
        var refs = Refs(typeof(LatexParser));
        Assert.DoesNotContain(refs, r => r.StartsWith("MathTypeX.Render", StringComparison.Ordinal));
    }
}
