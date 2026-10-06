using MathTypeX.Editing.Assist;
using MathTypeX.Editing.Catalog;

namespace MathTypeX.Editing.Tests;

public class CatalogSearchTests
{
    [Theory]
    [InlineData("fra", "frac")]
    [InlineData("sq", "sqrt")]
    [InlineData("alp", "alpha")]
    [InlineData("vphi", "varphi")]
    [InlineData("lim", "lim")]
    public void TriggerPrefixFindsCommand(string prefix, string expected)
    {
        var hits = CatalogSearch.ByTrigger(prefix);
        Assert.Equal(expected, hits[0].Entry.Trigger);
    }

    [Fact]
    public void IntSuggestsAllIntegralForms()
    {
        var triggers = CatalogSearch.ByTrigger("int").Select(h => h.Entry.Trigger).ToArray();
        Assert.Contains("int", triggers);
        Assert.Contains("iint", triggers);
        Assert.Contains("iiint", triggers);
        Assert.Contains("oint", triggers);
    }

    [Fact]
    public void PhiFindsBothVariants()
    {
        var triggers = CatalogSearch.ByText("phi").Select(h => h.Entry.Trigger).Take(5).ToArray();
        Assert.Contains("phi", triggers);
        Assert.Contains("varphi", triggers);
    }

    [Theory]
    [InlineData("phân số", "cmd:frac")]
    [InlineData("phan so", "cmd:frac")]
    [InlineData("căn", "cmd:sqrt")]
    [InlineData("tích phân", "tpl:defint")]
    [InlineData("nguyên hàm", "cmd:int")]
    [InlineData("tich phan xac dinh", "tpl:defint")]
    [InlineData("giới hạn", "cmd:lim")]
    [InlineData("tổng", "cmd:sum")]
    [InlineData("ma trận", "tpl:pmatrix2")]
    [InlineData("vecto", "cmd:vec")]
    [InlineData("góc", "tpl:angle")]
    [InlineData("xác suất có điều kiện", "tpl:condprob")]
    [InlineData("fraction", "cmd:frac")]
    [InlineData("với mọi", "cmd:forall")]
    public void PaletteFindsVietnameseAndEnglishNames(string query, string expectedId)
    {
        var top = CatalogSearch.ByText(query).Take(3).Select(h => h.Entry.Id).ToArray();
        Assert.Contains(expectedId, top);
    }

    [Fact]
    public void FoldingRemovesVietnameseDiacritics()
    {
        Assert.Equal("tich phan duong", TextFolding.Fold("Tích Phân Đường"));
    }

    [Fact]
    public void UsageBoostsFrequentItems()
    {
        var usage = new UsageStats();
        for (int i = 0; i < 20; i++) usage.Record("cmd:iint");
        Assert.Equal("iint", CatalogSearch.ByTrigger("i", usage)[0].Entry.Trigger);
        Assert.Equal("cmd:iint", usage.Recent[0]);
    }

