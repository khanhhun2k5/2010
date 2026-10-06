using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MathTypeX.Ast;
using MathTypeX.Render.MathMl;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MathTypeX.Editor;

/// <summary>
/// Preview MathML Core trong WebView2 (docs/02 §4.2, D4): Chromium dựng công thức bằng bảng OpenType MATH
/// của chính font đã chọn. Trang chỉ nạp một lần; mỗi lần gõ chỉ gọi <c>mtxRender</c> (vài ms).
/// Nếu máy không có WebView2 Runtime, preview tự ẩn và editor vẫn dùng được.
/// </summary>
internal sealed class PreviewPane : Border
{
    private readonly WebView2 _web = new() { Height = 48, MinWidth = 520 };
    private bool _ready;
    private bool _failed;
    private (MathNode Body, bool Display, string MathFont, string TextFont, double SizePt, bool Grow)? _pending;

    public PreviewPane()
    {
        BorderThickness = new Thickness(1);
        BorderBrush = SystemColors.ActiveBorderBrush;
        Margin = new Thickness(0, 8, 0, 0);
        Child = _web;
        Visibility = Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(_web, "Xem trước công thức");
    }

    public string? FailureReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            string userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MathTypeX", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(null, userData);
            await _web.EnsureCoreWebView2Async(environment);
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            var loaded = new TaskCompletionSource<bool>();
            _web.CoreWebView2.NavigationCompleted += (_, e) => loaded.TrySetResult(e.IsSuccess);
            _web.CoreWebView2.NavigateToString(PreviewPage.LivePage());
            await loaded.Task;
            _ready = true;
            Visibility = Visibility.Visible;
            if (_pending is { } p) await RenderAsync(p.Body, p.Display, p.MathFont, p.TextFont, p.SizePt, p.Grow);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or COMException or IOException)
        {
            _failed = true;
            FailureReason = "Không có WebView2 Runtime nên tắt xem trước. Cài tại https://go.microsoft.com/fwlink/p/?LinkId=2124703";
            Visibility = Visibility.Collapsed;
        }
    }

    public async Task RenderAsync(MathNode body, bool display, string mathFont, string textFont, double sizePt, bool grow)
    {
        if (_failed) return;
        if (!_ready)
        {
            _pending = (body, display, mathFont, textFont, sizePt, grow);
            return;
        }
        string mathml = MathMlWriter.WriteString(body, new MathMlOptions { Display = display, GrowLargeOperators = grow });
        double sizePx = Math.Max(sizePt, 12) * 96 / 72 * 1.5;
        string script = $"mtxRender({JsonSerializer.Serialize(mathml)}, {JsonSerializer.Serialize(mathFont)}, null, {JsonSerializer.Serialize(textFont)}, {sizePx.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
        try
        {
            string result = await _web.ExecuteScriptAsync(script);
            if (double.TryParse(result, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double height))
                _web.Height = Math.Min(Math.Max(height + 4, 40), 420);
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            // WebView2 bị đóng giữa chừng — bỏ qua lần vẽ này.
        }
    }
}
