using System.Text;
using MathTypeX.Ast;

namespace MathTypeX.Parsing;

/// <summary>
/// In AST thành LaTeX chuẩn hoá: luôn có ngoặc nhọn tường minh, tên lệnh chuẩn.
/// Idempotent: Print(Parse(Print(Parse(x)))) == Print(Parse(x)).
/// Đây cũng là đầu vào duy nhất mà backend TeX nhận (lớp bảo mật số 1, docs/02 §5.9.7).
/// </summary>
public static class LatexPrinter
{
    public static string Print(MathNode node)
    {
        var w = new Writer();
        w.Node(node);
        return w.ToString();
    }

    public static string Print(MathDocument document) => Print(document.Body);

    private static readonly Dictionary<(string, bool), string> AccentNames = BuildAccentNames();

    private static Dictionary<(string, bool), string> BuildAccentNames()
    {
        var map = new Dictionary<(string, bool), string>();
        foreach (var kv in LatexSymbols.Accents)
            if (!map.ContainsKey((kv.Value.Char, kv.Value.Stretchy)))
                map[(kv.Value.Char, kv.Value.Stretchy)] = kv.Key;
        return map;
    }

    private static readonly Dictionary<string, string> DelimiterNames = LatexSymbols.DelimiterWords
        .GroupBy(kv => kv.Value)
        .ToDictionary(g => g.Key, g => g.First().Key);

    private static readonly Dictionary<string, string> NegationBase = ParserCore.Negations
        .ToDictionary(kv => kv.Value, kv => kv.Key);

    private sealed class Writer
    {
        private readonly StringBuilder _sb = new();
        private bool _afterControlWord;

        public override string ToString() => _sb.ToString();

        private void Raw(string s)
        {
            if (s.Length == 0) return;
            if (_afterControlWord && (char.IsLetter(s[0]) || s[0] == '@')) _sb.Append(' ');
            _sb.Append(s);
            _afterControlWord = false;
        }

        private void Cmd(string name)
        {
            Raw("\\" + name);
            _afterControlWord = true;
        }

        private void Braced(MathNode? n)
        {
            Raw("{");
            if (n is not null) Node(n);
            Raw("}");
        }

        public void Node(MathNode node)
        {
            switch (node)
            {
                case Row r:
                    PrintRow(r.Children);
                    break;
                case Group g:
                    if (g.Content.Children.Count == 1 && PrintsOwnBraces(g.Content.Children[0]))
                        Node(g.Content.Children[0]);
                    else
                        Braced(g.Content);
                    break;
                case Identifier or Number:
                    PrintRow(new[] { node });
                    break;
                case Operator op:
                    PrintOperator(op);
                    break;
                case TextRun t:
                    Cmd(t.Style switch { TextStyle.Bold => "textbf", TextStyle.Italic => "textit", _ => "text" });
                    Raw("{" + EscapeText(t.Text) + "}");
                    break;
                case Space s:
                    PrintSpace(s.Kind);
                    break;
                case StyleSwitch ss:
                    Cmd(ss.Style switch
                    {
                        MathStyle.Display => "displaystyle",
                        MathStyle.Text => "textstyle",
                        MathStyle.Script => "scriptstyle",
                        _ => "scriptscriptstyle",
                    });
                    break;
                case Fraction f:
                    PrintFraction(f);
                    break;
                case Radical rad:
                    Cmd("sqrt");
                    if (rad.Index is not null)
                    {
                        Raw("[");
                        Node(rad.Index);
                        Raw("]");
                    }
                    Braced(rad.Radicand);
                    break;
                case Scripts s:
                    PrintScripts(s);
                    break;
                case LargeOperator op:
                    PrintLargeOperator(op);
                    break;
                case FunctionApply fn:
                    PrintFunction(fn);
                    break;
                case Fenced fe:
                    PrintFenced(fe);
                    break;
                case Accent a:
                    Cmd(AccentNames.TryGetValue((a.AccentChar, a.Stretchy), out var an) ? an
                        : AccentNames.TryGetValue((a.AccentChar, !a.Stretchy), out var an2) ? an2 : "hat");
                    Braced(a.Base);
                    break;
                case Bar b:
                    Cmd(b.Position == VerticalPosition.Top ? "overline" : "underline");
                    Braced(b.Base);
                    break;
                case GroupChar gc:
                    PrintGroupChar(gc);
                    break;
                case UnderOver uo:
                    PrintUnderOver(uo);
                    break;
                case Table t:
                    PrintTable(t);
                    break;
                case Alignment al:
                    PrintAlignment(al);
                    break;
                case Boxed bx:
                    Cmd(bx.Kind switch
                    {
                        BoxKind.Cancel => "cancel",
                        BoxKind.BCancel => "bcancel",
                        BoxKind.XCancel => "xcancel",
                        _ => "boxed",
                    });
                    Braced(bx.Content);
                    break;
                case Phantom ph:
                    Cmd(ph.Kind switch
                    {
                        PhantomKind.Horizontal => "hphantom",
                        PhantomKind.Vertical => "vphantom",
                        _ => "phantom",
                    });
                    Braced(ph.Content);
                    break;
                case Placeholder:
                    Raw("{}");
                    break;
                case ErrorNode e:
                    Raw(e.Raw);
                    break;
                case UnknownCommand u:
                    if (u.Name.Length > 0 && char.IsLetter(u.Name[0])) Cmd(u.Name);
                    else Raw("\\" + u.Name);
                    foreach (var a in u.Arguments) Braced(a);
                    break;
            }
        }

