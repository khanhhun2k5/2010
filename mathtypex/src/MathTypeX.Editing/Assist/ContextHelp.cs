using MathTypeX.Editing.Catalog;
using MathTypeX.Parsing;

namespace MathTypeX.Editing.Assist;

/// <summary>Trợ giúp ngữ cảnh cho Beginner mode (§26): con trỏ đang ở đối số nào của lệnh nào.</summary>
public sealed record HelpInfo(CatalogEntry Entry, int ArgumentIndex)
{
    public string? ArgumentName => ArgumentIndex >= 0 && ArgumentIndex < Entry.ArgumentsVi.Count ? Entry.ArgumentsVi[ArgumentIndex] : null;

    /// <summary>Ví dụ: "Phân số · đang nhập: mẫu số · \frac{tử số}{mẫu số} · Ví dụ: \frac{x+1}{x-1}".</summary>
    public string Describe()
    {
        var parts = new List<string> { Entry.NameVi };
        if (ArgumentName is not null) parts.Add("đang nhập: " + ArgumentName);
        if (Entry.SyntaxVi.Length > 0) parts.Add(Entry.SyntaxVi);
        if (Entry.Example.Length > 0) parts.Add("Ví dụ: " + Entry.Example);
        return string.Join(" · ", parts);
    }
}

public static class ContextHelp
{
    public static HelpInfo? At(string text, int caret)
    {
        var diagnostics = new List<MathTypeX.Diagnostic>();
        var tokens = Tokenizer.Tokenize(text, diagnostics);
        // Ngăn xếp: mỗi nhóm { } đang mở thuộc lệnh nào và là đối số thứ mấy.
        var stack = new Stack<(string? Command, int Arg)>();
        string? lastCommand = null;
        int lastCommandArgs = 0;
        int lastCommandEnd = -1;

        foreach (var t in tokens)
        {
            if (t.Span.Start >= caret) break;
            switch (t.Kind)
            {
                case TokenKind.ControlWord:
                    lastCommand = t.Text;
                    lastCommandArgs = 0;
                    lastCommandEnd = t.Span.End;
                    break;
                case TokenKind.BeginGroup:
                    stack.Push(lastCommand is null ? (null, -1) : (lastCommand, lastCommandArgs));
                    break;
                case TokenKind.EndGroup:
                    if (stack.Count > 0)
                    {
                        var closed = stack.Pop();
                        if (closed.Command is not null)
                        {
                            lastCommand = closed.Command;
                            lastCommandArgs = closed.Arg + 1;
                            lastCommandEnd = t.Span.End;
                            continue;
                        }
                    }
                    lastCommand = null;
                    break;
                case TokenKind.Space:
                    break;
                default:
                    if (t.Kind is not (TokenKind.Superscript or TokenKind.Subscript)) lastCommand = null;
                    break;
            }
        }

        foreach (var (command, arg) in stack)
        {
            if (command is null) continue;
            var entry = CommandCatalog.FindByTrigger(command);
            if (entry is not null) return new HelpInfo(entry, arg);
        }

        // Vừa gõ xong tên lệnh (\frac|) → giới thiệu lệnh.
        if (lastCommand is not null && lastCommandEnd == caret && CommandCatalog.FindByTrigger(lastCommand) is { } just)
            return new HelpInfo(just, -1);
        return null;
    }
}
