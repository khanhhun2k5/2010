using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using MathTypeX.Editing;
using MathTypeX.Fonts;
using MathTypeX.Parsing;
using MathTypeX.Render.MathMl;
using MathTypeX.Scanning;
using Microsoft.Web.WebView2.Core;

namespace MathTypeX.Editor;

/// <summary>
/// <c>MathTypeX.Editor.exe --self-test [báo-cáo.txt]</c>: kiểm tra bản đã publish (một tệp, self-contained) khởi động được
/// và đủ thành phần — .NET runtime, WPF, WebView2 (thư viện quản lý và WebView2Loader.dll gốc), parser, OMML, MathML —
/// mà không mở cửa sổ nào và không đụng tới pipe/mutex của bản đang chạy. CI chạy lệnh này trên gói tải về.
/// Mã thoát 0 = đạt, 1 = có mục FAIL. Báo cáo ghi ra tệp vì ứng dụng WinExe không có console.
/// </summary>
internal static class SelfTest
{
    public static int Run(string? reportPath)
    {
        var lines = new List<string>();
        bool ok = true;

        void Check(string name, Func<string> action)
        {
            try
            {
                lines.Add($"PASS {name}: {action()}");
            }
            catch (Exception ex)
            {
                ok = false;
                lines.Add($"FAIL {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        void Info(string name, Func<string> action)
        {
            try
            {
                lines.Add($"INFO {name}: {action()}");
            }
            catch (Exception ex)
            {
                lines.Add($"INFO {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Check("runtime", () => $"{RuntimeInformation.FrameworkDescription}, {RuntimeInformation.ProcessArchitecture}, "
            + $"exe={Environment.ProcessPath}, base={AppContext.BaseDirectory}");

        Check("compose", () =>
        {
            var outcome = EquationComposer.Compose(@"\int_0^1 \frac{x^2}{1+x^2}\,dx", new ComposeOptions { Display = true });
            if (outcome.Result is null) throw new InvalidOperationException(outcome.BlockingMessage);
            if (!outcome.Result.FlatOpc.Contains("m:nary", StringComparison.Ordinal)) throw new InvalidOperationException("OMML không có m:nary");
            return $"Flat OPC {outcome.Result.FlatOpc.Length} ký tự";
        });

        Check("mathml", () =>
        {
            string mathml = MathMlWriter.WriteString(LatexParser.Parse(@"\sqrt{x^2+1}").Body, new MathMlOptions());
            if (!mathml.Contains("msqrt", StringComparison.Ordinal)) throw new InvalidOperationException("MathML không có msqrt");
            return $"{mathml.Length} ký tự";
        });

        Check("scanner", () =>
        {
            int found = LatexScanner.Scan("Cho $x^2$ và $$y$$, giá $20 và $30.").Count;
            if (found != 2) throw new InvalidOperationException($"tìm thấy {found} công thức, cần 2");
            return "2 công thức";
        });

        Check("wpf", () =>
        {
            var family = new FontFamily("Cambria Math");
            return $"{typeof(Window).Assembly.GetName().Name} {typeof(Window).Assembly.GetName().Version}, font '{family.Source}'";
        });

        // Dựng (không hiện) cửa sổ editor: kiểm tra catalog, popup, palette, WebView2 control được khởi tạo không lỗi.
        Check("editor-window", () =>
        {
            var window = new EditorWindow(new UserSettings());
            return $"{window.GetType().Name} OK";
        });

        Check("webview2-managed", () => typeof(CoreWebView2Environment).Assembly.GetName().ToString());

        // Gọi vào WebView2Loader.dll: thiếu DLL gốc trong gói → DllNotFoundException (FAIL);
        // máy chưa cài WebView2 Runtime chỉ là thông tin (editor vẫn chạy, tắt preview).
        Check("webview2-loader", () =>
        {
            try
            {
                return "runtime " + (CoreWebView2Environment.GetAvailableBrowserVersionString() ?? "(không rõ)");
            }
            catch (WebView2RuntimeNotFoundException)
            {
                return "loader OK; máy này chưa cài WebView2 Runtime (preview sẽ tắt)";
            }
        });

        Info("math-fonts", () =>
        {
            var fonts = new FontScanner().ScanMathFonts();
            return $"{fonts.Count} font toán: {string.Join(", ", fonts.Select(f => f.Family).Distinct().Take(8))}";
        });

        lines.Add(ok ? "RESULT PASS" : "RESULT FAIL");
        string report = string.Join(Environment.NewLine, lines) + Environment.NewLine;
        string path = reportPath ?? Path.Combine(Path.GetTempPath(), "MathTypeX-self-test.txt");
        try
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, report);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return 2;
        }
        return ok ? 0 : 1;
    }
}
