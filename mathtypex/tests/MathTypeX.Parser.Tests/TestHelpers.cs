namespace MathTypeX.Parser.Tests;

internal static class H
{
    public static MathDocument Parse(string latex, bool decimalComma = false) =>
        LatexParser.Parse(latex, new ParserOptions { DecimalComma = decimalComma });

    /// <summary>Parse và trả về node duy nhất ở cấp cao nhất.</summary>
    public static MathNode Single(string latex)
    {
        var doc = Parse(latex);
        Assert.Single(doc.Body.Children);
        return doc.Body.Children[0];
    }

    public static IReadOnlyList<MathNode> Nodes(string latex) => Parse(latex).Body.Children;

    public static string Norm(string latex) => LatexPrinter.Print(Parse(latex));

    public static void NoErrors(MathDocument doc) =>
        Assert.True(doc.Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error),
            string.Join("\n", doc.Diagnostics.Select(d => d.ToString())));

    public static T As<T>(MathNode node) where T : MathNode => Assert.IsType<T>(node);

    public static string Text(MathNode node) => node switch
    {
        Identifier id => id.Text,
        Number n => n.Text,
        Operator op => op.Text,
        Row r => string.Concat(r.Children.Select(Text)),
        Group g => Text(g.Content),
        _ => node.GetType().Name,
    };
}
