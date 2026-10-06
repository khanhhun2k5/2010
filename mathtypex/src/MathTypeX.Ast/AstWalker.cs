namespace MathTypeX.Ast;

/// <summary>Duyệt và dựng lại cây một cách tổng quát (dùng cho normalizer và các renderer).</summary>
public static class AstWalker
{
    public static IEnumerable<MathNode> Children(MathNode node)
    {
        switch (node)
        {
            case Row r:
                foreach (var c in r.Children) yield return c;
                break;
            case Group g:
                yield return g.Content;
                break;
            case Fraction f:
                yield return f.Numerator;
                yield return f.Denominator;
                break;
            case Radical r:
                if (r.Index is not null) yield return r.Index;
                yield return r.Radicand;
                break;
            case Scripts s:
                yield return s.Base;
                if (s.Sub is not null) yield return s.Sub;
                if (s.Sup is not null) yield return s.Sup;
                break;
            case LargeOperator op:
                if (op.Lower is not null) yield return op.Lower;
                if (op.Upper is not null) yield return op.Upper;
                if (op.Operand is not null) yield return op.Operand;
                break;
            case FunctionApply fn:
                if (fn.Lower is not null) yield return fn.Lower;
                if (fn.Upper is not null) yield return fn.Upper;
                if (fn.Argument is not null) yield return fn.Argument;
                break;
            case Fenced fe:
                foreach (var p in fe.Parts) yield return p;
                break;
            case Accent a:
                yield return a.Base;
                break;
            case Bar b:
                yield return b.Base;
                break;
            case GroupChar gc:
                yield return gc.Base;
                if (gc.Label is not null) yield return gc.Label;
                break;
            case UnderOver uo:
                yield return uo.Base;
                if (uo.Under is not null) yield return uo.Under;
                if (uo.Over is not null) yield return uo.Over;
                break;
            case Table t:
                foreach (var row in t.Rows)
                    foreach (var c in row.Cells) yield return c;
                break;
            case Alignment al:
                foreach (var row in al.Rows)
                    foreach (var c in row.Cells) yield return c;
                break;
            case Boxed bx:
                yield return bx.Content;
                break;
            case Phantom ph:
                yield return ph.Content;
                break;
            case UnknownCommand u:
                foreach (var a in u.Arguments) yield return a;
                break;
        }
    }

    /// <summary>Mọi node con cháu (kể cả chính nó), theo thứ tự trước.</summary>
    public static IEnumerable<MathNode> Descendants(MathNode node)
    {
        yield return node;
        foreach (var c in Children(node))
            foreach (var d in Descendants(c))
                yield return d;
    }

    /// <summary>Dựng lại node với mọi node con đi qua <paramref name="map"/> (không đệ quy — hàm map tự quyết định).</summary>
    public static MathNode MapChildren(MathNode node, Func<MathNode, MathNode> map)
    {
        MathNode? Opt(MathNode? n) => n is null ? null : map(n);
        Row MapRow(Row r) => map(r) is Row rr ? rr : Row.Of(map(r));

        return node switch
        {
            Row r => r with { Children = r.Children.Select(map).ToArray() },
            Group g => g with { Content = MapRow(g.Content) },
            Fraction f => f with { Numerator = map(f.Numerator), Denominator = map(f.Denominator) },
            Radical r => r with { Radicand = map(r.Radicand), Index = Opt(r.Index) },
            Scripts s => s with { Base = map(s.Base), Sub = Opt(s.Sub), Sup = Opt(s.Sup) },
            LargeOperator op => op with { Lower = Opt(op.Lower), Upper = Opt(op.Upper), Operand = Opt(op.Operand) },
            FunctionApply fn => fn with { Lower = Opt(fn.Lower), Upper = Opt(fn.Upper), Argument = Opt(fn.Argument) },
            Fenced fe => fe with { Parts = fe.Parts.Select(map).ToArray() },
            Accent a => a with { Base = map(a.Base) },
            Bar b => b with { Base = map(b.Base) },
            GroupChar gc => gc with { Base = map(gc.Base), Label = Opt(gc.Label) },
            UnderOver uo => uo with { Base = map(uo.Base), Under = Opt(uo.Under), Over = Opt(uo.Over) },
            Table t => t with { Rows = t.Rows.Select(row => new TableRow(row.Cells.Select(map).ToArray())).ToArray() },
            Alignment al => al with { Rows = al.Rows.Select(row => row with { Cells = row.Cells.Select(map).ToArray() }).ToArray() },
            Boxed bx => bx with { Content = map(bx.Content) },
            Phantom ph => ph with { Content = map(ph.Content) },
            UnknownCommand u => u with { Arguments = u.Arguments.Select(map).ToArray() },
            _ => node,
        };
    }

    /// <summary>Bỏ lớp vỏ: Row một phần tử → phần tử đó.</summary>
    public static MathNode Unwrap(MathNode node) =>
        node is Row { Children.Count: 1 } r ? Unwrap(r.Children[0]) : node;

    public static bool IsEmpty(MathNode? node) => node switch
    {
        null => true,
        Row r => r.Children.All(IsEmpty),
        Group g => IsEmpty(g.Content),
        _ => false,
    };
}
