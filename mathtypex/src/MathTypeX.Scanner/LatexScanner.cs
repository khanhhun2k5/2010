using MathTypeX.Parsing;

namespace MathTypeX.Scanning;

/// <summary>Loại delimiter bao quanh công thức trong văn bản.</summary>
public enum MathDelimiter
{
    /// <summary><c>$…$</c> (inline).</summary>
    Dollar,
    /// <summary><c>$$…$$</c> (display).</summary>
    DoubleDollar,
    /// <summary><c>\(…\)</c> (inline).</summary>
    Paren,
    /// <summary><c>\[…\]</c> (display).</summary>
    Bracket,
    /// <summary><c>\begin{equation}…\end{equation}</c>, <c>align*</c>, … (display; riêng <c>math</c> là inline).</summary>
    Environment,
}

/// <summary>
/// Một ứng viên công thức tìm thấy trong văn bản: vị trí [Start, Start+Length) tính cả delimiter,
/// LaTeX bên trong, độ tin cậy 0–100 và lý do (để hộp Scan giải thích vì sao một mục không được tích sẵn).
/// </summary>
public sealed record MathCandidate
{
    public required int Start { get; init; }
    public required int Length { get; init; }
    public int End => Start + Length;
    public required MathDelimiter Delimiter { get; init; }
    /// <summary>LaTeX bên trong delimiter, giữ nguyên ký tự gốc (chưa thay dấu xuống dòng của Word).</summary>
    public required string Latex { get; init; }
    public required bool Display { get; init; }
    public required int Confidence { get; init; }
    /// <summary>Độ tin cậy đạt ngưỡng: được chuyển (hoặc được tích sẵn trong hộp Scan).</summary>
    public required bool Recommended { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
    /// <summary>Tên môi trường khi <see cref="Delimiter"/> là <see cref="MathDelimiter.Environment"/>.</summary>
    public string? Environment { get; init; }
}

public sealed record ScannerOptions
{
    public bool SingleDollar { get; init; } = true;
    public bool DoubleDollar { get; init; } = true;
    public bool Parentheses { get; init; } = true;
    public bool Brackets { get; init; } = true;
    public bool Environments { get; init; } = true;
    /// <summary>Ngưỡng độ tin cậy (0–100) để một ứng viên được đề xuất chuyển.</summary>
    public int MinConfidence { get; init; } = 50;
    public int MaxInlineLength { get; init; } = 1000;
    public int MaxDisplayLength { get; init; } = 8000;
    /// <summary>Số đoạn văn tối đa mà một công thức display được phép trải qua (chống ghép nhầm hai $$ xa nhau).</summary>
    public int MaxDisplayParagraphs { get; init; } = 12;

    public static ScannerOptions Default { get; } = new();
}

/// <summary>
/// Máy trạng thái tìm công thức LaTeX trong văn bản (docs/05 §9.4), làm việc trực tiếp trên text của Word:
/// <c>\r</c> là hết đoạn, <c>\v</c> là xuống dòng mềm, <c>\a</c> là hết ô bảng, 0x13…0x15 bao một field.
/// Quy tắc <c>$</c> theo Pandoc: <c>$</c> mở phải có ký tự khác khoảng trắng ngay bên phải; <c>$</c> đóng phải có
/// ký tự khác khoảng trắng ngay bên trái và không đứng ngay trước một chữ số — nhờ đó "$20 và $30" không bị coi là toán.
/// </summary>
public static class LatexScanner
{
    private const char FieldBegin = '\u0013';
    private const char FieldSeparator = '\u0014';
    private const char FieldEnd = '\u0015';

    /// <summary>Môi trường toán đứng một mình (không cần $…$). Giá trị: có phải display không.</summary>
    private static readonly Dictionary<string, bool> MathEnvironments = new(StringComparer.Ordinal)
    {
        ["equation"] = true, ["equation*"] = true, ["displaymath"] = true, ["math"] = false,
        ["align"] = true, ["align*"] = true, ["gather"] = true, ["gather*"] = true,
        ["multline"] = true, ["multline*"] = true, ["eqnarray"] = true, ["eqnarray*"] = true,
    };

