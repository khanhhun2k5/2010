using MathTypeX;
using MathTypeX.Ast;
using MathTypeX.OpenXml;
using MathTypeX.Parsing;
using MathTypeX.Render.Omml;

namespace MathTypeX.Cli;

/// <summary>
/// Sinh tài liệu thử: mỗi công thức × font × inline/display × cỡ ∫ (TeX/Grow).
/// Mở trong Word để quan sát và điền báo cáo spike S2 (docs/spikes/S2-word-math-fonts.md).
/// </summary>
internal static class DemoDocument
{
    public static int Build(Options opts)
    {
        string corpusPath = opts.Positional ?? throw new ArgumentException("Thiếu đường dẫn corpus .tex");
        string outPath = opts.Out ?? Path.ChangeExtension(Path.GetFileName(corpusPath), ".docx");
        var entries = Corpus.Load(corpusPath);

        var b = new DocxBuilder();
        b.Heading("MathTypeX — tài liệu thử Word Equation", 1);
        b.Text($"Corpus: {Path.GetFileName(corpusPath)} · Sinh lúc {DateTime.Now:yyyy-MM-dd HH:mm} · Font: {string.Join(", ", opts.Fonts)}");
        b.Text("Mọi công thức dưới đây là Word Equation gốc (OMML). Hãy kiểm tra: (1) font có được giữ không; "
             + "(2) cỡ và độ cân đối của ∫ ở inline/display; (3) chế độ Grow (m:grow) có kéo dãn ∫ không.");

        string? section = null;
        int problems = 0;
        foreach (var entry in entries)
        {
            if (entry.Section != section)
            {
                section = entry.Section;
                if (section.Length > 0) b.Heading(section, 2);
            }

            var doc = LatexParser.Parse(entry.Latex, new ParserOptions { DecimalComma = opts.DecimalComma });
            b.Text("LaTeX: " + entry.Latex, code: true, color: "555555");
            foreach (var d in doc.Diagnostics)
            {
                problems++;
                b.Text("⚠ " + DiagnosticFormatter.Format(d, UiLanguage.Vi), color: "C00000");
            }

            bool hasNary = AstWalker.Descendants(doc.Body).Any(n => n is LargeOperator);
            foreach (var font in opts.Fonts)
            {
                foreach (var sizing in hasNary ? opts.Sizing : opts.Sizing.Take(1).ToArray())
                {
                    var narySizing = sizing.Equals("Grow", StringComparison.OrdinalIgnoreCase) ? NarySizing.Grow : NarySizing.TeX;
                    string tag = hasNary ? $"{font} · {sizing}" : font;
                    foreach (var mode in opts.Modes)
                    {
                        bool display = mode == "display";
                        var omml = OmmlWriter.Write(doc, new OmmlOptions
                        {
                            Display = display,
                            MathFont = font,
                            IntegralFont = opts.IntegralFont,
                            NarySizing = narySizing,
                            UprightDifferential = opts.UprightD,
                            FontSizePt = opts.Size,
                            Alignment = opts.Align,
                        });
                        if (display)
                        {
                            b.Text($"[{tag} · display]", color: "1F4E79");
                            b.DisplayMath(omml.Element);
                        }
                        else
                        {
                            b.InlineMath($"[{tag} · inline] Ta có ", omml.Element, " là kết quả.");
                        }
                    }
                }
            }
        }

        // Thí nghiệm trộn font (§3): thân công thức một font, dấu tích phân một font khác.
        if (opts.Fonts.Length >= 2)
        {
            b.Heading("Thử trộn font: thân công thức và dấu tích phân khác font", 2);
            var doc = LatexParser.Parse(@"\int_0^1 \frac{x^2}{1+x^2}\,dx");
            for (int i = 0; i < opts.Fonts.Length; i++)
            {
                string main = opts.Fonts[i], integral = opts.Fonts[(i + 1) % opts.Fonts.Length];
                var omml = OmmlWriter.Write(doc, new OmmlOptions { Display = true, MathFont = main, IntegralFont = integral });
                b.Text($"[Thân: {main} · ∫: {integral}]", color: "1F4E79");
                b.DisplayMath(omml.Element);
            }
        }

        b.Save(outPath);
        var errors = DocxBuilder.Validate(outPath);
        Console.WriteLine($"Đã ghi {outPath} ({entries.Count} công thức, {problems} chẩn đoán).");
        foreach (var e in errors) Console.Error.WriteLine("OOXML: " + e);
        Console.WriteLine(errors.Count == 0 ? "Kiểm tra schema Office: hợp lệ." : $"Kiểm tra schema Office: {errors.Count} lỗi.");
        return errors.Count == 0 ? 0 : 1;
    }
}
