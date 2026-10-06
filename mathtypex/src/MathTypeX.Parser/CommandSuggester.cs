namespace MathTypeX.Parsing;

/// <summary>Gợi ý lệnh gần đúng khi người dùng gõ sai tên (\alpah → \alpha).</summary>
public static class CommandSuggester
{
    /// <summary>Các lệnh cấu trúc (không nằm trong bảng ký hiệu) mà parser hiểu.</summary>
    public static readonly IReadOnlyList<string> StructuralCommands = new[]
    {
        "frac", "dfrac", "tfrac", "cfrac", "binom", "dbinom", "tbinom", "sqrt", "left", "right", "middle",
        "big", "Big", "bigg", "Bigg", "bigl", "bigr", "Bigl", "Bigr", "biggl", "biggr", "Biggl", "Biggr",
        "mathrm", "mathit", "mathbf", "mathbfit", "boldsymbol", "bm", "mathsf", "mathtt", "mathbb", "mathcal",
        "mathscr", "mathfrak", "operatorname", "text", "textrm", "textbf", "textit", "mbox", "overline",
        "underline", "overbrace", "underbrace", "overset", "underset", "stackrel", "xrightarrow", "xleftarrow",
        "boxed", "cancel", "phantom", "hphantom", "vphantom", "displaystyle", "textstyle", "not", "begin", "end",
        "tag", "label", "nonumber", "limits", "nolimits", "hspace",
    };

    private static readonly string[] AllNames = LatexSymbols.CommandNames
        .Concat(LatexSymbols.Accents.Keys)
        .Concat(StructuralCommands)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    public static IReadOnlyList<string> KnownCommands => AllNames;

    public static IReadOnlyList<string> Suggest(string name, int max = 3)
    {
        if (string.IsNullOrEmpty(name)) return Array.Empty<string>();
        int limit = name.Length <= 3 ? 1 : name.Length <= 6 ? 2 : 3;
        var candidates = AllNames
            .Select(c => (Name: c, Distance: Distance(name, c)))
            .Where(x => x.Distance <= limit)
            .ToList();
        if (candidates.Count == 0) return Array.Empty<string>();
        int best = candidates.Min(x => x.Distance);
        // Chỉ giữ các ứng viên gần nhất để gợi ý không bị nhiễu (\alpah → \alpha, không kèm \aleph).
        return candidates
            .Where(x => x.Distance == best)
            .OrderBy(x => x.Distance)
            .ThenBy(x => Math.Abs(x.Name.Length - name.Length))
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .Take(max)
            .Select(x => x.Name)
            .ToArray();
    }

    /// <summary>Khoảng cách Damerau–Levenshtein (có tính hoán vị hai ký tự kề nhau).</summary>
    public static int Distance(string a, string b)
    {
        int n = a.Length, m = b.Length;
        var d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        }
        return d[n, m];
    }
}