        private static bool PrintsOwnBraces(MathNode n) => n is Fraction { Kind: FractionKind.NoBar };

        // ── Chữ và số (gộp các ký tự cùng kiểu chữ: \mathrm{d}, \mathbb{RN}) ──

        private void PrintRow(IReadOnlyList<MathNode> children)
        {
            int i = 0;
            while (i < children.Count)
            {
                var child = children[i];
                string? wrapper = WrapperOf(child);
                if (child is Identifier or Number)
                {
                    var sb = new StringBuilder();
                    int j = i;
                    while (j < children.Count && children[j] is Identifier or Number && WrapperOf(children[j]) == wrapper
                           && !(wrapper is null && j > i))
                    {
                        string piece = InnerText(children[j]);
                        if (sb.Length > 0 && EndsWithControlWord(sb) && piece.Length > 0 && char.IsLetter(piece[0])) sb.Append(' ');
                        sb.Append(piece);
                        j++;
                    }
                    if (wrapper is null)
                    {
                        RawToken(InnerText(child));
                        i++;
                    }
                    else
                    {
                        Cmd(wrapper);
                        Raw("{" + sb + "}");
                        i = j;
                    }
                    continue;
                }
                Node(child);
                i++;
            }
        }

        /// <summary>In một chuỗi có thể bắt đầu bằng lệnh (\alpha) hoặc ký tự thường.</summary>
        private void RawToken(string s)
        {
            if (s.StartsWith("\\", StringComparison.Ordinal) && s.Length > 1 && char.IsLetter(s[1]))
                Cmd(s.Substring(1));
            else
                Raw(s);
        }

        private static string InnerText(MathNode n)
        {
            string text = n switch
            {
                Identifier id => id.Text,
                Number num => num.Text.Replace(",", "{,}"),
                _ => "",
            };
            if (n is Identifier && LatexSymbols.TryGetCommandForChar(text, SymbolKind.Identifier, AtomClass.Ord, out var name))
                return "\\" + name;
            return text;
        }

        private static bool EndsWithControlWord(StringBuilder sb)
        {
            int i = sb.Length - 1;
            if (i < 0 || !char.IsLetter(sb[i])) return false;
            while (i >= 0 && char.IsLetter(sb[i])) i--;
            return i >= 0 && sb[i] == '\\';
        }

        private static string? WrapperOf(MathNode n)
        {
            MathVariant variant;
            string text;
            switch (n)
            {
                case Identifier id:
                    variant = id.Variant;
                    text = id.Text;
                    break;
                case Number num:
                    variant = num.Variant;
                    text = num.Text;
                    break;
                default:
                    return null;
            }

            var natural = LatexSymbols.TryGetChar(text, out var info) ? info.Variant : MathVariant.Default;
            if (variant == natural || variant == MathVariant.Default) return null;
            bool upright = !MathAlphabets.IsItalicByDefault(text);
            return variant switch
            {
                MathVariant.Normal => upright ? null : "mathrm",
                MathVariant.Italic => "mathit",
                MathVariant.Bold => "mathbf",
                MathVariant.BoldItalic => "boldsymbol",
                MathVariant.DoubleStruck => "mathbb",
                MathVariant.Script => "mathscr",
                MathVariant.Calligraphic => "mathcal",
                MathVariant.Fraktur => "mathfrak",
                MathVariant.BoldScript => "boldsymbol",
                MathVariant.BoldFraktur => "boldsymbol",
                MathVariant.SansSerif => "mathsf",
                MathVariant.SansSerifBold => "mathsf",
                MathVariant.SansSerifItalic => "mathsfit",
                MathVariant.SansSerifBoldItalic => "mathsfit",
                MathVariant.Monospace => "mathtt",
                _ => null,
            };
        }

