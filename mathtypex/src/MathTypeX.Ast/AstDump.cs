using System.Reflection;
using System.Text;

namespace MathTypeX.Ast;

/// <summary>In cây AST dạng thụt lề — dùng cho CLI (<c>mtx parse</c>) và khi gỡ lỗi.</summary>
public static class AstDump
{
    public static string Dump(MathNode node)
    {
        var sb = new StringBuilder();
        Write(sb, node, 0, null);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, MathNode node, int depth, string? label)
    {
        sb.Append(' ', depth * 2);
        if (label is not null) sb.Append(label).Append(": ");
        sb.Append(node.GetType().Name);

        var props = node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name is not ("Span" or "EqualityContract"))
            .ToArray();

        foreach (var p in props)
        {
            var value = p.GetValue(node);
            if (value is string s) sb.Append(' ').Append(p.Name).Append("=\"").Append(s).Append('"');
            else if (value is Enum or bool or int) sb.Append(' ').Append(p.Name).Append('=').Append(value);
        }
        sb.Append(' ').Append(node.Span).AppendLine();

        foreach (var p in props)
        {
            var value = p.GetValue(node);
            switch (value)
            {
                case Row row when node is Group:
                    foreach (var c in row.Children) Write(sb, c, depth + 1, null);
                    break;
                case MathNode child:
                    Write(sb, child, depth + 1, p.Name);
                    break;
                case IEnumerable<MathNode> list:
                    foreach (var c in list) Write(sb, c, depth + 1, node is Row ? null : p.Name);
                    break;
                case IEnumerable<TableRow> rows:
                    int r = 0;
                    foreach (var row in rows)
                    {
                        int c = 0;
                        foreach (var cell in row.Cells) Write(sb, cell, depth + 1, $"[{r},{c++}]");
                        r++;
                    }
                    break;
                case IEnumerable<AlignedRow> arows:
                    int ar = 0;
                    foreach (var row in arows)
                    {
                        int c = 0;
                        foreach (var cell in row.Cells) Write(sb, cell, depth + 1, $"[{ar},{c++}]");
                        ar++;
                    }
                    break;
            }
        }
    }
}
