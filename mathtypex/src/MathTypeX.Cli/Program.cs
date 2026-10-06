using System.Text;
using MathTypeX;
using MathTypeX.Ast;
using MathTypeX.Cli;
using MathTypeX.Fonts;
using MathTypeX.OpenXml;
using MathTypeX.Parsing;
using MathTypeX.Render.MathMl;
using MathTypeX.Render.Omml;

Console.OutputEncoding = Encoding.UTF8;
return Cli.Run(args);

internal static class Cli
{
    private const string Usage = """
        mtx — công cụ dòng lệnh của MathTypeX

          mtx parse    "<latex>"            In cây AST và chẩn đoán (tiếng Việt)
          mtx latex    "<latex>"            In LaTeX chuẩn hoá
          mtx omml     "<latex>" [tuỳ chọn] In OMML (Word Equation)
          mtx flatopc  "<latex>" [tuỳ chọn] In gói Flat OPC dùng cho Range.InsertXML
          mtx mathml   "<latex>" [--display] In MathML Core (preview)
          mtx preview  <corpus.tex> --out <file.html> [--fonts "A,B"]
                                            Trang HTML so sánh font (mở bằng trình duyệt Chromium/Edge)
          mtx docx     <corpus.tex> --out <file.docx> [tuỳ chọn]
                                            Sinh tài liệu Word thử font × chế độ × cỡ ∫ (spike S2)
          mtx validate <file.docx>          Kiểm tra tài liệu theo schema Office
          mtx fonts                         Liệt kê font OpenType MATH đã cài (và đặc điểm dấu ∫)

        Tuỳ chọn:
          --display                 Display equation (mặc định: inline)
          --font "<tên>"            Font toán (mặc định: Cambria Math)
          --integral-font "<tên>"   Font riêng cho dấu tích phân
          --grow                    ∫ ∑ ∏ co giãn theo nội dung (mặc định: kiểu TeX)
          --upright-d               Vi phân d đứng (ISO)
          --decimal-comma           3,14 là một số
          --size <pt>               Cỡ chữ
          --align eqarr|matrix      Cách biểu diễn aligned (mặc định matrix)
          --fonts "A,B,C"           (docx) danh sách font cần so sánh
          --modes inline,display    (docx)
          --sizing TeX,Grow         (docx)
        """;

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        var opts = Options.Parse(args.Skip(1).ToArray());
        try
        {
            switch (args[0])
            {
                case "parse":
                {
                    var doc = Parse(opts);
                    Console.Write(AstDump.Dump(doc.Body));
                    PrintDiagnostics(doc);
                    return doc.HasErrors ? 1 : 0;
                }
                case "latex":
                {
                    var doc = Parse(opts);
                    Console.WriteLine(LatexPrinter.Print(doc));
                    PrintDiagnostics(doc);
                    return doc.HasErrors ? 1 : 0;
                }
                case "omml":
                case "flatopc":
                {
                    var doc = Parse(opts);
                    var result = OmmlWriter.Write(doc, opts.ToOmml());
                    Console.WriteLine(args[0] == "omml"
                        ? result.Element.ToString()
                        : FlatOpc.ForMath(result.Element));
                    foreach (var a in result.Approximations) Console.Error.WriteLine($"~ gần đúng: {a}");
                    PrintDiagnostics(doc);
                    return doc.HasErrors ? 1 : 0;
                }
                case "mathml":
                {
                    var doc = Parse(opts);
                    Console.WriteLine(MathMlWriter.Write(doc.Body, new MathMlOptions { Display = opts.Display, GrowLargeOperators = opts.Grow }));
                    PrintDiagnostics(doc);
                    return doc.HasErrors ? 1 : 0;
                }
                case "preview":
                {
                    string corpus = opts.Positional ?? throw new ArgumentException("Thiếu đường dẫn corpus .tex");
                    var samples = Corpus.Load(corpus).Select(e =>
                    {
                        var d = LatexParser.Parse(e.Latex, new ParserOptions { DecimalComma = opts.DecimalComma });
                        return new PreviewPage.Sample(e.Section, e.Latex, MathMlWriter.WriteString(d.Body, new MathMlOptions { Display = opts.Display, GrowLargeOperators = opts.Grow }));
                    }).ToArray();
                    string outPath = opts.Out ?? Path.ChangeExtension(Path.GetFileName(corpus), ".html");
                    File.WriteAllText(outPath, PreviewPage.ComparePage(opts.Fonts, samples, "MathTypeX — so sánh font: " + Path.GetFileName(corpus)));
                    Console.WriteLine($"Đã ghi {outPath}");
                    return 0;
                }
                case "docx":
                    return DemoDocument.Build(opts);
                case "fonts":
                {
                    var fonts = new FontScanner(FontCache.Load()).ScanMathFonts();
                    foreach (var f in fonts)
                        Console.WriteLine($"{f.Family,-26} {(f.IsCff ? "CFF" : "TrueType"),-8} {FontDiagnostics.DescribeIntegral(f)}\n{"",-26} {f.Path}");
                    Console.WriteLine($"{fonts.Count} font toán.");
                    return 0;
                }
                case "validate":
                {
                    var errors = DocxBuilder.Validate(opts.Positional ?? throw new ArgumentException("Thiếu đường dẫn .docx"));
                    foreach (var e in errors) Console.WriteLine(e);
                    Console.WriteLine(errors.Count == 0 ? "Hợp lệ." : $"{errors.Count} lỗi.");
                    return errors.Count == 0 ? 0 : 1;
                }
                default:
                    Console.Error.WriteLine($"Lệnh không hợp lệ: {args[0]}\n");
                    Console.WriteLine(Usage);
                    return 2;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            Console.Error.WriteLine("Lỗi: " + ex.Message);
            return 2;
        }
    }

