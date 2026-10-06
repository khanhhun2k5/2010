namespace MathTypeX.Render.Omml.Tests;

internal static class O
{
    public static readonly XNamespace M = OmmlWriter.M;
    public static readonly XNamespace W = OmmlWriter.W;

    public static OmmlResult Write(string latex, OmmlOptions? options = null) =>
        OmmlWriter.Write(LatexParser.Parse(latex), options);

    public static XElement X(string latex, bool display = false) =>
        Write(latex, new OmmlOptions { Display = display }).Element;

    public static string? Val(this XElement? e) => e?.Attribute(M + "val")?.Value;

    public static XElement One(this XElement root, string localName) =>
        Assert.Single(root.Descendants(M + localName));

    /// <summary>Toàn bộ chữ trong các m:t (bỏ khoảng trắng toán học) — dùng để so nội dung.</summary>
    public static string Text(this XElement e) =>
        string.Concat(e.Descendants(M + "t").Select(t => t.Value)).Replace(" ", " ").Trim();

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MathTypeX.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Không tìm thấy thư mục gốc repo");
    }

    public static IEnumerable<(string Section, string Latex)> Corpus(string file)
    {
        string section = "";
        foreach (var raw in File.ReadAllLines(Path.Combine(RepoRoot(), "tests", "corpus", file)))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("%%", StringComparison.Ordinal)) { section = line.TrimStart('%').Trim(); continue; }
            if (line.StartsWith("%", StringComparison.Ordinal)) continue;
            yield return (section, line);
        }
    }
}