    [Fact]
    public void UsageStatsPersist()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mtx-usage-{Guid.NewGuid():N}.json");
        try
        {
            var usage = new UsageStats();
            usage.Record("cmd:frac");
            usage.Record("cmd:frac");
            usage.Save(path);
            var back = UsageStats.Load(path);
            Assert.Equal(usage.Boost("cmd:frac"), back.Boost("cmd:frac"));
            Assert.Equal("cmd:frac", back.Frequent[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EveryCatalogSnippetParsesWithoutErrorsOnceFilled()
    {
        foreach (var entry in CommandCatalog.All)
        {
            var expanded = SnippetParser.Expand(entry.Snippet);
            // Điền ô trống bằng "x" để kiểm tra cú pháp của mẫu.
            string text = expanded.Text;
            foreach (var stop in expanded.Stops.OrderByDescending(s => s.Start))
                text = text.Substring(0, stop.Start) + "x" + text.Substring(stop.End);
            var doc = Parsing.LatexParser.Parse(text);
            Assert.False(doc.HasErrors, $"{entry.Id}: {text}\n{string.Join("\n", doc.Diagnostics)}");
            if (entry.Example.Length > 0)
                Assert.False(Parsing.LatexParser.Parse(entry.Example).HasErrors, $"{entry.Id} ví dụ: {entry.Example}");
        }
    }

    [Fact]
    public void CatalogCoversAllLibraryGroups()
    {
        var categories = CommandCatalog.All.Select(e => e.Category).Distinct().ToArray();
        foreach (Category c in Enum.GetValues(typeof(Category)))
            if (c is not (Category.NumberTheory)) Assert.Contains(c, categories);
        Assert.True(CommandCatalog.All.Count > 300);
        Assert.Equal(CommandCatalog.All.Count, CommandCatalog.All.Select(e => e.Id).Distinct().Count());
    }
}

public class SnippetTests
{
    [Fact]
    public void ExpandsTabStopsAndExit()
    {
        var s = SnippetParser.Expand(@"\frac{$1}{$2}$0");
        Assert.Equal(@"\frac{}{}", s.Text);
        Assert.Equal(new[] { (1, 6, 6), (2, 8, 8) }, s.Stops);
        Assert.Equal(9, s.ExitOffset);
    }

    [Fact]
    public void PlaceholderWithDefaultText()
    {
        var s = SnippetParser.Expand(@"\int_0^1 ${1:f(x)}\,d${2:x}");
        Assert.Equal(@"\int_0^1 f(x)\,dx", s.Text);
        Assert.Equal((1, 9, 13), s.Stops[0]);
        Assert.Equal((2, 16, 17), s.Stops[1]);
    }

    /// <summary>§10: \int_{□}^{□} □\,d□ — Tab: cận dưới → cận trên → hàm → biến → ra ngoài.</summary>
    [Fact]
    public void IntegralTabOrderFollowsTheSpec()
    {
        var entry = CommandCatalog.All.Single(e => e.Id == "tpl:defint");
        var r = Completion.Accept(@"\int", 0, 4, entry);
        Assert.Equal(@"\int_{}^{}  \,d", r.Text.Replace(" \\,", "  \\,"));
        var session = r.Session!;
        string text = r.Text;

        string Type(string s, ref string t)
        {
            int at = session.Current.End;
            t = t.Insert(at, s);
            session.OnTextChanged(at, 0, s.Length);
            return t;
        }

        Type("0", ref text);
        session.Next();
        Type("1", ref text);
        session.Next();
        Type(@"\frac{x^2}{1+x^2}", ref text);
        session.Next();
        Type("x", ref text);
        var exit = session.Next();
        Assert.Equal(@"\int_{0}^{1} \frac{x^2}{1+x^2}\,dx", text);
        Assert.Equal(text.Length, exit.Start);
        Assert.False(session.IsActive);
    }

    [Fact]
    public void ShiftTabGoesBackAndDeletionShrinksTheStop()
    {
        var entry = CommandCatalog.All.Single(e => e.Id == "cmd:frac");
        var r = Completion.Accept(@"x+\fr", 2, 5, entry);
        Assert.Equal(@"x+\frac{}{}", r.Text);
        Assert.Equal((8, 0), (r.SelectionStart, r.SelectionLength));
        var s = r.Session!;
        s.OnTextChanged(8, 0, 2); // gõ "ab" vào tử
        s.OnTextChanged(9, 1, 0); // xoá "b"
        Assert.Equal((8, 9), (s.Current.Start, s.Current.End));
        var den = s.Next();
        Assert.Equal((11, 11), (den.Start, den.End));
        var back = s.Previous();
        Assert.Equal((8, 9), (back.Start, back.End));
        s.OnCaretMoved(0);
        Assert.False(s.IsActive);
    }

    [Theory]
    [InlineData(@"\fra", 4, 0, "fra")]
    [InlineData(@"x + \alp", 8, 4, "alp")]
    [InlineData(@"a \\ b", 4, null, null)]
    [InlineData(@"\\fra", 5, null, null)]
    [InlineData(@"frac", 4, null, null)]
    public void CommandPrefixDetection(string text, int caret, int? start, string? prefix)
    {
        var found = Completion.CommandPrefixAt(text, caret);
        Assert.Equal(start, found?.Start);
        Assert.Equal(prefix, found?.Prefix);
    }

    [Theory]
    [InlineData(@"\frac{}{}", 0, true, 6)]
    [InlineData(@"\frac{}{}", 6, true, 8)]
    [InlineData(@"\frac{}{}", 8, true, null)]
    [InlineData(@"\frac{}{}", 9, false, 8)]
    [InlineData(@"\frac{}{}", 8, false, 6)]
    [InlineData(@"\frac{}{}", 6, false, null)]
    [InlineData(@"\{} x^{}", 0, true, 7)] // \{ là ngoặc nhọn thật, không phải ô
    public void TabJumpsBetweenEmptyGroups(string text, int caret, bool forward, int? expected)
    {
        Assert.Equal(expected, Completion.NextEmptySlot(text, caret, forward));
    }

    [Fact]
    public void PlanReplacesOnlyThePrefix()
    {
        var entry = CommandCatalog.All.Single(e => e.Id == "cmd:frac");
        var plan = Completion.Plan(2, 5, entry);
        Assert.Equal((2, 3), (plan.ReplaceStart, plan.ReplaceLength));
        Assert.Equal(@"\frac{}{}", plan.InsertText);
        Assert.Equal((8, 0), (plan.SelectionStart, plan.SelectionLength));
        Assert.NotNull(plan.Session);

        var alpha = CommandCatalog.FindByTrigger("alpha")!;
        var simple = Completion.Plan(0, 3, alpha);
        Assert.Null(simple.Session);
        Assert.Equal(simple.InsertText.Length, simple.SelectionStart);
    }
}

public class ContextHelpTests
{
    [Theory]
    [InlineData(@"\frac{a}{b", "Phân số", "mẫu số")]
    [InlineData(@"\frac{a", "Phân số", "tử số")]
    [InlineData(@"\sqrt{x", "Căn bậc hai", "biểu thức dưới căn")]
    [InlineData(@"x + \frac{\sqrt{2}}{\sqrt{3", "Căn bậc hai", "biểu thức dưới căn")]
    public void HelpNamesTheArgumentBeingTyped(string text, string name, string argument)
    {
        var help = ContextHelp.At(text, text.Length)!;
        Assert.Equal(name, help.Entry.NameVi);
        Assert.Equal(argument, help.ArgumentName);
        Assert.Contains(argument, help.Describe());
    }

    [Fact]
    public void HelpAfterCommandName()
    {
        var help = ContextHelp.At(@"x+\frac", 7)!;
        Assert.Equal(-1, help.ArgumentIndex);
        Assert.Contains(@"\frac{tử số}{mẫu số}", help.Describe());
    }

    [Fact]
    public void NoHelpOutsideCommands()
    {
        Assert.Null(ContextHelp.At("x+y", 3));
    }
}
