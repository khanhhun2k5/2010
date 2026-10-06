using System.Xml.Linq;
using MathTypeX.Ast;

namespace MathTypeX.Render.MathMl;

public sealed record MathMlOptions
{
    public bool Display { get; init; }

    /// <summary>∫ ∑ ∏ kéo dãn theo phần thân (stretchy) thay vì hai cỡ kiểu TeX.</summary>
    public bool GrowLargeOperators { get; init; }

    /// <summary>Hiện ô trống □ (placeholder) bằng khung chấm.</summary>
    public bool ShowPlaceholders { get; init; } = true;
}

/// <summary>
/// AST → MathML Core (Chromium ≥ 109 dựng bằng bảng OpenType MATH của font đã cài) — dùng cho preview và clipboard.
/// MathML Core bỏ gần hết <c>mathvariant</c>, nên kiểu chữ toán được ánh xạ sang ký tự Unicode như ở OMML.
/// Lớp CSS: mtx-int (∫, để gán font riêng cho slot tích phân), mtx-ph (placeholder), mtx-err (lỗi).
/// </summary>
public static class MathMlWriter
{
    public static readonly XNamespace Ns = "http://www.w3.org/1998/Math/MathML";

    public static XElement Write(MathNode body, MathMlOptions? options = null)
    {
        var o = options ?? new MathMlOptions();
        var w = new Writer(o);
        var math = new XElement(Ns + "math", new XAttribute("display", o.Display ? "block" : "inline"), w.Row(body));
        return math;
    }

    public static string WriteString(MathNode body, MathMlOptions? options = null) =>
        Write(body, options).ToString(SaveOptions.DisableFormatting);

    private static readonly Dictionary<string, string> SpacingAccents = new()
    {
        ["̂"] = "^", ["̌"] = "ˇ", ["̃"] = "~", ["́"] = "´", ["̀"] = "`",
        ["̇"] = "˙", ["̈"] = "¨", ["⃛"] = "⋯", ["̆"] = "˘", ["̄"] = "¯",
        ["⃗"] = "→", ["⃖"] = "←", ["⃡"] = "↔", ["̊"] = "˚",
    };

    private sealed class Writer
    {
        private readonly MathMlOptions _o;

        public Writer(MathMlOptions o) => _o = o;

        private static XElement E(string name, params object?[] content) => new(Ns + name, content);

        /// <summary>Ô đối số: nếu trống (vừa chèn \frac{}{}) thì hiện □ để người dùng thấy chỗ cần điền.</summary>
        private XElement Arg(MathNode? node) =>
            _o.ShowPlaceholders && AstWalker.IsEmpty(node)
                ? E("mi", new XAttribute("class", "mtx-ph"), new XAttribute("mathvariant", "normal"), "□")
                : Row(node);

        /// <summary>Luôn trả về đúng một phần tử (mrow nếu cần) — các phần tử MathML có số con cố định.</summary>
        public XElement Row(MathNode? node)
        {
            if (node is null) return E("mrow");
            var items = Items(node).ToList();
            return items.Count == 1 ? items[0] : E("mrow", items);
        }

        private IEnumerable<XElement> Items(MathNode node)
        {
            switch (node)
            {
                case Row r:
                    foreach (var c in r.Children)
                        foreach (var x in Items(c)) yield return x;
                    break;
                case Group g:
                    yield return Row(g.Content);
                    break;
                default:
                    yield return Node(node);
                    break;
            }
        }

