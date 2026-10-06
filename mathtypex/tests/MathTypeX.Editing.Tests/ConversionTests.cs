using MathTypeX.Editing.Conversion;
using MathTypeX.Scanning;

namespace MathTypeX.Editing.Tests;

/// <summary>
/// Lập kế hoạch Convert Selection (docs/05 §9.3–9.4): tách đoạn cho display, gộp dấu câu, sửa ký tự AutoCorrect.
/// Mô phỏng thao tác của Word trên chuỗi để kiểm tra kết quả cuối cùng.
/// </summary>
public class ConversionTests
{
    /// <summary>Làm như add-in: từ cuối lên đầu, chèn dấu hết đoạn rồi thay đoạn bằng [I:latex] hoặc [D:latex].</summary>
    private static string Simulate(string text, int windowStart = 0, int? windowEnd = null)
    {
        var plan = ConversionPlanner.Plan(text, windowStart, windowEnd ?? text.Length);
        string result = text;
        foreach (var item in plan.Items.Reverse())
        {
            Assert.Equal(item.ExpectedText, result.Substring(item.ReplaceStart, item.ReplaceEnd - item.ReplaceStart));
            int rs = item.ReplaceStart, re = item.ReplaceEnd;
            if (item.BreakAfter) result = result.Insert(re, "\r");
            if (item.BreakBefore)
            {
                result = result.Insert(rs, "\r");
                rs++;
                re++;
            }
            result = result.Remove(rs, re - rs).Insert(rs, (item.Display ? "[D:" : "[I:") + item.Latex + "]");
        }
        return result;
    }

    [Theory]
    [InlineData("Cho $x^2$ và $y$.\r", "Cho [I:x^2] và [I:y].\r")]
    [InlineData("$$E=mc^2$$\r", "[D:E=mc^2]\r")]
    [InlineData("  $$E=mc^2$$  \r", "[D:E=mc^2]\r")]
    [InlineData("$$E=mc^2$$.\r", "[D:E=mc^2.]\r")]
    [InlineData("$$E=mc^2$$ .\r", "[D:E=mc^2.]\r")]
    [InlineData("Ta có $$E(X)=\\mu.$$\r", "Ta có\r[D:E(X)=\\mu.]\r")]
    [InlineData("$$a=b,$$ trong đó $a$ là số thực.\r", "[D:a=b,]\rtrong đó [I:a] là số thực.\r")]
    [InlineData("$$a=b$$, trong đó $a>0$.\r", "[D:a=b,]\rtrong đó [I:a>0].\r")]
    [InlineData("Ta có $$a$$ và $$b$$ nên\r", "Ta có\r[D:a]\rvà\r[D:b]\rnên\r")]
    [InlineData("$$a$$ $$b$$\r", "[D:a]\r[D:b]\r")]
    [InlineData("$$a$$$$b$$\r", "[D:a]\r[D:b]\r")]
    [InlineData("\\[a\\]\\(b\\)\r", "[D:a]\r[I:b]\r")]
    [InlineData("Với $X\\sim N(\\mu,\\sigma^2)$, ta có $$E(X)=\\mu.$$\r",
        "Với [I:X\\sim N(\\mu,\\sigma^2)], ta có\r[D:E(X)=\\mu.]\r")]
    [InlineData("Đoạn 1\rTa có $$x$$\rĐoạn 3\r", "Đoạn 1\rTa có\r[D:x]\rĐoạn 3\r")]
    [InlineData("$$a\r+b$$\r", "[D:a +b]\r")]
    [InlineData("ô 1 $$x$$ cuối\r\a", "ô 1\r[D:x]\rcuối\r\a")]
    [InlineData("\\begin{equation}a=b\\label{eq:1}\\end{equation}\r", "[D:a=b]\r")]
    [InlineData("\\begin{align*}a&=b\\\\c&=d\\end{align*}\r", "[D:\\begin{align*}a&=b\\\\c&=d\\end{align*}]\r")]
    [InlineData("Giá $20 và $30, còn $x$ thì không.\r", "Giá $20 và $30, còn [I:x] thì không.\r")]
    public void ProducesTheExpectedDocument(string text, string expected)
    {
        Assert.Equal(expected, Simulate(text));
    }

