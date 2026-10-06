using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MathTypeX.Ast;
using MathTypeX.Editing;
using MathTypeX.Interop;
using MathTypeX.Parsing;

namespace MathTypeX.Editor;

/// <summary>
/// Editor nổi ngay dưới con trỏ (docs/README §40): một dòng tuỳ chọn, ô LaTeX, dòng trạng thái.
/// Enter: chèn · Shift+Enter: xuống dòng · Esc: đóng · Ctrl+Alt+M: inline/display.
/// Cửa sổ được dùng lại giữa các lần mở (mở nhanh), chỉ ẩn khi đóng.
/// </summary>
internal sealed class EditorWindow : Window
{
    private static readonly string[] CandidateFonts =
    {
        "Cambria Math", "XITS Math", "Latin Modern Math", "STIX Two Math",
        "TeX Gyre Termes Math", "TeX Gyre Pagella Math", "Libertinus Math", "Fira Math", "Asana Math",
    };

    private readonly EditorSettings _settings;
    private readonly TextBox _source;
    private readonly ComboBox _font;
    private readonly ToggleButton _displayToggle;
    private readonly TextBlock _status;
    private readonly TextBlock _notice;
    private readonly DispatcherTimer _debounce;
    private TaskCompletionSource<EditResult>? _pending;
    private EditRequest _request = new();
    private Action? _standaloneExit;

    public EditorWindow(EditorSettings settings)
    {
        _settings = settings;
        Title = "MathTypeX";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        BorderThickness = new Thickness(1);
        BorderBrush = SystemColors.ActiveBorderBrush;

        _displayToggle = new ToggleButton { Content = "Inline", MinWidth = 76, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Inline / Display (Ctrl+Alt+M)" };
        _displayToggle.Click += (_, _) => UpdateDisplayToggle();
        AutomationPropertiesHelper.SetName(_displayToggle, "Chế độ inline hoặc display");

        _font = new ComboBox { MinWidth = 190, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Font toán (Word Equation)" };
        foreach (var name in CandidateFonts)
        {
            bool installed = Fonts.SystemFontFamilies.Any(f => string.Equals(f.Source, name, StringComparison.OrdinalIgnoreCase));
            _font.Items.Add(new ComboBoxItem
            {
                Content = installed ? name : name + " — chưa cài",
                Tag = name,
                IsEnabled = installed,
                FontFamily = installed ? new FontFamily(name) : null,
            });
        }
        _font.SelectionChanged += (_, _) => ScheduleValidation();
        AutomationPropertiesHelper.SetName(_font, "Font toán");

        var hint = new TextBlock
        {
            Text = "Enter: chèn · Shift+Enter: xuống dòng · Esc: đóng",
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.65,
            FontSize = 11,
        };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        header.Children.Add(_displayToggle);
        header.Children.Add(_font);
        header.Children.Add(hint);

        _source = new TextBox
        {
            MinWidth = 520,
            MaxWidth = 900,
            MinHeight = 36,
            MaxHeight = 220,
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = 15,
        };
        // Lớp L0 (docs/06 §11.7): tắt bộ gõ TSF cho ô LaTeX để \cos không thành \có.
        InputMethod.SetIsInputMethodEnabled(_source, false);
        _source.TextChanged += (_, _) => ScheduleValidation();
        AutomationPropertiesHelper.SetName(_source, "Công thức LaTeX");

        _notice = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 900, Margin = new Thickness(0, 6, 0, 0), Opacity = 0.8, Visibility = Visibility.Collapsed };
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 900, Margin = new Thickness(0, 6, 0, 0), FontSize = 12 };

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(header);
        root.Children.Add(_source);
        root.Children.Add(_notice);
        root.Children.Add(_status);
        Content = root;

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Validate();
        };

