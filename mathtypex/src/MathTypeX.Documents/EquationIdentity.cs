using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using MathTypeX.Render.Omml;

namespace MathTypeX.Documents;

/// <summary>
/// Khoá chuẩn hoá của một Word Equation (docs/07 §12.3, tầng L2): SHA-256 của LaTeX chuẩn hoá sau khi
/// chuyển ngược OMML. Không băm thẳng XML vì Word ghi lại OMML theo cách của nó (thêm w:rPr, m:ctrlPr, rsid,
/// tách/gộp run). Font và inline/display không thuộc khoá: cùng nội dung thì cùng source.
/// </summary>
public static class EquationIdentity
{
    public const string Prefix = "k1:";

    public static string KeyOf(XElement omml) => KeyOfNormalized(OmmlToLatex.Convert(omml).NormalizedLatex);

    public static string KeyOfNormalized(string normalizedLatex)
    {
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalizedLatex));
        var sb = new StringBuilder(Prefix);
        for (int i = 0; i < 16; i++) sb.Append(hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}

/// <summary>Lấy phần tử m:oMathPara / m:oMath đầu tiên từ XML mà Word trả về (Range.WordOpenXML là gói Flat OPC).</summary>
public static class OmmlExtractor
{
    public static XElement? FirstEquation(string wordOpenXml)
    {
        var doc = XDocument.Parse(wordOpenXml);
        return doc.Descendants(OmmlWriter.M + "oMathPara").FirstOrDefault()
            ?? doc.Descendants(OmmlWriter.M + "oMath").FirstOrDefault();
    }
}
