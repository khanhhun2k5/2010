using System.IO;
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
using MathTypeX.Editing.Assist;
using MathTypeX.Editing.Catalog;
using MathTypeX.Fonts;
using MathTypeX.Interop;
using MathTypeX.Parsing;

namespace MathTypeX.Editor;

/// <summary>
/// Editor nổi ngay dưới con trỏ (docs/README §40): một dòng tuỳ chọn, ô LaTeX, dòng trạng thái.
/// Enter: chèn · Shift+Enter: xuống dòng · Esc: đóng · Ctrl+Alt+M: inline/display ·
/// Tab/Shift+Tab: ô kế/ô trước · Ctrl+Shift+P: tìm lệnh · Ctrl+Shift+Enter: chèn dù còn ô trống · F1: trợ giúp.
/// Cửa sổ được dùng lại giữa các lần mở (mở nhanh), chỉ ẩn khi đóng.
/// </summary>
internal sealed class EditorWindow : Window
{
    /// <summary>Thứ tự ưu tiên hiển thị; các font toán khác (quét được) xếp sau.</summary>
    private static readonly string[] PreferredFonts =
    {
        "Cambria Math", "XITS Math", "STIX Two Math", "Latin Modern Math", "New Computer Modern Math",
        "TeX Gyre Termes Math", "TeX Gyre Pagella Math", "TeX Gyre Schola Math", "TeX Gyre Bonum Math",
        "Libertinus Math", "Fira Math", "Asana Math", "STIX Math",
    };

    private readonly UserSettings _settings;
    private readonly TextBox _source;
    private readonly ComboBox _font;
    private readonly ToggleButton _displayToggle;
    private readonly TextBlock _status;
    private readonly TextBlock _notice;
    private readonly TextBlock _help;
    private readonly PreviewPane _preview = new();
    private readonly DispatcherTimer _debounce;
    private readonly CompletionPopup _completion;
    private readonly CommandPalette _palette = new();
    private readonly UsageStats _usage;
    /// <summary>Các phiên Tab của snippet đang mở, lồng nhau (snippet trong snippet), phiên trong cùng ở cuối.</summary>
    private readonly List<SnippetSession> _snippets = new();
    /// <summary>Đang thay văn bản bằng code (chấp nhận gợi ý, nạp công thức) — bỏ qua theo dõi gõ phím.</summary>
    private bool _applying;
    private bool _afterEditQueued;
    private bool _typedSinceAfterEdit;

    private static string UsagePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MathTypeX", "usage.json");
    private TaskCompletionSource<EditResult>? _pending;
    private EditRequest _request = new();
    private Action? _standaloneExit;

