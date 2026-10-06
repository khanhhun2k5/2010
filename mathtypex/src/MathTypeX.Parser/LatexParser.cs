using System.Text;
using MathTypeX.Ast;

namespace MathTypeX.Parsing;

public sealed record ParserOptions
{
    /// <summary>Coi "3,14" là một số (dấu phẩy thập phân kiểu Việt Nam) thay vì "3, 14".</summary>
    public bool DecimalComma { get; init; }

    /// <summary>Chạy <see cref="Normalizer"/> sau khi parse (mặc định bật).</summary>
    public bool Normalize { get; init; } = true;

    public static ParserOptions Default { get; } = new();
}

/// <summary>
/// Parser LaTeX math: đệ quy xuống, chạy theo bảng lệnh, có error recovery.
/// Không bao giờ throw — mọi lỗi trở thành <see cref="Diagnostic"/> cộng node Placeholder/Error.
/// </summary>
public static class LatexParser
{
    public static MathDocument Parse(string? source, ParserOptions? options = null)
    {
        source ??= "";
        options ??= ParserOptions.Default;
        var diagnostics = new List<Diagnostic>();
        var tokens = Tokenizer.Tokenize(source, diagnostics);
        var core = new ParserCore(tokens, diagnostics, options);
        Row body = core.ParseTop();
        if (options.Normalize)
            body = Normalizer.Normalize(body);
        diagnostics.Sort((a, b) => a.Span.Start.CompareTo(b.Span.Start));
        return new MathDocument(body, diagnostics);
    }
}

[Flags]
internal enum Stop
{
    None = 0,
    EndGroup = 1,
    Right = 2,
    Alignment = 4,
    RowSeparator = 8,
    End = 16,
    CloseBracket = 32,
    MathShift = 64,
}

internal sealed class ParserCore
{
    private const int MaxDepth = 128;

    private static readonly Dictionary<string, MathVariant> VariantCommands = new()
    {
        ["mathrm"] = MathVariant.Normal,
        ["mathup"] = MathVariant.Normal,
        ["mathit"] = MathVariant.Italic,
        ["mathbf"] = MathVariant.Bold,
        ["mathbfit"] = MathVariant.BoldItalic,
        ["boldsymbol"] = MathVariant.BoldItalic,
        ["bm"] = MathVariant.BoldItalic,
        ["mathsf"] = MathVariant.SansSerif,
        ["mathsfit"] = MathVariant.SansSerifItalic,
        ["mathtt"] = MathVariant.Monospace,
        ["mathbb"] = MathVariant.DoubleStruck,
        ["mathcal"] = MathVariant.Calligraphic,
        ["mathscr"] = MathVariant.Script,
        ["mathfrak"] = MathVariant.Fraktur,
        ["mathnormal"] = MathVariant.Default,
    };

    private static readonly Dictionary<string, (string Char, VerticalPosition Pos)> GroupChars = new()
    {
        ["overbrace"] = ("⏞", VerticalPosition.Top),
        ["underbrace"] = ("⏟", VerticalPosition.Bottom),
        ["overbracket"] = ("⎴", VerticalPosition.Top),
        ["underbracket"] = ("⎵", VerticalPosition.Bottom),
        ["overparen"] = ("⏜", VerticalPosition.Top),
        ["underparen"] = ("⏝", VerticalPosition.Bottom),
    };

    internal static readonly Dictionary<string, string> ExtensibleArrows = new()
    {
        ["xrightarrow"] = "→",
        ["xleftarrow"] = "←",
        ["xRightarrow"] = "⇒",
        ["xLeftarrow"] = "⇐",
        ["xleftrightarrow"] = "↔",
        ["xLeftrightarrow"] = "⇔",
        ["xmapsto"] = "↦",
        ["xhookrightarrow"] = "↪",
    };

    internal static readonly Dictionary<string, string> Negations = new()
    {
        ["="] = "≠", ["∈"] = "∉", ["∋"] = "∌", ["⊂"] = "⊄", ["⊃"] = "⊅", ["⊆"] = "⊈", ["⊇"] = "⊉",
        ["≡"] = "≢", ["<"] = "≮", [">"] = "≯", ["≤"] = "≰", ["≥"] = "≱", ["∼"] = "≁", ["≈"] = "≉",
        ["≅"] = "≇", ["≃"] = "≄", ["∣"] = "∤", ["∥"] = "∦", ["∃"] = "∄", ["→"] = "↛", ["←"] = "↚",
        ["⇒"] = "⇏", ["⇔"] = "⇎", ["≺"] = "⊀", ["≻"] = "⊁", ["⊢"] = "⊬", ["⊨"] = "⊭",
    };

    private readonly List<Token> _tokens;
    private readonly List<Diagnostic> _diagnostics;
    private readonly ParserOptions _options;
    private int _pos;
    private int _lastEnd;
    private int _depth;
    private bool _depthReported;
    private Stop _inherited;

    // \tag, \label, \nonumber của dòng hiện tại (đọc bởi môi trường căn dòng hoặc ở cấp cao nhất).
    private string? _pendingTag;
    private string? _pendingLabel;
    private bool _pendingNoNumber;

    public ParserCore(List<Token> tokens, List<Diagnostic> diagnostics, ParserOptions options)
    {
        _tokens = tokens;
        _diagnostics = diagnostics;
        _options = options;
    }

    // ── Hạ tầng ───────────────────────────────────────────────────────────

    private Token Peek(int k = 0) => _tokens[Math.Min(_pos + k, _tokens.Count - 1)];

    private Token Advance()
    {
        var t = Peek();
        if (_pos < _tokens.Count - 1)
        {
            _pos++;
            _lastEnd = t.Span.End;
        }
        return t;
    }

    private void SkipSpaces()
    {
        while (Peek().Kind == TokenKind.Space) Advance();
    }

    private void Report(DiagnosticCode code, SourceSpan span, ArgRole? role = null, string? command = null,
        IReadOnlyList<string>? args = null, FixIt? fix = null, DiagnosticSeverity severity = DiagnosticSeverity.Error)
    {
        _diagnostics.Add(new Diagnostic(code, severity, span, role, command, args, fix is null ? null : new[] { fix }));
    }

    private static SourceSpan At(Token t) => new(t.Span.Start, 0);

    // ── Cấp cao nhất ──────────────────────────────────────────────────────

