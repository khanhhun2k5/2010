using MathTypeX.Editing.Catalog;

namespace MathTypeX.Editing.Assist;

/// <summary>Kết quả chấp nhận một gợi ý: văn bản mới, vị trí chọn đầu tiên và phiên Tab (nếu snippet có ô).</summary>
public sealed record CompletionResult(string Text, int SelectionStart, int SelectionLength, SnippetSession? Session);

/// <summary>Kế hoạch thay văn bản cho UI: thay [ReplaceStart, +ReplaceLength) bằng InsertText rồi chọn vùng.</summary>
public sealed record CompletionPlan(int ReplaceStart, int ReplaceLength, string InsertText, int SelectionStart, int SelectionLength, SnippetSession? Session);

/// <summary>Logic autocomplete (§7) độc lập với UI: tìm tiền tố "\fra" trước con trỏ và thay bằng snippet.</summary>
public static class Completion
{
    /// <summary>Kế hoạch thay cho UI (giữ được Undo của ô soạn vì chỉ thay đúng đoạn cần thay).</summary>
    public static CompletionPlan Plan(int replaceStart, int replaceEnd, CatalogEntry entry)
    {
        var expanded = SnippetParser.Expand(entry.Snippet);
        if (expanded.Stops.Count == 0)
            return new CompletionPlan(replaceStart, replaceEnd - replaceStart, expanded.Text, replaceStart + expanded.ExitOffset, 0, null);
        var session = new SnippetSession(expanded, replaceStart);
        return new CompletionPlan(replaceStart, replaceEnd - replaceStart, expanded.Text, session.Current.Start, session.Current.End - session.Current.Start, session);
    }

    /// <summary>Tiền tố lệnh ngay trước con trỏ: "\fra|" → (start của "\", "fra"). Null nếu không phải đang gõ lệnh.</summary>
    public static (int Start, string Prefix)? CommandPrefixAt(string text, int caret)
    {
        int i = caret;
        while (i > 0 && char.IsLetter(text[i - 1]) && text[i - 1] < 128) i--;
        if (i == 0 || text[i - 1] != '\\') return null;
        // "\\" (xuống dòng) không phải lệnh.
        if (i >= 2 && text[i - 2] == '\\') return null;
        return (i - 1, text.Substring(i, caret - i));
    }

    /// <summary>
    /// Tab/Shift+Tab khi không có phiên snippet: nhảy tới ô rỗng "{}" kế tiếp/trước đó (con trỏ vào giữa hai ngoặc).
    /// Trả về vị trí con trỏ mới, hoặc null nếu không còn ô nào.
    /// </summary>
    public static int? NextEmptySlot(string text, int caret, bool forward)
    {
        if (forward)
        {
            for (int i = text.IndexOf("{}", Math.Max(0, Math.Min(caret, text.Length)), StringComparison.Ordinal); i >= 0; i = text.IndexOf("{}", i + 1, StringComparison.Ordinal))
            {
                if (i + 1 > caret && !IsEscaped(text, i)) return i + 1;
            }
            return null;
        }
        for (int i = caret - 2; i >= 0; i--)
        {
            if (text[i] == '{' && i + 1 < text.Length && text[i + 1] == '}' && i + 1 < caret && !IsEscaped(text, i)) return i + 1;
        }
        return null;
    }

    private static bool IsEscaped(string text, int index)
    {
        int slashes = 0;
        for (int i = index - 1; i >= 0 && text[i] == '\\'; i--) slashes++;
        return slashes % 2 == 1;
    }

    /// <summary>Thay đoạn [replaceStart, caret) bằng snippet của mục catalog.</summary>
    public static CompletionResult Accept(string text, int replaceStart, int caret, CatalogEntry entry)
    {
        var expanded = SnippetParser.Expand(entry.Snippet);
        string result = text.Substring(0, replaceStart) + expanded.Text + text.Substring(caret);
        if (expanded.Stops.Count == 0)
            return new CompletionResult(result, replaceStart + expanded.ExitOffset, 0, null);
        var session = new SnippetSession(expanded, replaceStart);
        var first = session.Current;
        return new CompletionResult(result, first.Start, first.End - first.Start, session);
    }

    /// <summary>Chèn snippet tại vùng chọn (Command Palette, thư viện công thức).</summary>
    public static CompletionResult Insert(string text, int selectionStart, int selectionLength, CatalogEntry entry) =>
        Accept(text, selectionStart, selectionStart + selectionLength, entry);
}
