using System.Xml.Linq;

namespace MathTypeX.Render.Omml;

/// <summary>
/// Gói Flat OPC tối giản cho <c>Range.InsertXML</c> của Word: chỉ có document.xml, không có styles.xml
/// (để không làm bẩn style của tài liệu đích — docs/05 §9.2).
/// </summary>
public static class FlatOpc
{
    private static readonly XNamespace Pkg = "http://schemas.microsoft.com/office/2006/xmlPackage";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Đóng gói một m:oMath (inline) hoặc m:oMathPara (display) vào một đoạn văn.</summary>
    public static string ForMath(XElement omml)
    {
        var w = OmmlWriter.W;
        var document = new XElement(w + "document",
            new XAttribute(XNamespace.Xmlns + "w", w.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "m", OmmlWriter.M.NamespaceName),
            new XElement(w + "body", new XElement(w + "p", new XElement(omml))));

        var package = new XElement(Pkg + "package",
            new XAttribute(XNamespace.Xmlns + "pkg", Pkg.NamespaceName),
            new XElement(Pkg + "part",
                new XAttribute(Pkg + "name", "/_rels/.rels"),
                new XAttribute(Pkg + "contentType", "application/vnd.openxmlformats-package.relationships+xml"),
                new XElement(Pkg + "xmlData",
                    new XElement(Rel + "Relationships",
                        new XElement(Rel + "Relationship",
                            new XAttribute("Id", "rId1"),
                            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                            new XAttribute("Target", "word/document.xml"))))),
            new XElement(Pkg + "part",
                new XAttribute(Pkg + "name", "/word/document.xml"),
                new XAttribute(Pkg + "contentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"),
                new XElement(Pkg + "xmlData", document)));

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XProcessingInstruction("mso-application", "progid=\"Word.Document\""),
            package);
        return doc.Declaration + Environment.NewLine + doc.ToString(SaveOptions.DisableFormatting);
    }
}
