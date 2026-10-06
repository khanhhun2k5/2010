using MathTypeX.Ast;

namespace MathTypeX.Parsing;

/// <summary>
/// Các pass chuẩn hoá chạy sau parse (xem docs/03 §6.5):
/// gộp chỉ số vào large operator/hàm, bắt đối số của hàm, bắt phần thân (operand) của ∫ ∑ ∏,
/// đánh dấu vi phân d, đổi lớp Bin → Ord theo quy tắc TeX.
/// </summary>
public static class Normalizer
{
    public static Row Normalize(Row body) => (Row)NormalizeNode(body);

    private static MathNode NormalizeNode(MathNode node)
    {
        var mapped = AstWalker.MapChildren(node, NormalizeNode);
        return mapped switch
        {
            Row r => NormalizeRow(r),
            Scripts s => FoldScripts(s),
            _ => mapped,
        };
    }

    // ── Gộp chỉ số ───────────────────────────────────────────────────────

    private static MathNode FoldScripts(Scripts s)
    {
        switch (s.Base)
        {
            case LargeOperator op when op.Lower is null && op.Upper is null && op.Operand is null:
                return op with { Lower = s.Sub, Upper = s.Sup, Span = s.Span };
            case FunctionApply fn when fn.Lower is null && fn.Upper is null && fn.Argument is null:
                return fn with { Lower = s.Sub, Upper = s.Sup, Span = s.Span };
            case GroupChar { Position: VerticalPosition.Top, Label: null } gc when s.Sup is not null && !IsArrow(gc.Char):
            {
                MathNode result = gc with { Label = s.Sup, Span = s.Span };
                return s.Sub is null ? result : new Scripts(result, s.Sub, null) { Span = s.Span };
            }
            case GroupChar { Position: VerticalPosition.Bottom, Label: null } gc when s.Sub is not null && !IsArrow(gc.Char):
            {
                MathNode result = gc with { Label = s.Sub, Span = s.Span };
                return s.Sup is null ? result : new Scripts(result, null, s.Sup) { Span = s.Span };
            }
            default:
                return s;
        }
    }

    internal static bool IsArrow(string ch) => ParserCore.ExtensibleArrows.ContainsValue(ch);

    // ── Pass trên một dãy atom ───────────────────────────────────────────

    private static Row NormalizeRow(Row row)
    {
        var items = new List<MathNode>(row.Children.Count);
        foreach (var child in row.Children)
        {
            if (child is Row inner) items.AddRange(inner.Children);
            else items.Add(child);
        }

        CaptureFunctionArguments(items);
        CaptureNaryOperands(items);
        ReclassifyBinary(items);
        return row with { Children = items };
    }

    private static AtomClass ClassOf(MathNode node) => node switch
    {
        Operator op => op.Class,
        Scripts s => ClassOf(s.Base),
        // Đã bắt được phần thân/đối số thì cả cụm đóng vai một atom thường (∑ aᵢ + b: dấu + vẫn là hai ngôi).
        LargeOperator op => op.Operand is null ? AtomClass.Op : AtomClass.Ord,
        FunctionApply fn => fn.Argument is null ? AtomClass.Op : AtomClass.Ord,
        Fenced => AtomClass.Inner,
        _ => AtomClass.Ord,
    };

    private static string? FenceText(MathNode node) => node switch
    {
        Operator op => op.Text,
        Scripts s => FenceText(s.Base),
        _ => null,
    };

    /// <summary>Tìm vị trí đóng tương ứng với ngoặc mở tại <paramref name="open"/> (−1 nếu không có).</summary>
    private static int FindClose(List<MathNode> items, int open)
    {
        int depth = 0;
        for (int k = open; k < items.Count; k++)
        {
            var cls = ClassOf(items[k]);
            if (cls == AtomClass.Open) depth++;
            else if (cls == AtomClass.Close)
            {
                depth--;
                if (depth == 0) return k;
            }
        }
        return -1;
    }

    /// <summary>|x| hay ‖v‖: tìm thanh đóng cùng loại.</summary>
    private static int FindMatchingBar(List<MathNode> items, int open)
    {
        string? bar = FenceText(items[open]);
        for (int k = open + 1; k < items.Count; k++)
            if (FenceText(items[k]) == bar) return k;
        return -1;
    }

    private static bool IsBar(MathNode node) => FenceText(node) is "|" or "‖" && ClassOf(node) == AtomClass.Ord;

    private static bool IsMultiplicative(MathNode node) => node switch
    {
        Identifier or Number or Group or Fenced or Fraction or Radical or Accent or Bar or Boxed or Phantom or Table or Placeholder => true,
        Scripts s => s.Base is not Operator && IsMultiplicative(s.Base) || s.Base is Row,
        UnderOver uo => uo.Base is not Operator,
        GroupChar gc => !IsArrow(gc.Char),
        _ => false,
    };

