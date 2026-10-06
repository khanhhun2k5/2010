namespace MathTypeX.Fonts.Tests;

/// <summary>Kiểm tra với font OpenType MATH thật nếu máy có (bỏ qua nếu không).</summary>
public class SystemFontTests
{
    private static string? Find(string fileName) =>
        FontScanner.DefaultDirectories()
            .SelectMany(d =>
            {
                try { return Directory.EnumerateFiles(d, fileName, SearchOption.AllDirectories); }
                catch (Exception) { return Array.Empty<string>(); }
            })
            .FirstOrDefault();

    [Theory]
    [InlineData("latinmodern-math.otf", "Latin Modern Math")]
    [InlineData("texgyretermes-math.otf", "TeX Gyre Termes Math")]
    [InlineData("STIXMath-Regular.otf", "STIX Math")]
    [InlineData("XITSMath-Regular.otf", "XITS Math")]
    [InlineData("STIXTwoMath-Regular.otf", "STIX Two Math")]
    public void RealMathFont(string fileName, string family)
    {
        string? path = Find(fileName);
        if (path is null) Assert.Skip($"Không có {fileName} trên máy này.");
        var face = Assert.Single(OpenTypeReader.ReadFile(path!));
        Assert.Equal(family, face.Family);
        Assert.True(face.HasMathTable);
        Assert.True(face.Math!.DisplayOperatorMinHeight > 0);
        Assert.NotNull(face.Math.Integral);
        Assert.True(face.Math.Integral!.VariantCount >= 2, FontDiagnostics.DescribeIntegral(face));
        foreach (string s in new[] { "∫", "α", "ℝ", "𝔤", "𝓕", "∑", "≤" })
            Assert.True(face.Coverage.Contains(s), $"{family} thiếu {s}");
    }

    [Fact]
    public void ScanFindsInstalledMathFonts()
    {
        var fonts = new FontScanner().ScanMathFonts();
        if (fonts.Count == 0) Assert.Skip("Máy không có font OpenType MATH nào.");
        Assert.All(fonts, f => Assert.True(f.HasMathTable));
    }
}
