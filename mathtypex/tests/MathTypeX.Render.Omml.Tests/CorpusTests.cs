using System.Security.Cryptography;
using System.Text;
using MathTypeX.OpenXml;

namespace MathTypeX.Render.Omml.Tests;

/// <summary>Chạy toàn bộ corpus §50/§51: bất biến typography, golden snapshot, tài liệu Word hợp lệ.</summary>
public class CorpusTests
{
    public static TheoryData<string, string> AllCorpus()
    {
        var data = new TheoryData<string, string>();
        foreach (var file in new[] { "typography.tex", "integrals.tex" })
            foreach (var (_, latex) in O.Corpus(file))
                data.Add(file, latex);
        return data;
    }

    private static readonly char[] IntegralChars = "∫∬∭⨌∮∯∰∑∏".ToCharArray();

    /// <summary>Bất biến D9: ký hiệu ∫ ∑ ∏ không bao giờ là chữ thường trong m:t (luôn nằm trong m:naryPr/m:chr).</summary>
    [Theory]
    [MemberData(nameof(AllCorpus))]
    public void LargeOperatorsAreNeverPlainText(string file, string latex)
    {
        _ = file;
        foreach (bool display in new[] { false, true })
        {
            var x = O.X(latex, display);
            foreach (var t in x.Descendants(O.M + "t"))
                Assert.True(t.Value.IndexOfAny(IntegralChars) < 0, $"Ký hiệu large operator nằm trong m:t: \"{t.Value}\"");
        }
    }

    [Theory]
    [MemberData(nameof(AllCorpus))]
    public void CorpusParsesWithoutErrors(string file, string latex)
    {
        _ = file;
        var doc = LatexParser.Parse(latex);
        Assert.False(doc.HasErrors, string.Join("\n", doc.Diagnostics));
    }

    /// <summary>
    /// Golden snapshot: OMML sinh ra phải giống hệt bản đã duyệt trong tests/golden/omml.
    /// Đặt biến môi trường MTX_UPDATE_GOLDEN=1 để ghi lại khi thay đổi có chủ đích.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllCorpus))]
    public void GoldenSnapshot(string file, string latex)
    {
        string name = Path.GetFileNameWithoutExtension(file) + "-" + Slug(latex);
        string dir = Path.Combine(O.RepoRoot(), "tests", "golden", "omml");
        string path = Path.Combine(dir, name + ".xml");
        string actual = "<!-- " + latex.Replace("--", "- -") + " -->\n"
            + "<!-- inline -->\n" + O.X(latex).ToString() + "\n"
            + "<!-- display -->\n" + O.X(latex, display: true).ToString() + "\n";

        if (Environment.GetEnvironmentVariable("MTX_UPDATE_GOLDEN") == "1" || !File.Exists(path))
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, actual, new UTF8Encoding(false));
            if (Environment.GetEnvironmentVariable("MTX_UPDATE_GOLDEN") != "1")
                Assert.Fail($"Chưa có golden, đã tạo {path}. Kiểm tra rồi chạy lại.");
            return;
        }
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), actual);
    }

    private static string Slug(string latex)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(latex));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    /// <summary>Tài liệu Word chứa toàn bộ corpus × inline/display × các tuỳ chọn phải hợp lệ theo schema Office.</summary>
    [Fact]
    public void GeneratedDocumentIsValidOoxml()
    {
        var b = new DocxBuilder();
        b.Heading("Corpus");
        var variants = new[]
        {
            new OmmlOptions(),
            new OmmlOptions { MathFont = "XITS Math", IntegralFont = "Latin Modern Math", NarySizing = NarySizing.Grow, FontSizePt = 13 },
            new OmmlOptions { Alignment = AlignmentStrategy.EquationArray, UprightDifferential = true },
        };
        foreach (var file in new[] { "typography.tex", "integrals.tex" })
        {
            foreach (var (_, latex) in O.Corpus(file))
            {
                var doc = LatexParser.Parse(latex);
                foreach (var v in variants)
                {
                    b.InlineMath("Ta có ", OmmlWriter.Write(doc, v with { Display = false }).Element, ".");
                    b.DisplayMath(OmmlWriter.Write(doc, v with { Display = true }).Element);
                }
            }
        }
        string path = Path.Combine(Path.GetTempPath(), $"mtx-corpus-{Guid.NewGuid():N}.docx");
        try
        {
            b.Save(path);
            var errors = DocxBuilder.Validate(path);
            Assert.True(errors.Count == 0, string.Join("\n", errors.Take(20)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExtraStructuresAreValidOoxml()
    {
        string[] samples =
        {
            @"\boxed{x} \cancel{y} \bcancel{z} \xcancel{w}", @"\phantom{x}\hphantom{y}\vphantom{z}",
            @"\xrightarrow[a]{f} \overset{!}{=} \underset{x}{\max} \stackrel{def}{=}", @"\underbrace{x+y}_{n}",
            @"\begin{gather} a=b \\ c=d \end{gather}", @"\begin{array}{lcr} a & b & c \\ d & e & f \end{array}",
            @"\begin{vmatrix} 1 & 2 \\ 3 & 4 \end{vmatrix} \begin{Vmatrix} x \end{Vmatrix} \begin{Bmatrix} x \end{Bmatrix}",
            @"\operatorname*{arg\,max}_x f(x)", @"\sin^2 x + \cos^2 x = 1", @"\alpah + \frac{a}{", @"x \quad y \qquad z \; w \: v \! u ~ t \ s",
            @"{}^{14}_{6}C", @"\left\langle x \right\rangle \left\lfloor x \right\rfloor", @"\iiint_V \nabla\cdot\vec F\,dV = \oiint_S \vec F\cdot d\vec S",
            @"\begin{rcases} a \\ b \end{rcases}", @"\displaystyle\sum_{n} x_n", @"3{,}14", @"\text{có $x$ và \textbf{đậm}}",
        };
        var b = new DocxBuilder();
        foreach (var s in samples)
        {
            var doc = LatexParser.Parse(s);
            b.InlineMath("", OmmlWriter.Write(doc).Element);
            b.DisplayMath(OmmlWriter.Write(doc, new OmmlOptions { Display = true }).Element);
        }
        string path = Path.Combine(Path.GetTempPath(), $"mtx-extra-{Guid.NewGuid():N}.docx");
        try
        {
            b.Save(path);
            var errors = DocxBuilder.Validate(path);
            Assert.True(errors.Count == 0, string.Join("\n", errors.Take(20)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