    // ── Đối số của hàm ───────────────────────────────────────────────────

    private static void CaptureFunctionArguments(List<MathNode> items)
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (items[i] is not FunctionApply { Argument: null } fn) continue;
            int j = i + 1;
            int end = j;
            int k = j;
            while (k < items.Count)
            {
                var it = items[k];
                if (ClassOf(it) == AtomClass.Open)
                {
                    int close = FindClose(items, k);
                    end = close < 0 ? items.Count : close + 1;
                    k = end;
                    // f(x) đã trọn: dừng nếu đây là phần mở đầu; nếu là lời gọi f(x) sau một chữ thì cũng dừng.
                    break;
                }
                if (IsBar(it) && k == j)
                {
                    int close = FindMatchingBar(items, k);
                    end = close < 0 ? k + 1 : close + 1;
                    k = end;
                    break;
                }
                if (it is FunctionApply)
                {
                    if (k == j) end = k + 1; // \sin\cos x = sin(cos x)
                    break;
                }
                if (!IsMultiplicative(it)) break;
                end = k + 1;
                k++;
                // f(x): chữ ngay trước dấu mở ngoặc
                if (k < items.Count && it is Identifier && ClassOf(items[k]) == AtomClass.Open)
                {
                    int close = FindClose(items, k);
                    end = close < 0 ? items.Count : close + 1;
                    k = end;
                }
            }

            if (end <= j) continue;
            var arg = Take(items, j, end);
            items[i] = fn with { Argument = arg, Span = fn.Span.Union(arg.Span) };
        }
    }

    // ── Phần thân của ∫ ∑ ∏ (docs/03 §6.4) ───────────────────────────────

    private static void CaptureNaryOperands(List<MathNode> items)
    {
        // Phải sang trái để ∫∫ f dx dy lồng đúng: ∫( ∫ f dx ) dy.
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (items[i] is not LargeOperator { Operand: null } op) continue;
            bool integral = op.Kind.IsIntegral();
            int needed = op.Kind.DifferentialCount();
            int found = 0;
            int depth = 0;
            int j = i + 1;
            int end = j;

            for (int k = j; k < items.Count; k++)
            {
                var it = items[k];
                var cls = ClassOf(it);
                if (depth == 0)
                {
                    if (cls is AtomClass.Rel or AtomClass.Punct) break;
                    if (!integral && cls == AtomClass.Bin && k > j) break;
                    if (it is TextRun && k > j) break;
                    if (it is Space { Kind: SpaceKind.Quad or SpaceKind.QQuad }) break;
                    if (cls == AtomClass.Close) break;
                }
                if (cls == AtomClass.Open) depth++;
                else if (cls == AtomClass.Close) depth--;

                end = k + 1;

                if (integral && depth == 0 && TryMarkDifferential(items, k))
                {
                    end = k + 2;
                    found++;
                    k++;
                    if (found >= needed) break;
                }
            }

            if (end <= j) continue;
            var operand = Take(items, j, end);
            items[i] = op with { Operand = operand, Span = op.Span.Union(operand.Span) };
        }
    }

    private static bool TryMarkDifferential(List<MathNode> items, int k)
    {
        if (k + 1 >= items.Count) return false;
        if (items[k] is not Identifier { Text: "d" } d) return false;
        if (d.Variant is not (MathVariant.Default or MathVariant.Normal or MathVariant.Italic)) return false;
        var next = items[k + 1];
        bool variable = next is Identifier { Role: not IdentifierRole.Differential }
            || next is Scripts { Base: Identifier }
            || next is Group;
        if (!variable) return false;
        items[k] = d with { Role = IdentifierRole.Differential };
        return true;
    }

    private static MathNode Take(List<MathNode> items, int start, int end)
    {
        var taken = items.GetRange(start, end - start);
        items.RemoveRange(start, end - start);
        if (taken.Count == 1) return taken[0];
        ReclassifyBinary(taken);
        return new Row(taken) { Span = taken[0].Span.Union(taken[taken.Count - 1].Span) };
    }

    // ── Bin → Ord ─────────────────────────────────────────────────────────

    private static void ReclassifyBinary(List<MathNode> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not Operator { Class: AtomClass.Bin } op) continue;
            var prev = i == 0 ? (AtomClass?)null : ClassOf(items[i - 1]);
            var next = i + 1 >= items.Count ? (AtomClass?)null : ClassOf(items[i + 1]);
            bool unaryContext = prev is null or AtomClass.Bin or AtomClass.Rel or AtomClass.Open or AtomClass.Punct or AtomClass.Op;
            bool trailing = next is null or AtomClass.Rel or AtomClass.Close or AtomClass.Punct;
            if (unaryContext || trailing)
                items[i] = op with { Class = AtomClass.Ord };
        }
    }
}