        private XElement Node(MathNode node) => node switch
        {
            Identifier id => IdentifierElement(id),
            Number n => E("mn", MapVariant(n.Text, n.Variant)),
            Operator op => OperatorElement(op),
            TextRun t => E("mtext", t.Style switch
            {
                TextStyle.Bold => new XAttribute("style", "font-weight:bold"),
                TextStyle.Italic => new XAttribute("style", "font-style:italic"),
                _ => null,
            }, KeepEdgeSpaces(t.Text)),
            Space s => E("mspace", new XAttribute("width", SpaceWidth(s.Kind))),
            StyleSwitch => E("mrow"),
            Fraction f => E("mfrac",
                f.Kind == FractionKind.NoBar ? new XAttribute("linethickness", "0") : null,
                f.Style == MathStyleOverride.Display ? new XAttribute("displaystyle", "true") : f.Style == MathStyleOverride.Text ? new XAttribute("displaystyle", "false") : null,
                Arg(f.Numerator), Arg(f.Denominator)),
            Radical r => r.Index is null ? E("msqrt", Arg(r.Radicand)) : E("mroot", Arg(r.Radicand), Arg(r.Index)),
            Scripts s => ScriptsElement(Row(s.Base), s.Sub, s.Sup, under: false),
            LargeOperator op => LargeOperatorElement(op),
            FunctionApply fn => FunctionElement(fn),
            Fenced fe => FencedElement(fe),
            Accent a => E("mover", new XAttribute("accent", "true"), Arg(a.Base),
                E("mo", a.Stretchy ? new XAttribute("stretchy", "true") : new XAttribute("stretchy", "false"),
                    SpacingAccents.TryGetValue(a.AccentChar, out var spacing) ? spacing : a.AccentChar)),
            Bar b => b.Position == VerticalPosition.Top
                ? E("mover", new XAttribute("accent", "true"), Row(b.Base), E("mo", new XAttribute("stretchy", "true"), "‾"))
                : E("munder", new XAttribute("accentunder", "true"), Row(b.Base), E("mo", new XAttribute("stretchy", "true"), "_")),
            GroupChar gc => GroupCharElement(gc),
            UnderOver uo => uo.Under is not null && uo.Over is not null
                ? E("munderover", Row(uo.Base), Row(uo.Under), Row(uo.Over))
                : uo.Over is not null ? E("mover", Row(uo.Base), Row(uo.Over)) : E("munder", Row(uo.Base), Row(uo.Under)),
            Table t => TableElement(t),
            Alignment al => AlignmentElement(al),
            Boxed bx => E("mrow", new XAttribute("style", bx.Kind switch
            {
                BoxKind.Boxed => "border:1px solid currentColor;padding:0.15em 0.25em",
                BoxKind.Cancel => "background:linear-gradient(to top right,transparent calc(50% - 0.6px),currentColor,transparent calc(50% + 0.6px))",
                BoxKind.BCancel => "background:linear-gradient(to bottom right,transparent calc(50% - 0.6px),currentColor,transparent calc(50% + 0.6px))",
                _ => "background:linear-gradient(to top right,transparent calc(50% - 0.6px),currentColor,transparent calc(50% + 0.6px)),linear-gradient(to bottom right,transparent calc(50% - 0.6px),currentColor,transparent calc(50% + 0.6px))",
            }), Row(bx.Content)),
            Phantom ph => ph.Kind switch
            {
                PhantomKind.Vertical => E("mpadded", new XAttribute("width", "0"), E("mphantom", Row(ph.Content))),
                PhantomKind.Horizontal => E("mpadded", new XAttribute("height", "0"), new XAttribute("depth", "0"), E("mphantom", Row(ph.Content))),
                _ => E("mphantom", Row(ph.Content)),
            },
            Placeholder => _o.ShowPlaceholders ? E("mi", new XAttribute("class", "mtx-ph"), new XAttribute("mathvariant", "normal"), "□") : E("mrow"),
            ErrorNode err => E("mtext", new XAttribute("class", "mtx-err"), err.Raw),
            UnknownCommand u => E("mrow", E("mtext", new XAttribute("class", "mtx-err"), "\\" + u.Name), u.Arguments.Select(Row)),
            _ => E("mrow"),
        };

        // ── Atom ─────────────────────────────────────────────────────────

        private static XElement IdentifierElement(Identifier id)
        {
            string text = id.Text;
            switch (id.Variant)
            {
                case MathVariant.Default:
                    // MathML Core tự nghiêng mi một ký tự; chữ Hy Lạp hoa giữ đứng như TeX.
                    return text.Length == 1 && MathAlphabets.IsGreekUpper(text[0])
                        ? new XElement(Ns + "mi", new XAttribute("mathvariant", "normal"), text)
                        : new XElement(Ns + "mi", text);
                case MathVariant.Normal:
                    return new XElement(Ns + "mi", new XAttribute("mathvariant", "normal"), text);
                case MathVariant.Bold when !(text.Length == 1 && MathAlphabets.IsLatin(text[0])):
                    return new XElement(Ns + "mi", new XAttribute("mathvariant", "normal"), new XAttribute("style", "font-weight:bold"), text);
                case MathVariant.BoldItalic when !(text.Length == 1 && MathAlphabets.IsLatin(text[0])):
                    return new XElement(Ns + "mi", new XAttribute("style", "font-weight:bold"), text);
                default:
                    string mapped = MapVariant(text, id.Variant);
                    return mapped == text
                        ? new XElement(Ns + "mi", text)
                        : new XElement(Ns + "mi", new XAttribute("mathvariant", "normal"), mapped);
            }
        }

        private static string MapVariant(string text, MathVariant variant) =>
            variant is MathVariant.Default or MathVariant.Normal ? text : MathAlphabets.Map(text, variant);