        PreviewKeyDown += OnPreviewKeyDown;
        Closing += (_, e) =>
        {
            e.Cancel = true;
            Cancel();
        };
    }

    // ── Vòng đời một lần soạn ───────────────────────────────────────────

    public Task<EditResult> EditAsync(EditRequest request)
    {
        _pending?.TrySetResult(EditResult.Cancel());
        _pending = new TaskCompletionSource<EditResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _request = request;

        _displayToggle.IsEnabled = request.DisplayAllowed;
        _displayToggle.IsChecked = request.DisplayAllowed && (request.Display || _settings.PreferDisplay);
        UpdateDisplayToggle();
        SelectFont(request.PreferredMathFont ?? _settings.MathFont);

        _notice.Text = request.Notice ?? (request.DisplayAllowed ? "" : "Display chỉ dùng được khi con trỏ ở một đoạn trống (Word yêu cầu display equation đứng riêng một đoạn).");
        _notice.Visibility = string.IsNullOrEmpty(_notice.Text) ? Visibility.Collapsed : Visibility.Visible;

        _source.Text = request.Latex;
        Validate();
        ShowNear(request.Caret);
        return _pending.Task;
    }

    public void ShowStandalone(Action exit)
    {
        _standaloneExit = exit;
        _ = EditAsync(new EditRequest { Host = "Standalone", Notice = "Chế độ thử độc lập: Enter sẽ chép OMML vào clipboard." }).ContinueWith(t =>
        {
            if (t.Result is { Cancelled: false } r) Clipboard.SetText(r.FlatOpc);
            _standaloneExit?.Invoke();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private ComposeOptions CurrentOptions() => new()
    {
        MathFont = SelectedFont(),
        TextFont = _request.TextFont ?? "Times New Roman",
        Display = _displayToggle.IsChecked == true,
        FontSizePt = _request.FontSizePt,
        DecimalComma = _settings.DecimalComma,
        UprightDifferential = _settings.UprightDifferential,
        NarySizing = _settings.GrowIntegrals ? NarySizing.Grow : NarySizing.TeX,
    };

    private void Submit()
    {
        var outcome = EquationComposer.Compose(_source.Text, CurrentOptions());
        if (outcome.Result is null)
        {
            ShowStatus(outcome.BlockingMessage ?? "", isError: true);
            SystemSounds.Beep.Play();
            return;
        }

        _settings.MathFont = outcome.Result.MathFont;
        _settings.PreferDisplay = outcome.Result.Display;
        _settings.Save();
        Complete(outcome.Result);
    }

    private void Cancel() => Complete(EditResult.Cancel());

    private void Complete(EditResult result)
    {
        var pending = _pending;
        _pending = null;
        Hide();
        if (_request.OwnerWindow != 0) Win32.SetForegroundWindow(new IntPtr(_request.OwnerWindow));
        pending?.TrySetResult(result);
    }

    // ── Kiểm tra trực tiếp khi gõ ───────────────────────────────────────

    private void ScheduleValidation()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void Validate()
    {
        string text = _source.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowStatus("Gõ LaTeX, ví dụ \\int_0^1 \\frac{x^2}{1+x^2}\\,dx", isError: false);
            return;
        }
        var doc = EquationComposer.Analyze(text, CurrentOptions());
        var error = doc.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        if (error is not null)
        {
            ShowStatus(DiagnosticFormatter.Format(error, UiLanguage.Vi), isError: true);
            HighlightSpan(error.Span);
            return;
        }
        var warning = doc.Diagnostics.FirstOrDefault();
        ShowStatus(warning is not null
            ? "⚠ " + DiagnosticFormatter.Format(warning, UiLanguage.Vi)
            : "✓ " + LatexPrinter.Print(doc), isError: false);
    }

    private void ShowStatus(string text, bool isError)
    {
        _status.Text = text;
        _status.Foreground = isError ? Brushes.IndianRed : SystemColors.GrayTextBrush;
    }

    private void HighlightSpan(SourceSpan span)
    {
        // Chỉ báo vị trí bằng tooltip để không làm mất vị trí con trỏ khi đang gõ.
        _source.ToolTip = $"Lỗi tại ký tự {span.Start + 1}";
    }

    // ── Phím ─────────────────────────────────────────────────────────────

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
        else if (key == Key.Enter && (mods == ModifierKeys.None || mods == ModifierKeys.Control))
        {
            Submit();
            e.Handled = true;
        }
        else if (key == Key.M && mods == (ModifierKeys.Control | ModifierKeys.Alt) && _displayToggle.IsEnabled)
        {
            _displayToggle.IsChecked = _displayToggle.IsChecked != true;
            UpdateDisplayToggle();
            e.Handled = true;
        }
    }

    private void UpdateDisplayToggle()
    {
        _displayToggle.Content = _displayToggle.IsChecked == true ? "Display" : "Inline";
        ScheduleValidation();
    }

    private void SelectFont(string name)
    {
        foreach (ComboBoxItem item in _font.Items)
        {
            if ((string)item.Tag == name && item.IsEnabled)
            {
                _font.SelectedItem = item;
                return;
            }
        }
        _font.SelectedIndex = 0;
    }

    private string SelectedFont() => (_font.SelectedItem as ComboBoxItem)?.Tag as string ?? "Cambria Math";

    // ── Định vị ngay dưới con trỏ (pixel vật lý, đa màn hình, đa DPI) ──

    private void ShowNear(ScreenRect? caret)
    {
        Opacity = 0;
        Show();
        UpdateLayout();
        var hwnd = new WindowInteropHelper(this).Handle;
        Win32.GetWindowRect(hwnd, out var rect);
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;

        int x, y;
        if (caret is not null)
        {
            var work = Win32.WorkAreaAt(caret.Left, caret.Top + caret.Height);
            x = caret.Left;
            y = caret.Top + caret.Height + 6;
            if (y + height > work.Bottom) y = caret.Top - height - 6;
            x = Math.Max(work.Left, Math.Min(x, work.Right - width));
            y = Math.Max(work.Top, y);
        }
        else
        {
            var work = Win32.WorkAreaAt(0, 0);
            x = work.Left + (work.Right - work.Left - width) / 2;
            y = work.Top + (work.Bottom - work.Top - height) / 3;
        }

        Win32.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER);
        Opacity = 1;
        Activate();
        Win32.SetForegroundWindow(hwnd);
        _source.Focus();
        Keyboard.Focus(_source);
        _source.CaretIndex = _source.Text.Length;
    }
}

/// <summary>Nhãn cho trình đọc màn hình (§43).</summary>
internal static class AutomationPropertiesHelper
{
    public static void SetName(DependencyObject element, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(element, name);
}
