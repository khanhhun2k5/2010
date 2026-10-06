namespace MathTypeX.Editing.Catalog;

/// <summary>Một kết quả tìm kiếm, kèm điểm để sắp xếp.</summary>
public sealed record SearchHit(CatalogEntry Entry, int Score);

/// <summary>
/// Tìm kiếm mờ trong catalog (§7, §8): theo tên lệnh khi gõ "\fra", theo tên tiếng Việt/Anh trong Command Palette
/// ("phân số", "tich phan", "phi" → \phi và \varphi). Có cộng điểm cho mục hay dùng (học cục bộ, §27).
/// </summary>
public static class CatalogSearch
{
    /// <summary>Autocomplete sau dấu "\": ưu tiên tên lệnh bắt đầu bằng phần đã gõ.</summary>
    public static IReadOnlyList<SearchHit> ByTrigger(string prefix, UsageStats? usage = null, int max = 10)
    {
        if (prefix.Length == 0) return Array.Empty<SearchHit>();
        var hits = new List<SearchHit>();
        foreach (var e in CommandCatalog.All)
        {
            int score = Score(prefix, e.Trigger, exactBonus: true);
            if (score == 0)
            {
                // "\phi" cũng gợi ý \varphi; "\int" gợi ý cả ∬ ∭ ∮ qua tên tiếng Anh.
                int nameScore = Math.Max(Score(prefix, e.NameEn), Score(prefix, e.KeywordsEn));
                if (nameScore >= 400) score = nameScore / 3;
            }
            if (score == 0) continue;
            if (CaseSensitiveSubsequence(prefix, e.Trigger)) score += 10; // \vphi → \varphi trước \varPhi
            hits.Add(new SearchHit(e, score + (usage?.Boost(e.Id) ?? 0)));
        }
        return Rank(hits, max);
    }

    private static bool CaseSensitiveSubsequence(string query, string text)
    {
        int ti = 0;
        foreach (char c in query)
        {
            while (ti < text.Length && text[ti] != c) ti++;
            if (ti == text.Length) return false;
            ti++;
        }
        return true;
    }

    /// <summary>Command Palette: mọi từ trong truy vấn phải khớp một trường nào đó (tên VI/EN, từ khoá, lệnh, ký hiệu).</summary>
    public static IReadOnlyList<SearchHit> ByText(string query, UsageStats? usage = null, int max = 30, Category? category = null)
    {
        var words = TextFolding.Fold(query).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var hits = new List<SearchHit>();
        foreach (var e in CommandCatalog.All)
        {
            if (category is not null && e.Category != category) continue;
            if (words.Length == 0)
            {
                hits.Add(new SearchHit(e, usage?.Boost(e.Id) ?? 0));
                continue;
            }

            // Truy vấn nguyên cụm khớp nguyên tên → điểm rất cao ("phân số" → Phân số).
            int phrase = Math.Max(Score(query, e.NameVi), Score(query, e.NameEn));
            int total = 0;
            bool all = true;
            foreach (var w in words)
            {
                int best = new[]
                {
                    Score(w, e.NameVi), Score(w, e.NameEn), Score(w, e.KeywordsVi), Score(w, e.KeywordsEn),
                    Score(w, e.Trigger, exactBonus: true), e.Symbol is null ? 0 : Score(w, e.Symbol, exactBonus: true),
                }.Max();
                if (best == 0)
                {
                    all = false;
                    break;
                }
                total += best;
            }
            if (!all) continue;
            hits.Add(new SearchHit(e, total + phrase / 2 + (usage?.Boost(e.Id) ?? 0)));
        }
        return Rank(hits, max);
    }

    private static IReadOnlyList<SearchHit> Rank(List<SearchHit> hits, int max) => hits
        .OrderByDescending(h => h.Score)
        .ThenBy(h => h.Entry.Id.StartsWith("cmd:", StringComparison.Ordinal) ? 0 : 1)
        .ThenBy(h => h.Entry.Trigger.Length)
        .ThenBy(h => h.Entry.Id, StringComparer.Ordinal)
        .Take(max)
        .ToArray();

    /// <summary>
    /// Điểm khớp mờ (0 = không khớp): khớp nguyên văn &gt; tiền tố &gt; tiền tố của một từ &gt; chứa &gt; dãy con.
    /// So khớp sau khi gập dấu và chữ thường.
    /// </summary>
    public static int Score(string query, string text, bool exactBonus = false)
    {
        if (query.Length == 0 || text.Length == 0) return 0;
        string q = TextFolding.Fold(query).Trim();
        string t = TextFolding.Fold(text);
        if (q.Length == 0) return 0;
        if (t == q) return exactBonus ? 1200 : 1000;
        if (t.StartsWith(q, StringComparison.Ordinal)) return 800 - Math.Min(200, t.Length - q.Length);
        int wordStart = WordPrefixIndex(t, q);
        if (wordStart >= 0) return 600 - Math.Min(100, wordStart);
        int contains = t.IndexOf(q, StringComparison.Ordinal);
        if (contains >= 0) return 450 - Math.Min(100, contains);
        return Subsequence(q, t);
    }

    private static int WordPrefixIndex(string text, string query)
    {
        for (int i = 1; i < text.Length; i++)
        {
            if (!char.IsLetterOrDigit(text[i - 1]) && string.CompareOrdinal(text, i, query, 0, query.Length) == 0) return i;
        }
        return -1;
    }

    private static int Subsequence(string q, string t)
    {
        if (q.Length < 2) return 0;
        int score = 0, ti = 0, run = 0;
        foreach (char c in q)
        {
            if (c == ' ') continue;
            bool found = false;
            while (ti < t.Length)
            {
                if (t[ti] == c)
                {
                    score += 10 + (run > 0 ? 15 : 0) + (ti == 0 || !char.IsLetterOrDigit(t[ti - 1]) ? 20 : 0);
                    run++;
                    ti++;
                    found = true;
                    break;
                }
                run = 0;
                ti++;
            }
            if (!found) return 0;
        }
        return Math.Min(score, 400);
    }
}
