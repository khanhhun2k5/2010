using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MathTypeX.Editing;
using MathTypeX.Interop;

namespace MathTypeX.Editor;

/// <summary>
/// Hộp duyệt "Chuyển LaTeX trong tài liệu" (VS-9, docs/05 §9.4): danh sách ứng viên có ô tích, mục chắc chắn được tích sẵn,
/// mục chưa chắc hoặc bị khoá kèm lý do; xem trước công thức đang chọn; "luôn bỏ qua" ghi vào ignore list.
/// Phím: ↑/↓ chọn · Space tích/bỏ · Del luôn bỏ qua · Ctrl+A tích hết · Ctrl+Shift+A bỏ hết · Enter chuyển · Esc huỷ.
/// </summary>
internal sealed class ScanWindow : Window
{
    private sealed class Row
    {
        public required ScanItem Item { get; init; }
        public required CheckBox Check { get; init; }
        public required ListBoxItem Container { get; init; }
        public required TextBlock Source { get; init; }
        public bool Ignored { get; set; }
    }

    private readonly ScanRequest _request;
    private readonly UserSettings _settings;
    private readonly List<Row> _rows = new();
    private readonly ListBox _list = new() { Height = 300, MinWidth = 760 };
    private readonly TextBlock _summary = new() { Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _context = new() { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly TextBlock _diagnostic = new() { Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed };
    private readonly Button _convert = new() { MinWidth = 150, Padding = new Thickness(12, 4, 12, 4), IsDefault = true };
    private readonly PreviewPane _preview = new();
    private readonly TaskCompletionSource<ScanResult> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ScanWindow(ScanRequest request, UserSettings settings)
    {
        _request = request;
        _settings = settings;
        Title = "MathTypeX — Chuyển LaTeX trong tài liệu";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = request.OwnerWindow != 0 ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;

        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(_summary);
        root.Children.Add(_list);
        root.Children.Add(_context);
        root.Children.Add(_diagnostic);
        root.Children.Add(_preview);

        var hint = new TextBlock
        {
            Text = "↑/↓ chọn · Space tích/bỏ · Del luôn bỏ qua · Ctrl+A tích hết · Ctrl+Shift+A bỏ hết · Enter chuyển · Esc huỷ",
            Opacity = 0.65,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var cancel = new Button { Content = "Huỷ", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 4, 12, 4), IsCancel = true };
        _convert.Click += (_, _) => Finish(cancelled: false);
        cancel.Click += (_, _) => Finish(cancelled: true);
        var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
        DockPanel.SetDock(hint, Dock.Left);
        DockPanel.SetDock(cancel, Dock.Right);
        DockPanel.SetDock(_convert, Dock.Right);
        buttons.Children.Add(hint);
        buttons.Children.Add(cancel);
        buttons.Children.Add(_convert);
        root.Children.Add(buttons);
        Content = root;

        AutomationPropertiesHelper.SetName(_list, "Danh sách công thức tìm thấy");
        _list.SelectionChanged += (_, _) => ShowSelected();
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += (_, _) => _done.TrySetResult(new ScanResult { Cancelled = true });

        foreach (var item in request.Items) _rows.Add(CreateRow(item));
        UpdateSummary();
    }

    /// <summary>Mở hộp duyệt; kết thúc khi người dùng nhấn Chuyển, Huỷ hoặc đóng cửa sổ.</summary>
    public static async Task<ScanResult> ReviewAsync(ScanRequest request, UserSettings settings)
    {
        var window = new ScanWindow(request, settings);
        if (request.OwnerWindow != 0) new WindowInteropHelper(window).Owner = new IntPtr(request.OwnerWindow);
        window.Show();
        window.Activate();
        Win32.SetForegroundWindow(new WindowInteropHelper(window).Handle);
        if (window._rows.Count > 0) window._list.SelectedIndex = 0;
        window._list.Focus();
        _ = window._preview.InitializeAsync();
        var result = await window._done.Task;
        window.Close();
        return result;
    }

    private Row CreateRow(ScanItem item)
    {
        var check = new CheckBox
        {
            IsChecked = item.Recommended && item.Blocked is null,
            IsEnabled = item.Blocked is null,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Focusable = false,
        };
        var source = new TextBlock
        {
            Text = OneLine(item.Source, 70),
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            Width = 380,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var kind = new TextBlock { Text = item.Display ? "display" : "inline", Width = 56, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center };
        var location = new TextBlock { Text = item.Location, Width = 110, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        string note = item.Blocked ?? (item.Recommended ? "" : string.Join(", ", item.Reasons));
        var reason = new TextBlock
        {
            Text = note,
            Width = 200,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = item.Blocked is not null ? Brushes.IndianRed : SystemColors.GrayTextBrush,
            ToolTip = note.Length > 0 ? note : null,
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(check);
        panel.Children.Add(source);
        panel.Children.Add(kind);
        panel.Children.Add(location);
        panel.Children.Add(reason);

        var container = new ListBoxItem { Content = panel, Padding = new Thickness(4, 2, 4, 2) };
        System.Windows.Automation.AutomationProperties.SetName(container, $"{item.Source}, {(item.Recommended ? "đề xuất chuyển" : "chưa chắc")}{(note.Length > 0 ? ", " + note : "")}");
        var row = new Row { Item = item, Check = check, Container = container, Source = source };
        check.Click += (_, _) => UpdateSummary();
        container.MouseDoubleClick += (_, _) => Toggle(row);
        container.Tag = row;
        _list.Items.Add(container);
        return row;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        var current = (_list.SelectedItem as ListBoxItem)?.Tag as Row;
        switch (e.Key)
        {
            case Key.Space when current is not null:
                Toggle(current);
                e.Handled = true;
                break;
            case Key.Delete when current is not null:
                ToggleIgnore(current);
                e.Handled = true;
                break;
            case Key.A when mods == ModifierKeys.Control:
                SetAll(true);
                e.Handled = true;
                break;
            case Key.A when mods == (ModifierKeys.Control | ModifierKeys.Shift):
                SetAll(false);
                e.Handled = true;
                break;
            case Key.Enter:
                Finish(cancelled: false);
                e.Handled = true;
                break;
            case Key.Escape:
                Finish(cancelled: true);
                e.Handled = true;
                break;
        }
    }

    private void Toggle(Row row)
    {
        if (!row.Check.IsEnabled) return;
        row.Check.IsChecked = row.Check.IsChecked != true;
        UpdateSummary();
    }

    /// <summary>Del: luôn bỏ qua văn bản này (cả lần sau) — bỏ tích và gạch ngang.</summary>
    private void ToggleIgnore(Row row)
    {
        row.Ignored = !row.Ignored;
        row.Source.TextDecorations = row.Ignored ? TextDecorations.Strikethrough : null;
        if (row.Ignored) row.Check.IsChecked = false;
        row.Check.IsEnabled = !row.Ignored && row.Item.Blocked is null;
        UpdateSummary();
        ShowSelected();
    }

    private void SetAll(bool value)
    {
        foreach (var row in _rows.Where(r => r.Check.IsEnabled)) row.Check.IsChecked = value;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        int recommended = _rows.Count(r => r.Item.Recommended && r.Item.Blocked is null);
        int selected = _rows.Count(r => r.Check.IsChecked == true);
        _summary.Text = $"Tìm thấy {_rows.Count} công thức trong \"{_request.DocumentName}\". Đã tích sẵn {recommended} mục chắc chắn là công thức; "
            + "mục không tích là chưa chắc (xem lý do) hoặc không thể chuyển. Một lần Ctrl+Z trong Word hoàn tác toàn bộ.";
        _convert.Content = $"Chuyển {selected} công thức";
        _convert.IsEnabled = selected > 0;
    }

    private void ShowSelected()
    {
        if ((_list.SelectedItem as ListBoxItem)?.Tag is not Row row)
        {
            _context.Text = "";
            _diagnostic.Text = "";
            return;
        }
        _context.Text = row.Item.Context + (row.Ignored ? "   — sẽ luôn bỏ qua văn bản này" : "");
        var doc = EquationComposer.Analyze(row.Item.Latex, new ComposeOptions { DecimalComma = _settings.DecimalComma });
        var error = doc.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        _diagnostic.Text = error is null ? "" : "Lỗi: " + DiagnosticFormatter.Format(error, UiLanguage.Vi);
        _ = _preview.RenderAsync(doc.Body, row.Item.Display, _request.MathFont, "Times New Roman", 12, _settings.GrowIntegrals);
    }

    private void Finish(bool cancelled)
    {
        var ignored = _rows.Where(r => r.Ignored).Select(r => r.Item.Source).ToArray();
        if (!cancelled && ignored.Length > 0)
        {
            _settings.Ignore(ignored);
            _settings.Save();
        }
        _done.TrySetResult(new ScanResult
        {
            Cancelled = cancelled,
            SelectedIds = cancelled ? Array.Empty<int>() : _rows.Where(r => r.Check.IsChecked == true).Select(r => r.Item.Id).ToArray(),
            IgnoredSources = cancelled ? Array.Empty<string>() : ignored,
        });
    }

    private static string OneLine(string s, int max)
    {
        string line = s.Replace('\r', ' ').Replace('\n', ' ').Replace('\v', ' ');
        return line.Length <= max ? line : line.Substring(0, max - 1) + "…";
    }
}
