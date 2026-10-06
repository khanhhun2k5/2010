using System.Text;
using MathTypeX.Scanning;

namespace MathTypeX.Editing.Conversion;

/// <summary>
/// Một thao tác thay thế trong văn bản (toạ độ ký tự của chuỗi đã scan):
/// thay [ReplaceStart, ReplaceEnd) bằng công thức; với display có thể phải chèn dấu hết đoạn trước/sau.
/// </summary>
public sealed record ConversionItem
{
    public required MathCandidate Candidate { get; init; }
    public required int ReplaceStart { get; init; }
    public required int ReplaceEnd { get; init; }
    /// <summary>LaTeX sẽ đưa vào parser: đã sửa ký tự do AutoCorrect của Word, đã gộp dấu câu đứng sau display.</summary>
    public required string Latex { get; init; }
    public required bool Display { get; init; }
    /// <summary>Phải chèn dấu hết đoạn ngay trước công thức (display đang đứng giữa đoạn).</summary>
    public required bool BreakBefore { get; init; }
    /// <summary>Phải chèn dấu hết đoạn ngay sau công thức (còn chữ phía sau trong cùng đoạn).</summary>
    public required bool BreakAfter { get; init; }
    /// <summary>Văn bản phải còn nguyên tại [ReplaceStart, ReplaceEnd) lúc thay — khác thì bỏ qua (tài liệu đã đổi).</summary>
    public required string ExpectedText { get; init; }
    /// <summary>Văn bản gốc kèm delimiter, lưu vào metadata để có thể "Revert to LaTeX text".</summary>
    public required string SourceText { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

/// <summary>Kết quả lập kế hoạch: các mục sẽ chuyển (theo thứ tự xuất hiện) và các ứng viên bị bỏ qua vì độ tin cậy thấp.</summary>
public sealed record ConversionPlan(IReadOnlyList<ConversionItem> Items, IReadOnlyList<MathCandidate> LowConfidence);

/// <summary>
/// Từ text của các đoạn văn chứa vùng chọn, quyết định thay đoạn nào bằng công thức và tách đoạn ra sao
/// (docs/05 §9.3–9.4). Thuần tuý trên chuỗi để test được không cần Word.
/// <para>
/// Display: Word chỉ coi equation là display khi nó đứng riêng một đoạn, nên
/// "Ta có $$E(X)=\mu.$$ Do đó…" thành ba đoạn "Ta có" / [display] / "Do đó…".
/// Dấu câu đứng ngay sau $$…$$ được đưa vào trong công thức như thói quen viết LaTeX.
/// </para>
/// </summary>
public static class ConversionPlanner
{
    private const string Punctuation = ".,;:";

    /// <param name="text">Text của trọn các đoạn văn chứa vùng chọn (\r hết đoạn, \a hết ô bảng).</param>
    /// <param name="windowStart">Đầu vùng chọn trong <paramref name="text"/>.</param>
    /// <param name="windowEnd">Cuối vùng chọn; chỉ công thức nằm trọn trong [windowStart, windowEnd) được chuyển.</param>
    public static ConversionPlan Plan(string text, int windowStart, int windowEnd, ScannerOptions? options = null)
    {
        var items = new List<ConversionItem>();
        var low = new List<MathCandidate>();
        ConversionItem? previous = null;

        foreach (var c in LatexScanner.Scan(text, options))
        {
            if (c.Start < windowStart || c.End > windowEnd) continue;
            if (!c.Recommended)
            {
                low.Add(c);
                continue;
            }

            var notes = new List<string>();
            string latex = NormalizeWordText(c.Latex);
            if (c.Delimiter != MathDelimiter.Environment || c.Environment is "equation" or "equation*" or "displaymath" or "math")
                latex = StripNumbering(latex, notes);
            latex = latex.Trim();

            int limit = previous?.ReplaceEnd ?? 0;
            ConversionItem item;
            if (!c.Display)
            {
                item = Make(c, c.Start, c.End, latex, display: false, breakBefore: false, breakAfter: false, text, notes);
            }
            else
            {
                int pStart = ParagraphStart(text, c.Start);
                int pEnd = ParagraphEnd(text, c.End);

                // Trước công thức: đoạn chỉ có khoảng trắng → nuốt luôn; còn chữ → tách đoạn, bỏ khoảng trắng thừa ở cuối.
                int rs;
                bool breakBefore;
                if (IsBlank(text, pStart, c.Start))
                {
                    rs = Math.Max(pStart, limit);
                    breakBefore = false;
                }
                else
                {
                    rs = c.Start;
                    while (rs > Math.Max(pStart, limit) && IsInlineSpace(text[rs - 1])) rs--;
                    // Công thức display ngay trước đã tự tách đoạn phía sau nó — không tách lần nữa (tránh đoạn trống).
                    breakBefore = !(previous is { BreakAfter: true } && previous.ReplaceEnd == rs);
                }

                // Sau công thức: dấu câu → vào công thức; phần còn lại có chữ → tách đoạn.
                int k = c.End;
                while (k < pEnd && IsInlineSpace(text[k])) k++;
                int punctEnd = k;
                while (punctEnd < pEnd && Punctuation.IndexOf(text[punctEnd]) >= 0) punctEnd++;
                if (punctEnd > k)
                {
                    latex += text.Substring(k, punctEnd - k);
                    notes.Add("đưa dấu câu phía sau vào công thức");
                    k = punctEnd;
                    while (k < pEnd && IsInlineSpace(text[k])) k++;
                }

                int re;
                bool breakAfter;
                if (k >= pEnd)
                {
                    re = pEnd;
                    breakAfter = false;
                }
                else
                {
                    re = k;
                    breakAfter = true;
                }
                item = Make(c, rs, re, latex, display: true, breakBefore, breakAfter, text, notes);
            }
            items.Add(item);
            previous = item;
        }
        return new ConversionPlan(items, low);
    }

    private static ConversionItem Make(MathCandidate c, int rs, int re, string latex, bool display, bool breakBefore, bool breakAfter, string text, List<string> notes) => new()
    {
        Candidate = c,
        ReplaceStart = rs,
        ReplaceEnd = re,
        Latex = latex,
        Display = display,
        BreakBefore = breakBefore,
        BreakAfter = breakAfter,
        ExpectedText = text.Substring(rs, re - rs),
        SourceText = text.Substring(c.Start, c.Length),
        Notes = notes,
    };

    /// <summary>
    /// Sửa các ký tự mà Word tự đổi khi gõ văn bản (AutoCorrect, xuống dòng mềm…) để LaTeX trở lại như người dùng viết:
    /// ’ → ' (dấu phẩy trên), – → -, khoảng trắng không ngắt → khoảng trắng, xuống dòng → khoảng trắng,
    /// gạch nối không ngắt của Word → -, bỏ gạch nối mềm và ký tự độ rộng 0.
    /// </summary>
    public static string NormalizeWordText(string latex)
    {
        var sb = new StringBuilder(latex.Length);
        foreach (char ch in latex)
        {
            switch (ch)
            {
                case '\u2019':
                    sb.Append('\'');
                    break;
                case '\u2013':
                case '\u001E':
                case '\u2011':
                    sb.Append('-');
                    break;
                case '\u00A0':
                case '\u2009':
                case '\u202F':
                case '\v':
                case '\r':
                case '\n':
                case '\t':
                    sb.Append(' ');
                    break;
                case '\u00AD':
                case '\u001F':
                case '\u200B':
                case '\u200C':
                case '\u200D':
                case '\uFEFF':
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Bỏ \label{…}, \tag{…}, \nonumber, \notag của công thức một dòng: Word Equation không có đánh số kiểu LaTeX
    /// (đánh số là Phase 3), còn giữ lại thì cây sẽ thành bảng một ô.
    /// </summary>
    public static string StripNumbering(string latex, List<string> notes)
    {
        string result = latex;
        foreach (var command in new[] { "\\label", "\\tag*", "\\tag" })
        {
            int at;
            while ((at = FindCommand(result, command)) >= 0)
            {
                int open = at + command.Length;
                while (open < result.Length && result[open] == ' ') open++;
                if (open >= result.Length || result[open] != '{') break;
                int close = MatchingBrace(result, open);
                if (close < 0) break;
                notes.Add("bỏ " + result.Substring(at, close + 1 - at) + " (đánh số công thức chưa hỗ trợ)");
                result = result.Remove(at, close + 1 - at);
            }
        }
        foreach (var command in new[] { "\\nonumber", "\\notag" })
        {
            int at;
            while ((at = FindCommand(result, command)) >= 0) result = result.Remove(at, command.Length);
        }
        return result;
    }

    private static int FindCommand(string s, string command)
    {
        for (int i = s.IndexOf(command, StringComparison.Ordinal); i >= 0; i = s.IndexOf(command, i + 1, StringComparison.Ordinal))
        {
            int after = i + command.Length;
            bool wordEnds = after >= s.Length || !char.IsLetter(s[after]) || command.EndsWith("*", StringComparison.Ordinal);
            int slashes = 0;
            for (int k = i - 1; k >= 0 && s[k] == '\\'; k--) slashes++;
            if (wordEnds && slashes % 2 == 0) return i;
        }
        return -1;
    }

    private static int MatchingBrace(string s, int open)
    {
        int depth = 0;
        for (int i = open; i < s.Length; i++)
        {
            if (s[i] == '\\')
            {
                i++;
                continue;
            }
            if (s[i] == '{') depth++;
            else if (s[i] == '}' && --depth == 0) return i;
        }
        return -1;
    }

    private static bool IsParagraphEnd(char c) => c is '\r' or '\a';

    private static int ParagraphStart(string text, int index)
    {
        int i = index;
        while (i > 0 && !IsParagraphEnd(text[i - 1])) i--;
        return i;
    }

    private static int ParagraphEnd(string text, int index)
    {
        int i = index;
        while (i < text.Length && !IsParagraphEnd(text[i])) i++;
        return i;
    }

    private static bool IsInlineSpace(char c) => c is ' ' or '\t' or '\u00A0' or '\v';

    private static bool IsBlank(string text, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (!IsInlineSpace(text[i])) return false;
        }
        return true;
    }
}
