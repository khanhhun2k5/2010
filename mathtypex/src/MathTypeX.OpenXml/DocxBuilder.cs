using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using MathTypeX.Render.Omml;
using M = DocumentFormat.OpenXml.Math;

namespace MathTypeX.OpenXml;

/// <summary>
/// Dựng tài liệu .docx chứa equation gốc (OMML) — dùng cho CLI demo, bộ đo spike S2 và test hợp lệ OOXML.
/// </summary>
public sealed class DocxBuilder
{
    private readonly List<OpenXmlElement> _blocks = new();

    public DocxBuilder Heading(string text, int level = 1)
    {
        var p = new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = "Heading" + level }),
            new Run(new Text(text)));
        _blocks.Add(p);
        return this;
    }

    public DocxBuilder Text(string text, bool code = false, string? color = null)
    {
        // Thứ tự con của w:rPr theo schema: rFonts, …, color, …, sz.
        var props = new RunProperties();
        if (code) props.Append(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas", ComplexScript = "Consolas" });
        if (color is not null) props.Append(new Color { Val = color });
        if (code) props.Append(new FontSize { Val = "18" });
        _blocks.Add(new Paragraph(new Run(props, new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
        return this;
    }

    /// <summary>Đoạn văn: chữ trước + equation inline + chữ sau.</summary>
    public DocxBuilder InlineMath(string before, XElement oMath, string after = "")
    {
        var p = new Paragraph();
        if (before.Length > 0) p.Append(new Run(new Text(before) { Space = SpaceProcessingModeValues.Preserve }));
        p.Append(new M.OfficeMath(Outer(oMath)));
        if (after.Length > 0) p.Append(new Run(new Text(after) { Space = SpaceProcessingModeValues.Preserve }));
        _blocks.Add(p);
        return this;
    }

    /// <summary>Đoạn văn trộn chữ (string) và equation inline (phần tử m:oMath) theo thứ tự — kết quả của Convert LaTeX.</summary>
    public DocxBuilder Mixed(IEnumerable<object> parts)
    {
        var p = new Paragraph();
        foreach (var part in parts)
        {
            if (part is XElement oMath) p.Append(new M.OfficeMath(Outer(oMath)));
            else if (part is string text && text.Length > 0) p.Append(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        }
        _blocks.Add(p);
        return this;
    }

    /// <summary>Đoạn văn chỉ chứa một display equation (Word yêu cầu display equation đứng riêng một đoạn).</summary>
    public DocxBuilder DisplayMath(XElement oMathPara)
    {
        _blocks.Add(new Paragraph(new M.Paragraph(Outer(oMathPara))));
        return this;
    }

    private static string Outer(XElement element) => element.ToString(SaveOptions.DisableFormatting);

    public void Save(string path, string? documentMathFont = null)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(_blocks.Select(b => b.CloneNode(true))));

        var styles = main.AddNewPart<StyleDefinitionsPart>();
        styles.Styles = new Styles(
            HeadingStyle("Heading1", "heading 1", 32),
            HeadingStyle("Heading2", "heading 2", 26));

        if (documentMathFont is not null)
        {
            var settings = main.AddNewPart<DocumentSettingsPart>();
            settings.Settings = new Settings(new M.MathProperties(new M.MathFont { Val = documentMathFont }));
        }
        main.Document.Save();
    }

    private static Style HeadingStyle(string id, string name, int halfPoints) =>
        new(new StyleName { Val = name },
            new BasedOn { Val = "Normal" },
            new StyleRunProperties(new Bold(), new FontSize { Val = halfPoints.ToString(System.Globalization.CultureInfo.InvariantCulture) }))
        {
            Type = StyleValues.Paragraph,
            StyleId = id,
        };

    /// <summary>Kiểm tra tài liệu theo schema Office (OpenXmlValidator); trả về danh sách lỗi.</summary>
    public static IReadOnlyList<string> Validate(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(doc)
            .Select(e => $"{e.Path?.XPath}: {e.Description}")
            .ToArray();
    }
}
