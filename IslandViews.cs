using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LumenLite;

/// <summary>
/// The Island's extra pages, laid out like Vorssaint's: a grid of tool tiles, and
/// an AI-agents page made of dark cards. Both open inside the expanded Island and
/// have a back arrow in the header.
/// </summary>
public sealed partial class IslandWindow
{
    private const string ViewTools = "tools", ViewAgents = "agents", ViewCalendar = "calendar", ViewRemote = "remote";

    private static readonly Brush TileBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x16)));
    private static readonly Brush TileHover = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x25)));
    private static readonly Brush TileBorder = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2D)));
    private static readonly Brush Claude = Freeze(new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x57)));
    private static readonly Brush ClaudeSoft = Freeze(new SolidColorBrush(Color.FromRgb(0x3A, 0x22, 0x1A)));
    private static readonly Brush Red = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x37, 0x5F)));
    private static readonly Brush Orange = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)));
    private static readonly Brush TrackBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2F)));
    private static readonly Brush Faint = Freeze(new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93)));

    private string _view = "";
    private readonly UniformGrid _tools = new() { Columns = 4, Margin = new Thickness(-4, -2, -4, 0) };
    private readonly ScrollViewer _agents = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Height = 322 };
    private readonly StackPanel _agentsContent = new();
    private readonly StackPanel _remote = new();
    private int _remoteCount;
    // RustDesk machines on the main page, under the controls and under the player.
    private readonly UniformGrid _stripControls = new() { Rows = 1, Margin = new Thickness(-4, 8, -4, 0) };
    private readonly UniformGrid _stripPlayer = new() { Rows = 1, Margin = new Thickness(-4, 10, -4, 0) };
    private int _stripCount;
    private Border? _back;

    public Action? OpenCommandBar { get; set; }
    public Action? OpenRadial { get; set; }

    private bool IsCalendar => _view == ViewCalendar;

    /// <summary>Opens the Island on its AI Agents page, as the radial menu's spark does.</summary>
    public void ShowAgents() => ShowView(ViewAgents);

    /// <summary>Switches the open Island to a page ("" is the player or controls).</summary>
    private void ShowView(string view)
    {
        _view = view;
        if (view == ViewCalendar)
            _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        SetMode(Mode.Expanded);
        Animate(HeightProperty, ExpandedHeight());
        UpdateTiles();
    }

    private void BuildViews()
    {
        // Back arrow at the left of the header, shown on every page but the main one.
        if (_heading.Parent is Grid header)
        {
            _back = Theme.Pressable(Theme.Glyph("", 12), () => ShowView(""), new Thickness(6, 3, 6, 3));
            _back.HorizontalAlignment = HorizontalAlignment.Left;
            _back.VerticalAlignment = VerticalAlignment.Center;
            _back.Margin = new Thickness(-6, 0, 0, 0);
            header.Children.Add(_back);
        }

        GridTile("", new SolidColorBrush(Color.FromRgb(0x24, 0x9C, 0xF6)), "RustDesk", "Đổi máy", () => ShowView(ViewRemote));
        GridTile("", null, "Âm lượng", "Từng app", () => OpenPanel?.Invoke("sound"));
        GridTile("", Red, "Đang phát", null, () => ShowView(""));
        GridTile("", null, "Ghi chú", null, () => OpenNotes?.Invoke());
        GridTile("", null, "Tải về", null, () => CommandBar.Launch(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")));
        GridTile("", null, "Hệ thống", null, () => OpenPanel?.Invoke("system"));
        GridTile("", null, "Tiện ích", null, () => OpenPanel?.Invoke("tools"));
        GridTile("", Red, "Lịch", null, () => ShowView(ViewCalendar));
        GridTile("", null, "Command Bar", null, () => OpenCommandBar?.Invoke());
        GridTile("", null, "Menu vòng tròn", null, () => OpenRadial?.Invoke());
        GridTile("", null, "Thiết bị", null, () => OpenPanel?.Invoke("devices"));
        GridTile("✦", Claude, "AI Agents", null, () => ShowView(ViewAgents), symbol: true);
        Grid.SetRow(_tools, 1);
        _expanded.Children.Add(_tools);

        _agents.Content = _agentsContent;
        Grid.SetRow(_agents, 1);
        _expanded.Children.Add(_agents);
        Grid.SetRow(_remote, 1);
        _expanded.Children.Add(_remote);

        _controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_stripControls, 2);
        Grid.SetColumnSpan(_stripControls, 2);
        _controls.Children.Add(_stripControls);
        _player.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_stripPlayer, 2);
        Grid.SetColumnSpan(_stripPlayer, 2);
        _player.Children.Add(_stripPlayer);

        AgentMonitor.Updated += () =>
        {
            if (_mode == Mode.Expanded && _view == ViewAgents)
                DrawAgents();
        };
    }

    /// <summary>One square tile; returns its small detail line so state can be shown there.</summary>
    private TextBlock GridTile(string glyph, Brush? color, string label, string? detail, Action onClick, bool symbol = false)
    {
        var icon = symbol
            ? new TextBlock { Text = glyph, FontSize = 24, Foreground = color, HorizontalAlignment = HorizontalAlignment.Center, FontFamily = new FontFamily("Segoe UI Symbol") }
            : Theme.Glyph(glyph, 22);
        if (color != null) icon.Foreground = color;
        icon.Margin = new Thickness(0, 0, 0, 8);
        var name = new TextBlock { Text = label, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var small = new TextBlock { Text = detail ?? "", FontSize = 10.5, Foreground = Faint, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 3, 0, 0), Visibility = detail == null ? Visibility.Collapsed : Visibility.Visible };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(icon);
        stack.Children.Add(name);
        stack.Children.Add(small);
        var tile = new Border
        {
            Height = 92,
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(16),
            Background = TileBrush,
            BorderBrush = TileBorder,
            BorderThickness = new Thickness(1),
            Child = stack,
            Cursor = Cursors.Hand,
            Padding = new Thickness(4),
        };
        var pressed = false;
        tile.MouseEnter += (_, _) => tile.Background = TileHover;
        tile.MouseLeave += (_, _) => { tile.Background = TileBrush; pressed = false; };
        tile.MouseLeftButtonDown += (_, e) => { pressed = true; e.Handled = true; };
        tile.MouseLeftButtonUp += (_, e) =>
        {
            if (!pressed) return;
            pressed = false;
            e.Handled = true;
            try
            {
                onClick();
            }
            catch (Exception ex)
            {
                Store.Log($"island tile '{label}' failed: " + ex.Message);
            }
        };
        _tools.Children.Add(tile);
        return small;
    }

    /// <summary>Shows the parts of the open Island that belong to the current page.</summary>
    private void ApplyView()
    {
        var main = _view == "";
        _heading.Text = _view switch
        {
            ViewCalendar => "Lịch",
            ViewTools => "Tiện ích",
            ViewAgents => "AI Agents",
            ViewRemote => "RustDesk",
            _ => _hasMedia ? "Đang phát" : "Điều khiển",
        };
        _heading.Margin = new Thickness(main ? 0 : 22, 0, 0, 0);
        if (_back != null) _back.Visibility = main ? Visibility.Collapsed : Visibility.Visible;
        _player.Visibility = main && _hasMedia ? Visibility.Visible : Visibility.Collapsed;
        _controls.Visibility = main && !_hasMedia ? Visibility.Visible : Visibility.Collapsed;
        _calendar.Visibility = _view == ViewCalendar ? Visibility.Visible : Visibility.Collapsed;
        _tools.Visibility = _view == ViewTools ? Visibility.Visible : Visibility.Collapsed;
        _agents.Visibility = _view == ViewAgents ? Visibility.Visible : Visibility.Collapsed;
        _remote.Visibility = _view == ViewRemote ? Visibility.Visible : Visibility.Collapsed;
        if (_view == ViewRemote) DrawRemote();
        if (main) DrawStrips();
        if (_view == ViewCalendar) DrawCalendar();
        if (_view == ViewAgents)
        {
            _agents.ScrollToTop();
            DrawAgents();
        }
    }

    private double ViewHeight() => _view switch
    {
        ViewTools => 64 + 3 * 100,
        ViewAgents => 400,
        ViewRemote => 92 + Math.Max(1, _remoteCount) * 66,
        _ => -1,
    };

    // ---------- RustDesk on the main page ----------

    /// <summary>Extra height the RustDesk row adds to the main page.</summary>
    private double StripHeight() => _stripCount > 0 ? 50 : 0;

    private void DrawStrips()
    {
        var peers = RustDesk.Available ? RustDesk.List().Take(3).ToList() : new System.Collections.Generic.List<RemotePeer>();
        _stripCount = peers.Count;
        foreach (var strip in new[] { _stripControls, _stripPlayer })
        {
            strip.Children.Clear();
            strip.Visibility = peers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            strip.Columns = Math.Max(1, peers.Count);
            foreach (var peer in peers)
                strip.Children.Add(StripButton(peer));
        }
    }

    /// <summary>A machine as a pill: click to bring up RustDesk on that machine.</summary>
    private Border StripButton(RemotePeer peer)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var mac = peer.Platform.Contains("Mac", StringComparison.OrdinalIgnoreCase);
        var icon = Theme.Glyph(mac ? "\uE7F8" : "\uE7F4", 14);
        icon.Foreground = peer.Showing ? Theme.Accent : new SolidColorBrush(Color.FromRgb(0x24, 0x9C, 0xF6));
        icon.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(icon);
        var name = new TextBlock
        {
            Text = peer.Host.Length > 0 ? peer.Host : peer.Id,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        var pill = new Border
        {
            Height = 38,
            Margin = new Thickness(4, 0, 4, 0),
            Padding = new Thickness(12, 0, 10, 0),
            CornerRadius = new CornerRadius(12),
            Background = TileBrush,
            BorderBrush = peer.Showing ? Theme.Accent : TileBorder,
            BorderThickness = new Thickness(1),
            Child = row,
            Cursor = Cursors.Hand,
            ToolTip = $"RustDesk · {peer.Id}" + (peer.User.Length > 0 ? $" · {peer.User}" : "") + " · bấm để mở, bấm lần nữa để thu nhỏ",
        };
        pill.MouseEnter += (_, _) => pill.Background = TileHover;
        pill.MouseLeave += (_, _) => pill.Background = TileBrush;
        var id = peer.Id;
        pill.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            try
            {
                // Pressed again while that screen is in front: RustDesk is minimized, back to the desktop.
                RustDesk.Toggle(id);
            }
            catch (Exception ex)
            {
                Store.Log("rustdesk connect failed: " + ex.Message);
            }
            // Collapse so the RustDesk window is not covered, and mark the new machine next time.
            SetMode(Wanted());
        };
        return pill;
    }

    // ---------- RustDesk page ----------

    private void DrawRemote()
    {
        _remote.Children.Clear();
        if (!RustDesk.Available)
        {
            _remoteCount = 1;
            _remote.Children.Add(Card(new TextBlock { Text = "Chưa thấy RustDesk trên máy này.", FontSize = 13, Foreground = Theme.Muted }));
            return;
        }
        var peers = RustDesk.List().Take(5).ToList();
        _remoteCount = peers.Count;
        foreach (var peer in peers)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var mac = peer.Platform.Contains("Mac", StringComparison.OrdinalIgnoreCase);
            var icon = Theme.Glyph(mac ? "\uE7F8" : "\uE7F4", 20);
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            icon.Foreground = peer.Showing ? Theme.Accent : Theme.Text;
            grid.Children.Add(icon);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = peer.Host.Length > 0 ? peer.Host : peer.Id, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = string.Join(" · ", new[] { peer.Id, peer.User, peer.Platform }.Where(x => x.Length > 0)), FontSize = 11.5, Foreground = Faint, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var state = new Border
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 3, 10, 4),
                Background = peer.Showing ? new SolidColorBrush(Color.FromArgb(0x40, 0x8B, 0x7C, 0xF6)) : TrackBrush,
                Child = new TextBlock { Text = peer.Showing ? "Đang xem" : "Chuyển", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = peer.Showing ? Theme.Accent : Theme.Text },
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(state, 2);
            grid.Children.Add(state);
            var card = Card(grid, new Thickness(0, 0, 0, 8));
            card.Padding = new Thickness(14, 10, 14, 10);
            card.Cursor = Cursors.Hand;
            card.MouseEnter += (_, _) => card.Background = TileHover;
            card.MouseLeave += (_, _) => card.Background = TileBrush;
            var id = peer.Id;
            card.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                try
                {
                    RustDesk.Toggle(id);
                }
                catch (Exception ex)
                {
                    Store.Log("rustdesk connect failed: " + ex.Message);
                }
                // The window title changes once RustDesk has switched tabs.
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    if (_view == ViewRemote) DrawRemote();
                };
                timer.Start();
            };
            _remote.Children.Add(card);
        }
        if (peers.Count == 0)
        {
            _remoteCount = 1;
            _remote.Children.Add(Card(new TextBlock { Text = "RustDesk chưa lưu máy nào.", FontSize = 13, Foreground = Theme.Muted }));
        }
        var open = Theme.Pressable(new TextBlock { Text = "Mở RustDesk  ›", FontSize = 12.5, Foreground = Theme.Accent }, RustDesk.OpenApp, new Thickness(8, 2, 8, 4));
        open.HorizontalAlignment = HorizontalAlignment.Center;
        _remote.Children.Add(open);
    }

}
