namespace MathTypeX.Fonts;

/// <summary>Chẩn đoán font (§19): ký hiệu nào thiếu glyph và font nào nên dùng thay.</summary>
public static class FontDiagnostics
{
    public sealed record MissingGlyph(string Text, IReadOnlyList<string> Fallbacks);

    public static IReadOnlyList<MissingGlyph> FindMissing(FontFace font, IEnumerable<string> texts, IReadOnlyList<FontFace> candidates)
    {
        var result = new List<MissingGlyph>();
        foreach (var text in texts.Distinct(StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(text) || font.Coverage.Contains(text)) continue;
            var fallbacks = candidates
                .Where(c => !string.Equals(c.Family, font.Family, StringComparison.OrdinalIgnoreCase) && c.Coverage.Contains(text))
                .Select(c => c.Family)
                .ToArray();
            result.Add(new MissingGlyph(text, fallbacks));
        }
        return result;
    }

    /// <summary>Mô tả ngắn về dấu ∫ trong font — hiển thị trong font selector và báo cáo spike S2.</summary>
    public static string DescribeIntegral(FontFace font)
    {
        if (font.Math?.Integral is not { } integral) return "∫: không có biến thể kích thước";
        string assembly = integral.HasAssembly ? ", có lắp ghép (kéo dãn tuỳ ý)" : "";
        return $"∫: {integral.VariantCount} cỡ{assembly}; cỡ display tối thiểu {font.Math.DisplayOperatorMinHeight}/{font.UnitsPerEm} em";
    }
}
