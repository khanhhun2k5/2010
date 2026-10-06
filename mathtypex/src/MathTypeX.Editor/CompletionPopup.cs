using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MathTypeX.Editing.Catalog;

namespace MathTypeX.Editor;

/// <summary>
/// Danh sách gợi ý khi gõ "\fra" (§7): hiện ngay dưới tiền tố, không lấy focus khỏi ô soạn.
/// Phím do <see cref="EditorWindow"/> chuyển vào: ↑/↓ chọn, Tab/Enter chấp nhận, Esc đóng.
/// </summary>
internal sealed class CompletionPopup
{
    private readonly Popup _popup;
    private readonly ListBox _list;
    private readonly TextBlock _detail;

    public CompletionPopup(UIElement target)
    {
        _list = new ListBox
        {
            Focusable = false,
            MaxHeight = 260,
            MinWidth = 320,
            BorderThickness = new Thickness(0),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.SelectionChanged += (_, _) => UpdateDetail();
        EnableMouse(_list, entry => Accepted?.Invoke(entry));

        _detail = new TextBlock { Margin = new Thickness(8, 4, 8, 6), FontSize = 11, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };

        var panel = new DockPanel();
        DockPanel.SetDock(_detail, Dock.Bottom);
        panel.Children.Add(_detail);
        panel.Children.Add(_list);

        var border = new Border { BorderThickness = new Thickness(1), Child = panel };
        border.SetResourceReference(Border.BackgroundProperty, SystemColors.WindowBrushKey);
        border.SetResourceReference(Border.BorderBrushProperty, SystemColors.ActiveBorderBrushKey);

        _popup = new Popup
        {
            PlacementTarget = target,
            Placement = PlacementMode.Bottom,
            StaysOpen = true,
            Focusable = false,
            Child = border,
        };
        AutomationPropertiesHelper.SetName(_list, "Gợi ý lệnh");
    }

    /// <summary>Người dùng nhấp đúp một gợi ý.</summary>
    public event Action<CatalogEntry>? Accepted;

    public bool IsOpen => _popup.IsOpen;

    /// <summary>Vị trí dấu "\" của tiền tố đang gợi ý.</summary>
    public int ReplaceStart { get; private set; } = -1;

    public CatalogEntry? Selected => (_list.SelectedItem as ListBoxItem)?.Tag as CatalogEntry;

    public void Show(IReadOnlyList<SearchHit> hits, int replaceStart, Rect anchor)
    {
        bool reposition = !_popup.IsOpen || ReplaceStart != replaceStart || _popup.PlacementRectangle != anchor;
        ReplaceStart = replaceStart;
        _list.Items.Clear();
        foreach (var hit in hits) _list.Items.Add(CreateItem(hit.Entry));
        _list.SelectedIndex = 0;
        _list.ScrollIntoView(_list.SelectedItem);
        if (!reposition) return;
        _popup.PlacementRectangle = anchor;
        // Đặt lại IsOpen để popup bám theo vị trí mới (chỉ khi tiền tố đổi chỗ, tránh nhấp nháy khi gõ).
        _popup.IsOpen = false;
        _popup.IsOpen = true;
    }

    public void Close()
    {
        _popup.IsOpen = false;
        ReplaceStart = -1;
    }

    public void Move(int delta)
    {
        if (_list.Items.Count == 0) return;
        int index = Math.Clamp(_list.SelectedIndex + delta, 0, _list.Items.Count - 1);
        _list.SelectedIndex = index;
        _list.ScrollIntoView(_list.SelectedItem);
    }

    private void UpdateDetail()
    {
        var e = Selected;
        _detail.Text = e is null ? "" : string.Join(" · ", new[] { e.SyntaxVi, e.DescriptionVi, e.Example.Length > 0 ? "Ví dụ: " + e.Example : "" }.Where(s => s.Length > 0));
        _detail.Visibility = _detail.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Mục trong danh sách không nhận focus (để focus ở lại ô soạn/ô tìm), nên WPF không tự chọn khi nhấp chuột:
    /// nhấp để chọn, nhấp đúp để chấp nhận.
    /// </summary>
    internal static void EnableMouse(ListBox list, Action<CatalogEntry> accept)
    {
        list.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not DependencyObject source) return;
            if (ItemsControl.ContainerFromElement(list, source) is not ListBoxItem { Tag: CatalogEntry entry } item) return;
            list.SelectedItem = item;
            if (e.ClickCount >= 2) accept(entry);
            e.Handled = true;
        };
    }

