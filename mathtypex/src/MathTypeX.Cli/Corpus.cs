namespace MathTypeX.Cli;

/// <summary>File corpus: mỗi dòng một công thức; "%%" mở một nhóm; "%" là chú thích.</summary>
public sealed record CorpusEntry(string Section, string Latex);

public static class Corpus
{
    public static IReadOnlyList<CorpusEntry> Load(string path)
    {
        var entries = new List<CorpusEntry>();
        string section = "";
        foreach (var raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("%%", StringComparison.Ordinal))
            {
                section = line.TrimStart('%').Trim();
                continue;
            }
            if (line.StartsWith("%", StringComparison.Ordinal)) continue;
            entries.Add(new CorpusEntry(section, line));
        }
        return entries;
    }
}