    /// <summary>Môi trường mà LaTeX chuyển vào editor chỉ là phần bên trong (không có cấu trúc dòng).</summary>
    private static readonly HashSet<string> InnerOnlyEnvironments = new(StringComparer.Ordinal)
    {
        "equation", "equation*", "displaymath", "math",
    };

    public static IReadOnlyList<MathCandidate> Scan(string? text, ScannerOptions? options = null)
    {
        var o = options ?? ScannerOptions.Default;
        var result = new List<MathCandidate>();
        if (string.IsNullOrEmpty(text)) return result;
        string s = text!;
        int n = s.Length;
        int i = 0;
        while (i < n)
        {
            char c = s[i];
            if (c == FieldBegin)
            {
                i = SkipField(s, i);
                continue;
            }
            if (c == '\\')
            {
                char next = i + 1 < n ? s[i + 1] : '\0';
                MathCandidate? found = null;
                if (next == '(' && o.Parentheses) found = TryDelimited(s, i, "\\(", "\\)", MathDelimiter.Paren, display: false, o);
                else if (next == '[' && o.Brackets) found = TryDelimited(s, i, "\\[", "\\]", MathDelimiter.Bracket, display: true, o);
                else if (next == 'b' && o.Environments) found = TryEnvironment(s, i, o);
                if (found is not null)
                {
                    result.Add(found);
                    i = found.End;
                    continue;
                }
                // "\$", "\\", "\alpha"…: bỏ qua cả cặp để ký tự bị escape không bao giờ là delimiter.
                i += 2;
                continue;
            }
            if (c == '$')
            {
                bool dbl = i + 1 < n && s[i + 1] == '$';
                MathCandidate? found = null;
                if (dbl && o.DoubleDollar) found = TryDelimited(s, i, "$$", "$$", MathDelimiter.DoubleDollar, display: true, o);
                else if (!dbl && o.SingleDollar) found = TrySingleDollar(s, i, o);
                if (found is not null)
                {
                    result.Add(found);
                    i = found.End;
                    continue;
                }
                i += dbl ? 2 : 1;
                continue;
            }
            i++;
        }
        return result;
    }

    // ── Các dạng delimiter ───────────────────────────────────────────────

    private static MathCandidate? TrySingleDollar(string s, int open, ScannerOptions o)
    {
        int n = s.Length;
        if (open + 1 >= n || IsSpace(s[open + 1])) return null;
        int j = open + 1;
        int depth = 0;
        while (j < n)
        {
            char c = s[j];
            if (j - open > o.MaxInlineLength || IsBarrier(c) || c == '\r' || IsBlankLine(s, j)) return null;
            if (c == '\\')
            {
                j += 2;
                continue;
            }
            if (c == '{') depth++;
            else if (c == '}') depth = Math.Max(0, depth - 1);
            else if (c == '$' && depth == 0)
            {
                bool closes = !IsSpace(s[j - 1]) && !(j + 1 < n && char.IsDigit(s[j + 1]));
                if (closes)
                {
                    string latex = s.Substring(open + 1, j - open - 1);
                    return Make(s, open, j + 1, MathDelimiter.Dollar, latex, display: false, o, followedBy: j + 1 < n ? s[j + 1] : '\0');
                }
                // Một "$" ở ngoài mọi nhóm {…} mà không đóng được: "$20 và $30, còn $x$" — dấu $ mở này không phải toán,
                // dừng lại để dấu $ phía sau được thử làm dấu mở (Pandoc sẽ nuốt cả đoạn "20 và $30, còn $x").
                return null;
            }
            j++;
        }
        return null;
    }

    private static MathCandidate? TryDelimited(string s, int open, string opener, string closer, MathDelimiter kind, bool display, ScannerOptions o)
    {
        int n = s.Length;
        int contentStart = open + opener.Length;
        int max = display ? o.MaxDisplayLength : o.MaxInlineLength;
        int paragraphs = 0;
        int j = contentStart;
        while (j < n)
        {
            char c = s[j];
            if (j - open > max || IsBarrier(c)) return null;
            if (c == '\r' || IsBlankLine(s, j))
            {
                if (!display || ++paragraphs > o.MaxDisplayParagraphs) return null;
            }
            if (string.CompareOrdinal(s, j, closer, 0, closer.Length) == 0)
            {
                string latex = s.Substring(contentStart, j - contentStart);
                if (IsBlank(latex)) return null;
                return Make(s, open, j + closer.Length, kind, latex, display, o, followedBy: '\0');
            }
            j += c == '\\' ? 2 : 1;
        }
        return null;
    }

