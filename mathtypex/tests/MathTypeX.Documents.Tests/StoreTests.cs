namespace MathTypeX.Documents.Tests;

public class StoreTests
{
    // Chuỗi có nháy kép, &, < > và tiếng Việt: phải qua được JSON lồng trong XML.
    private const string Tricky = "\\text{v\u1EDBi \"m\u1ECDi\" \\& <x>}";

    [Fact]
    public void StoreRoundTripsThroughXml()
    {
        var store = new EquationStore();
        var a = EquationRecord.Create("k1:aaa", @"\frac12", @"\frac{1}{2}", false, "XITS Math");
        var b = EquationRecord.Create("k1:bbb", Tricky, @"\text{...}", true, "Cambria Math");
        store.Upsert(a);
        store.Upsert(b);
        string xml = store.ToXml();
        Assert.Contains(EquationStore.Namespace, xml);

        var back = EquationStore.Parse(xml);
        Assert.Equal(2, back.Records.Count);
        Assert.Equal(@"\frac12", back.FindByKey("k1:aaa")!.OriginalLatex);
        Assert.Equal(Tricky, back.FindByKey("k1:bbb")!.OriginalLatex);
        Assert.True(back.FindByKey("k1:bbb")!.Display);
    }

    [Fact]
    public void NewerRecordWinsForSameKey()
    {
        var store = new EquationStore();
        var old = EquationRecord.Create("k1:x", "x^2", "x^{2}", false, "Cambria Math");
        old.UpdatedUtc = "2026-01-01T00:00:00.0000000Z";
        var fresh = EquationRecord.Create("k1:x", "x^{2}", "x^{2}", false, "XITS Math");
        store.Upsert(fresh);
        store.Upsert(old);
        Assert.Equal("XITS Math", store.FindByKey("k1:x")!.MathFont);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<not-xml")]
    [InlineData("<other xmlns=\"urn:x\"/>")]
    [InlineData("<store xmlns=\"urn:mathtypex:equations:v1\"><eq key=\"k\">{bad json</eq></store>")]
    public void DamagedStoreIsTreatedAsEmpty(string? xml)
    {
        Assert.Empty(EquationStore.Parse(xml).Records);
    }

    [Fact]
    public void LocalStoreFindsLatestByKey()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mtx-local-{Guid.NewGuid():N}.jsonl");
        try
        {
            var local = new LocalEquationStore(path);
            Assert.Null(local.FindByKey("k1:a"));
            local.Save(EquationRecord.Create("k1:a", "first", "first", false, "F"));
            local.Save(EquationRecord.Create("k1:b", "other", "other", false, "F"));
            local.Save(EquationRecord.Create("k1:a", "second", "second", false, "F"));
            Assert.Equal("second", local.FindByKey("k1:a")!.OriginalLatex);
            local.Clear();
            Assert.Null(local.FindByKey("k1:a"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
