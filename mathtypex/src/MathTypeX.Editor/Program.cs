using System.Windows;

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

        var settings = EditorSettings.Load();
        var window = new EditorWindow(settings);
        new EditorHost(app.Dispatcher, window).Start();

        if (!server)
        {
            app.Startup += (_, _) => window.ShowStandalone(() => app.Shutdown());
        }
        return app.Run();
    }
}