        // ── Toán tử ──────────────────────────────────────────────────────

        private void PrintOperator(Operator op)
        {
            if (op.Size != DelimiterSize.Normal)
            {
                string size = op.Size switch
                {
                    DelimiterSize.Big => "big",
                    DelimiterSize.Big2 => "Big",
                    DelimiterSize.Big3 => "bigg",
                    _ => "Bigg",
                };
                string suffix = op.Class switch
                {
                    AtomClass.Open => "l",
                    AtomClass.Close => "r",
                    AtomClass.Rel => "m",
                    _ => "",
                };
                Cmd(size + suffix);
                PrintDelimiter(op.Text);
                return;
            }

            switch (op.Text)
            {
                case "−":
                    Raw("-");
                    return;
                case "∗":
                    Raw("*");
                    return;
                case "{":
                    Raw("\\{");
                    return;
                case "}":
                    Raw("\\}");
                    return;
                case "‖":
                    Raw("\\|");
                    return;
                case "′":
                    Cmd("prime");
                    return;
                case "$" or "%" or "&" or "#" or "_":
                    Raw("\\" + op.Text);
                    return;
            }

            if (LatexSymbols.TryGetCommandForChar(op.Text, SymbolKind.Operator, op.Class, out var name)
                || LatexSymbols.TryGetCommandForChar(op.Text, SymbolKind.Operator, AtomClass.Rel, out name)
                || LatexSymbols.TryGetCommandForChar(op.Text, SymbolKind.Operator, AtomClass.Bin, out name)
                || LatexSymbols.TryGetCommandForChar(op.Text, SymbolKind.Operator, AtomClass.Inner, out name)
                || LatexSymbols.TryGetCommandForChar(op.Text, SymbolKind.Operator, AtomClass.Punct, out name))
            {
                Cmd(name);
                return;
            }

            if (NegationBase.TryGetValue(op.Text, out var baseChar))
            {
                Cmd("not");
                PrintOperator(new Operator(baseChar, AtomClass.Rel));
                return;
            }

            if (LatexSymbols.TryGetCommandForChar(op.Text, SymbolKind.Identifier, AtomClass.Ord, out var idName))
            {
                Cmd(idName);
                return;
            }

            Raw(op.Text);
        }

        private void PrintDelimiter(string d)
        {
            switch (d)
            {
                case "":
                    Raw(".");
                    return;
                case "{":
                    Raw("\\{");
                    return;
                case "}":
                    Raw("\\}");
                    return;
                case "‖":
                    Raw("\\|");
                    return;
                case "(" or ")" or "[" or "]" or "|" or "/":
                    Raw(d);
                    return;
            }
            if (DelimiterNames.TryGetValue(d, out var name)) Cmd(name);
            else Raw(d);
        }

        private void PrintSpace(SpaceKind kind)
        {
            switch (kind)
            {
                case SpaceKind.Thin:
                    Raw("\\,");
                    break;
                case SpaceKind.Medium:
                    Raw("\\:");
                    break;
                case SpaceKind.Thick:
                    Raw("\\;");
                    break;
                case SpaceKind.NegativeThin:
                    Raw("\\!");
                    break;
                case SpaceKind.Quad:
                    Cmd("quad");
                    break;
                case SpaceKind.QQuad:
                    Cmd("qquad");
                    break;
                case SpaceKind.Interword:
                    Raw("\\ ");
                    break;
                case SpaceKind.NonBreaking:
                    Raw("~");
                    break;
                case SpaceKind.En:
                    Cmd("enspace");
                    break;
            }
        }

        // ── Cấu trúc ─────────────────────────────────────────────────────

