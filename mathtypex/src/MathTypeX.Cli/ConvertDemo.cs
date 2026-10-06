using System.Xml.Linq;
using MathTypeX.Ast;
using MathTypeX.Editing;
using MathTypeX.Editing.Conversion;
using MathTypeX.OpenXml;
using MathTypeX.Render.Omml;
using MathTypeX.Scanning;

namespace MathTypeX.Cli;

/// <summary>
/// Demo VS-8 ngoài Word: chạy đúng scanner + planner + composer mà lệnh "Chuyển LaTeX" trong Word dùng,
/// rồi dựng .docx (mỗi dòng của tệp vào là một đoạn văn) để mở bằng Word/LibreOffice và kiểm tra schema.
/// </summary>
internal static class ConvertDemo
{
    public static int Scan(string text)
    {
        var found = LatexScanner.Scan(text.Replace("\\n", "\n"));
        foreach (var c in found)
        {
            Console.WriteLine($"{(c.Recommended ? "✓" : "·")} {c.Delimiter,-12} [{c.Start}..{c.End}) tin cậy {c.Confidence,3}  {c.Latex}");
            if (c.Reasons.Count > 0) Console.WriteLine($"{"",17}{string.Join(", ", c.Reasons)}");
        }
        Console.WriteLine(found.Count == 0 ? "Không thấy công thức." : $"{found.Count} ứng viên, {found.Count(c => c.Recommended)} sẽ được chuyển.");
        return 0;
    }

    public static int Convert(Options opts)
    {
        string input = opts.Positional ?? throw new ArgumentException("Thiếu tệp văn bản");
        string outPath = opts.Out ?? Path.ChangeExtension(input, ".docx");
        // Văn bản kiểu Word: mỗi đoạn kết thúc bằng \r.
        string text = string.Join("\r", File.ReadAllText(input).Replace("\r\n", "\n").TrimEnd('\n').Split('\n')) + "\r";
        var plan = ConversionPlanner.Plan(text, 0, text.Length);

        var builder = new DocxBuilder();
        var parts = new List<object>();
        int cursor = 0, converted = 0;

        void Flush()
        {
            builder.Mixed(parts);
            parts = new List<object>();
        }

        void AddText(string chunk)
        {
            var pieces = chunk.Split('\r');
            for (int k = 0; k < pieces.Length - 1; k++)
            {
                parts.Add(pieces[k]);
                Flush();
            }
            if (pieces[^1].Length > 0) parts.Add(pieces[^1]);
        }

        foreach (var item in plan.Items)
        {
            var outcome = EquationComposer.Compose(item.Latex, new ComposeOptions
            {
                Display = item.Display,
                MathFont = opts.Font,
                DecimalComma = opts.DecimalComma,
                UprightDifferential = opts.UprightD,
                NarySizing = opts.Grow ? NarySizing.Grow : NarySizing.TeX,
            }, UiLanguage.Vi, allowEmptySlots: true, stripDelimiters: false);
            if (outcome.Result is null)
            {
                Console.WriteLine($"✗ {item.SourceText} — {outcome.BlockingMessage}");
                continue;
            }

            var package = XDocument.Parse(outcome.Result.FlatOpc);
            var element = item.Display
                ? package.Descendants(OmmlWriter.M + "oMathPara").First()
                : package.Descendants(OmmlWriter.M + "oMath").First();

            AddText(text.Substring(cursor, item.ReplaceStart - cursor));
            if (item.Display)
            {
                // Planner đã quyết định: phần chữ trước (nếu có) ở đoạn riêng, công thức đứng một mình một đoạn.
                if (parts.Count > 0) Flush();
                builder.DisplayMath(element);
                cursor = !item.BreakAfter && item.ReplaceEnd < text.Length && text[item.ReplaceEnd] == '\r' ? item.ReplaceEnd + 1 : item.ReplaceEnd;
            }
            else
            {
                parts.Add(element);
                cursor = item.ReplaceEnd;
            }
            converted++;
            string notes = item.Notes.Count > 0 ? "  (" + string.Join("; ", item.Notes) + ")" : "";
            Console.WriteLine($"✓ {(item.Display ? "display" : "inline "),-7} {item.Latex}{notes}");
        }
        AddText(text.Substring(cursor));
        if (parts.Count > 0) Flush();

        foreach (var low in plan.LowConfidence)
            Console.WriteLine($"· bỏ qua {text.Substring(low.Start, low.Length)} — tin cậy {low.Confidence}: {string.Join(", ", low.Reasons)}");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        builder.Save(outPath, opts.Font);
        var errors = DocxBuilder.Validate(outPath);
        foreach (var e in errors) Console.WriteLine(e);
        Console.WriteLine($"Đã chuyển {converted} công thức → {outPath} ({(errors.Count == 0 ? "OOXML hợp lệ" : errors.Count + " lỗi schema")})");
        return errors.Count == 0 ? 0 : 1;
    }
}