    public Row ParseTop()
    {
        var body = ParseRow(Stop.None);
        if (_pendingTag is not null || _pendingLabel is not null)
        {
            var row = new AlignedRow(new MathNode[] { body }, _pendingTag, _pendingLabel, _pendingNoNumber);
            _pendingTag = _pendingLabel = null;
            return new Row(new MathNode[] { new Alignment(AlignmentKind.Equation, new[] { row }) { Span = body.Span } }) { Span = body.Span };
        }
        return body;
    }

    // ── Danh sách atom ───────────────────────────────────────────────────

    private Row ParseRow(Stop explicitStops)
    {
        var stop = explicitStops | _inherited;
        int start = Peek().Span.Start;
        var items = new List<MathNode>();
        List<MathNode>? infixNumerator = null;
        Token infixToken = default;

        if (++_depth > MaxDepth)
        {
            if (!_depthReported)
            {
                Report(DiagnosticCode.NestingTooDeep, At(Peek()));
                _depthReported = true;
            }
            _depth--;
            return new Row(Array.Empty<MathNode>()) { Span = At(Peek()) };
        }

        try
        {
            while (true)
            {
                var t = Peek();
                if (t.Kind == TokenKind.EndOfInput) break;

                if (t.Kind == TokenKind.Space)
                {
                    Advance();
                    continue;
                }

                if (t.Kind == TokenKind.EndGroup)
                {
                    if ((stop & Stop.EndGroup) != 0) break;
                    Report(DiagnosticCode.ExtraCloseBrace, t.Span, fix: new FixIt(t.Span, ""));
                    Advance();
                    continue;
                }

                if (t.Kind is TokenKind.Superscript or TokenKind.Subscript)
                {
                    ParseScript(items);
                    continue;
                }

                if (t.Kind == TokenKind.Alignment)
                {
                    if ((stop & Stop.Alignment) != 0) break;
                    Report(DiagnosticCode.MisplacedAlignment, t.Span);
                    Advance();
                    continue;
                }

                if (t.Kind == TokenKind.MathShift)
                {
                    if ((stop & Stop.MathShift) != 0) break;
                    Report(DiagnosticCode.UnexpectedMathShift, t.Span, fix: new FixIt(t.Span, ""), severity: DiagnosticSeverity.Warning);
                    Advance();
                    continue;
                }

                if (t.Kind == TokenKind.Parameter)
                {
                    Report(DiagnosticCode.UnexpectedParameter, t.Span);
                    Advance();
                    items.Add(new ErrorNode("#") { Span = t.Span });
                    continue;
                }

                if (t.IsOther("]") && (stop & Stop.CloseBracket) != 0) break;

                if (t.IsOther("'"))
                {
                    ParsePrimes(items);
                    continue;
                }

                if (t.Kind == TokenKind.ControlSymbol && t.Text == "\\")
                {
                    if ((stop & Stop.RowSeparator) != 0) break;
                    Report(DiagnosticCode.MisplacedRowSeparator, t.Span);
                    Advance();
                    continue;
                }

                if (t.Kind == TokenKind.ControlWord)
                {
                    switch (t.Text)
                    {
                        case "right":
                        case "middle":
                            if ((stop & Stop.Right) != 0) goto done;
                            Advance();
                            Report(DiagnosticCode.UnmatchedRight, t.Span);
                            var orphan = ParseDelimiter(t);
                            if (orphan is not null)
                                items.Add(new Operator(orphan.Length == 0 ? "" : orphan, t.Text == "right" ? AtomClass.Close : AtomClass.Rel) { Span = t.Span });
                            continue;
                        case "end":
                            if ((stop & Stop.End) != 0) goto done;
                            Advance();
                            string endName = RawTextArgument(ArgRole.EnvironmentName, t) ?? "";
                            Report(DiagnosticCode.MisplacedEnd, SourceSpan.FromBounds(t.Span.Start, _lastEnd), args: new[] { endName });
                            continue;
                        case "limits":
                        case "nolimits":
                        case "displaylimits":
                            Advance();
                            ApplyLimits(items, t);
                            continue;
                        case "over":
                        case "atop":
                        case "choose":
                        case "brack":
                        case "brace":
                            Advance();
                            if (infixNumerator is null)
                            {
                                infixNumerator = items;
                                infixToken = t;
                                items = new List<MathNode>();
                            }
                            else
                            {
                                // TeX: "Ambiguous; you need another { and }" — giữ phân số đầu, coi phần sau là mẫu số.
                                Report(DiagnosticCode.UnsupportedCommand, t.Span, command: "\\" + t.Text, severity: DiagnosticSeverity.Warning);
                            }
                            continue;
                    }
                }

                var atom = ParseAtom();
                if (atom is not null) items.Add(atom);
            }

            done:
            if (infixNumerator is not null)
            {
                var num = MakeRow(infixNumerator, start);
                var den = MakeRow(items, infixToken.Span.End);
                var frac = BuildInfix(infixToken.Text, num, den);
                return new Row(new[] { frac }) { Span = SourceSpan.FromBounds(start, _lastEnd) };
            }
            return new Row(items) { Span = SourceSpan.FromBounds(start, Math.Max(start, _lastEnd)) };
        }
        finally
        {
            _depth--;
        }
    }

    private Row MakeRow(List<MathNode> items, int fallbackStart)
    {
        if (items.Count == 0) return new Row(Array.Empty<MathNode>()) { Span = new SourceSpan(fallbackStart, 0) };
        var span = items[0].Span.Union(items[items.Count - 1].Span);
        return new Row(items) { Span = span };
    }

    private static MathNode BuildInfix(string kind, Row num, Row den)
    {
        var span = num.Span.Union(den.Span);
        MathNode n = AstWalker.Unwrap(num), d = AstWalker.Unwrap(den);
        return kind switch
        {
            "over" => new Fraction(n, d) { Span = span },
            "atop" => new Fraction(n, d, FractionKind.NoBar) { Span = span },
            "choose" => new Fenced("(", ")", new MathNode[] { new Fraction(n, d, FractionKind.NoBar) { Span = span } }, Array.Empty<string>()) { Span = span },
            "brack" => new Fenced("[", "]", new MathNode[] { new Fraction(n, d, FractionKind.NoBar) { Span = span } }, Array.Empty<string>()) { Span = span },
            _ => new Fenced("{", "}", new MathNode[] { new Fraction(n, d, FractionKind.NoBar) { Span = span } }, Array.Empty<string>()) { Span = span },
        };
    }

