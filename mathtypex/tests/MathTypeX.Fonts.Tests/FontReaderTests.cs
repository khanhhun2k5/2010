namespace MathTypeX.Fonts.Tests;

public class FontReaderTests
{
    [Fact]
    public void ReadsNamesMetricsAndMathTable()
    {
        var face = Assert.Single(OpenTypeReader.Read(SyntheticFont.Build()));
        Assert.Equal("Test Math", face.Family);
        Assert.Equal("Test Math Regular", face.FullName);
        Assert.Equal("Version 1.000", face.Version);
        Assert.True(face.IsCff);
        Assert.Equal(1000, face.UnitsPerEm);
        Assert.True(face.HasMathTable);
        Assert.Equal(1450, face.Math!.DisplayOperatorMinHeight);
        Assert.Equal(250, face.Math.AxisHeight);
        Assert.Equal(70, face.Math.ScriptPercentScaleDown);
    }

    [Fact]
    public void ReadsIntegralVariantsAndAssembly()
    {
        var integral = OpenTypeReader.Read(SyntheticFont.Build())[0].Math!.Integral!;
        Assert.Equal(3, integral.VariantCount);
        Assert.True(integral.HasAssembly);
        Assert.Equal(2200, integral.LargestVariantAdvance);
    }

    [Fact]
    public void CoverageFromCmapFormat4()
    {
        var face = OpenTypeReader.Read(SyntheticFont.Build())[0];
        Assert.True(face.Coverage.Contains('A'));
        Assert.True(face.Coverage.Contains('Z'));
        Assert.True(face.Coverage.Contains(0x222B));
        Assert.False(face.Coverage.Contains('a'));
        Assert.False(face.Coverage.Contains(0xFFFF));
        Assert.True(face.Coverage.Contains("AZ∫"));
        Assert.False(face.Coverage.Contains("Aα"));
    }

    [Fact]
    public void FontWithoutMathTable()
    {
        Assert.False(OpenTypeReader.Read(SyntheticFont.Build(withMath: false))[0].HasMathTable);
    }

    [Fact]
    public void EmbeddingRestrictionFromFsType()
    {
        Assert.True(OpenTypeReader.Read(SyntheticFont.Build(fsType: 2))[0].EmbeddingRestricted);
        Assert.False(OpenTypeReader.Read(SyntheticFont.Build(fsType: 8))[0].EmbeddingRestricted);
    }

    [Fact]
    public void TrueTypeCollectionWithTwoFaces()
    {
        var faces = OpenTypeReader.Read(SyntheticFont.Collection());
        Assert.Equal(2, faces.Count);
        Assert.Equal(("Collection Text", false), (faces[0].Family, faces[0].HasMathTable));
        Assert.Equal(("Collection Math", true), (faces[1].Family, faces[1].HasMathTable));
        Assert.Equal(1, faces[1].FaceIndex);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 })]
    public void GarbageIsRejectedCleanly(byte[] data)
    {
        Assert.Throws<InvalidFontException>(() => OpenTypeReader.Read(data));
    }

    [Fact]
    public void TruncatedFontIsRejectedCleanly()
    {
        var full = SyntheticFont.Build();
        for (int len = 12; len < full.Length; len += 7)
        {
            var cut = full.Take(len).ToArray();
            try
            {
                OpenTypeReader.Read(cut);
            }
            catch (InvalidFontException)
            {
                // mong đợi
            }
        }
    }

    [Fact]
    public void QuickScanAndCacheRoundTrip()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mtx-fonts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "math.otf"), SyntheticFont.Build("Alpha Math"));
            File.WriteAllBytes(Path.Combine(dir, "text.ttf"), SyntheticFont.Build("Plain Text", withMath: false, cff: false));
            File.WriteAllBytes(Path.Combine(dir, "both.ttc"), SyntheticFont.Collection());
            File.WriteAllText(Path.Combine(dir, "broken.otf"), "not a font");
            Assert.True(FontScanner.QuickHasMathTable(Path.Combine(dir, "math.otf")));
            Assert.False(FontScanner.QuickHasMathTable(Path.Combine(dir, "text.ttf")));

            string cachePath = Path.Combine(dir, "cache.json");
            var first = new FontScanner(FontCache.Load(cachePath)).ScanMathFonts(new[] { dir });
            Assert.Equal(new[] { "Alpha Math", "Collection Math" }, first.Select(f => f.Family));
            Assert.True(File.Exists(cachePath));

            var second = new FontScanner(FontCache.Load(cachePath)).ScanMathFonts(new[] { dir });
            Assert.Equal(first.Select(f => f.Family), second.Select(f => f.Family));
            Assert.Equal(3, second[0].Math!.Integral!.VariantCount);
            Assert.True(second[0].Coverage.Contains(0x222B));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DiagnosticsFindMissingGlyphsAndFallbacks()
    {
        var test = OpenTypeReader.Read(SyntheticFont.Build("Test Math"))[0];
        var other = OpenTypeReader.Read(SyntheticFont.Build("Other Math"))[0];
        var missing = FontDiagnostics.FindMissing(test, new[] { "A", "α", "∫" }, new[] { test, other });
        var m = Assert.Single(missing);
        Assert.Equal("α", m.Text);
        Assert.Empty(m.Fallbacks);
        Assert.Contains("3 cỡ", FontDiagnostics.DescribeIntegral(test));
    }
}