    [Fact]
    public void OnlyFormulasInsideTheSelectionAreConverted()
    {
        const string text = "$a$ rồi $b$ rồi $c$\r";
        int start = text.IndexOf("$b$", StringComparison.Ordinal);
        Assert.Equal("$a$ rồi [I:b] rồi $c$\r", Simulate(text, start, start + 3));
        // Chọn lẹm vào giữa công thức thì không chuyển công thức đó.
        Assert.Equal(text, Simulate(text, start + 1, text.Length - 5));
    }

    [Fact]
    public void LowConfidenceCandidatesAreReportedNotConverted()
    {
        const string text = "echo $HOME$ và $x$\r";
        var plan = ConversionPlanner.Plan(text, 0, text.Length);
        Assert.Equal("x", Assert.Single(plan.Items).Latex);
        Assert.Equal("HOME", Assert.Single(plan.LowConfidence).Latex);
    }

    [Fact]
    public void KeepsTheOriginalSourceForRevert()
    {
        var item = ConversionPlanner.Plan("Ta có $$x$$. Do đó\r", 0, 18).Items.Single();
        Assert.Equal("$$x$$", item.SourceText);
        Assert.Equal("x.", item.Latex);
        Assert.Contains(item.Notes, n => n.Contains("dấu câu"));
    }

    [Theory]
    [InlineData("f\u2019(x)", "f'(x)")]
    [InlineData("a \u2013 b", "a - b")]
    [InlineData("a\u00A0+\u00A0b", "a + b")]
    [InlineData("a\v\\\\\vb", "a \\\\ b")]
    [InlineData("x\u00ADy\u200B", "xy")]
    [InlineData("a\u001Eb", "a-b")]
    public void UndoesWordAutoCorrect(string input, string expected)
    {
        Assert.Equal(expected, ConversionPlanner.NormalizeWordText(input));
    }

    [Theory]
    [InlineData("a=b\\label{eq:1}", "a=b")]
    [InlineData("a=b \\tag{3.1}", "a=b ")]
    [InlineData("a=b\\tag*{A}", "a=b")]
    [InlineData("a=b\\nonumber", "a=b")]
    [InlineData("a=b\\notag", "a=b")]
    [InlineData("\\labelx", "\\labelx")]
    [InlineData("\\\\label{x}", "\\\\label{x}")]
    [InlineData("a\\label{eq:{nested}}b", "ab")]
    public void StripsEquationNumbering(string input, string expected)
    {
        Assert.Equal(expected, ConversionPlanner.StripNumbering(input, new List<string>()));
    }

    [Fact]
    public void EveryPlannedItemComposesToWordEquation()
    {
        const string text = "Cho $f(x)=\\frac{1}{x}$, ta có $$\\int_1^e f(x)\\,dx = 1.$$ Và \\(\\alpha\\).\r";
        var plan = ConversionPlanner.Plan(text, 0, text.Length);
        Assert.Equal(3, plan.Items.Count);
        foreach (var item in plan.Items)
        {
            var outcome = EquationComposer.Compose(item.Latex, new ComposeOptions { Display = item.Display });
            Assert.NotNull(outcome.Result);
            Assert.Equal(item.Display, outcome.Result!.Display);
            Assert.Contains(item.Display ? "oMathPara" : "oMath", outcome.Result.FlatOpc);
        }
    }

    [Fact]
    public void SettingsRoundTripAndDefaults()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mtx-settings-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "settings.json");
        try
        {
            // Tệp cũ (do bản trước ghi) thiếu các trường mới: phải nhận giá trị mặc định chứ không phải false/0.
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, "{\"MathFont\":\"XITS Math\",\"PreferDisplay\":true}");
            var old = UserSettings.Load(path);
            Assert.Equal("XITS Math", old.MathFont);
            Assert.True(old.PreferDisplay);
            Assert.True(old.BeginnerMode);
            Assert.Equal(50, old.ConvertMinConfidence);

            old.GrowIntegrals = true;
            old.BeginnerMode = false;
            old.Save(path);
            old.Save(path); // ghi đè lần hai qua tệp tạm
            var again = UserSettings.Load(path);
            Assert.True(again.GrowIntegrals);
            Assert.False(again.BeginnerMode);
            Assert.Equal("XITS Math", again.MathFont);

            File.WriteAllText(path, "{ hỏng");
            Assert.Equal("Cambria Math", UserSettings.Load(path).MathFont);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