    // ── Chỉ số trên/dưới, dấu phẩy trên ───────────────────────────────────

    private void ParseScript(List<MathNode> items)
    {
        var op = Advance();
        bool sup = op.Kind == TokenKind.Superscript;
        MathNode baseNode = PopBase(items, op);
        var arg = ScriptArgument(sup ? ArgRole.Superscript : ArgRole.Subscript, op);
        items.Add(AttachScript(baseNode, arg, sup, op.Span));
    }

    private MathNode PopBase(List<MathNode> items, Token op)
    {
        if (items.Count == 0) return new Row(Array.Empty<MathNode>()) { Span = At(op) };
        var b = items[items.Count - 1];
        items.RemoveAt(items.Count - 1);
        return b;
    }

    private MathNode AttachScript(MathNode baseNode, MathNode arg, bool sup, SourceSpan opSpan)
    {
        var span = baseNode.Span.Union(arg.Span);
        if (baseNode is Scripts s)
        {
            if (sup && s.Sup is null) return s with { Sup = arg, Span = span };
            if (!sup && s.Sub is null) return s with { Sub = arg, Span = span };
            Report(sup ? DiagnosticCode.DoubleSuperscript : DiagnosticCode.DoubleSubscript, opSpan);
            var wrapped = new Group(new Row(new MathNode[] { s }) { Span = s.Span }) { Span = s.Span };
            return new Scripts(wrapped, sup ? null : arg, sup ? arg : null) { Span = span };
        }
        return new Scripts(baseNode, sup ? null : arg, sup ? arg : null) { Span = span };
    }

    private void ParsePrimes(List<MathNode> items)
    {
        var first = Peek();
        int count = 0;
        while (Peek().IsOther("'"))
        {
            Advance();
            count++;
        }
        string text = count switch
        {
            1 => "′",
            2 => "″",
            3 => "‴",
            4 => "⁗",
            _ => new string('′', count),
        };
        MathNode sup = new Operator(text, AtomClass.Ord) { Span = SourceSpan.FromBounds(first.Span.Start, _lastEnd) };
        SkipSpaces();
        if (Peek().Kind == TokenKind.Superscript)
        {
            var op = Advance();
            var arg = ScriptArgument(ArgRole.Superscript, op);
            sup = new Row(new[] { sup, arg }) { Span = sup.Span.Union(arg.Span) };
        }
        var baseNode = PopBase(items, first);
        items.Add(AttachScript(baseNode, sup, true, first.Span));
    }

    private MathNode ScriptArgument(ArgRole role, Token op)
    {
        SkipSpaces();
        var t = Peek();
        if (t.Kind == TokenKind.BeginGroup) return AstWalker.Unwrap(GroupArgument(role));
        if (IsArgumentStart(t)) return SingleTokenArgument();
        Report(DiagnosticCode.MissingArgument, At(t), role, op.Text, fix: new FixIt(At(t), "{}"));
        return new Placeholder { Span = At(t) };
    }

    // ── Đối số ───────────────────────────────────────────────────────────

    private bool IsArgumentStart(Token t) => t.Kind switch
    {
        TokenKind.Letter or TokenKind.Digit or TokenKind.Active => true,
        TokenKind.Other => true,
        TokenKind.ControlWord => t.Text is not ("right" or "middle" or "end" or "over" or "atop" or "choose" or "limits" or "nolimits"),
        TokenKind.ControlSymbol => t.Text != "\\",
        _ => false,
    };

    /// <summary>Đối số bắt buộc: {…} hoặc đúng một token (quy tắc TeX: \frac12 = ½).</summary>
    private MathNode Argument(ArgRole role, Token command)
    {
        SkipSpaces();
        var t = Peek();
        if (t.Kind == TokenKind.BeginGroup) return AstWalker.Unwrap(GroupArgument(role));
        if (IsArgumentStart(t)) return SingleTokenArgument();
        Report(DiagnosticCode.MissingArgument, At(t), role, "\\" + command.Text, fix: new FixIt(At(t), "{}"));
        return new Placeholder { Span = At(t) };
    }

    private MathNode SingleTokenArgument()
    {
        var t = Peek();
        switch (t.Kind)
        {
            case TokenKind.Digit:
                Advance();
                return new Number(t.Text) { Span = t.Span };
            case TokenKind.Letter:
                Advance();
                return MakeLetter(t);
            default:
                return ParseAtom() ?? new Placeholder { Span = At(t) };
        }
    }

    /// <summary>Đọc {…}; báo thiếu } theo vai trò của đối số.</summary>
    private Row GroupArgument(ArgRole? role)
    {
        var open = Advance();
        var saved = _inherited;
        _inherited |= Stop.EndGroup;
        Row row;
        try
        {
            row = ParseRow(Stop.EndGroup);
        }
        finally
        {
            _inherited = saved;
        }

        if (Peek().Kind == TokenKind.EndGroup)
        {
            Advance();
        }
        else
        {
            var at = At(Peek());
            Report(DiagnosticCode.MissingCloseBrace, at, role, fix: new FixIt(at, "}"));
        }
        return row with { Span = SourceSpan.FromBounds(open.Span.Start, Math.Max(open.Span.End, _lastEnd)) };
    }

    private MathNode? OptionalArgument(ArgRole role)
    {
        SkipSpaces();
        if (!Peek().IsOther("[")) return null;
        Advance();
        var saved = _inherited;
        _inherited &= ~Stop.CloseBracket;
        Row row;
        try
        {
            row = ParseRow(Stop.CloseBracket);
        }
        finally
        {
            _inherited = saved;
        }
        if (Peek().IsOther("]"))
        {
            Advance();
        }
        else
        {
            var at = At(Peek());
            Report(DiagnosticCode.MissingCloseBracket, at, role, fix: new FixIt(at, "]"));
        }
        return AstWalker.Unwrap(row);
    }