    public EditorWindow(UserSettings settings)
    {
        _settings = settings;
        _usage = UsageStats.Load(UsagePath);
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
        // Trước khi quét xong: chỉ có Cambria Math (luôn có trên Windows/Office) và font đã chọn lần trước.
        SetFonts(new[] { "Cambria Math", settings.MathFont }.Distinct().Select(n => (n, (string?)null)).ToArray());
        _font.SelectionChanged += (_, _) => ScheduleValidation();
        AutomationPropertiesHelper.SetName(_font, "Font toán");

        var hint = new TextBlock
        {
            Text = "Enter: chèn · Tab: ô kế · Ctrl+Shift+P: tìm lệnh · F1: trợ giúp · Esc: đóng",
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
        _source.TextChanged += OnSourceTextChanged;
        _source.SelectionChanged += (_, _) => QueueAfterEdit(typed: false);
        AutomationPropertiesHelper.SetName(_source, "Công thức LaTeX");

        _completion = new CompletionPopup(_source);
        _completion.Accepted += AcceptCompletion;
        _palette.Chosen += InsertFromPalette;
        _palette.Dismissed += FocusSource;

        _help = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 900, Margin = new Thickness(0, 4, 0, 0), FontSize = 12, Opacity = 0.75, Visibility = Visibility.Collapsed };
        AutomationPropertiesHelper.SetName(_help, "Trợ giúp ngữ cảnh");

        _notice = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 900, Margin = new Thickness(0, 6, 0, 0), Opacity = 0.8, Visibility = Visibility.Collapsed };
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 900, Margin = new Thickness(0, 6, 0, 0), FontSize = 12 };

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(header);
        root.Children.Add(_source);
        root.Children.Add(_help);
        root.Children.Add(_palette);
        root.Children.Add(_preview);
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
        // Popup gợi ý là cửa sổ riêng: đóng khi editor mất focus để nó không nổi trên ứng dụng khác.
        Deactivated += (_, _) => _completion.Close();
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

        _completion.Close();
        _palette.Close();
        _snippets.Clear();
        _applying = true;
        try
        {
            _source.Text = request.Latex;
        }
        finally
        {
            _applying = false;
        }
        Validate();
        UpdateHelp();
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

    private void Submit(bool allowEmptySlots = false)
    {
        // Delimiter người dùng gõ/dán kèm ($$…$$) quyết định display, nhưng chỉ khi vị trí chèn cho phép display
        // (Word yêu cầu display đứng riêng một đoạn) — nếu không thì chèn inline.
        var (latex, delimiterDisplay) = EquationComposer.StripDelimiters(_source.Text);
        var options = CurrentOptions();
        options = options with { Display = (delimiterDisplay ?? options.Display) && _request.DisplayAllowed };
        var outcome = EquationComposer.Compose(latex, options, UiLanguage.Vi, allowEmptySlots, stripDelimiters: false);
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
        _completion.Close();
        _palette.Close();
        _snippets.Clear();
        if (!result.Cancelled) _usage.Save(UsagePath);
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
        var options = CurrentOptions();
        var doc = EquationComposer.Analyze(text, options);
        // Vẫn vẽ preview khi có lỗi: parser luôn phục hồi được cây, chỗ thiếu hiện □, lệnh sai tô đỏ.
        _ = _preview.RenderAsync(doc.Body, options.Display, options.MathFont, options.TextFont, options.FontSizePt ?? 12, _settings.GrowIntegrals);
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
            : _preview.FailureReason ?? "✓ " + LatexPrinter.Print(doc), isError: false);
        _source.ToolTip = null;
    }

    /// <summary>Gắn danh sách font toán quét được; giữ lựa chọn hiện tại nếu còn.</summary>
    public void SetFonts(IReadOnlyList<(string Family, string? Description)> fonts)
    {
        string current = (_font.SelectedItem as ComboBoxItem)?.Tag as string ?? _settings.MathFont;
        var ordered = fonts
            .GroupBy(f => f.Family, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(f => Array.FindIndex(PreferredFonts, p => string.Equals(p, f.Family, StringComparison.OrdinalIgnoreCase)) is var i && i >= 0 ? i : 1000)
            .ThenBy(f => f.Family, StringComparer.OrdinalIgnoreCase);
        _font.Items.Clear();
        foreach (var (family, description) in ordered)
        {
            _font.Items.Add(new ComboBoxItem
            {
                Content = family,
                Tag = family,
                FontFamily = new FontFamily(family),
                ToolTip = description,
            });
        }
        SelectFont(current);
    }

    public void SetFonts(IReadOnlyList<FontFace> faces) =>
        SetFonts(faces.Select(f => (f.Family, (string?)(FontDiagnostics.DescribeIntegral(f) + (f.IsCff ? " · CFF: có thể không nhúng được vào .docx" : "")))).ToArray());

    /// <summary>Hiện cửa sổ ngoài màn hình một lần để khởi tạo WebView2 trước lần Alt+M đầu tiên.</summary>
    public async Task PrewarmAsync()
    {
        Left = -10000;
        Top = -10000;
        Opacity = 0;
        Show();
        await _preview.InitializeAsync();
        if (_pending is null) Hide();
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
        bool ctrlShift = mods == (ModifierKeys.Control | ModifierKeys.Shift);

        if (key == Key.P && ctrlShift)
        {
            TogglePalette();
            e.Handled = true;
            return;
        }
        if (_palette.IsOpen && _palette.IsKeyboardFocusWithin)
        {
            e.Handled = _palette.HandleKey(key, mods);
            return;
        }
        if (_completion.IsOpen && HandleCompletionKey(key, mods))
        {
            e.Handled = true;
            return;
        }

        if (key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
        else if (key == Key.Enter && ctrlShift)
        {
            Submit(allowEmptySlots: true);
            e.Handled = true;
        }
        else if (key == Key.Enter && (mods == ModifierKeys.None || mods == ModifierKeys.Control))
        {
            Submit();
            e.Handled = true;
        }
        else if (key == Key.Tab && (mods == ModifierKeys.None || mods == ModifierKeys.Shift) && _source.IsKeyboardFocused)
        {
            NavigateSlot(forward: mods == ModifierKeys.None);
            e.Handled = true;
        }
        else if (key == Key.Space && mods == ModifierKeys.Control && _source.IsKeyboardFocused)
        {
            // Ctrl+Space: mở gợi ý ngay cả khi chưa gõ chữ nào sau "".
            RefreshCompletion(force: true);
            e.Handled = true;
        }
        else if (key == Key.F1 && mods == ModifierKeys.None)
        {
            _settings.BeginnerMode = !_settings.BeginnerMode;
            _settings.Save();
            UpdateHelp();
            e.Handled = true;
        }
        else if (key == Key.M && mods == (ModifierKeys.Control | ModifierKeys.Alt) && _displayToggle.IsEnabled)
        {
            _displayToggle.IsChecked = _displayToggle.IsChecked != true;
            UpdateDisplayToggle();
            e.Handled = true;
        }
    }

    /// <summary>Phím khi danh sách gợi ý đang mở; trả true nếu đã dùng phím.</summary>
    private bool HandleCompletionKey(Key key, ModifierKeys mods)
    {
        if (mods != ModifierKeys.None) return false;
        switch (key)
        {
            case Key.Down:
                _completion.Move(1);
                return true;
            case Key.Up:
                _completion.Move(-1);
                return true;
            case Key.PageDown:
                _completion.Move(8);
                return true;
            case Key.PageUp:
                _completion.Move(-8);
                return true;
            case Key.Escape:
                _completion.Close();
                return true;
            case Key.Tab:
                if (_completion.Selected is { } tabEntry) AcceptCompletion(tabEntry);
                return true;
            case Key.Enter:
                if (_completion.Selected is not { } entry) return false;
                // Đã gõ đủ "\alpha" thì Enter chèn công thức luôn, không bắt nhấn Enter hai lần.
                if (IsAlreadyComplete(entry))
                {
                    _completion.Close();
                    return false;
                }
                AcceptCompletion(entry);
                return true;
            default:
                return false;
        }
    }

    private bool IsAlreadyComplete(CatalogEntry entry)
    {
        var prefix = Completion.CommandPrefixAt(_source.Text, _source.CaretIndex);
        return prefix is not null && SnippetParser.Expand(entry.Snippet) is { Stops.Count: 0 } expanded && expanded.Text.TrimEnd() == "\\" + prefix.Value.Prefix;
    }

    // ── Gợi ý lệnh, snippet và Tab giữa các ô (§7, §10) ─────────────────

    private void OnSourceTextChanged(object sender, TextChangedEventArgs e)
    {
        ScheduleValidation();
        if (_applying) return;
        foreach (var change in e.Changes)
        {
            foreach (var session in _snippets) session.OnTextChanged(change.Offset, change.RemovedLength, change.AddedLength);
        }
        QueueAfterEdit(typed: true);
    }

    /// <summary>
    /// Gom xử lý sau mỗi lần gõ/di chuyển con trỏ vào một lượt Dispatcher, sau khi TextBox đã cập nhật cả văn bản
    /// lẫn vị trí con trỏ và bố cục (thứ tự TextChanged/SelectionChanged của WPF không cố định).
    /// </summary>
    private void QueueAfterEdit(bool typed)
    {
        if (_applying) return;
        _typedSinceAfterEdit |= typed;
        if (_afterEditQueued) return;
        _afterEditQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, AfterEdit);
    }

    private void AfterEdit()
    {
        _afterEditQueued = false;
        bool typed = _typedSinceAfterEdit;
        _typedSinceAfterEdit = false;

        int caret = _source.SelectionStart;
        foreach (var session in _snippets) session.OnCaretMoved(caret);
        _snippets.RemoveAll(s => !s.IsActive);

        if (typed || _completion.IsOpen) RefreshCompletion(force: false);
        UpdateHelp();
    }

    private void RefreshCompletion(bool force)
    {
        string text = _source.Text;
        int caret = _source.CaretIndex;
        var prefix = _source.SelectionLength == 0 ? Completion.CommandPrefixAt(text, caret) : null;
        if (prefix is null || (prefix.Value.Prefix.Length == 0 && !force))
        {
            _completion.Close();
            return;
        }

        IReadOnlyList<SearchHit> hits = prefix.Value.Prefix.Length > 0
            ? CatalogSearch.ByTrigger(prefix.Value.Prefix, _usage, max: 12)
            : RecentOrCommon();
        if (hits.Count == 0)
        {
            _completion.Close();
            return;
        }
        var rect = _source.GetRectFromCharacterIndex(prefix.Value.Start);
        _completion.Show(hits, prefix.Value.Start, rect.IsEmpty ? new Rect(0, 0, 1, _source.ActualHeight) : new Rect(rect.Left, rect.Top, 1, rect.Height));
    }

    /// <summary>"" + Ctrl+Space: lệnh vừa dùng, sau đó các lệnh hay gặp.</summary>
    private IReadOnlyList<SearchHit> RecentOrCommon()
    {
        var recent = _usage.Recent
            .Select(id => CommandCatalog.All.FirstOrDefault(e => e.Id == id))
            .Where(e => e is not null)
            .Select(e => new SearchHit(e!, 0));
        return recent.Concat(CatalogSearch.ByText("", _usage, max: 12)).GroupBy(h => h.Entry.Id).Select(g => g.First()).Take(12).ToArray();
    }

    private void AcceptCompletion(CatalogEntry entry)
    {
        int start = _completion.ReplaceStart;
        int caret = _source.CaretIndex;
        _completion.Close();
        if (start < 0 || start > caret) return;
        ApplyPlan(Completion.Plan(start, caret, entry), entry);
    }

    private void InsertFromPalette(CatalogEntry entry)
    {
        FocusSource();
        int start = _source.SelectionStart;
        ApplyPlan(Completion.Plan(start, start + _source.SelectionLength, entry), entry);
    }

    /// <summary>Thay đúng đoạn cần thay (giữ Undo của ô soạn) rồi chọn ô đầu tiên của snippet.</summary>
    private void ApplyPlan(CompletionPlan plan, CatalogEntry entry)
    {
        _applying = true;
        try
        {
            _source.Select(plan.ReplaceStart, plan.ReplaceLength);
            _source.SelectedText = plan.InsertText;
            // Các phiên bên ngoài (snippet lồng nhau) cũng phải dịch theo đoạn vừa thay.
            foreach (var session in _snippets) session.OnTextChanged(plan.ReplaceStart, plan.ReplaceLength, plan.InsertText.Length);
            if (plan.Session is not null) _snippets.Add(plan.Session);
            _source.Select(plan.SelectionStart, plan.SelectionLength);
        }
        finally
        {
            _applying = false;
        }
        _usage.Record(entry.Id);
        UpdateHelp();
    }

    /// <summary>Tab/Shift+Tab: theo phiên snippet trong cùng; không có thì nhảy tới ô "{}" rỗng kế tiếp.</summary>
    private void NavigateSlot(bool forward)
    {
        _snippets.RemoveAll(s => !s.IsActive);
        if (_snippets.Count > 0)
        {
            var session = _snippets[^1];
            var stop = forward ? session.Next() : session.Previous();
            if (!session.IsActive) _snippets.Remove(session);
            SelectClamped(stop.Start, stop.End - stop.Start);
            return;
        }

        var slot = Completion.NextEmptySlot(_source.Text, _source.CaretIndex, forward);
        if (slot is int position) SelectClamped(position, 0);
        else SystemSounds.Beep.Play();
    }

    private void SelectClamped(int start, int length)
    {
        int s = Math.Clamp(start, 0, _source.Text.Length);
        int l = Math.Clamp(length, 0, _source.Text.Length - s);
        _applying = true;
        try
        {
            _source.Select(s, l);
        }
        finally
        {
            _applying = false;
        }
        UpdateHelp();
    }

    private void TogglePalette()
    {
        if (_palette.IsOpen)
        {
            _palette.Close();
            FocusSource();
            return;
        }
        _completion.Close();
        _palette.Open(_usage);
    }

    private void FocusSource()
    {
        _source.Focus();
        Keyboard.Focus(_source);
    }

    /// <summary>Beginner mode: "Phân số · đang nhập: mẫu số · \frac{tử số}{mẫu số} · Ví dụ: …".</summary>
    private void UpdateHelp()
    {
        string? text = _settings.BeginnerMode ? ContextHelp.At(_source.Text, _source.CaretIndex)?.Describe() : null;
        if (_settings.BeginnerMode && text is null && _source.Text.Length == 0)
            text = "Gõ \\ để gợi ý lệnh (\\frac, \\int, \\alpha…) · Ctrl+Shift+P: tìm theo tên tiếng Việt · F1: tắt trợ giúp";
        _help.Text = text ?? "";
        _help.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
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
            if (string.Equals((string)item.Tag, name, StringComparison.OrdinalIgnoreCase))
            {
                _font.SelectedItem = item;
                return;
            }
        }
        if (_font.Items.Count > 0) _font.SelectedIndex = 0;
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