    private static MathCandidate? TryEnvironment(string s, int open, ScannerOptions o)
    {
        const string Begin = "\\begin{";
        if (string.CompareOrdinal(s, open, Begin, 0, Begin.Length) != 0) return null;
        int nameStart = open + Begin.Length;
        int close = s.IndexOf('}', nameStart);
        if (close < 0 || close - nameStart > 20) return null;
        string name = s.Substring(nameStart, close - nameStart);
        if (!MathEnvironments.TryGetValue(name, out bool display)) return null;

        string endTag = "\\end{" + name + "}";
        int contentStart = close + 1;
        int paragraphs = 0;
        int j = contentStart;
        while (j < s.Length)
        {
            char c = s[j];
            if (j - open > o.MaxDisplayLength || IsBarrier(c)) return null;
            if (c == '\r' && (!display || ++paragraphs > o.MaxDisplayParagraphs)) return null;
            if (string.CompareOrdinal(s, j, endTag, 0, endTag.Length) == 0)
            {
                int end = j + endTag.Length;
                string inner = s.Substring(contentStart, j - contentStart);
                if (IsBlank(inner)) return null;
                string latex = InnerOnlyEnvironments.Contains(name) ? inner : s.Substring(open, end - open);
                return Make(s, open, end, MathDelimiter.Environment, latex, display, o, followedBy: '\0', environment: name);
            }
            j += c == '\\' && string.CompareOrdinal(s, j, "\\end{", 0, 5) != 0 ? 2 : 1;
        }
        return null;
    }

    private static int SkipField(string s, int begin)
    {
        int depth = 0;
        for (int j = begin; j < s.Length; j++)
        {
            if (s[j] == FieldBegin) depth++;
            else if (s[j] == FieldEnd && --depth == 0) return j + 1;
        }
        return s.Length;
    }

    // ── Chấm điểm độ tin cậy (docs/05 §9.4, bảng quy tắc chống convert nhầm) ──

    private static MathCandidate Make(string s, int start, int end, MathDelimiter kind, string latex, bool display, ScannerOptions o, char followedBy, string? environment = null)
    {
        var reasons = new List<string>();
        int score = kind switch
        {
            MathDelimiter.Dollar => 60,
            MathDelimiter.DoubleDollar => 85,
            MathDelimiter.Environment => 95,
            _ => 90,
        };
        string body = latex.Trim();

        if (HasCommand(body))
        {
            score += 25;
            reasons.Add("có lệnh LaTeX");
        }
        if (body.IndexOfAny(new[] { '^', '_' }) >= 0)
        {
            score += 20;
            reasons.Add("có chỉ số trên/dưới");
        }
        if (body.IndexOfAny(new[] { '=', '<', '>', '+', '≤', '≥', '≠' }) >= 0)
        {
            score += 10;
            reasons.Add("có phép toán");
        }
        if (body.Length == 1 && char.IsLetter(body[0]))
        {
            score += 20;
            reasons.Add("một biến");
        }

        if (kind == MathDelimiter.Dollar)
        {
            if (IsAllDigits(body))
            {
                score -= 10;
                reasons.Add("chỉ là số");
            }
            if (LooksLikePath(body))
            {
                score -= 60;
                reasons.Add("giống đường dẫn hoặc biến môi trường");
            }
            else if (IsUpperIdentifier(body))
            {
                score -= 35;
                reasons.Add("giống tên biến môi trường");
            }
            if (LooksLikeProse(body))
            {
                score -= 45;
                reasons.Add("giống câu văn hơn là công thức");
            }
            if (char.IsLetterOrDigit(followedBy) && !HasCommand(body) && body.Length > 1 && !(body.Length <= 3 && body.All(char.IsLetterOrDigit)))
            {
                score -= 15;
                reasons.Add("dính liền chữ phía sau");
            }
        }

        var doc = LatexParser.Parse(environment is not null && !InnerOnlyEnvironments.Contains(environment) ? latex : body);
        if (doc.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            score -= 45;
            reasons.Add("có lỗi cú pháp");
        }

        score = Math.Max(0, Math.Min(100, score));
        return new MathCandidate
        {
            Start = start,
            Length = end - start,
            Delimiter = kind,
            Latex = latex,
            Display = display,
            Confidence = score,
            Recommended = score >= o.MinConfidence,
            Reasons = reasons,
            Environment = environment,
        };
    }