    /// <summary>Đọc {chữ thô} cho tên môi trường, \label, \tag, \operatorname…</summary>
    private string? RawTextArgument(ArgRole role, Token command)
    {
        SkipSpaces();
        if (Peek().Kind != TokenKind.BeginGroup)
        {
            var t = Peek();
            if (t.Kind is TokenKind.Letter or TokenKind.Digit or TokenKind.Other)
            {
                Advance();
                return t.Text;
            }
            Report(DiagnosticCode.MissingArgument, At(t), role, "\\" + command.Text, fix: new FixIt(At(t), "{}"));
            return null;
        }

        Advance();
        var sb = new StringBuilder();
        int depth = 0;
        while (true)
        {
            var t = Peek();
            if (t.Kind == TokenKind.EndOfInput)
            {
                Report(DiagnosticCode.MissingCloseBrace, At(t), role, fix: new FixIt(At(t), "}"));
                break;
            }
            Advance();
            if (t.Kind == TokenKind.EndGroup)
            {
                if (depth == 0) break;
                depth--;
                continue;
            }
            if (t.Kind == TokenKind.BeginGroup)
            {
                depth++;
                continue;
            }
            switch (t.Kind)
            {
                case TokenKind.Space:
                    sb.Append(' ');
                    break;
                case TokenKind.ControlSymbol:
                    sb.Append(t.Text is "," or ";" or ":" or " " ? " " : t.Text);
                    break;
                case TokenKind.ControlWord:
                    break;
                default:
                    sb.Append(t.Text);
                    break;
            }
        }
        return sb.ToString().Trim();
    }

    // ── Atom ─────────────────────────────────────────────────────────────

    private MathNode? ParseAtom()
    {
        var t = Peek();
        switch (t.Kind)
        {
            case TokenKind.Letter:
                Advance();
                return MakeLetter(t);
            case TokenKind.Digit:
                return ParseNumber();
            case TokenKind.Other:
                Advance();
                return MakeChar(t);
            case TokenKind.BeginGroup:
            {
                var row = GroupArgument(null);
                return new Group(row) { Span = row.Span };
            }
            case TokenKind.ControlWord:
                return ParseControlWord();
            case TokenKind.ControlSymbol:
                return ParseControlSymbol();
            case TokenKind.Active:
                Advance();
                return new Space(SpaceKind.NonBreaking) { Span = t.Span };
            default:
                Advance();
                return null;
        }
    }

    private static bool TryCodePoint(string text, out int codePoint)
    {
        codePoint = 0;
        if (text.Length == 0) return false;
        if (!char.IsSurrogate(text[0]))
        {
            codePoint = text[0];
            return true;
        }
        if (text.Length >= 2 && char.IsSurrogatePair(text[0], text[1]))
        {
            codePoint = char.ConvertToUtf32(text[0], text[1]);
            return true;
        }
        return false;
    }

    private static MathNode MakeLetter(Token t)
    {
        if (TryCodePoint(t.Text, out int cp) && MathAlphabets.TryDecompose(cp, out char baseChar, out var variant))
            return new Identifier(baseChar.ToString(), variant) { Span = t.Span };
        return new Identifier(t.Text) { Span = t.Span };
    }

    private static MathNode MakeChar(Token t)
    {
        string text = t.Text switch
        {
            "-" => "−",
            "*" => "∗",
            _ => t.Text,
        };

        if (TryCodePoint(text, out int cp) && MathAlphabets.TryDecompose(cp, out char baseChar, out var variant))
        {
            return char.IsDigit(baseChar)
                ? new Number(baseChar.ToString(), variant) { Span = t.Span }
                : new Identifier(baseChar.ToString(), variant) { Span = t.Span };
        }

        if (text == "'") return new Operator("′", AtomClass.Ord) { Span = t.Span };

        if (LatexSymbols.TryGetChar(text, out var info))
        {
            return info.Kind switch
            {
                SymbolKind.Identifier => new Identifier(text, info.Variant) { Span = t.Span },
                // Bất biến D9: ∫ gõ trực tiếp vẫn là cấu trúc n-ary.
                SymbolKind.LargeOperator => new LargeOperator(text, info.Nary) { Span = t.Span },
                _ => new Operator(text, info.Class) { Span = t.Span },
            };
        }

        if (Negations.ContainsValue(text)) return new Operator(text, AtomClass.Rel) { Span = t.Span };

        return new Operator(text, AtomClass.Ord) { Span = t.Span };
    }

    private MathNode ParseNumber()
    {
        var first = Peek();
        var sb = new StringBuilder();
        while (true)
        {
            var t = Peek();
            if (t.Kind == TokenKind.Digit)
            {
                sb.Append(Advance().Text);
                continue;
            }
            if (sb.Length > 0 && Peek(1).Kind == TokenKind.Digit)
            {
                if (t.IsOther("."))
                {
                    sb.Append(Advance().Text);
                    continue;
                }
                if (t.IsOther(",") && _options.DecimalComma)
                {
                    sb.Append(Advance().Text);
                    continue;
                }
            }
            // Thành ngữ TeX cho dấu phẩy thập phân: 3{,}14
            if (sb.Length > 0 && t.Kind == TokenKind.BeginGroup && Peek(1).IsOther(",")
                && Peek(2).Kind == TokenKind.EndGroup && Peek(3).Kind == TokenKind.Digit)
            {
                Advance();
                Advance();
                Advance();
                sb.Append(',');
                continue;
            }
            break;
        }
        return new Number(sb.ToString()) { Span = SourceSpan.FromBounds(first.Span.Start, _lastEnd) };
    }

    private MathNode? ParseControlSymbol()
    {
        var t = Advance();
        return t.Text switch
        {
            "," => new Space(SpaceKind.Thin) { Span = t.Span },
            ":" or ">" => new Space(SpaceKind.Medium) { Span = t.Span },
            ";" => new Space(SpaceKind.Thick) { Span = t.Span },
            "!" => new Space(SpaceKind.NegativeThin) { Span = t.Span },
            " " => new Space(SpaceKind.Interword) { Span = t.Span },
            "{" => new Operator("{", AtomClass.Open) { Span = t.Span },
            "}" => new Operator("}", AtomClass.Close) { Span = t.Span },
            "|" => new Operator("‖", AtomClass.Ord) { Span = t.Span },
            "$" or "%" or "&" or "#" or "_" => new Operator(t.Text, AtomClass.Ord) { Span = t.Span },
            _ => Unknown(t, t.Text),
        };
    }

    private MathNode Unknown(Token t, string name)
    {
        var suggestions = CommandSuggester.Suggest(name);
        Report(DiagnosticCode.UnknownCommand, t.Span, command: "\\" + name, args: suggestions);
        return new UnknownCommand(name, Array.Empty<MathNode>()) { Span = t.Span };
    }

