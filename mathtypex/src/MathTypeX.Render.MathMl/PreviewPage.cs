using System.Text;

namespace MathTypeX.Render.MathMl;

/// <summary>
/// Trang HTML của preview (WebView2 trong editor, và trang so sánh font của CLI).
/// Hàm JS <c>mtxRender(mathml, mathFont, integralFont, textFont, sizePx)</c> thay nội dung mà không tải lại trang (vài ms).
/// </summary>
public static class PreviewPage
{
    public const string Style = """
        html, body { margin: 0; background: #ffffff; color: #000000; }
        body { font-family: "Segoe UI", system-ui, sans-serif; }
        #eq { padding: 10px 14px; min-height: 36px; overflow-x: auto; }
        math { font-size: var(--mtx-size, 22px); font-family: var(--mtx-math, "Cambria Math"), math; }
        math .mtx-int { font-family: var(--mtx-int, var(--mtx-math, "Cambria Math")), math; }
        math mtext { font-family: var(--mtx-text, "Times New Roman"), serif; }
        .mtx-ph { color: #8a8a8a; }
        .mtx-err { color: #c00000; font-family: Consolas, monospace; }
        """;

    public const string Script = """
        function mtxRender(mathml, mathFont, integralFont, textFont, sizePx) {
          const root = document.documentElement.style;
          root.setProperty('--mtx-math', '"' + mathFont + '"');
          root.setProperty('--mtx-int', '"' + (integralFont || mathFont) + '"');
          root.setProperty('--mtx-text', '"' + (textFont || 'Times New Roman') + '"');
          root.setProperty('--mtx-size', (sizePx || 22) + 'px');
          document.getElementById('eq').innerHTML = mathml;
          const r = document.getElementById('eq').getBoundingClientRect();
          return Math.ceil(r.height);
        }
        """;

    /// <summary>Trang rỗng cho WebView2; nội dung được nạp bằng <c>mtxRender</c>.</summary>
    public static string LivePage() =>
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>" + Style + "</style></head>"
        + "<body><div id=\"eq\"></div><script>" + Script + "</script></body></html>";

    public sealed record Sample(string Label, string Latex, string MathMl);

    /// <summary>Trang so sánh: mỗi hàng một công thức, mỗi cột một font (Compare mode, §17).</summary>
    public static string ComparePage(IReadOnlyList<string> fonts, IReadOnlyList<Sample> samples, string title)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>").Append(Escape(title)).Append("</title><style>")
          .Append(Style)
          .Append("table{border-collapse:collapse;margin:12px}td,th{border:1px solid #ddd;padding:8px 12px;vertical-align:middle}")
          .Append("th{font-weight:600;background:#f6f6f6}code{font-size:11px;color:#555}")
          .Append("</style></head><body><h3 style=\"margin:12px\">").Append(Escape(title)).Append("</h3><table><tr><th>LaTeX</th>");
        foreach (var f in fonts) sb.Append("<th>").Append(Escape(f)).Append("</th>");
        sb.Append("</tr>");
        foreach (var s in samples)
        {
            sb.Append("<tr><td><code>").Append(Escape(s.Latex)).Append("</code></td>");
            foreach (var f in fonts)
                sb.Append("<td style=\"--mtx-math:'").Append(Escape(f)).Append("'\">").Append(s.MathMl).Append("</td>");
            sb.Append("</tr>");
        }
        sb.Append("</table></body></html>");
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