    private static bool HasCommand(string s)
    {
        for (int i = 0; i + 1 < s.Length; i++)
        {
            if (s[i] == '\\' && char.IsLetter(s[i + 1])) return true;
            if (s[i] == '\\') i++;
        }
        return false;
    }

    private static bool IsAllDigits(string s) =>
        s.Length > 0 && s.All(c => char.IsDigit(c) || c is '.' or ',' or ' ');

    /// <summary>C:\…, /usr/…, ~/…, ./…, %APPDATA%, {HOME}/bin…</summary>
    private static bool LooksLikePath(string s)
    {
        if (s.Length >= 3 && char.IsLetter(s[0]) && s[1] == ':' && (s[2] == '\\' || s[2] == '/')) return true;
        if (s.StartsWith("/", StringComparison.Ordinal) || s.StartsWith("~/", StringComparison.Ordinal) || s.StartsWith("./", StringComparison.Ordinal) || s.StartsWith("../", StringComparison.Ordinal)) return true;
        if (s.Length > 2 && s[0] == '%' && s.IndexOf('%', 1) > 1) return true;
        if (s.StartsWith("{", StringComparison.Ordinal) && s.IndexOf('}') is var close && close > 1 && IsUpperIdentifier(s.Substring(1, close - 1))) return true;
        // Đoạn có '/' giữa hai từ chữ và không có lệnh LaTeX: "HOME/bin:", "usr/local".
        int slash = s.IndexOf('/');
        return slash > 0 && slash < s.Length - 1 && char.IsLetter(s[slash - 1]) && char.IsLetter(s[slash + 1]) && !HasCommand(s) && s.Count(char.IsLetter) >= 4;
    }

    private static bool IsUpperIdentifier(string s) =>
        s.Length >= 2 && s.All(c => (c >= 'A' && c <= 'Z') || c == '_' || char.IsDigit(c)) && s.Count(c => c >= 'A' && c <= 'Z') >= 2;

    /// <summary>Có từ 3 từ chữ cái (≥ 2 ký tự) liên tiếp, không có lệnh LaTeX hay ^ _ =: nhiều khả năng là câu văn giữa hai dấu $.</summary>
    private static bool LooksLikeProse(string s)
    {
        if (HasCommand(s) || s.IndexOfAny(new[] { '^', '_', '=', '{', '}' }) >= 0) return false;
        int run = 0, best = 0;
        foreach (var word in s.Split(new[] { ' ', '\t', '\u00A0', '\v', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string w = word.TrimEnd(',', '.', ';', ':', '!', '?');
            if (w.Length >= 2 && w.All(char.IsLetter)) run++;
            else run = 0;
            best = Math.Max(best, run);
        }
        return best >= 3;
    }

    // ── Ký tự ────────────────────────────────────────────────────────────

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\u00A0' or '\r' or '\n' or '\v' or '\u2009' or '\u202F';

    /// <summary>Ký tự đặc biệt của Word mà công thức không thể chứa: hết ô, ngắt trang/cột, ảnh, chú thích, field…</summary>
    private static bool IsBarrier(char c) => c is '\a' or '\f' or '\u000E' or '\u0001' or '\u0002' or '\u0005' or '\u0008' or '\0'
        or FieldBegin or FieldSeparator or FieldEnd;

    private static bool IsBlank(string s)
    {
        foreach (char c in s)
        {
            if (!IsSpace(c)) return false;
        }
        return true;
    }

    /// <summary>Dòng trống trong văn bản thuần ("\n" + khoảng trắng + "\n") — tương đương hết đoạn.</summary>
    private static bool IsBlankLine(string s, int j)
    {
        if (s[j] != '\n') return false;
        for (int k = j + 1; k < s.Length; k++)
        {
            if (s[k] == '\n') return true;
            if (s[k] is not (' ' or '\t' or '\r')) return false;
        }
        return false;
    }
}