    private static MathDocument Parse(Options opts) =>
        LatexParser.Parse(opts.Positional ?? throw new ArgumentException("Thiếu công thức LaTeX"), new ParserOptions { DecimalComma = opts.DecimalComma });

    private static void PrintDiagnostics(MathDocument doc)
    {
        foreach (var d in doc.Diagnostics)
            Console.Error.WriteLine($"{d.Severity} {d.Span}: {DiagnosticFormatter.Format(d, UiLanguage.Vi)}");
    }
}

internal sealed class Options
{
    public string? Positional { get; private set; }
    public bool Display { get; private set; }
    public string Font { get; private set; } = "Cambria Math";
    public string? IntegralFont { get; private set; }
    public bool Grow { get; private set; }
    public bool UprightD { get; private set; }
    public bool DecimalComma { get; private set; }
    public double? Size { get; private set; }
    public AlignmentStrategy Align { get; private set; } = AlignmentStrategy.Matrix;
    public string? Out { get; private set; }
    public string[] Fonts { get; private set; } = { "Cambria Math", "XITS Math", "Latin Modern Math", "STIX Two Math" };
    public string[] Modes { get; private set; } = { "inline", "display" };
    public string[] Sizing { get; private set; } = { "TeX", "Grow" };

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Thiếu giá trị cho {args[i]}");
            static string[] List(string s) => s.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            switch (args[i])
            {
                case "--display": o.Display = true; break;
                case "--font": o.Font = Next(); break;
                case "--integral-font": o.IntegralFont = Next(); break;
                case "--grow": o.Grow = true; break;
                case "--upright-d": o.UprightD = true; break;
                case "--decimal-comma": o.DecimalComma = true; break;
                case "--size": o.Size = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--align": o.Align = Next() == "eqarr" ? AlignmentStrategy.EquationArray : AlignmentStrategy.Matrix; break;
                case "--out": o.Out = Next(); break;
                case "--fonts": o.Fonts = List(Next()); break;
                case "--modes": o.Modes = List(Next()); break;
                case "--sizing": o.Sizing = List(Next()); break;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Tuỳ chọn không hợp lệ: {args[i]}");
                    o.Positional = args[i];
                    break;
            }
        }
        return o;
    }

    public OmmlOptions ToOmml() => new()
    {
        Display = Display,
        MathFont = Font,
        IntegralFont = IntegralFont,
        NarySizing = Grow ? NarySizing.Grow : NarySizing.TeX,
        UprightDifferential = UprightD,
        FontSizePt = Size,
        Alignment = Align,
    };
}
