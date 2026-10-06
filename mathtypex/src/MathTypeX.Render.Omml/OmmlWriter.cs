using System.Xml.Linq;
using MathTypeX.Ast;

namespace MathTypeX.Render.Omml;

/// <summary>
/// AST → OMML (Office Math Markup Language) theo từng node (docs/03 §6.3) — không dùng thay chuỗi.
/// ∫ ∑ ∏ luôn thành m:nary (bất biến D9); phân số m:f; căn m:rad; ngoặc co giãn m:d; ma trận m:m.
/// </summary>
public static class OmmlWriter
{
    public static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";
    public static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static OmmlResult Write(MathNode body, OmmlOptions? options = null)
    {
        var emitter = new Emitter(options ?? OmmlOptions.Default);
        var content = new List<XElement>();
        emitter.Emit(body, content);
        var oMath = new XElement(M + "oMath", content);
        XElement root = emitter.Options.Display
            ? new XElement(M + "oMathPara",
                new XElement(M + "oMathParaPr", new XElement(M + "jc", Val("centerGroup"))),
                oMath)
            : oMath;
        root.Add(new XAttribute(XNamespace.Xmlns + "m", M.NamespaceName), new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName));
        return new OmmlResult(root, emitter.Approximations.Distinct().ToArray());
    }

    public static OmmlResult Write(MathDocument document, OmmlOptions? options = null) => Write(document.Body, options);

    private static XAttribute Val(string value) => new(M + "val", value);

    /// <summary>Thuộc tính của một m:r — hai run cạnh nhau có cùng thuộc tính được gộp làm một.</summary>
    private sealed record RunProps(string Font, string? Sty, bool Nor, bool Bold, bool Italic, string? Color);

    private sealed class Emitter
    {
        public Emitter(OmmlOptions options) => Options = options;

        public OmmlOptions Options { get; }

        public List<string> Approximations { get; } = new();

        private RunProps MathRun(string? sty = null) => new(Options.MathFont, sty, false, false, false, null);

        // ── Run ──────────────────────────────────────────────────────────

        private XElement WordRPr(string font, bool bold = false, bool italic = false, string? color = null)
        {
            var rPr = new XElement(W + "rPr",
                new XElement(W + "rFonts",
                    new XAttribute(W + "ascii", font),
                    new XAttribute(W + "hAnsi", font),
                    new XAttribute(W + "cs", font)));
            if (bold) rPr.Add(new XElement(W + "b"));
            if (italic) rPr.Add(new XElement(W + "i"));
            if (color is not null) rPr.Add(new XElement(W + "color", new XAttribute(W + "val", color)));
            if (Options.FontSizePt is double pt)
            {
                string halfPoints = Math.Round(pt * 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
                rPr.Add(new XElement(W + "sz", new XAttribute(W + "val", halfPoints)));
                rPr.Add(new XElement(W + "szCs", new XAttribute(W + "val", halfPoints)));
            }
            return rPr;
        }

        private XElement CtrlPr(string? font = null) => new(M + "ctrlPr", WordRPr(font ?? Options.MathFont));

        private void AppendRun(List<XElement> output, RunProps props, string text)
        {
            if (text.Length == 0) return;
            if (output.Count > 0 && output[output.Count - 1] is { } last && last.Annotation<RunProps>() is { } lastProps && lastProps == props)
            {
                var t = last.Element(M + "t")!;
                t.Value += text;
                return;
            }

            var run = new XElement(M + "r");
            if (props.Nor)
                run.Add(new XElement(M + "rPr", new XElement(M + "nor")));
            else if (props.Sty is not null)
                run.Add(new XElement(M + "rPr", new XElement(M + "sty", Val(props.Sty))));
            run.Add(WordRPr(props.Font, props.Bold, props.Italic, props.Color));
            run.Add(new XElement(M + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text));
            run.AddAnnotation(props);
            output.Add(run);
        }

        private XElement Container(string name, MathNode? node)
        {
            var list = new List<XElement>();
            if (node is not null) Emit(node, list);
            return new XElement(M + name, list);
        }

        private XElement E(MathNode? node) => Container("e", node);

        // ── Duyệt node ───────────────────────────────────────────────────

        public void Emit(MathNode node, List<XElement> output)
        {
            switch (node)
            {
                case Row r:
                    foreach (var c in r.Children) Emit(c, output);
                    break;
                case Group g:
                    Emit(g.Content, output);
                    break;
                case Identifier id:
                    EmitIdentifier(id, output);
                    break;
                case Number n:
                    EmitNumber(n, output);
                    break;
                case Operator op:
                    if (op.Size != DelimiterSize.Normal) Approximations.Add("\\big");
                    AppendRun(output, MathRun(), op.Text);
                    break;
                case TextRun t:
                    AppendRun(output, new RunProps(Options.TextFont, null, true, t.Style == TextStyle.Bold, t.Style == TextStyle.Italic, null), t.Text);
                    break;
                case Space s:
                    EmitSpace(s, output);
                    break;
                case StyleSwitch ss:
                    if (ss.Style == MathStyle.Display && !Options.Display) Approximations.Add("\\displaystyle");
                    break;
                case Fraction f:
                    output.Add(EmitFraction(f));
                    break;
                case Radical rad:
                    output.Add(new XElement(M + "rad",
                        new XElement(M + "radPr",
                            rad.Index is null ? new XElement(M + "degHide", Val("1")) : null,
                            CtrlPr()),
                        Container("deg", rad.Index),
                        E(rad.Radicand)));
                    break;
                case Scripts s:
                    output.Add(EmitScripts(E(s.Base), s.Sub, s.Sup));
                    break;
                case LargeOperator op:
                    output.Add(EmitNary(op));
                    break;
                case FunctionApply fn:
                    EmitFunction(fn, output);
                    break;
                case Fenced fe:
                    output.Add(EmitFenced(fe));
                    break;
                case Accent a:
                    output.Add(new XElement(M + "acc",
                        new XElement(M + "accPr", new XElement(M + "chr", Val(a.AccentChar)), CtrlPr()),
                        E(a.Base)));
                    break;
                case Bar b:
                    output.Add(new XElement(M + "bar",
                        new XElement(M + "barPr", new XElement(M + "pos", Val(b.Position == VerticalPosition.Top ? "top" : "bot")), CtrlPr()),
                        E(b.Base)));
                    break;
                case GroupChar gc:
                    output.Add(EmitGroupChar(gc));
                    break;
                case UnderOver uo:
                    output.Add(EmitUnderOver(uo));
                    break;
                case Table t:
                    output.Add(EmitTable(t));
                    break;
                case Alignment al:
                    EmitAlignment(al, output);
                    break;
                case Boxed bx:
                    output.Add(EmitBoxed(bx));
                    break;
                case Phantom ph:
                    output.Add(new XElement(M + "phant",
                        new XElement(M + "phantPr",
                            new XElement(M + "show", Val("0")),
                            ph.Kind == PhantomKind.Vertical ? new XElement(M + "zeroWid", Val("1")) : null,
                            ph.Kind == PhantomKind.Horizontal ? new XElement(M + "zeroAsc", Val("1")) : null,
                            ph.Kind == PhantomKind.Horizontal ? new XElement(M + "zeroDesc", Val("1")) : null,
                            CtrlPr()),
                        E(ph.Content)));
                    break;
                case Placeholder:
                    // m:e rỗng: Word tự hiện ô chấm khi soạn.
                    break;
                case ErrorNode err:
                    AppendRun(output, MathRun("p") with { Color = "C00000" }, err.Raw);
                    break;
                case UnknownCommand u:
                    AppendRun(output, MathRun("p") with { Color = "C00000" }, "\\" + u.Name);
                    foreach (var a in u.Arguments) Emit(a, output);
                    break;
            }
        }

        // ── Atom ─────────────────────────────────────────────────────────

        private void EmitIdentifier(Identifier id, List<XElement> output)
        {
            string text = id.Text;
            bool letter = text.Length == 1 && (MathAlphabets.IsLatin(text[0]) || MathAlphabets.IsGreekLower(text[0]) || MathAlphabets.IsGreekUpper(text[0]));
            string? sty = null;

            switch (id.Variant)
            {
                case MathVariant.Default:
                    if (text.Length == 1 && MathAlphabets.IsGreekUpper(text[0])) sty = "p";
                    if (id.Role == IdentifierRole.Differential && Options.UprightDifferential) sty = "p";
                    break;
                case MathVariant.Normal:
                    if (letter) sty = "p";
                    break;
                case MathVariant.Italic:
                    sty = "i";
                    break;
                case MathVariant.Bold:
                    sty = "b";
                    break;
                case MathVariant.BoldItalic:
                    sty = "bi";
                    break;
                default:
                    if (text.Length == 1 && MathAlphabets.TryMap(text[0], id.Variant, out var mapped))
                    {
                        text = mapped;
                        sty = "p";
                    }
                    else
                    {
                        Approximations.Add(id.Variant + " " + text);
                        sty = id.Variant is MathVariant.BoldScript or MathVariant.BoldFraktur or MathVariant.SansSerifBold ? "b" : "p";
                    }
                    break;
            }

            AppendRun(output, MathRun(sty), text);
        }

        private void EmitNumber(Number n, List<XElement> output)
        {
            string text = n.Text;
            string? sty = null;
            switch (n.Variant)
            {
                case MathVariant.Default or MathVariant.Normal:
                    break;
                case MathVariant.Bold or MathVariant.BoldItalic:
                    sty = "b";
                    break;
                case MathVariant.Italic:
                    sty = "i";
                    break;
                default:
                    text = MathAlphabets.Map(text, n.Variant);
                    sty = "p";
                    break;
            }
            AppendRun(output, MathRun(sty), text);
        }

        private void EmitSpace(Space s, List<XElement> output)
        {
            string text = s.Kind switch
            {
                SpaceKind.Thin => " ",
                SpaceKind.Medium => " ",
                SpaceKind.Thick => " ",
                SpaceKind.Quad => " ",
                SpaceKind.QQuad => "  ",
                SpaceKind.Interword => " ",
                SpaceKind.NonBreaking => " ",
                SpaceKind.En => " ",
                _ => "",
            };
            if (s.Kind == SpaceKind.NegativeThin) Approximations.Add("\\!");
            AppendRun(output, MathRun(), text);
        }

        // ── Cấu trúc ─────────────────────────────────────────────────────

        private XElement EmitFraction(Fraction f)
        {
            string? type = f.Kind switch
            {
                FractionKind.NoBar => "noBar",
                FractionKind.Linear => "lin",
                FractionKind.Skewed => "skw",
                _ => null,
            };
            if (f.Style == MathStyleOverride.Display && !Options.Display) Approximations.Add("\\dfrac");
            return new XElement(M + "f",
                new XElement(M + "fPr", type is null ? null : new XElement(M + "type", Val(type)), CtrlPr()),
                Container("num", f.Numerator),
                Container("den", f.Denominator));
        }

        private XElement EmitScripts(XElement baseE, MathNode? sub, MathNode? sup)
        {
            if (sub is not null && sup is not null)
                return new XElement(M + "sSubSup", new XElement(M + "sSubSupPr", CtrlPr()), baseE, Container("sub", sub), Container("sup", sup));
            if (sub is not null)
                return new XElement(M + "sSub", new XElement(M + "sSubPr", CtrlPr()), baseE, Container("sub", sub));
            return new XElement(M + "sSup", new XElement(M + "sSupPr", CtrlPr()), baseE, Container("sup", sup));
        }

        private XElement EmitNary(LargeOperator op)
        {
            bool integral = op.Kind.IsIntegral();
            bool under = op.Limits switch
            {
                LimitPlacement.Limits => true,
                LimitPlacement.NoLimits => false,
                // Quy ước TeX: tích phân để giới hạn bên cạnh; ∑ ∏ đặt trên/dưới khi display, bên cạnh khi inline.
                _ => !integral && Options.Display,
            };
            var sizing = op.Sizing == NarySizing.Default ? Options.NarySizing : op.Sizing;

            return new XElement(M + "nary",
                new XElement(M + "naryPr",
                    new XElement(M + "chr", Val(op.Symbol)),
                    new XElement(M + "limLoc", Val(under ? "undOvr" : "subSup")),
                    sizing == NarySizing.Grow ? new XElement(M + "grow", Val("1")) : null,
                    op.Lower is null ? new XElement(M + "subHide", Val("1")) : null,
                    op.Upper is null ? new XElement(M + "supHide", Val("1")) : null,
                    CtrlPr(integral ? Options.IntegralFont : null)),
                Container("sub", op.Lower),
                Container("sup", op.Upper),
                E(op.Operand));
        }

        private void EmitFunction(FunctionApply fn, List<XElement> output)
        {
            var nameRun = new List<XElement>();
            AppendRun(nameRun, MathRun("p"), fn.Name);
            List<XElement> name = nameRun;

            if (fn.Lower is not null || fn.Upper is not null)
            {
                // lim, max, min… luôn dùng m:limLow/m:limUpp (cấu trúc Word tự sinh cho "lim", kể cả inline);
                // sin², log₂… dùng chỉ số trong m:fName.
                bool under = fn.Limits switch
                {
                    LimitPlacement.Limits => true,
                    LimitPlacement.NoLimits => false,
                    _ => fn.LimitsByDefault,
                };
                XElement structure;
                if (under)
                {
                    XElement inner = new XElement(M + "e", name);
                    if (fn.Lower is not null)
                        inner = new XElement(M + "e", new XElement(M + "limLow", new XElement(M + "limLowPr", CtrlPr()), inner, Container("lim", fn.Lower)));
                    if (fn.Upper is not null)
                        inner = new XElement(M + "e", new XElement(M + "limUpp", new XElement(M + "limUppPr", CtrlPr()), inner, Container("lim", fn.Upper)));
                    structure = inner.Elements().Single();
                }
                else
                {
                    structure = EmitScripts(new XElement(M + "e", name), fn.Lower, fn.Upper);
                }
                name = new List<XElement> { structure };
            }

            if (fn.Argument is null)
            {
                output.AddRange(name);
                return;
            }

            output.Add(new XElement(M + "func",
                new XElement(M + "funcPr", CtrlPr()),
                new XElement(M + "fName", name),
                E(fn.Argument)));
        }

        private XElement EmitFenced(Fenced fe)
        {
            if (fe.Separators.Distinct().Count() > 1) Approximations.Add("\\middle");
            var dPr = new XElement(M + "dPr",
                new XElement(M + "begChr", Val(fe.Open)),
                fe.Separators.Count > 0 ? new XElement(M + "sepChr", Val(fe.Separators[0])) : null,
                new XElement(M + "endChr", Val(fe.Close)),
                CtrlPr());
            return new XElement(M + "d", dPr, fe.Parts.Select(p => E(p)));
        }

        private XElement EmitGroupChar(GroupChar gc)
        {
            bool arrow = gc.Position == VerticalPosition.Bottom && IsArrow(gc.Char);
            var groupChr = new XElement(M + "groupChr",
                new XElement(M + "groupChrPr",
                    new XElement(M + "chr", Val(gc.Char)),
                    new XElement(M + "pos", Val(gc.Position == VerticalPosition.Top ? "top" : "bot")),
                    new XElement(M + "vertJc", Val(gc.Position == VerticalPosition.Top || arrow ? "bot" : "top")),
                    CtrlPr()),
                E(gc.Base));
            if (gc.Label is null) return groupChr;
            bool labelAbove = gc.Position == VerticalPosition.Top;
            string kind = labelAbove ? "limUpp" : "limLow";
            return new XElement(M + kind,
                new XElement(M + kind + "Pr", CtrlPr()),
                new XElement(M + "e", groupChr),
                Container("lim", gc.Label));
        }

        private static bool IsArrow(string ch) => ch is "→" or "←" or "⇒" or "⇐" or "↔" or "⇔" or "↦" or "↪";

        private XElement EmitUnderOver(UnderOver uo)
        {
            XElement current = E(uo.Base);
            if (uo.Under is not null)
                current = new XElement(M + "e", new XElement(M + "limLow", new XElement(M + "limLowPr", CtrlPr()), current, Container("lim", uo.Under)));
            if (uo.Over is not null)
                current = new XElement(M + "e", new XElement(M + "limUpp", new XElement(M + "limUppPr", CtrlPr()), current, Container("lim", uo.Over)));
            return current.Elements().Single();
        }

        private XElement Matrix(IReadOnlyList<IReadOnlyList<MathNode>> rows, IReadOnlyList<ColumnAlign> columns, bool tightColumns)
        {
            int cols = Math.Max(1, rows.Count == 0 ? 0 : rows.Max(r => r.Count));
            var mcs = new XElement(M + "mcs");
            for (int c = 0; c < cols; c++)
            {
                var align = c < columns.Count ? columns[c] : columns.Count > 0 ? columns[columns.Count - 1] : ColumnAlign.Center;
                mcs.Add(new XElement(M + "mc",
                    new XElement(M + "mcPr",
                        new XElement(M + "count", Val("1")),
                        new XElement(M + "mcJc", Val(align switch { ColumnAlign.Left => "left", ColumnAlign.Right => "right", _ => "center" })))));
            }
            var mPr = new XElement(M + "mPr",
                new XElement(M + "plcHide", Val("1")),
                tightColumns ? new XElement(M + "cGpRule", Val("3")) : null,
                tightColumns ? new XElement(M + "cGp", Val("0")) : null,
                mcs,
                CtrlPr());
            var m = new XElement(M + "m", mPr);
            foreach (var row in rows)
            {
                var mr = new XElement(M + "mr");
                for (int c = 0; c < cols; c++)
                    mr.Add(E(c < row.Count ? row[c] : null));
                m.Add(mr);
            }
            return m;
        }

        private XElement EmitTable(Table t)
        {
            if (t.Kind == TableKind.SmallMatrix) Approximations.Add("smallmatrix");
            var rows = t.Rows.Select(r => r.Cells).ToArray();
            var m = Matrix(rows, t.Columns, tightColumns: false);
            (string? open, string? close) = t.Kind switch
            {
                TableKind.PMatrix => ("(", ")"),
                TableKind.BMatrix => ("[", "]"),
                TableKind.BBMatrix => ("{", "}"),
                TableKind.VMatrix => ("|", "|"),
                TableKind.VVMatrix => ("‖", "‖"),
                TableKind.Cases => ("{", ""),
                TableKind.RCases => ("", "}"),
                _ => ((string?)null, (string?)null),
            };
            if (open is null) return m;
            return new XElement(M + "d",
                new XElement(M + "dPr",
                    new XElement(M + "begChr", Val(open)),
                    new XElement(M + "endChr", Val(close!)),
                    CtrlPr()),
                new XElement(M + "e", m));
        }

        private void EmitAlignment(Alignment al, List<XElement> output)
        {
            if (al.Rows.Count == 1 && al.Rows[0].Cells.Count == 1)
            {
                Emit(al.Rows[0].Cells[0], output);
                return;
            }

            bool hasAlignmentPoints = al.Rows.Any(r => r.Cells.Count > 1);
            if (!hasAlignmentPoints)
            {
                output.Add(new XElement(M + "eqArr",
                    new XElement(M + "eqArrPr", CtrlPr()),
                    al.Rows.Select(r => E(r.Cells.Count == 0 ? null : r.Cells[0]))));
                return;
            }

            if (Options.Alignment == AlignmentStrategy.EquationArray)
            {
                var eqArr = new XElement(M + "eqArr", new XElement(M + "eqArrPr", CtrlPr()));
                foreach (var row in al.Rows)
                {
                    var list = new List<XElement>();
                    for (int c = 0; c < row.Cells.Count; c++)
                    {
                        if (c > 0) AppendRun(list, MathRun(), "&");
                        Emit(row.Cells[c], list);
                    }
                    eqArr.Add(new XElement(M + "e", list));
                }
                output.Add(eqArr);
                return;
            }

            // Matrix: cột lẻ căn phải, cột chẵn căn trái (đúng như cặp "rl" của align trong TeX).
            int cols = al.Rows.Max(r => r.Cells.Count);
            var columns = Enumerable.Range(0, cols).Select(c => c % 2 == 0 ? ColumnAlign.Right : ColumnAlign.Left).ToArray();
            var rows = al.Rows.Select(r => (IReadOnlyList<MathNode>)r.Cells.Select((cell, c) =>
                c > 0 && c % 2 == 0 ? new Row(new MathNode[] { new Space(SpaceKind.QQuad), cell }) : cell).ToArray()).ToArray();
            output.Add(Matrix(rows, columns, tightColumns: true));
        }

        private XElement EmitBoxed(Boxed bx)
        {
            var pr = new XElement(M + "borderBoxPr");
            if (bx.Kind != BoxKind.Boxed)
            {
                pr.Add(new XElement(M + "hideTop", Val("1")), new XElement(M + "hideBot", Val("1")),
                    new XElement(M + "hideLeft", Val("1")), new XElement(M + "hideRight", Val("1")));
                if (bx.Kind is BoxKind.Cancel or BoxKind.XCancel) pr.Add(new XElement(M + "strikeBLTR", Val("1")));
                if (bx.Kind is BoxKind.BCancel or BoxKind.XCancel) pr.Add(new XElement(M + "strikeTLBR", Val("1")));
            }
            pr.Add(CtrlPr());
            return new XElement(M + "borderBox", pr, E(bx.Content));
        }
    }
}