        private void PrintFraction(Fraction f)
        {
            switch (f.Kind)
            {
                case FractionKind.NoBar:
                    Raw("{");
                    Node(f.Numerator);
                    Cmd("atop");
                    Node(f.Denominator);
                    Raw("}");
                    return;
                default:
                    Cmd(f.Style switch
                    {
                        MathStyleOverride.Display => "dfrac",
                        MathStyleOverride.Text => "tfrac",
                        _ => "frac",
                    });
                    Braced(f.Numerator);
                    Braced(f.Denominator);
                    return;
            }
        }

        private static bool IsPrime(MathNode? n) => n is Operator { Text: "′" or "″" or "‴" or "⁗" };

        private void PrintScripts(Scripts s)
        {
            switch (s.Base)
            {
                case Row { Children.Count: 0 }:
                    Raw("{}");
                    break;
                case Row r:
                    Braced(r);
                    break;
                default:
                    Node(s.Base);
                    break;
            }

            MathNode? sup = s.Sup;
            if (IsPrime(sup))
            {
                Raw(PrimeMarks((Operator)sup!));
                sup = null;
            }
            else if (sup is Row { Children.Count: >= 2 } sr && IsPrime(sr.Children[0]))
            {
                Raw(PrimeMarks((Operator)sr.Children[0]));
                sup = sr.Children.Count == 2 ? sr.Children[1] : new Row(sr.Children.Skip(1).ToArray());
            }

            if (s.Sub is not null)
            {
                Raw("_");
                Braced(s.Sub);
            }
            if (sup is not null)
            {
                Raw("^");
                Braced(sup);
            }
        }

        private static string PrimeMarks(Operator op) => op.Text switch
        {
            "″" => "''",
            "‴" => "'''",
            "⁗" => "''''",
            _ => "'",
        };

        private void PrintLimitScripts(MathNode? lower, MathNode? upper, LimitPlacement limits)
        {
            if (limits == LimitPlacement.Limits) Cmd("limits");
            else if (limits == LimitPlacement.NoLimits) Cmd("nolimits");
            if (lower is not null)
            {
                Raw("_");
                Braced(lower);
            }
            if (upper is not null)
            {
                Raw("^");
                Braced(upper);
            }
        }

        private void PrintLargeOperator(LargeOperator op)
        {
            if (LatexSymbols.TryGetCommandForChar(op.Symbol, SymbolKind.LargeOperator, AtomClass.Op, out var name)) Cmd(name);
            else Raw(op.Symbol);
            PrintLimitScripts(op.Lower, op.Upper, op.Limits);
            if (op.Operand is not null) Node(op.Operand);
        }

        private void PrintFunction(FunctionApply fn)
        {
            if (fn.IsBuiltin)
            {
                Cmd(fn.Name);
            }
            else
            {
                Cmd("operatorname");
                if (fn.LimitsByDefault) Raw("*");
                Raw("{" + EscapeText(fn.Name) + "}");
            }
            PrintLimitScripts(fn.Lower, fn.Upper, fn.Limits);
            if (fn.Argument is not null) Node(fn.Argument);
        }

        private void PrintFenced(Fenced fe)
        {
            if (fe.Open == "(" && fe.Close == ")" && fe.Parts.Count == 1
                && AstWalker.Unwrap(fe.Parts[0]) is Fraction { Kind: FractionKind.NoBar } binom)
            {
                Cmd(binom.Style switch
                {
                    MathStyleOverride.Display => "dbinom",
                    MathStyleOverride.Text => "tbinom",
                    _ => "binom",
                });
                Braced(binom.Numerator);
                Braced(binom.Denominator);
                return;
            }

            Cmd("left");
            PrintDelimiter(fe.Open);
            for (int i = 0; i < fe.Parts.Count; i++)
            {
                if (i > 0)
                {
                    Cmd("middle");
                    PrintDelimiter(i - 1 < fe.Separators.Count ? fe.Separators[i - 1] : "|");
                }
                Node(fe.Parts[i]);
            }
            Cmd("right");
            PrintDelimiter(fe.Close);
        }