        private static XElement OperatorElement(Operator op)
        {
            var mo = new XElement(Ns + "mo", op.Text);
            switch (op.Class)
            {
                case AtomClass.Open:
                case AtomClass.Close:
                    mo.Add(new XAttribute("stretchy", "false"));
                    break;
                case AtomClass.Ord:
                    // |x|, dấu trừ một ngôi: không có khoảng cách toán tử.
                    mo.Add(new XAttribute("lspace", "0"), new XAttribute("rspace", "0"));
                    break;
            }
            if (op.Size != DelimiterSize.Normal)
            {
                string size = op.Size switch
                {
                    DelimiterSize.Big => "1.2em",
                    DelimiterSize.Big2 => "1.8em",
                    DelimiterSize.Big3 => "2.4em",
                    _ => "3em",
                };
                mo.SetAttributeValue("stretchy", "true");
                mo.SetAttributeValue("symmetric", "true");
                mo.Add(new XAttribute("minsize", size), new XAttribute("maxsize", size));
            }
            return mo;
        }

        private static string SpaceWidth(SpaceKind kind) => kind switch
        {
            SpaceKind.Thin => "0.1667em",
            SpaceKind.Medium => "0.2222em",
            SpaceKind.Thick => "0.2778em",
            SpaceKind.NegativeThin => "-0.1667em",
            SpaceKind.Quad => "1em",
            SpaceKind.QQuad => "2em",
            SpaceKind.Interword => "0.3333em",
            SpaceKind.NonBreaking => "0.3333em",
            SpaceKind.En => "0.5em",
            _ => "0",
        };

        // ── Cấu trúc ─────────────────────────────────────────────────────

        private XElement ScriptsElement(XElement baseElement, MathNode? sub, MathNode? sup, bool under)
        {
            if (sub is not null && sup is not null)
                return E(under ? "munderover" : "msubsup", baseElement, Arg(sub), Arg(sup));
            if (sub is not null)
                return E(under ? "munder" : "msub", baseElement, Arg(sub));
            if (sup is not null)
                return E(under ? "mover" : "msup", baseElement, Arg(sup));
            return baseElement;
        }

        private XElement LargeOperatorElement(LargeOperator op)
        {
            bool integral = op.Kind.IsIntegral();
            bool under = op.Limits switch
            {
                LimitPlacement.Limits => true,
                LimitPlacement.NoLimits => false,
                _ => !integral && _o.Display,
            };
            bool grow = op.Sizing == NarySizing.Grow || (op.Sizing == NarySizing.Default && _o.GrowLargeOperators);
            var mo = E("mo",
                new XAttribute("largeop", "true"),
                new XAttribute("movablelimits", "false"),
                integral ? new XAttribute("class", "mtx-int") : null,
                grow ? new XAttribute("stretchy", "true") : null,
                grow ? new XAttribute("symmetric", "true") : null,
                op.Symbol);
            var head = ScriptsElement(mo, op.Lower, op.Upper, under);
            if (op.Operand is null) return head;
            return E("mrow", head, Items(op.Operand));
        }

        private XElement FunctionElement(FunctionApply fn)
        {
            var name = new XElement(Ns + "mi", fn.Name.Length == 1 ? new XAttribute("mathvariant", "normal") : null, fn.Name);
            bool under = fn.Limits switch
            {
                LimitPlacement.Limits => true,
                LimitPlacement.NoLimits => false,
                _ => fn.LimitsByDefault && _o.Display,
            };
            var head = ScriptsElement(name, fn.Lower, fn.Upper, under);
            if (fn.Argument is null) return head;
            // TeX: Op–Ord có khoảng hẹp (\sin x), Op–Open thì không (\sin(x)). Chromium không tự thêm.
            bool opensWithFence = StartsWithFence(fn.Argument);
            return E("mrow", head, E("mo", "⁡"),
                opensWithFence ? null : E("mspace", new XAttribute("width", "0.1667em")),
                Row(fn.Argument));
        }

        private static bool StartsWithFence(MathNode node) => node switch
        {
            Fenced => true,
            Operator { Class: AtomClass.Open } => true,
            Operator { Text: "|" or "‖" } => true,
            Row { Children.Count: > 0 } r => StartsWithFence(r.Children[0]),
            Scripts s => StartsWithFence(s.Base),
            _ => false,
        };

        /// <summary>MathML cắt khoảng trắng đầu/cuối token; dùng NBSP để giữ "nếu " trong \text{nếu }.</summary>
        private static string KeepEdgeSpaces(string text)
        {
            int start = 0, end = text.Length;
            while (start < end && text[start] == ' ') start++;
            while (end > start && text[end - 1] == ' ') end--;
            return new string(' ', start) + text.Substring(start, end - start) + new string(' ', text.Length - end);
        }

