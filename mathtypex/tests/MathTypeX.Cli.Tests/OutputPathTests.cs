using MathTypeX.OpenXml;

namespace MathTypeX.Cli.Tests;

[CollectionDefinition(nameof(ConsoleCollection), DisableParallelization = true)]
public sealed class ConsoleCollection;

/// <summary>
/// Hồi quy cho lỗi CI "Could not find a part of the path …/out/integrals-demo.docx": mọi lệnh có <c>--out</c>
/// phải tự tạo thư mục cha (kể cả lồng nhiều cấp), báo lỗi rõ ràng với mã thoát 2 khi đầu vào sai,
/// và chỉ báo thành công khi tệp ra thật sự tồn tại và khác rỗng.
/// Gọi thẳng <see cref="global::Cli.Run"/> trong tiến trình; lớp này đổi Console/thư mục hiện hành nên không chạy song song.
/// </summary>
[Collection(nameof(ConsoleCollection))]
public sealed class OutputPathTests : IDisposable
{
    private static readonly string RepoRoot = FindRepoRoot();
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "mtx-cli-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MathTypeX.slnx"))) return dir.FullName;
        }
        throw new InvalidOperationException("Không tìm thấy thư mục gốc chứa MathTypeX.slnx");
    }

    private static string Corpus(string name) => Path.Combine(RepoRoot, "tests", "corpus", name);

    private static (int Exit, string Out, string Err) Run(params string[] args)
    {
        var (oldOut, oldErr) = (Console.Out, Console.Error);
        var (stdout, stderr) = (new StringWriter(), new StringWriter());
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            return (global::Cli.Run(args), stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(oldOut);
            Console.SetError(oldErr);
        }
    }

    /// <summary>Đúng các lệnh demo mà GitHub Actions chạy, nhưng --out trỏ vào nested/a/b chưa tồn tại.</summary>
    public static TheoryData<string, string[], string> DemoCommands() => new()
    {
        { "docx", new[] { "docx", Corpus("integrals.tex") }, "result.docx" },
        { "docx-sizing", new[] { "docx", Corpus("typography.tex"), "--sizing", "TeX" }, "result.docx" },
        { "preview", new[] { "preview", Corpus("integrals.tex"), "--display", "--fonts", "Latin Modern Math,TeX Gyre Termes Math" }, "result.html" },
        { "convert", new[] { "convert", Corpus("convert-sample.txt") }, "result.docx" },
    };

    [Theory]
    [MemberData(nameof(DemoCommands))]
    public void NestedOutputDirectoryIsCreated(string name, string[] command, string fileName)
    {
        string nested = Path.Combine(_temp, name, "nested", "a", "b");
        Assert.False(Directory.Exists(nested));
        string output = Path.Combine(nested, fileName);

        var (exit, stdout, stderr) = Run(command.Concat(new[] { "--out", output }).ToArray());

        Assert.True(exit == 0, $"exit {exit}\n{stdout}\n{stderr}");
        Assert.True(File.Exists(output), output);
        Assert.True(new FileInfo(output).Length > 0, "tệp ra rỗng");
        if (fileName.EndsWith(".docx", StringComparison.Ordinal)) Assert.Empty(DocxBuilder.Validate(output));
        else Assert.Contains("<math", File.ReadAllText(output));
    }

    /// <summary>Dạng đường dẫn tương đối như trong workflow: "--out out/…" khi thư mục out/ chưa có.</summary>
    [Fact]
    public void RelativeNestedOutputMatchesTheCiInvocation()
    {
        Directory.CreateDirectory(_temp);
        string previous = Environment.CurrentDirectory;
        Environment.CurrentDirectory = _temp;
        try
        {
            var (exit, stdout, stderr) = Run("docx", Corpus("integrals.tex"), "--out", "out/nested/a/b/result.docx");
            Assert.True(exit == 0, $"exit {exit}\n{stdout}\n{stderr}");
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
        string output = Path.Combine(_temp, "out", "nested", "a", "b", "result.docx");
        Assert.True(new FileInfo(output).Length > 0);
        Assert.Empty(DocxBuilder.Validate(output));
    }

    [Theory]
    [InlineData("docx")]
    [InlineData("preview")]
    [InlineData("convert")]
    [InlineData("validate")]
    public void MissingInputFileIsAClearErrorAndWritesNothing(string command)
    {
        string missing = Path.Combine(_temp, "khong-co.tex");
        string output = Path.Combine(_temp, "nested", "a", "b", "result.docx");

        var (exit, _, stderr) = Run(command, missing, "--out", output);

        Assert.Equal(2, exit);
        Assert.Contains("Không tìm thấy", stderr);
        Assert.Contains("khong-co.tex", stderr);
        Assert.False(Directory.Exists(Path.Combine(_temp, "nested")), "không được tạo thư mục ra khi đầu vào sai");
    }

    [Theory]
    [InlineData("docx")]
    [InlineData("preview")]
    [InlineData("convert")]
    public void MissingInputArgumentIsAClearError(string command)
    {
        var (exit, _, stderr) = Run(command, "--out", Path.Combine(_temp, "x.out"));
        Assert.Equal(2, exit);
        Assert.Contains("Thiếu", stderr);
    }

    [Fact]
    public void OutputPointingAtADirectoryIsRejected()
    {
        Directory.CreateDirectory(_temp);
        var (exit, _, stderr) = Run("docx", Corpus("integrals.tex"), "--out", _temp);
        Assert.Equal(2, exit);
        Assert.Contains("không phải thư mục", stderr);
    }

    [Fact]
    public void ValidatingANonDocxFileDoesNotCrash()
    {
        var (exit, _, stderr) = Run("validate", Corpus("integrals.tex"));
        Assert.Equal(2, exit);
        Assert.StartsWith("Lỗi:", stderr);
    }

    [Fact]
    public void DocxBuilderCreatesMissingDirectoriesItself()
    {
        string output = Path.Combine(_temp, "lib", "nested", "a", "b", "builder.docx");
        new DocxBuilder().Text("MathTypeX").Save(output);
        Assert.True(new FileInfo(output).Length > 0);
        Assert.Empty(DocxBuilder.Validate(output));
    }
}
