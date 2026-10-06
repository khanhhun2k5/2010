using System.Text;

namespace MathTypeX.Ast;

/// <summary>
/// Ánh xạ giữa (ký tự gốc, kiểu chữ) và ký tự Unicode Mathematical Alphanumeric Symbols (U+1D400–1D7FF),
/// có xử lý các "lỗ" nằm trong khối Letterlike Symbols (ℝ, ℬ, ℭ…).
/// </summary>
public static class MathAlphabets
{
    private sealed record Alphabet(MathVariant Variant, int Upper, int Lower, int Digits);

    // Điểm bắt đầu của A, a, 0 cho từng kiểu chữ (-1 = không có).
    private static readonly Alphabet[] Alphabets =
    {
        new(MathVariant.Bold, 0x1D400, 0x1D41A, 0x1D7CE),
        new(MathVariant.Italic, 0x1D434, 0x1D44E, -1),
        new(MathVariant.BoldItalic, 0x1D468, 0x1D482, -1),
        new(MathVariant.Script, 0x1D49C, 0x1D4B6, -1),
        new(MathVariant.BoldScript, 0x1D4D0, 0x1D4EA, -1),
        new(MathVariant.Fraktur, 0x1D504, 0x1D51E, -1),
        new(MathVariant.DoubleStruck, 0x1D538, 0x1D552, 0x1D7D8),
        new(MathVariant.BoldFraktur, 0x1D56C, 0x1D586, -1),
        new(MathVariant.SansSerif, 0x1D5A0, 0x1D5BA, 0x1D7E2),
        new(MathVariant.SansSerifBold, 0x1D5D4, 0x1D5EE, 0x1D7EC),
        new(MathVariant.SansSerifItalic, 0x1D608, 0x1D622, -1),
        new(MathVariant.SansSerifBoldItalic, 0x1D63C, 0x1D656, -1),
        new(MathVariant.Monospace, 0x1D670, 0x1D68A, 0x1D7F6),
    };

    // Các ký tự đã có sẵn trong khối Letterlike Symbols nên khối toán để trống.
    private static readonly Dictionary<(MathVariant, char), int> Holes = new()
    {
        [(MathVariant.Italic, 'h')] = 0x210E,
        [(MathVariant.Script, 'B')] = 0x212C,
        [(MathVariant.Script, 'E')] = 0x2130,
        [(MathVariant.Script, 'F')] = 0x2131,
        [(MathVariant.Script, 'H')] = 0x210B,
        [(MathVariant.Script, 'I')] = 0x2110,
        [(MathVariant.Script, 'L')] = 0x2112,
        [(MathVariant.Script, 'M')] = 0x2133,
        [(MathVariant.Script, 'R')] = 0x211B,
        [(MathVariant.Script, 'e')] = 0x212F,
        [(MathVariant.Script, 'g')] = 0x210A,
        [(MathVariant.Script, 'o')] = 0x2134,
        [(MathVariant.Fraktur, 'C')] = 0x212D,
        [(MathVariant.Fraktur, 'H')] = 0x210C,
        [(MathVariant.Fraktur, 'I')] = 0x2111,
        [(MathVariant.Fraktur, 'R')] = 0x211C,
        [(MathVariant.Fraktur, 'Z')] = 0x2128,
        [(MathVariant.DoubleStruck, 'C')] = 0x2102,
        [(MathVariant.DoubleStruck, 'H')] = 0x210D,
        [(MathVariant.DoubleStruck, 'N')] = 0x2115,
        [(MathVariant.DoubleStruck, 'P')] = 0x2119,
        [(MathVariant.DoubleStruck, 'Q')] = 0x211A,
        [(MathVariant.DoubleStruck, 'R')] = 0x211D,
        [(MathVariant.DoubleStruck, 'Z')] = 0x2124,
    };

    private static readonly Dictionary<int, (char Base, MathVariant Variant)> Reverse = BuildReverse();

    private static Dictionary<int, (char, MathVariant)> BuildReverse()
    {
        var map = new Dictionary<int, (char, MathVariant)>();
        foreach (var a in Alphabets)
        {
            for (int i = 0; i < 26; i++)
            {
                map[a.Upper + i] = ((char)('A' + i), a.Variant);
                map[a.Lower + i] = ((char)('a' + i), a.Variant);
            }
            if (a.Digits >= 0)
                for (int i = 0; i < 10; i++)
                    map[a.Digits + i] = ((char)('0' + i), a.Variant);
        }
        foreach (var kv in Holes)
            map[kv.Value] = (kv.Key.Item2, kv.Key.Item1);
        return map;
    }

    private static MathVariant Canonical(MathVariant v) => v == MathVariant.Calligraphic ? MathVariant.Script : v;

    /// <summary>Có ký tự Unicode riêng cho (c, variant) không?</summary>
    public static bool TryMap(char c, MathVariant variant, out string mapped)
    {
        var v = Canonical(variant);
        mapped = c.ToString();
        if (Holes.TryGetValue((v, c), out int hole))
        {
            mapped = char.ConvertFromUtf32(hole);
            return true;
        }
        foreach (var a in Alphabets)
        {
            if (a.Variant != v) continue;
            int cp = c switch
            {
                >= 'A' and <= 'Z' => a.Upper + (c - 'A'),
                >= 'a' and <= 'z' => a.Lower + (c - 'a'),
                >= '0' and <= '9' when a.Digits >= 0 => a.Digits + (c - '0'),
                _ => -1,
            };
            if (cp < 0) return false;
            mapped = char.ConvertFromUtf32(cp);
            return true;
        }
        return false;
    }

    /// <summary>Ánh xạ cả chuỗi; ký tự không có trong bảng giữ nguyên.</summary>
    public static string Map(string text, MathVariant variant)
    {
        var sb = new StringBuilder(text.Length * 2);
        foreach (char c in text)
            sb.Append(TryMap(c, variant, out var m) ? m : c.ToString());
        return sb.ToString();
    }

    /// <summary>Tách ký tự toán (ví dụ "ℝ") thành ký tự gốc + kiểu chữ ("R", DoubleStruck).</summary>
    public static bool TryDecompose(int codePoint, out char baseChar, out MathVariant variant)
    {
        if (Reverse.TryGetValue(codePoint, out var r))
        {
            baseChar = r.Base;
            variant = r.Variant;
            return true;
        }
        baseChar = '\0';
        variant = MathVariant.Default;
        return false;
    }

    public static bool IsGreekUpper(char c) => c is >= 'Α' and <= 'Ω';

    public static bool IsGreekLower(char c) => c is >= 'α' and <= 'ω' || c is 'ϑ' or 'ϕ' or 'ϖ' or 'ϰ' or 'ϱ' or 'ϵ' or 'ϝ';

    public static bool IsLatin(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    /// <summary>Quy ước TeX cho <see cref="MathVariant.Default"/>: chữ có nghiêng không?</summary>
    public static bool IsItalicByDefault(string text) =>
        text.Length == 1 && (IsLatin(text[0]) || IsGreekLower(text[0]));
}
