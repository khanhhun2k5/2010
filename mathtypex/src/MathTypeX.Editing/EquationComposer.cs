using MathTypeX.Ast;
using MathTypeX.Interop;
using MathTypeX.Parsing;
using MathTypeX.Render.Omml;

namespace MathTypeX.Editing;

public sealed record ComposeOptions
{
    public string MathFont { get; init; } = "Cambria Math";
    public string TextFont { get; init; } = "Times New Roman";
    public bool Display { get; init; }
    public double? FontSizePt { get; init; }
    public bool DecimalComma { get; init; }
    public bool UprightDifferential { get; init; }
    public NarySizing NarySizing { get; init; } = NarySizing.TeX;
}

/// <summary>Kết quả soạn: cây đã parse, kết quả để chèn (nếu được), hoặc lý do chặn chèn.</summary>
public sealed record ComposeOutcome(MathDocument Document, EditResult? Result, string? BlockingMessage, IReadOnlyList<string> Approximations);

/// <summary>Biến LaTeX người dùng gõ trong editor thành kết quả sẵn sàng chèn vào Word.</summary>
public static class EquationComposer
{
    /// <summary>Bỏ delimiter mà người dùng dán kèm ($…$, \(…\), \[…\], $$…$$). Trả về chế độ display nếu delimiter cho biết.</summary>
    public static (string Latex, bool? Display) StripDelimiters(string input)
    {
        string s = input.Trim();
        if (s.Length >= 4 && s.StartsWith("$$", StringComparison.Ordinal) && s.EndsWith("$$", StringComparison.Ordinal))
            return (s.Substring(2, s.Length - 4).Trim(), true);
        if (s.Length >= 4 && s.StartsWith("\\[", StringComparison.Ordinal) && s.EndsWith("\\]", StringComparison.Ordinal))
            return (s.Substring(2, s.Length - 4).Trim(), true);
        if (s.Length >= 4 && s.StartsWith("\\(", StringComparison.Ordinal) && s.EndsWith("\\)", StringComparison.Ordinal))
            return (s.Substring(2, s.Length - 4).Trim(), false);
        if (s.Length >= 2 && s[0] == '$' && s[s.Length - 1] == '$' && !(s.Length >= 2 && s[s.Length - 2] == '\\'))
            return (s.Substring(1, s.Length - 2).Trim(), false);
        return (s, null);
    }

    public static MathDocument Analyze(string latex, ComposeOptions options) =>
        LatexParser.Parse(StripDelimiters(latex).Latex, new ParserOptions { DecimalComma = options.DecimalComma });

    /// <summary>Có ô đối số còn trống không (□ trong preview): \frac{}{}, \sqrt{}, x^{}…</summary>
    public static bool HasEmptySlot(MathNode node) => AstWalker.Descendants(node).Any(n => n switch
    {
        Placeholder => true,
        Fraction f => AstWalker.IsEmpty(f.Numerator) || AstWalker.IsEmpty(f.Denominator),
        Radical r => AstWalker.IsEmpty(r.Radicand) || (r.Index is not null && AstWalker.IsEmpty(r.Index)),
        Scripts s => (s.Sub is not null && AstWalker.IsEmpty(s.Sub)) || (s.Sup is not null && AstWalker.IsEmpty(s.Sup)),
        Accent a => AstWalker.IsEmpty(a.Base),
        _ => false,
    });

    /// <param name="stripDelimiters">
    /// Bỏ $…$/$$…$$… mà người dùng dán kèm và lấy chế độ display theo delimiter. Tắt khi LaTeX đã được tách sẵn
    /// (Convert Selection) để <see cref="ComposeOptions.Display"/> luôn được tôn trọng.
    /// </param>
    public static ComposeOutcome Compose(string input, ComposeOptions options, UiLanguage language = UiLanguage.Vi, bool allowEmptySlots = false, bool stripDelimiters = true)
    {
        var (latex, delimiterDisplay) = stripDelimiters ? StripDelimiters(input) : (input.Trim(), (bool?)null);
        bool display = delimiterDisplay ?? options.Display;
        var doc = LatexParser.Parse(latex, new ParserOptions { DecimalComma = options.DecimalComma });

        if (doc.Body.Children.Count == 0)
            return new ComposeOutcome(doc, null, language == UiLanguage.Vi ? "Chưa nhập công thức." : "The formula is empty.", Array.Empty<string>());

        var firstError = doc.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        if (firstError is not null)
            return new ComposeOutcome(doc, null, DiagnosticFormatter.Format(firstError, language), Array.Empty<string>());

        if (!allowEmptySlots && HasEmptySlot(doc.Body))
            return new ComposeOutcome(doc, null, language == UiLanguage.Vi
                ? "Còn ô trống □ chưa điền (Ctrl+Shift+Enter để vẫn chèn)."
                : "Some placeholders □ are still empty (Ctrl+Shift+Enter to insert anyway).", Array.Empty<string>());

        var omml = OmmlWriter.Write(doc, new OmmlOptions
        {
            Display = display,
            MathFont = options.MathFont,
            TextFont = options.TextFont,
            FontSizePt = options.FontSizePt,
            UprightDifferential = options.UprightDifferential,
            NarySizing = options.NarySizing,
        });

        var result = new EditResult
        {
            Latex = latex,
            NormalizedLatex = LatexPrinter.Print(doc),
            Display = display,
            MathFont = options.MathFont,
            FlatOpc = FlatOpc.ForMath(omml.Element),
        };
        return new ComposeOutcome(doc, result, null, omml.Approximations);
    }
}