    private MathNode? ParseControlWord()
    {
        var t = Advance();
        string name = t.Text;

        if (LatexSymbols.TryGetCommand(name, out var info))
        {
            return info.Kind switch
            {
                SymbolKind.Identifier => new Identifier(info.Text, info.Variant) { Span = t.Span },
                SymbolKind.Operator => new Operator(info.Text, info.Class) { Span = t.Span },
                SymbolKind.LargeOperator => new LargeOperator(info.Text, info.Nary) { Span = t.Span },
                SymbolKind.Function => new FunctionApply(info.Text, true, info.Limits) { Span = t.Span },
                _ => new Space(info.Space) { Span = t.Span },
            };
        }

        if (LatexSymbols.Accents.TryGetValue(name, out var accent))
        {
            var b = Argument(ArgRole.AccentBase, t);
            return new Accent(b, accent.Char, accent.Stretchy) { Span = Span(t) };
        }

        if (VariantCommands.TryGetValue(name, out var variant))
        {
            var arg = Argument(ArgRole.Content, t);
            return ApplyVariant(arg, variant, adaptiveBold: name is "boldsymbol" or "bm");
        }

        if (GroupChars.TryGetValue(name, out var gc))
        {
            var b = Argument(ArgRole.Base, t);
            return new GroupChar(b, gc.Char, gc.Pos) { Span = Span(t) };
        }

        if (ExtensibleArrows.TryGetValue(name, out var arrow))
        {
            var below = OptionalArgument(ArgRole.Under);
            var above = Argument(ArgRole.Over, t);
            return new GroupChar(above, arrow, VerticalPosition.Bottom, below) { Span = Span(t) };
        }

        switch (name)
        {
            case "frac":
            case "dfrac":
            case "tfrac":
            case "cfrac":
            {
                var num = Argument(ArgRole.Numerator, t);
                var den = Argument(ArgRole.Denominator, t);
                var style = name switch
                {
                    "dfrac" or "cfrac" => MathStyleOverride.Display,
                    "tfrac" => MathStyleOverride.Text,
                    _ => MathStyleOverride.Auto,
                };
                return new Fraction(num, den, FractionKind.Bar, style) { Span = Span(t) };
            }
            case "binom":
            case "dbinom":
            case "tbinom":
            {
                var n = Argument(ArgRole.Over, t);
                var k = Argument(ArgRole.Under, t);
                var style = name == "dbinom" ? MathStyleOverride.Display : name == "tbinom" ? MathStyleOverride.Text : MathStyleOverride.Auto;
                var frac = new Fraction(n, k, FractionKind.NoBar, style) { Span = Span(t) };
                return Fenced.Simple("(", ")", frac) with { Span = frac.Span };
            }
            case "sqrt":
            {
                var index = OptionalArgument(ArgRole.RootIndex);
                var radicand = Argument(ArgRole.Radicand, t);
                return new Radical(radicand, index) { Span = Span(t) };
            }
            case "left":
                return ParseLeftRight(t);
            case "big":
            case "Big":
            case "bigg":
            case "Bigg":
            case "bigl":
            case "Bigl":
            case "biggl":
            case "Biggl":
            case "bigr":
            case "Bigr":
            case "biggr":
            case "Biggr":
            case "bigm":
            case "Bigm":
            case "biggm":
            case "Biggm":
                return ParseBigDelimiter(t);
            case "operatorname":
            {
                bool star = false;
                if (Peek().IsOther("*"))
                {
                    Advance();
                    star = true;
                }
                string text = RawTextArgument(ArgRole.OperatorName, t) ?? "";
                return new FunctionApply(text, false, star) { Span = Span(t) };
            }
            case "text":
            case "textrm":
            case "textup":
            case "textnormal":
            case "textsf":
            case "texttt":
            case "mbox":
            case "hbox":
                return ParseText(t, TextStyle.Normal);
            case "textbf":
                return ParseText(t, TextStyle.Bold);
            case "textit":
            case "emph":
                return ParseText(t, TextStyle.Italic);
            case "overline":
                return new Bar(Argument(ArgRole.Base, t), VerticalPosition.Top) { Span = Span(t) };
            case "underline":
                return new Bar(Argument(ArgRole.Base, t), VerticalPosition.Bottom) { Span = Span(t) };
            case "overset":
            case "stackrel":
            {
                var over = Argument(ArgRole.Over, t);
                var b = Argument(ArgRole.Base, t);
                return new UnderOver(b, null, over) { Span = Span(t) };
            }
            case "underset":
            {
                var under = Argument(ArgRole.Under, t);
                var b = Argument(ArgRole.Base, t);
                return new UnderOver(b, under, null) { Span = Span(t) };
            }
            case "boxed":
                return new Boxed(Argument(ArgRole.Content, t), BoxKind.Boxed) { Span = Span(t) };
            case "cancel":
                return new Boxed(Argument(ArgRole.Content, t), BoxKind.Cancel) { Span = Span(t) };
            case "bcancel":
                return new Boxed(Argument(ArgRole.Content, t), BoxKind.BCancel) { Span = Span(t) };
            case "xcancel":
                return new Boxed(Argument(ArgRole.Content, t), BoxKind.XCancel) { Span = Span(t) };
            case "phantom":
                return new Phantom(Argument(ArgRole.Content, t), PhantomKind.Full) { Span = Span(t) };
            case "hphantom":
                return new Phantom(Argument(ArgRole.Content, t), PhantomKind.Horizontal) { Span = Span(t) };
            case "vphantom":
                return new Phantom(Argument(ArgRole.Content, t), PhantomKind.Vertical) { Span = Span(t) };
            case "displaystyle":
                return new StyleSwitch(MathStyle.Display) { Span = t.Span };
            case "textstyle":
                return new StyleSwitch(MathStyle.Text) { Span = t.Span };
            case "scriptstyle":
                return new StyleSwitch(MathStyle.Script) { Span = t.Span };
            case "scriptscriptstyle":
                return new StyleSwitch(MathStyle.ScriptScript) { Span = t.Span };
            case "not":
                return ParseNot(t);
            case "begin":
                return ParseEnvironment(t);
            case "tag":
            {
                if (Peek().IsOther("*")) Advance();
                _pendingTag = RawTextArgument(ArgRole.Tag, t);
                return null;
            }
            case "label":
                _pendingLabel = RawTextArgument(ArgRole.Label, t);
                return null;
            case "nonumber":
            case "notag":
                _pendingNoNumber = true;
                return null;
            case "hspace":
            {
                if (Peek().IsOther("*")) Advance();
                string dim = RawTextArgument(ArgRole.Content, t) ?? "";
                return new Space(SpaceFromDimension(dim)) { Span = Span(t) };
            }
            case "hline":
            case "midrule":
            case "toprule":
            case "bottomrule":
                Report(DiagnosticCode.ApproximateRendering, t.Span, args: new[] { "\\" + name }, severity: DiagnosticSeverity.Info);
                return null;
            case "color":
            {
                RawTextArgument(ArgRole.Content, t);
                Report(DiagnosticCode.UnsupportedCommand, t.Span, command: "\\color", severity: DiagnosticSeverity.Warning);
                return null;
            }
            case "textcolor":
            {
                RawTextArgument(ArgRole.Content, t);
                Report(DiagnosticCode.UnsupportedCommand, t.Span, command: "\\textcolor", severity: DiagnosticSeverity.Warning);
                return Argument(ArgRole.Content, t);
            }
        }

        return Unknown(t, name);
    }