        private void PrintGroupChar(GroupChar gc)
        {
            var arrow = ParserCore.ExtensibleArrows.FirstOrDefault(kv => kv.Value == gc.Char).Key;
            if (arrow is not null && gc.Position == VerticalPosition.Bottom)
            {
                Cmd(arrow);
                if (gc.Label is not null)
                {
                    Raw("[");
                    Node(gc.Label);
                    Raw("]");
                }
                Braced(gc.Base);
                return;
            }

            string name = (gc.Char, gc.Position) switch
            {
                ("⏟", _) => "underbrace",
                ("⎴", _) => "overbracket",
                ("⎵", _) => "underbracket",
                ("⏜", _) => "overparen",
                ("⏝", _) => "underparen",
                _ => "overbrace",
            };
            Cmd(name);
            Braced(gc.Base);
            if (gc.Label is not null)
            {
                Raw(gc.Position == VerticalPosition.Top ? "^" : "_");
                Braced(gc.Label);
            }
        }

        private void PrintUnderOver(UnderOver uo)
        {
            if (uo.Over is not null && uo.Under is not null)
            {
                Cmd("overset");
                Braced(uo.Over);
                Raw("{");
                Cmd("underset");
                Braced(uo.Under);
                Braced(uo.Base);
                Raw("}");
            }
            else if (uo.Over is not null)
            {
                Cmd("overset");
                Braced(uo.Over);
                Braced(uo.Base);
            }
            else
            {
                Cmd("underset");
                Braced(uo.Under);
                Braced(uo.Base);
            }
        }

        private void PrintTable(Table t)
        {
            string env = t.Kind switch
            {
                TableKind.Matrix => "matrix",
                TableKind.PMatrix => "pmatrix",
                TableKind.BMatrix => "bmatrix",
                TableKind.BBMatrix => "Bmatrix",
                TableKind.VMatrix => "vmatrix",
                TableKind.VVMatrix => "Vmatrix",
                TableKind.SmallMatrix => "smallmatrix",
                TableKind.Array => "array",
                TableKind.Cases => "cases",
                _ => "rcases",
            };
            Cmd("begin");
            Raw("{" + env + "}");
            if (t.Kind == TableKind.Array)
                Raw("{" + string.Concat(t.Columns.Select(c => c switch { ColumnAlign.Left => "l", ColumnAlign.Right => "r", _ => "c" })) + "}");
            PrintRows(t.Rows.Select(r => (r.Cells, (string?)null, (string?)null, false)).ToArray());
            Cmd("end");
            Raw("{" + env + "}");
        }

        private void PrintAlignment(Alignment al)
        {
            string env = al.Kind switch
            {
                AlignmentKind.Aligned => "aligned",
                AlignmentKind.Align => "align",
                AlignmentKind.AlignStar => "align*",
                AlignmentKind.Gather => "gather",
                AlignmentKind.GatherStar => "gather*",
                AlignmentKind.Gathered => "gathered",
                AlignmentKind.Split => "split",
                AlignmentKind.Equation => "equation",
                AlignmentKind.EquationStar => "equation*",
                _ => "multline",
            };
            Cmd("begin");
            Raw("{" + env + "}");
            PrintRows(al.Rows.Select(r => (r.Cells, r.Tag, r.Label, r.NoNumber)).ToArray());
            Cmd("end");
            Raw("{" + env + "}");
        }

        private void PrintRows(IReadOnlyList<(IReadOnlyList<MathNode> Cells, string? Tag, string? Label, bool NoNumber)> rows)
        {
            for (int r = 0; r < rows.Count; r++)
            {
                if (r > 0) Raw("\\\\");
                var row = rows[r];
                for (int c = 0; c < row.Cells.Count; c++)
                {
                    if (c > 0) Raw("&");
                    Node(row.Cells[c]);
                }
                if (row.Tag is not null)
                {
                    Cmd("tag");
                    Raw("{" + EscapeText(row.Tag) + "}");
                }
                if (row.Label is not null)
                {
                    Cmd("label");
                    Raw("{" + row.Label + "}");
                }
                if (row.NoNumber) Cmd("nonumber");
            }
        }

        private static string EscapeText(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\\':
                        sb.Append("\\textbackslash{}");
                        break;
                    case '{' or '}' or '$' or '%' or '&' or '#' or '_':
                        sb.Append('\\').Append(c);
                        break;
                    case '^':
                        sb.Append("\\^{}");
                        break;
                    case '~':
                        sb.Append("\\~{}");
                        break;
                    case ' ':
                        sb.Append('~');
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
