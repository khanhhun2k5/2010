using System.Text;

namespace MathTypeX.Editing.Assist;

/// <summary>Một điểm Tab trong snippet: vùng [Start, End) trong văn bản.</summary>
public sealed class TabStop
{
    public TabStop(int index, int start, int end)
    {
        Index = index;
        Start = start;
        End = end;
    }

    public int Index { get; }
    public int Start { get; internal set; }
    public int End { get; internal set; }
}

/// <summary>Snippet đã mở rộng: văn bản thuần (không còn $1…) và vị trí các điểm Tab theo thứ tự.</summary>
public sealed record ExpandedSnippet(string Text, IReadOnlyList<(int Index, int Start, int End)> Stops, int ExitOffset);

/// <summary>
/// Cú pháp snippet (§10, §15): $1 $2 … là điểm Tab, ${1:gợi ý} có chữ mặc định (được chọn sẵn để gõ đè),
/// $0 là vị trí kết thúc; \$ là dấu $ thật.
/// </summary>
public static class SnippetParser
{
    public static ExpandedSnippet Expand(string snippet)
    {
        var text = new StringBuilder();
        var stops = new List<(int Index, int Start, int End)>();
        int exit = -1;
        for (int i = 0; i < snippet.Length; i++)
        {
            char c = snippet[i];
            if (c == '\\' && i + 1 < snippet.Length && snippet[i + 1] == '$')
            {
                text.Append('$');
                i++;
                continue;
            }
            if (c == '$' && i + 1 < snippet.Length && char.IsDigit(snippet[i + 1]))
            {
                int j = i + 1;
                while (j < snippet.Length && char.IsDigit(snippet[j])) j++;
                int index = int.Parse(snippet.Substring(i + 1, j - i - 1), System.Globalization.CultureInfo.InvariantCulture);
                if (index == 0) exit = text.Length;
                else stops.Add((index, text.Length, text.Length));
                i = j - 1;
                continue;
            }
            if (c == '$' && i + 2 < snippet.Length && snippet[i + 1] == '{' && char.IsDigit(snippet[i + 2]))
            {
                int colon = snippet.IndexOf(':', i);
                int close = snippet.IndexOf('}', i);
                if (colon > 0 && close > colon)
                {
                    int index = int.Parse(snippet.Substring(i + 2, colon - i - 2), System.Globalization.CultureInfo.InvariantCulture);
                    string hint = snippet.Substring(colon + 1, close - colon - 1);
                    int start = text.Length;
                    text.Append(hint);
                    if (index == 0) exit = start;
                    else stops.Add((index, start, text.Length));
                    i = close;
                    continue;
                }
            }
            text.Append(c);
        }
        if (exit < 0) exit = text.Length;
        return new ExpandedSnippet(text.ToString(), stops.OrderBy(s => s.Index).ToArray(), exit);
    }
}

/// <summary>
/// Phiên điều hướng Tab trong một snippet vừa chèn (§10): Tab sang ô kế, Shift+Tab về ô trước,
/// sau ô cuối thì ra vị trí $0. Vị trí các ô tự dịch khi người dùng gõ.
/// </summary>
public sealed class SnippetSession
{
    private readonly List<TabStop> _stops;
    private int _current;

    public SnippetSession(ExpandedSnippet snippet, int insertedAt)
    {
        _stops = snippet.Stops.Select(s => new TabStop(s.Index, insertedAt + s.Start, insertedAt + s.End)).ToList();
        _stops.Add(new TabStop(int.MaxValue, insertedAt + snippet.ExitOffset, insertedAt + snippet.ExitOffset));
        _current = 0;
    }

    public bool IsActive { get; private set; } = true;

    public TabStop Current => _stops[_current];

    public bool AtExit => _current == _stops.Count - 1;

    /// <summary>Cập nhật vị trí sau một thay đổi văn bản (offset, số ký tự bị xoá, số ký tự được thêm).</summary>
    public void OnTextChanged(int offset, int removed, int added)
    {
        if (!IsActive) return;
        int delta = added - removed;
        foreach (var stop in _stops)
        {
            bool insideCurrent = ReferenceEquals(stop, Current) && offset >= stop.Start && offset <= stop.End;
            if (insideCurrent)
            {
                stop.End = Math.Max(stop.Start, stop.End + delta);
            }
            else if (offset < stop.Start || (offset == stop.Start && !ReferenceEquals(stop, Current) && StopIndex(stop) > _current))
            {
                stop.Start = Math.Max(offset, stop.Start + delta);
                stop.End = Math.Max(stop.Start, stop.End + delta);
            }
            else if (offset < stop.End)
            {
                stop.End = Math.Max(stop.Start, stop.End + delta);
            }
        }
    }

    private int StopIndex(TabStop stop) => _stops.IndexOf(stop);

    /// <summary>Sang ô kế tiếp; trả về vùng cần chọn. Khi đã ra $0 thì phiên kết thúc.</summary>
    public TabStop Next()
    {
        if (_current < _stops.Count - 1) _current++;
        if (AtExit) IsActive = false;
        return Current;
    }

    public TabStop Previous()
    {
        if (_current > 0) _current--;
        return Current;
    }

    /// <summary>Con trỏ ra ngoài mọi ô (người dùng click chỗ khác) thì kết thúc phiên.</summary>
    public void OnCaretMoved(int caret)
    {
        if (!IsActive) return;
        if (!_stops.Any(s => caret >= s.Start && caret <= s.End)) IsActive = false;
    }

    public void Cancel() => IsActive = false;
}
