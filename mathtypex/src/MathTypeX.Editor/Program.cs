using System.IO;
using System.Windows;
using MathTypeX.Editing;
using MathTypeX.Fonts;

namespace MathTypeX.Editor;

internal static class Program
{
    /// <summary>
    /// <c>--server</c>: chạy nền chờ add-in gọi (mặc định khi Word khởi động editor).
    /// Không tham số: mở editor ở chế độ thử độc lập.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        bool server = args.Contains("--server");
        using var mutex = new Mutex(true, @"Local\MathTypeX.Editor.v1", out bool firstInstance);
        if (!firstInstance) return 0;

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.ThemeMode = ThemeMode.System;

        var settings = UserSettings.Load();
        var window = new EditorWindow(settings);
        new EditorHost(app.Dispatcher, window, settings).Start();

        // Quét font toán ở nền (có cache) rồi cập nhật danh sách trong editor.
        Task.Run(() =>
        {
            try
            {
                var fonts = new FontScanner(FontCache.Load()).ScanMathFonts();
                app.Dispatcher.InvokeAsync(() => window.SetFonts(fonts));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Không quét được thì giữ danh sách mặc định.
            }
        });

        app.Startup += async (_, _) =>
        {
            if (server) await window.PrewarmAsync();
            else window.ShowStandalone(() => app.Shutdown());
        };
        return app.Run();
    }
}
