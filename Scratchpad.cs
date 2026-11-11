using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using LumenLite.Panel;

namespace LumenLite;

/// <summary>
/// Floating notes in tabs with a Markdown preview, saved as you type
/// (in settings.json, next to the Notes widget's text).
/// </summary>
internal sealed class Scratchpad : Window
{
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly TextBox _editor;
    private readonly FlowDocumentScrollViewer _preview = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed, IsToolBarVisible = false };
    private readonly TextBlock _previewLabel = Kit.Label("Xem trước", 12, true, Kit.Secondary);
    private readonly TextBlock _pinGlyph = Kit.Glyph("", 13, Kit.Secondary);
    private int _active;
    private bool _loading;

    public Scratchpad()
    {
        Title = "Scratchpad";
        Icon = Branding.Icon;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Width = 520;
        Height = 460;
        MinWidth = 320;
        MinHeight = 220;
        FontFamily = Theme.Font;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (Store.S.Pads.Count == 0)
            Store.S.Pads.Add(new ScratchPad { Title = "Ghi chú 1", Text = "# Scratchpad\n\nGõ **Markdown** ở đây: `- danh sách`, `## tiêu đề`, `[link](https://…)`.\nTự lưu khi bạn gõ.\n" });

        _editor = new TextBox
        {
            Background = Brushes.Transparent,
            Foreground = Kit.Text,
            CaretBrush = Kit.Text,
            BorderThickness = new Thickness(0),
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontSize = 14,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Segoe UI"),
            Padding = new Thickness(4),
        };
        _editor.TextChanged += (_, _) =>
        {
            if (_loading) return;
            Store.S.Pads[_active].Text = _editor.Text;
            Store.SaveSoon();
        };

        var header = new Grid { Margin = new Thickness(12, 10, 10, 6), Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new ScrollViewer { Content = _tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(Kit.Clickable(Kit.Glyph("", 13), AddPad, new Thickness(8, 5, 8, 5)));
        actions.Children.Add(Kit.Clickable(_previewLabel, TogglePreview, new Thickness(8, 5, 8, 5)));
        var pin = Kit.Clickable(_pinGlyph, () =>
        {
            Topmost = !Topmost;
            _pinGlyph.Foreground = Topmost ? Kit.Blue : Kit.Secondary;
        }, new Thickness(8, 5, 8, 5));
        pin.ToolTip = "Ghim nổi trên các cửa sổ khác";
        actions.Children.Add(pin);
        actions.Children.Add(Kit.Clickable(Kit.Glyph("", 11, Kit.Secondary), Hide, new Thickness(8, 5, 8, 5)));
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 1)
                DragMove();
        };

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(header);
        var line = new Border { Background = Kit.Divider };
        Grid.SetRow(line, 1);
        body.Children.Add(line);
        var content = new Grid { Margin = new Thickness(14, 10, 14, 14) };
        content.Children.Add(_editor);
        content.Children.Add(_preview);
        Grid.SetRow(content, 2);
        body.Children.Add(content);

        Content = new Border
        {
            Background = Kit.Sheet,
            BorderBrush = Kit.SheetBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Margin = new Thickness(12),
            Child = body,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.5, Color = Colors.Black },
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Hide();
            if (e.Key == Key.T && Keyboard.Modifiers == ModifierKeys.Control) { AddPad(); e.Handled = true; }
            if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control) { TogglePreview(); e.Handled = true; }
        };
        Closing += (_, e) =>
        {
            if (!WidgetWindow.Exiting)
            {
                e.Cancel = true;
                Hide();
            }
        };
        Select(0);
        IsVisibleChanged += (_, _) => { if (!IsVisible) Memory.TrimSoon(); };
    }

    public void Open()
    {
        Show();
        Activate();
        if (_preview.Visibility != Visibility.Visible)
            _editor.Focus();
    }

    private void AddPad()
    {
        Store.S.Pads.Add(new ScratchPad { Title = $"Ghi chú {Store.S.Pads.Count + 1}" });
        Store.SaveSoon();
        Select(Store.S.Pads.Count - 1);
        _editor.Focus();
    }

    private void Select(int index)
    {
        _active = Math.Clamp(index, 0, Store.S.Pads.Count - 1);
        _loading = true;
        _editor.Text = Store.S.Pads[_active].Text;
        _loading = false;
        if (_preview.Visibility == Visibility.Visible)
            _preview.Document = Markdown(_editor.Text);
        DrawTabs();
    }

    private void DrawTabs()
    {
        _tabs.Children.Clear();
        for (var i = 0; i < Store.S.Pads.Count; i++)
        {
            var index = i;
            var pad = Store.S.Pads[i];
            var selected = i == _active;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var title = Kit.Label(pad.Title, 12.5, selected, selected ? Kit.Text : Kit.Secondary);
            title.MaxWidth = 130;
            row.Children.Add(title);
            if (Store.S.Pads.Count > 1)
            {
                var close = Kit.Clickable(Kit.Glyph("", 8, Kit.Secondary), () => ClosePad(index), new Thickness(5, 3, 3, 3), 4);
                close.Margin = new Thickness(6, 0, -4, 0);
                row.Children.Add(close);
            }
            var tab = Kit.Clickable(row, () => Select(index), new Thickness(10, 5, 10, 5), 9, selected ? Kit.Inset : null);
            tab.Margin = new Thickness(0, 0, 4, 0);
            tab.ToolTip = "Bấm đúp để đổi tên";
            tab.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                    Rename(index, tab);
            };
            _tabs.Children.Add(tab);
        }
    }

    private void Rename(int index, Border tab)
    {
        var box = new TextBox
        {
            Text = Store.S.Pads[index].Title,
            Background = Kit.Inset,
            Foreground = Kit.Text,
            CaretBrush = Kit.Text,
            BorderThickness = new Thickness(0),
            FontSize = 12.5,
            MinWidth = 80,
        };
        void Commit()
        {
            var name = box.Text.Trim();
            if (name.Length > 0)
                Store.S.Pads[index].Title = name;
            Store.SaveSoon();
            DrawTabs();
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape) { Commit(); e.Handled = true; }
        };
        box.LostFocus += (_, _) => Commit();
        tab.Child = box;
        box.Focus();
        box.SelectAll();
    }

    private void ClosePad(int index)
    {
        var pad = Store.S.Pads[index];
        if (pad.Text.Trim().Length > 0 && MessageBox.Show(this, $"Xóa ghi chú \"{pad.Title}\"?", "Scratchpad", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        Store.S.Pads.RemoveAt(index);
        Store.SaveSoon();
        Select(Math.Min(_active, Store.S.Pads.Count - 1));
    }

    private void TogglePreview()
    {
        var show = _preview.Visibility != Visibility.Visible;
        _preview.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _editor.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        _previewLabel.Text = show ? "Sửa" : "Xem trước";
        _previewLabel.Foreground = show ? Kit.Blue : Kit.Secondary;
        if (show)
            _preview.Document = Markdown(_editor.Text);
        else
            _editor.Focus();
    }

    // ---------- a small Markdown renderer ----------

    private static FlowDocument Markdown(string text)
    {
        var doc = new FlowDocument { FontFamily = Theme.Font, FontSize = 14, Foreground = Kit.Text, PagePadding = new Thickness(2), Background = Brushes.Transparent };
        List? list = null;
        Paragraph? code = null;
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```"))
            {
                if (code == null)
                {
                    code = new Paragraph { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12.5, Background = Kit.Inset, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 4, 0, 8) };
                    doc.Blocks.Add(code);
                }
                else
                    code = null;
                list = null;
                continue;
            }
            if (code != null)
            {
                if (code.Inlines.Count > 0) code.Inlines.Add(new LineBreak());
                code.Inlines.Add(new Run(line));
                continue;
            }
            var bullet = Regex.Match(line, @"^\s*([-*+]|\d+\.)\s+(.*)$");
            if (bullet.Success)
            {
                var numbered = char.IsDigit(bullet.Groups[1].Value[0]);
                if (list == null || (list.MarkerStyle == TextMarkerStyle.Decimal) != numbered)
                {
                    list = new List { MarkerStyle = numbered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc, Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(20, 0, 0, 0) };
                    doc.Blocks.Add(list);
                }
                var item = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
                var content = bullet.Groups[2].Value;
                if (content.StartsWith("[ ] ") || content.StartsWith("[x] "))
                {
                    item.Inlines.Add(new Run(content[1] == 'x' ? "☑ " : "☐ "));
                    content = content[4..];
                }
                AddInlines(item, content);
                list.ListItems.Add(new ListItem(item));
                continue;
            }
            list = null;
            if (line.Length == 0)
                continue;
            if (Regex.IsMatch(line, @"^\s*(---+|\*\*\*+)\s*$"))
            {
                doc.Blocks.Add(new BlockUIContainer(new Border { Height = 1, Background = Kit.Divider, Margin = new Thickness(0, 6, 0, 6) }));
                continue;
            }
            var heading = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
            if (heading.Success)
            {
                var level = heading.Groups[1].Length;
                paragraph.FontSize = level switch { 1 => 24, 2 => 19, 3 => 16, _ => 14.5 };
                paragraph.FontWeight = FontWeights.SemiBold;
                paragraph.Margin = new Thickness(0, level == 1 ? 2 : 8, 0, 6);
                AddInlines(paragraph, heading.Groups[2].Value);
            }
            else if (line.StartsWith(">"))
            {
                paragraph.BorderBrush = Kit.Blue;
                paragraph.BorderThickness = new Thickness(3, 0, 0, 0);
                paragraph.Padding = new Thickness(10, 2, 0, 2);
                paragraph.Foreground = Kit.Secondary;
                AddInlines(paragraph, line.TrimStart('>', ' '));
            }
            else
                AddInlines(paragraph, line);
            doc.Blocks.Add(paragraph);
        }
        return doc;
    }

    private static readonly Regex InlinePattern = new(@"(\*\*(.+?)\*\*|__(.+?)__|\*(.+?)\*|_(.+?)_|`(.+?)`|\[(.+?)\]\((.+?)\)|~~(.+?)~~)", RegexOptions.Compiled);

    private static void AddInlines(Paragraph paragraph, string text)
    {
        var last = 0;
        foreach (Match m in InlinePattern.Matches(text))
        {
            if (m.Index > last)
                paragraph.Inlines.Add(new Run(text[last..m.Index]));
            if (m.Groups[2].Success || m.Groups[3].Success)
                paragraph.Inlines.Add(new Bold(new Run(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value)));
            else if (m.Groups[4].Success || m.Groups[5].Success)
                paragraph.Inlines.Add(new Italic(new Run(m.Groups[4].Success ? m.Groups[4].Value : m.Groups[5].Value)));
            else if (m.Groups[6].Success)
                paragraph.Inlines.Add(new Run(m.Groups[6].Value) { FontFamily = new FontFamily("Cascadia Mono, Consolas"), Background = Kit.Inset, FontSize = 12.5 });
            else if (m.Groups[7].Success)
            {
                var url = m.Groups[8].Value;
                var link = new Hyperlink(new Run(m.Groups[7].Value)) { Foreground = Kit.Blue, Cursor = Cursors.Hand };
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    link.Click += (_, _) => CommandBar.Launch(uri.AbsoluteUri);
                paragraph.Inlines.Add(link);
            }
            else if (m.Groups[9].Success)
                paragraph.Inlines.Add(new Run(m.Groups[9].Value) { TextDecorations = TextDecorations.Strikethrough });
            last = m.Index + m.Length;
        }
        if (last < text.Length)
            paragraph.Inlines.Add(new Run(text[last..]));
    }
}