    /// <summary>Một dòng: ký hiệu (font toán) · \lệnh · tên tiếng Việt.</summary>
    internal static ListBoxItem CreateItem(CatalogEntry entry)
    {
        var symbol = new TextBlock
        {
            Text = entry.Symbol ?? "",
            Width = 30,
            FontFamily = new FontFamily("Cambria Math"),
            FontSize = 16,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var command = new TextBlock
        {
            Text = entry.Id.StartsWith("tpl:", StringComparison.Ordinal) ? entry.SyntaxVi.Length > 0 ? entry.SyntaxVi : entry.Example : "\\" + entry.Trigger,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            Margin = new Thickness(4, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var name = new TextBlock { Text = entry.NameVi, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(symbol);
        row.Children.Add(command);
        row.Children.Add(name);
        var item = new ListBoxItem { Content = row, Tag = entry, Focusable = false, Padding = new Thickness(2, 1, 2, 1) };
        System.Windows.Automation.AutomationProperties.SetName(item, $"{entry.NameVi}, \\{entry.Trigger}");
        return item;
    }
}

/// <summary>
/// Command Palette (Ctrl+Shift+P, §8): tìm lệnh/mẫu theo tên tiếng Việt hoặc tiếng Anh, có hoặc không dấu.
/// Nằm ngay trong cửa sổ editor (không phải popup) để giữ focus bàn phím ổn định.
/// </summary>
internal sealed class CommandPalette : Border
{
    private readonly TextBox _query;
    private readonly ListBox _list;
    private readonly TextBlock _detail;
    private UsageStats? _usage;

    public CommandPalette()
    {
        Visibility = Visibility.Collapsed;
        Margin = new Thickness(0, 8, 0, 0);
        Padding = new Thickness(6);
        BorderThickness = new Thickness(1);
        SetResourceReference(BorderBrushProperty, SystemColors.ActiveBorderBrushKey);

        _query = new TextBox { FontSize = 14, Margin = new Thickness(0, 0, 0, 6) };
        // Ô tìm kiếm cho phép gõ tiếng Việt ("phân số"); gõ không dấu cũng tìm được.
        InputMethod.SetIsInputMethodEnabled(_query, true);
        _query.TextChanged += (_, _) => Refresh();
        AutomationPropertiesHelper.SetName(_query, "Tìm lệnh hoặc mẫu công thức");

        _list = new ListBox { MaxHeight = 240, Focusable = false };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.SelectionChanged += (_, _) => UpdateDetail();
        CompletionPopup.EnableMouse(_list, _ => Choose());

        _detail = new TextBlock { FontSize = 11, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, MaxWidth = 860, Margin = new Thickness(0, 6, 0, 0) };

        var title = new TextBlock { Text = "Tìm lệnh (Ctrl+Shift+P) — gõ \"phân số\", \"tich phan\", \"alpha\"… · Enter: chèn · Esc: đóng", FontSize = 11, Opacity = 0.65, Margin = new Thickness(0, 0, 0, 4) };

        var panel = new StackPanel();
        panel.Children.Add(title);
        panel.Children.Add(_query);
        panel.Children.Add(_list);
        panel.Children.Add(_detail);
        Child = panel;
    }

    /// <summary>Người dùng chọn một mục: chèn vào ô soạn.</summary>
    public event Action<CatalogEntry>? Chosen;

    /// <summary>Đóng palette (Esc) — trả focus về ô soạn.</summary>
    public event Action? Dismissed;

    public bool IsOpen => Visibility == Visibility.Visible;

    public void Open(UsageStats usage)
    {
        _usage = usage;
        Visibility = Visibility.Visible;
        _query.Text = "";
        Refresh();
        _query.Focus();
        Keyboard.Focus(_query);
    }

    public void Close()
    {
        Visibility = Visibility.Collapsed;
    }

    /// <summary>Phím khi focus đang trong palette; trả true nếu đã xử lý.</summary>
    public bool HandleKey(Key key, ModifierKeys mods)
    {
        switch (key)
        {
            case Key.Escape:
                Close();
                Dismissed?.Invoke();
                return true;
            case Key.Enter when mods == ModifierKeys.None:
                Choose();
                return true;
            case Key.Down:
                Move(1);
                return true;
            case Key.Up:
                Move(-1);
                return true;
            case Key.PageDown:
                Move(8);
                return true;
            case Key.PageUp:
                Move(-8);
                return true;
            default:
                return false;
        }
    }

    private void Refresh()
    {
        _list.Items.Clear();
        foreach (var hit in CatalogSearch.ByText(_query.Text, _usage, max: 40))
        {
            var item = CompletionPopup.CreateItem(hit.Entry);
            if (item.Content is StackPanel row)
                row.Children.Add(new TextBlock { Text = "  · " + CategoryText.Vi(hit.Entry.Category), Opacity = 0.45, VerticalAlignment = VerticalAlignment.Center });
            _list.Items.Add(item);
        }
        _list.SelectedIndex = _list.Items.Count > 0 ? 0 : -1;
        if (_list.SelectedItem is not null) _list.ScrollIntoView(_list.SelectedItem);
        UpdateDetail();
    }

    private void Move(int delta)
    {
        if (_list.Items.Count == 0) return;
        _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + delta, 0, _list.Items.Count - 1);
        _list.ScrollIntoView(_list.SelectedItem);
    }

    private void Choose()
    {
        if ((_list.SelectedItem as ListBoxItem)?.Tag is not CatalogEntry entry)
        {
            SystemSoundsHelper.Beep();
            return;
        }
        Close();
        Chosen?.Invoke(entry);
    }

    private void UpdateDetail()
    {
        var e = (_list.SelectedItem as ListBoxItem)?.Tag as CatalogEntry;
        _detail.Text = e is null
            ? "Không tìm thấy. Thử từ khác, ví dụ \"căn\", \"ma trận\", \"vector\"."
            : string.Join(" · ", new[] { e.NameVi + " / " + e.NameEn, e.SyntaxVi, e.DescriptionVi, e.Example.Length > 0 ? "Ví dụ: " + e.Example : "" }.Where(s => s.Length > 0));
    }
}

internal static class SystemSoundsHelper
{
    public static void Beep() => System.Media.SystemSounds.Beep.Play();
}