    private SourceSpan Span(Token start) => SourceSpan.FromBounds(start.Span.Start, Math.Max(start.Span.End, _lastEnd));

    private static SpaceKind SpaceFromDimension(string dim)
    {
        dim = dim.Trim();
        double factor = dim.EndsWith("em", StringComparison.Ordinal) ? 1
            : dim.EndsWith("ex", StringComparison.Ordinal) ? 0.43
            : dim.EndsWith("pt", StringComparison.Ordinal) ? 0.1
            : dim.EndsWith("mu", StringComparison.Ordinal) ? 1.0 / 18
            : 0.1;
        string number = new string(dim.TakeWhile(c => char.IsDigit(c) || c is '.' or '-').ToArray());
        if (!double.TryParse(number, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
            return SpaceKind.Thin;
        double em = v * factor;
        return em switch
        {
            < 0 => SpaceKind.NegativeThin,
            >= 1.5 => SpaceKind.QQuad,
            >= 0.75 => SpaceKind.Quad,
            >= 0.4 => SpaceKind.En,
            >= 0.26 => SpaceKind.Thick,
            >= 0.2 => SpaceKind.Medium,
            _ => SpaceKind.Thin,
        };
    }

    private MathNode? ParseNot(Token t)
    {
        SkipSpaces();
        var next = ParseAtom();
        switch (next)
        {
            case Operator op when Negations.TryGetValue(op.Text, out var neg):
                return new Operator(neg, AtomClass.Rel) { Span = Span(t) };
            case Identifier id when Negations.TryGetValue(id.Text, out var negId):
                return new Identifier(negId, id.Variant) { Span = Span(t) };
            case Operator op:
                return new Operator(op.Text + "̸", op.Class) { Span = Span(t) };
            case null:
                Report(DiagnosticCode.MissingArgument, At(Peek()), ArgRole.Content, "\\not");
                return null;
            default:
                return next;
        }
    }

    private MathNode ApplyVariant(MathNode node, MathVariant variant, bool adaptiveBold)
    {
        MathVariant Combine(MathVariant current, string text)
        {
            if (adaptiveBold)
            {
                return current switch
                {
                    MathVariant.Default => MathAlphabets.IsItalicByDefault(text) ? MathVariant.BoldItalic : MathVariant.Bold,
                    MathVariant.Normal => MathVariant.Bold,
                    MathVariant.Italic => MathVariant.BoldItalic,
                    MathVariant.Script or MathVariant.Calligraphic => MathVariant.BoldScript,
                    MathVariant.Fraktur => MathVariant.BoldFraktur,
                    MathVariant.SansSerif => MathVariant.SansSerifBold,
                    MathVariant.SansSerifItalic => MathVariant.SansSerifBoldItalic,
                    _ => current,
                };
            }
            // Lệnh bên trong thắng (TeX: \mathbf{\mathcal F} vẫn là \mathcal).
            return current == MathVariant.Default ? variant : current;
        }

        MathNode Rewrite(MathNode n) => n switch
        {
            Identifier id => id with { Variant = Combine(id.Variant, id.Text) },
            Number num => num with { Variant = Combine(num.Variant, num.Text) },
            _ => AstWalker.MapChildren(n, Rewrite),
        };

        return Rewrite(node);
    }

    // ── \left … \right ───────────────────────────────────────────────────

    private string? ParseDelimiter(Token command)
    {
        SkipSpaces();
        var t = Peek();
        switch (t.Kind)
        {
            case TokenKind.Other when LatexSymbols.DelimiterChars.TryGetValue(t.Text, out var ch):
                Advance();
                return ch;
            case TokenKind.ControlSymbol when t.Text is "{" or "}" or "|":
                Advance();
                return t.Text == "|" ? "‖" : t.Text;
            case TokenKind.ControlWord when LatexSymbols.DelimiterWords.TryGetValue(t.Text, out var word):
                Advance();
                return word;
            case TokenKind.Letter or TokenKind.Other:
                // Ký tự lạ vẫn được nhận (ví dụ \left\uparrow đã ở trên; còn đây là ký tự gõ trực tiếp) nhưng cảnh báo.
                Advance();
                Report(DiagnosticCode.MissingDelimiter, t.Span, command: "\\" + command.Text, severity: DiagnosticSeverity.Warning);
                return t.Text;
            default:
                Report(DiagnosticCode.MissingDelimiter, At(t), command: "\\" + command.Text, fix: new FixIt(At(t), "."));
                return null;
        }
    }

    private MathNode ParseLeftRight(Token left)
    {
        string open = ParseDelimiter(left) ?? "";
        var parts = new List<MathNode>();
        var separators = new List<string>();
        string close = "";
        var saved = _inherited;
        _inherited |= Stop.Right;
        try
        {
            while (true)
            {
                var row = ParseRow(Stop.Right);
                parts.Add(row);
                var t = Peek();
                if (t.IsWord("middle"))
                {
                    Advance();
                    separators.Add(ParseDelimiter(t) ?? "|");
                    continue;
                }
                if (t.IsWord("right"))
                {
                    Advance();
                    close = ParseDelimiter(t) ?? "";
                    break;
                }
                Report(DiagnosticCode.UnmatchedLeft, left.Span, args: new[] { open.Length == 0 ? "." : open },
                    fix: new FixIt(At(t), "\\right."));
                break;
            }
        }
        finally
        {
            _inherited = saved;
        }
        return new Fenced(open, close, parts, separators) { Span = Span(left) };
    }

    private MathNode? ParseBigDelimiter(Token t)
    {
        string name = t.Text;
        var size = name.StartsWith("Bigg", StringComparison.Ordinal) ? DelimiterSize.Big4
            : name.StartsWith("bigg", StringComparison.Ordinal) ? DelimiterSize.Big3
            : name.StartsWith("Big", StringComparison.Ordinal) ? DelimiterSize.Big2
            : DelimiterSize.Big;
        var cls = name[name.Length - 1] switch
        {
            'l' => AtomClass.Open,
            'r' => AtomClass.Close,
            'm' => AtomClass.Rel,
            _ => AtomClass.Ord,
        };
        var delim = ParseDelimiter(t);
        if (delim is null) return null;
        return new Operator(delim, cls, size) { Span = Span(t) };
    }

    // ── \text{…} ─────────────────────────────────────────────────────────

    private MathNode ParseText(Token command, TextStyle style)
    {
        SkipSpaces();
        if (Peek().Kind != TokenKind.BeginGroup)
        {
            var single = Peek();
            if (single.Kind is TokenKind.Letter or TokenKind.Digit or TokenKind.Other)
            {
                Advance();
                return new TextRun(single.Text, style) { Span = Span(command) };
            }
            Report(DiagnosticCode.MissingArgument, At(single), ArgRole.Text, "\\" + command.Text, fix: new FixIt(At(single), "{}"));
            return new Placeholder { Span = At(single) };
        }

        Advance();
        var pieces = new List<MathNode>();
        var sb = new StringBuilder();
        int depth = 0;
        int pieceStart = _lastEnd;

        void Flush()
        {
            if (sb.Length == 0) return;
            pieces.Add(new TextRun(SafeNfc(sb.ToString()), style) { Span = SourceSpan.FromBounds(pieceStart, _lastEnd) });
            sb.Clear();
        }

        while (true)
        {
            var t = Peek();
            if (t.Kind == TokenKind.EndOfInput)
            {
                Report(DiagnosticCode.MissingCloseBrace, At(t), ArgRole.Text, fix: new FixIt(At(t), "}"));
                break;
            }
            if (t.Kind == TokenKind.EndGroup)
            {
                Advance();
                if (depth == 0) break;
                depth--;
                continue;
            }
            if (t.Kind == TokenKind.BeginGroup)
            {
                Advance();
                depth++;
                continue;
            }
            if (t.Kind == TokenKind.MathShift)
            {
                Flush();
                Advance();
                var saved = _inherited;
                _inherited = Stop.MathShift;
                Row math;
                try
                {
                    math = ParseRow(Stop.MathShift);
                }
                finally
                {
                    _inherited = saved;
                }
                if (Peek().Kind == TokenKind.MathShift) Advance();
                else Report(DiagnosticCode.UnexpectedMathShift, At(Peek()), fix: new FixIt(At(Peek()), "$"));
                pieces.Add(math);
                pieceStart = _lastEnd;
                continue;
            }

            Advance();
            switch (t.Kind)
            {
                case TokenKind.Space:
                    sb.Append(' ');
                    break;
                case TokenKind.Active:
                    sb.Append(' ');
                    break;
                case TokenKind.ControlSymbol:
                    sb.Append(t.Text switch
                    {
                        "," => " ",
                        " " => " ",
                        "\\" => "",
                        _ => t.Text,
                    });
                    break;
                case TokenKind.ControlWord:
                    switch (t.Text)
                    {
                        case "textbf":
                        case "textit":
                        case "text":
                        case "textrm":
                        case "emph":
                            Flush();
                            pieces.Add(ParseText(t, t.Text == "textbf" ? TextStyle.Bold : t.Text is "textit" or "emph" ? TextStyle.Italic : style));
                            pieceStart = _lastEnd;
                            break;
                        case "ldots":
                        case "dots":
                            sb.Append('…');
                            break;
                        case "quad":
                            sb.Append(' ');
                            break;
                        case "textbackslash":
                            sb.Append('\\');
                            break;
                        default:
                            Report(DiagnosticCode.UnsupportedCommand, t.Span, command: "\\" + t.Text, severity: DiagnosticSeverity.Warning);
                            break;
                    }
                    break;
                default:
                    sb.Append(t.Text);
                    break;
            }
        }
        Flush();

        if (pieces.Count == 0) return new TextRun("", style) { Span = Span(command) };
        if (pieces.Count == 1) return pieces[0] with { Span = Span(command) };
        return new Row(pieces) { Span = Span(command) };
    }

    /// <summary>Chuẩn hoá NFC (tiếng Việt gõ dạng tổ hợp); surrogate lẻ được thay bằng U+FFFD thay vì làm throw.</summary>
    private static string SafeNfc(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                sb.Append(c).Append(text[++i]);
            }
            else if (char.IsSurrogate(c))
            {
                sb.Append('\uFFFD');
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    // ── Môi trường \begin{…} ─────────────────────────────────────────────

    private static readonly Dictionary<string, TableKind> TableEnvironments = new()
    {
        ["matrix"] = TableKind.Matrix,
        ["pmatrix"] = TableKind.PMatrix,
        ["bmatrix"] = TableKind.BMatrix,
        ["Bmatrix"] = TableKind.BBMatrix,
        ["vmatrix"] = TableKind.VMatrix,
        ["Vmatrix"] = TableKind.VVMatrix,
        ["smallmatrix"] = TableKind.SmallMatrix,
        ["array"] = TableKind.Array,
        ["cases"] = TableKind.Cases,
        ["dcases"] = TableKind.Cases,
        ["rcases"] = TableKind.RCases,
    };

    private static readonly Dictionary<string, AlignmentKind> AlignEnvironments = new()
    {
        ["aligned"] = AlignmentKind.Aligned,
        ["align"] = AlignmentKind.Align,
        ["align*"] = AlignmentKind.AlignStar,
        ["gather"] = AlignmentKind.Gather,
        ["gather*"] = AlignmentKind.GatherStar,
        ["gathered"] = AlignmentKind.Gathered,
        ["split"] = AlignmentKind.Split,
        ["equation"] = AlignmentKind.Equation,
        ["equation*"] = AlignmentKind.EquationStar,
        ["multline"] = AlignmentKind.Multline,
        ["multline*"] = AlignmentKind.Multline,
        ["eqnarray"] = AlignmentKind.Align,
        ["eqnarray*"] = AlignmentKind.AlignStar,
    };

    private MathNode ParseEnvironment(Token begin)
    {
        string? name = EnvironmentName(begin);
        if (name is null) return new ErrorNode("\\begin") { Span = begin.Span };

        IReadOnlyList<ColumnAlign>? arrayColumns = null;
        if (name == "array")
        {
            string spec = RawTextArgument(ArgRole.ColumnSpec, begin) ?? "";
            arrayColumns = ParseColumnSpec(spec, begin);
        }

        bool known = TableEnvironments.ContainsKey(name) || AlignEnvironments.ContainsKey(name);
        if (!known)
            Report(DiagnosticCode.UnknownEnvironment, begin.Span, args: new[] { name }, severity: DiagnosticSeverity.Warning);

        var rows = ParseEnvironmentBody(name, begin);

        if (TableEnvironments.TryGetValue(name, out var tableKind))
        {
            var tableRows = rows.Select(r => new TableRow(r.Cells)).ToArray();
            int cols = tableRows.Length == 0 ? 0 : tableRows.Max(r => r.Cells.Count);
            IReadOnlyList<ColumnAlign> columns = arrayColumns
                ?? (tableKind is TableKind.Cases or TableKind.RCases
                    ? Enumerable.Repeat(ColumnAlign.Left, Math.Max(cols, 1)).ToArray()
                    : Enumerable.Repeat(ColumnAlign.Center, Math.Max(cols, 1)).ToArray());
            return new Table(tableKind, tableRows, columns) { Span = Span(begin) };
        }

        var kind = AlignEnvironments.TryGetValue(name, out var k) ? k : AlignmentKind.Aligned;
        return new Alignment(kind, rows) { Span = Span(begin) };
    }

    private string? EnvironmentName(Token command)
    {
        string? name = RawTextArgument(ArgRole.EnvironmentName, command);
        return name?.Replace(" ", "");
    }

    private IReadOnlyList<ColumnAlign> ParseColumnSpec(string spec, Token begin)
    {
        var cols = new List<ColumnAlign>();
        bool approximated = false;
        foreach (char c in spec)
        {
            switch (c)
            {
                case 'c':
                    cols.Add(ColumnAlign.Center);
                    break;
                case 'l':
                    cols.Add(ColumnAlign.Left);
                    break;
                case 'r':
                    cols.Add(ColumnAlign.Right);
                    break;
                case ' ':
                    break;
                default:
                    approximated = true;
                    break;
            }
        }
        if (approximated)
            Report(DiagnosticCode.ApproximateRendering, begin.Span, args: new[] { "array {" + spec + "}" }, severity: DiagnosticSeverity.Info);
        return cols.Count == 0 ? new[] { ColumnAlign.Center } : cols;
    }

    private List<AlignedRow> ParseEnvironmentBody(string name, Token begin)
    {
        var rows = new List<AlignedRow>();
        var saved = _inherited;
        _inherited |= Stop.Alignment | Stop.RowSeparator | Stop.End;
        string? savedTag = _pendingTag, savedLabel = _pendingLabel;
        bool savedNoNumber = _pendingNoNumber;
        _pendingTag = _pendingLabel = null;
        _pendingNoNumber = false;
        try
        {
            while (true)
            {
                var cells = new List<MathNode>();
                while (true)
                {
                    cells.Add(ParseRow(Stop.None));
                    if (Peek().Kind == TokenKind.Alignment)
                    {
                        Advance();
                        continue;
                    }
                    break;
                }
                rows.Add(new AlignedRow(cells, _pendingTag, _pendingLabel, _pendingNoNumber));
                _pendingTag = _pendingLabel = null;
                _pendingNoNumber = false;

                var t = Peek();
                if (t.Kind == TokenKind.ControlSymbol && t.Text == "\\")
                {
                    Advance();
                    // \\[2pt]
                    if (Peek().IsOther("["))
                    {
                        while (Peek().Kind != TokenKind.EndOfInput && !Peek().IsOther("]")) Advance();
                        if (Peek().IsOther("]")) Advance();
                    }
                    continue;
                }
                if (t.IsWord("end"))
                {
                    Advance();
                    _inherited = saved;
                    string endName = EnvironmentName(t) ?? "";
                    if (endName != name)
                        Report(DiagnosticCode.MismatchedEnvironmentEnd, SourceSpan.FromBounds(t.Span.Start, _lastEnd), args: new[] { name, endName });
                    break;
                }
                Report(DiagnosticCode.MissingEnvironmentEnd, begin.Span, args: new[] { name }, fix: new FixIt(At(t), "\\end{" + name + "}"));
                break;
            }
        }
        finally
        {
            _inherited = saved;
            _pendingTag = savedTag;
            _pendingLabel = savedLabel;
            _pendingNoNumber = savedNoNumber;
        }

        // Dòng rỗng sau \\ cuối cùng không được tính.
        if (rows.Count > 1 && rows[rows.Count - 1].Cells.All(AstWalker.IsEmpty) && rows[rows.Count - 1].Tag is null)
            rows.RemoveAt(rows.Count - 1);
        return rows;
    }

    // ── \limits / \nolimits ──────────────────────────────────────────────

    private void ApplyLimits(List<MathNode> items, Token t)
    {
        var placement = t.Text switch
        {
            "limits" => LimitPlacement.Limits,
            "nolimits" => LimitPlacement.NoLimits,
            _ => LimitPlacement.Auto,
        };
        if (items.Count > 0)
        {
            int last = items.Count - 1;
            switch (items[last])
            {
                case LargeOperator op:
                    items[last] = op with { Limits = placement };
                    return;
                case FunctionApply fn:
                    items[last] = fn with { Limits = placement };
                    return;
                case Scripts { Base: LargeOperator op2 } s:
                    items[last] = s with { Base = op2 with { Limits = placement } };
                    return;
                case Scripts { Base: FunctionApply fn2 } s:
                    items[last] = s with { Base = fn2 with { Limits = placement } };
                    return;
            }
        }
        Report(DiagnosticCode.MisplacedLimits, t.Span, command: "\\" + t.Text, severity: DiagnosticSeverity.Warning);
    }
}
