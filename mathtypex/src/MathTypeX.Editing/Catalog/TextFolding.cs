using System.Globalization;
using System.Text;

namespace MathTypeX.Editing.Catalog;

/// <summary>Gập dấu tiếng Việt để tìm "tich phan" ra "tích phân" (đ → d).</summary>
public static class TextFolding
{
    public static string Fold(string text)
    {
        string decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c switch
            {
                'đ' => 'd',
                'Đ' => 'd',
                _ => char.ToLowerInvariant(c),
            });
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