        private XElement FencedElement(Fenced fe)
        {
            var row = E("mrow");
            if (fe.Open.Length > 0) row.Add(E("mo", new XAttribute("fence", "true"), new XAttribute("stretchy", "true"), new XAttribute("symmetric", "true"), fe.Open));
            for (int i = 0; i < fe.Parts.Count; i++)
            {
                if (i > 0)
                {
                    string sep = i - 1 < fe.Separators.Count ? fe.Separators[i - 1] : "|";
                    row.Add(E("mo", new XAttribute("stretchy", "true"), new XAttribute("symmetric", "true"), sep));
                }
                row.Add(Items(fe.Parts[i]));
            }
            if (fe.Close.Length > 0) row.Add(E("mo", new XAttribute("fence", "true"), new XAttribute("stretchy", "true"), new XAttribute("symmetric", "true"), fe.Close));
            return row;
        }

        private XElement GroupCharElement(GroupChar gc)
        {
            var symbol = E("mo", new XAttribute("stretchy", "true"), gc.Char);
            bool arrow = gc.Position == VerticalPosition.Bottom && "→←⇒⇐↔⇔↦↪".Contains(gc.Char);
            if (arrow)
            {
                // \xrightarrow[dưới]{trên}: mũi tên co giãn ở giữa, chữ ở trên/dưới.
                return gc.Label is null ? E("mover", symbol, Row(gc.Base)) : E("munderover", symbol, Row(gc.Label), Row(gc.Base));
            }
            var withChar = gc.Position == VerticalPosition.Top ? E("mover", Row(gc.Base), symbol) : E("munder", Row(gc.Base), symbol);
            if (gc.Label is null) return withChar;
            return gc.Position == VerticalPosition.Top ? E("mover", withChar, Row(gc.Label)) : E("munder", withChar, Row(gc.Label));
        }

        private XElement Mtable(IReadOnlyList<IReadOnlyList<MathNode>> rows, Func<int, ColumnAlign> align, string? extraStyle = null)
        {
            var table = E("mtable", extraStyle is null ? null : new XAttribute("style", extraStyle));
            foreach (var row in rows)
            {
                var tr = E("mtr");
                for (int c = 0; c < row.Count; c++)
                {
                    string textAlign = align(c) switch { ColumnAlign.Left => "left", ColumnAlign.Right => "right", _ => "center" };
                    tr.Add(E("mtd", new XAttribute("style", "text-align:" + textAlign), Row(row[c])));
                }
                table.Add(tr);
            }
            return table;
        }

        private XElement TableElement(Table t)
        {
            var rows = t.Rows.Select(r => r.Cells).ToArray();
            var table = Mtable(rows, c => c < t.Columns.Count ? t.Columns[c] : ColumnAlign.Center);
            (string open, string close) = t.Kind switch
            {
                TableKind.PMatrix => ("(", ")"),
                TableKind.BMatrix => ("[", "]"),
                TableKind.BBMatrix => ("{", "}"),
                TableKind.VMatrix => ("|", "|"),
                TableKind.VVMatrix => ("‖", "‖"),
                TableKind.Cases => ("{", ""),
                TableKind.RCases => ("", "}"),
                _ => ("", ""),
            };
            return open.Length == 0 && close.Length == 0 ? table : WrapFence(open, close, table);
        }

        private static XElement WrapFence(string open, string close, XElement inner)
        {
            var row = new XElement(Ns + "mrow");
            if (open.Length > 0) row.Add(new XElement(Ns + "mo", new XAttribute("fence", "true"), new XAttribute("stretchy", "true"), new XAttribute("symmetric", "true"), open));
            row.Add(inner);
            if (close.Length > 0) row.Add(new XElement(Ns + "mo", new XAttribute("fence", "true"), new XAttribute("stretchy", "true"), new XAttribute("symmetric", "true"), close));
            return row;
        }

        private XElement AlignmentElement(Alignment al)
        {
            if (al.Rows.Count == 1 && al.Rows[0].Cells.Count == 1) return Row(al.Rows[0].Cells[0]);
            var rows = al.Rows.Select(r => r.Cells).ToArray();
            bool pairs = al.Rows.Any(r => r.Cells.Count > 1);
            return Mtable(rows, c => pairs ? (c % 2 == 0 ? ColumnAlign.Right : ColumnAlign.Left) : ColumnAlign.Center,
                pairs ? "border-spacing:0" : null);
        }
    }
}
