using System.Globalization;

namespace MathTypeX.Parsing;

/// <summary>
/// Tách source LaTeX thành token với bảng catcode cố định (người dùng không đổi được catcode).
/// Ký pháp ^^ bị từ chối — đây cũng là một lớp bảo mật (^^5c chính là dấu \).
/// </summary>
public static class Tokenizer
{
    public static List<Token> Tokenize(string source, List<Diagnostic> diagnostics)
    {
        var tokens = new List<Token>(source.Length);
        int i = 0;
        int n = source.Length;

        while (i < n)
        {
            char c = source[i];
            int start = i;

            switch (c)
            {
                case '\\':
                    if (i + 1 >= n)
                    {
                        diagnostics.Add(new Diagnostic(DiagnosticCode.IncompleteCommand, DiagnosticSeverity.Error, new SourceSpan(i, 1)));
                        i++;
                        break;
                    }
                    if (IsAsciiLetter(source[i + 1]))
                    {
                        int j = i + 1;
                        while (j < n && IsAsciiLetter(source[j])) j++;
                        tokens.Add(new Token(TokenKind.ControlWord, source.Substring(i + 1, j - i - 1), SourceSpan.FromBounds(i, j)));
                        // Quy tắc TeX: bỏ khoảng trắng ngay sau control word.
                        while (j < n && IsSpace(source[j])) j++;
                        i = j;
                    }
                    else
                    {
                        int len = char.IsSurrogatePair(source, i + 1) ? 2 : 1;
                        tokens.Add(new Token(TokenKind.ControlSymbol, source.Substring(i + 1, len), new SourceSpan(i, 1 + len)));
                        i += 1 + len;
                    }
                    break;
                case '{':
                    tokens.Add(new Token(TokenKind.BeginGroup, "{", new SourceSpan(i++, 1)));
                    break;
                case '}':
                    tokens.Add(new Token(TokenKind.EndGroup, "}", new SourceSpan(i++, 1)));
                    break;
                case '$':
                    tokens.Add(new Token(TokenKind.MathShift, "$", new SourceSpan(i++, 1)));
                    break;
                case '&':
                    tokens.Add(new Token(TokenKind.Alignment, "&", new SourceSpan(i++, 1)));
                    break;
                case '^':
                    if (i + 1 < n && source[i + 1] == '^')
                    {
                        int j = i + 2;
                        if (j + 1 < n && IsLowerHex(source[j]) && IsLowerHex(source[j + 1])) j += 2;
                        else if (j < n) j += 1;
                        diagnostics.Add(new Diagnostic(DiagnosticCode.UnsupportedCaretNotation, DiagnosticSeverity.Error, SourceSpan.FromBounds(i, j)));
                        i = j;
                    }
                    else
                    {
                        tokens.Add(new Token(TokenKind.Superscript, "^", new SourceSpan(i++, 1)));
                    }
                    break;
                case '_':
                    tokens.Add(new Token(TokenKind.Subscript, "_", new SourceSpan(i++, 1)));
                    break;
                case '%':
                    while (i < n && source[i] != '\n' && source[i] != '\r') i++;
                    break;
                case '~':
                    tokens.Add(new Token(TokenKind.Active, "~", new SourceSpan(i++, 1)));
                    break;
                case '#':
                    tokens.Add(new Token(TokenKind.Parameter, "#", new SourceSpan(i++, 1)));
                    break;
                default:
                    if (IsSpace(c))
                    {
                        while (i < n && IsSpace(source[i])) i++;
                        tokens.Add(new Token(TokenKind.Space, " ", SourceSpan.FromBounds(start, i)));
                    }
                    else if (c is >= '0' and <= '9')
                    {
                        tokens.Add(new Token(TokenKind.Digit, c.ToString(), new SourceSpan(i++, 1)));
                    }
                    else
                    {
                        int len = char.IsSurrogatePair(source, i) ? 2 : 1;
                        string text = source.Substring(i, len);
                        var cat = CharUnicodeInfo.GetUnicodeCategory(source, i);
                        bool letter = cat is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
                            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter;
                        tokens.Add(new Token(letter ? TokenKind.Letter : TokenKind.Other, text, new SourceSpan(i, len)));
                        i += len;
                    }
                    break;
            }
        }

        tokens.Add(new Token(TokenKind.EndOfInput, "", new SourceSpan(n, 0)));
        return tokens;
    }

    private static bool IsAsciiLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    private static bool IsLowerHex(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f';

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\r';
}
