using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using LumenLite.Widgets;
using Windows.Media.Control;
using MediaManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using MediaSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace LumenLite;

/// <summary>
/// A black notch hanging from the top centre of the main screen, in the spirit
/// of a phone's "Dynamic Island". At rest it is a small pill (showing what is
/// playing, the keep-awake and timer state); hovering opens a panel with the
/// player, volume, brightness and quick buttons. Volume and brightness changes,
/// finished downloads and similar events show briefly. The window is fully
/// transparent around the pill, so clicks there reach the windows below.
/// </summary>
public sealed partial class IslandWindow : Window
{
    private enum Mode { Hidden, Idle, Compact, Expanded, Level, Message }

    private const double WindowWidth = 720, WindowHeight = 450;
    private const double ExpandedWidth = 540;
    private static readonly Brush PillBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xFA, 0x06, 0x06, 0x09)));
    private static readonly Brush RimBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush CardBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush ActiveBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x66, 0x8B, 0x7C, 0xF6)));

    private readonly Border _pill;
    private readonly StackPanel _leftButtons = new() { Margin = new Thickness(0, 14, 12, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly StackPanel _rightButtons = new() { Margin = new Thickness(12, 14, 0, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly Grid _compact = new();
    private readonly Grid _expanded = new();
    private readonly Grid _level = new();
    private readonly Grid _message = new();

    // Compact
    private readonly Border _miniCover = Cover(22, 7);
    private readonly TextBlock _miniTitle = new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _bars = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Height = 14, Margin = new Thickness(10, 0, 0, 0) };
    private readonly TextBlock _miniAgent = new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x8A, 0x5B)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), ToolTip = "Claude Code đang làm việc" };

    // Expanded: header
    private readonly TextBlock _heading = new() { FontSize = 13.5, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _clock = new() { FontSize = 12.5, Foreground = Theme.Muted, HorizontalAlignment = HorizontalAlignment.Right };
    // Expanded: player
    private readonly Grid _player = new();
    private readonly Border _cover = Cover(104, 18);
    private readonly TextBlock _title = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _artist = new() { FontSize = 12.5, Foreground = Theme.Muted, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
    private readonly Grid _progress = new() { Height = 14, Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Border _progressFill = new() { Height = 4, CornerRadius = new CornerRadius(2), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _elapsed = new() { FontSize = 10.5, Foreground = Theme.Muted };
    private readonly TextBlock _remaining = new() { FontSize = 10.5, Foreground = Theme.Muted, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Grid _times = new();
    private readonly TextBlock _playGlyph = Theme.Glyph("", 18);
    private readonly Border _prev, _play, _next;
    private readonly TextBlock _outputName = new() { FontSize = 11.5, Foreground = Theme.Muted, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 150, VerticalAlignment = VerticalAlignment.Center };
    private readonly VolumeBar _playerVolume = new();
    // Expanded: nothing playing
    private readonly Grid _controls = new();
    private readonly VolumeBar _controlsVolume = new();
    private readonly VolumeBar _brightness;
    private readonly List<string> _brightnessKeys = new();
    private readonly Tile _tileMixer, _tileSnip, _tileShutdown, _tileRestart;
    private readonly Border _sideAgents;

    // Level
    private readonly TextBlock _levelGlyph = Theme.Glyph(VolumeBar.VolumeGlyph, 15);
    private readonly Border _levelFill = new() { Height = 6, CornerRadius = new CornerRadius(3), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _levelText = new() { FontSize = 12, Width = 36, TextAlignment = TextAlignment.Right, Foreground = Theme.Muted, VerticalAlignment = VerticalAlignment.Center };

    // Message
    private readonly TextBlock _messageGlyph = Theme.Glyph("", 15);
    private readonly TextBlock _messageText = new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private double _messageWidth = 260;

    // Idle readout and calendar
    /// <summary>The chosen readout (CPU/RAM or network) beside the compact pill's other marks.</summary>
    private readonly TextBlock _miniMetrics = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private readonly TextBlock _idleText = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _calendar = new();
    private readonly TextBlock _calendarTitle = new() { FontSize = 14, FontWeight = FontWeights.SemiBold };
    private readonly UniformGrid _days = new() { Columns = 7 };
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _live = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _meter = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer _transient = new();
    private readonly DispatcherTimer _expandDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _collapse = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly Random _random = new();

    private MediaManager? _manager;
    private MediaSession? _session;
    private bool _playing;
    private bool _hasMedia;
    private float? _lastVolume;
    private bool? _lastMuted;
    private Mode _mode = Mode.Hidden;
    private bool _enabled;
    private bool _alwaysShow = true;

    /// <summary>Opens the control panel on a tab: "sound" or "toggles".</summary>
    public Action<string>? OpenPanel { get; set; }

    /// <summary>Opens the Scratchpad notes.</summary>
    public Action? OpenNotes { get; set; }

    public IslandWindow()
    {
        Title = "LumenLite Island";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Width = WindowWidth;
        Height = WindowHeight;
        FontFamily = Theme.Font;
        Foreground = Theme.Text;

        _brightness = new VolumeBar(() => null, level =>
        {
            foreach (var key in _brightnessKeys)
                Brightness.Set(key, level);
        }, null, VolumeBar.BrightnessGlyph);

        _prev = Theme.Pressable(Theme.Glyph("", 18), () => Send(s => s.TrySkipPreviousAsync()), new Thickness(10, 6, 10, 6));
        _play = Theme.Pressable(new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(21),
            Background = Brushes.White,
            Child = _playGlyph,
        }, () => Send(s => s.TryTogglePlayPauseAsync()), new Thickness(4));
        _play.CornerRadius = new CornerRadius(25);
        _playGlyph.Foreground = Brushes.Black;
        _next = Theme.Pressable(Theme.Glyph("", 18), () => Send(s => s.TrySkipNextAsync()), new Thickness(10, 6, 10, 6));

        _tileMixer = new Tile("", "Âm thanh", () => OpenPanel?.Invoke("sound"));
        _tileSnip = new Tile("", "Ghi chú", () => OpenNotes?.Invoke());
        _tileShutdown = ConfirmTile("", "Tắt máy", "Bấm lần nữa để tắt", "/s");
        _tileRestart = ConfirmTile("", "Khởi động lại", "Bấm lần nữa để khởi động lại", "/r");

        BuildCompact();
        BuildExpanded();
        BuildLevel();
        BuildMessage();
        BuildCalendar();
        BuildViews();
        SystemMonitor.Sampled += _ => UpdateIdleText();

        _leftButtons.Children.Add(SideButton("", "Tiện ích", () => ShowView(_view == ViewTools ? "" : ViewTools)));
        _rightButtons.Children.Add(SideButton("", "Mixer âm lượng", () => OpenPanel?.Invoke("sound")));
        _rightButtons.Children.Add(_sideAgents = SideButton("✦", "AI Agents", () => ShowView(ViewAgents)));

        _leftButtons.Children.Add(SideButton("\uE787", "Lịch tháng", () => ShowView(IsCalendar ? "" : ViewCalendar)));

        var content = new Grid();
        content.Children.Add(_compact);
        content.Children.Add(_expanded);
        content.Children.Add(_level);
        content.Children.Add(_message);
        content.Children.Add(_idleText);
        _pill = new Border
        {
            Background = PillBrush,
            BorderBrush = RimBrush,
            BorderThickness = new Thickness(1, 0, 1, 1),
            VerticalAlignment = VerticalAlignment.Top,
            ClipToBounds = true,
            Child = content,
            Visibility = Visibility.Collapsed,
            Cursor = Cursors.Hand,
            Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Opacity = 0.5, Color = Colors.Black },
        };

        var root = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        _leftButtons.HorizontalAlignment = HorizontalAlignment.Right;
        root.Children.Add(_leftButtons);
        Grid.SetColumn(_pill, 1);
        root.Children.Add(_pill);
        Grid.SetColumn(_rightButtons, 2);
        root.Children.Add(_rightButtons);
        Content = root;
        ShowSideButtons(false);

        foreach (var target in new FrameworkElement[] { _pill, _leftButtons, _rightButtons })
        {
            target.MouseEnter += (_, _) => HoverStarted();
            target.MouseLeave += (_, _) => HoverEnded();
        }
        // A click opens it straight away, without waiting for the hover delay.
        _pill.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (_mode is Mode.Idle or Mode.Compact or Mode.Level or Mode.Message)
                SetMode(Mode.Expanded);
        };
        _expandDelay.Tick += (_, _) =>
        {
            _expandDelay.Stop();
            if (IsHovered() && _mode is Mode.Idle or Mode.Compact)
                SetMode(Mode.Expanded);
        };
        _collapse.Tick += (_, _) =>
        {
            _collapse.Stop();
            if (!IsHovered() && _mode == Mode.Expanded)
                SetMode(Wanted());
        };
        _transient.Tick += (_, _) =>
        {
            _transient.Stop();
            if (_mode is Mode.Level or Mode.Message)
                SetMode(Wanted());
        };
        _poll.Tick += (_, _) => Poll();
        _live.Tick += (_, _) => UpdateLive();
        _meter.Tick += (_, _) => FollowMusic();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            style |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            style &= ~Native.WS_EX_APPWINDOW;
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(style));
            Place();
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place, DispatcherPriority.Background);
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(Place);
        Loaded += async (_, _) => await ConnectAsync();

        Brightness.Changed += level => ShowLevel(VolumeBar.BrightnessGlyph, level, false);
        AgentMonitor.Updated += OnToolsChanged;
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (value)
            {
                Show();
                _poll.Start();
                SetMode(Wanted());
            }
            else
            {
                _poll.Stop();
                SetMode(Mode.Hidden);
                Hide();
            }
        }
    }

    /// <summary>Keep the small pill on screen even when there is nothing to show.</summary>
    public bool AlwaysShow
    {
        get => _alwaysShow;
        set
        {
            _alwaysShow = value;
            if (_mode is Mode.Hidden or Mode.Idle or Mode.Compact)
                SetMode(Wanted());
        }
    }

    /// <summary>
    /// Centres the window on the main screen in physical pixels: WPF's own
    /// coordinates follow the system DPI, which can differ from the screen's.
    /// </summary>
    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out var rect))
            return;
        var screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        var width = rect.Right - rect.Left;
        Native.SetWindowPos(hwnd, new IntPtr(-1), screen.Left + (screen.Width - width) / 2, screen.Top, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    // ---------- layout ----------

    private void BuildCompact()
    {
        for (var i = 0; i < 5; i++)
            _compact.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        _compact.Margin = new Thickness(8, 0, 14, 0);

        _miniCover.Margin = new Thickness(0, 0, 10, 0);
        _miniCover.VerticalAlignment = VerticalAlignment.Center;
        _compact.Children.Add(_miniCover);
        Grid.SetColumn(_miniTitle, 1);
        _compact.Children.Add(_miniTitle);

        for (var i = 0; i < 4; i++)
        {
            _bars.Children.Add(new Rectangle
            {
                Width = 3,
                Height = 14,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Fill = Theme.Accent,
                Margin = new Thickness(1.2, 0, 1.2, 0),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 0.25),
            });
        }
        Grid.SetColumn(_bars, 2);
        _compact.Children.Add(_bars);

        Grid.SetColumn(_miniAgent, 3);
        _compact.Children.Add(_miniAgent);
        Grid.SetColumn(_miniMetrics, 4);
        _compact.Children.Add(_miniMetrics);
    }

    private void BuildExpanded()
    {
        _expanded.Margin = new Thickness(20, 12, 20, 16);
        _expanded.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _expanded.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(_heading);
        header.Children.Add(_clock);
        _expanded.Children.Add(header);

        // ---- player ----
        _player.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _player.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _player.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _player.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _cover.Margin = new Thickness(0, 0, 18, 0);
        _cover.VerticalAlignment = VerticalAlignment.Top;
        _player.Children.Add(_cover);

        _progress.Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.Press });
        _progress.Children.Add(_progressFill);
        _progress.MouseLeftButtonUp += (_, e) => Seek(e.GetPosition(_progress).X / Math.Max(1, _progress.ActualWidth));
        _times.Children.Add(_elapsed);
        _times.Children.Add(_remaining);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(_prev);
        buttons.Children.Add(_play);
        buttons.Children.Add(_next);

        var info = new StackPanel();
        info.Children.Add(_title);
        info.Children.Add(_artist);
        info.Children.Add(_progress);
        info.Children.Add(_times);
        info.Children.Add(buttons);
        Grid.SetColumn(info, 1);
        _player.Children.Add(info);

        var bottom = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.Children.Add(_playerVolume);
        var outputRow = new StackPanel { Orientation = Orientation.Horizontal };
        var outputGlyph = Theme.Glyph("", 13);
        outputGlyph.Margin = new Thickness(0, 0, 6, 0);
        outputRow.Children.Add(outputGlyph);
        outputRow.Children.Add(_outputName);
        var output = Theme.Pressable(outputRow, NextOutput, new Thickness(8, 5, 8, 5));
        output.ToolTip = "Bấm để chuyển loa/tai nghe";
        output.Margin = new Thickness(8, 0, -8, 0);
        Grid.SetColumn(output, 1);
        bottom.Children.Add(output);
        Grid.SetRow(bottom, 1);
        Grid.SetColumnSpan(bottom, 2);
        _player.Children.Add(bottom);

        Grid.SetRow(_player, 1);
        _expanded.Children.Add(_player);

        // ---- nothing playing ----
        _controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        _controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var idleCard = new Grid();
        idleCard.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        idleCard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var note = Cover(44, 10);
        note.Child = Theme.Glyph("", 18);
        note.Margin = new Thickness(0, 0, 12, 0);
        idleCard.Children.Add(note);
        var idleText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        idleText.Children.Add(new TextBlock { Text = "Không có nhạc đang phát", FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        idleText.Children.Add(new TextBlock { Text = "Mở Spotify, YouTube… để điều khiển ở đây", FontSize = 11, Foreground = Theme.Muted, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(idleText, 1);
        idleCard.Children.Add(idleText);
        var card = new Border { Background = CardBrush, CornerRadius = new CornerRadius(14), Padding = new Thickness(12), Child = idleCard, Margin = new Thickness(0, 0, 12, 0) };
        _controls.Children.Add(card);

        var levels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        levels.Children.Add(_controlsVolume);
        levels.Children.Add(_brightness);
        Grid.SetColumn(levels, 1);
        _controls.Children.Add(levels);

        var tiles = new UniformGrid { Columns = 4, Margin = new Thickness(-4, 12, -4, 0) };
        foreach (var tile in new[] { _tileMixer, _tileSnip, _tileShutdown, _tileRestart })
            tiles.Children.Add(tile);
        Grid.SetRow(tiles, 1);
        Grid.SetColumnSpan(tiles, 2);
        _controls.Children.Add(tiles);

        Grid.SetRow(_controls, 1);
        _expanded.Children.Add(_controls);
    }

    private void BuildLevel()
    {
        _level.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _level.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _level.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _level.Margin = new Thickness(16, 0, 14, 0);

        _levelGlyph.Margin = new Thickness(0, 0, 12, 0);
        _level.Children.Add(_levelGlyph);
        var track = new Grid { VerticalAlignment = VerticalAlignment.Center };
        track.Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Theme.Press });
        track.Children.Add(_levelFill);
        Grid.SetColumn(track, 1);
        _level.Children.Add(track);
        track.SizeChanged += (_, _) => _levelFill.Width = track.ActualWidth * (_levelFill.Tag is double d ? d : 0);
        Grid.SetColumn(_levelText, 2);
        _level.Children.Add(_levelText);
    }

    private void BuildCalendar()
    {
        _calendar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _calendar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _calendar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(_calendarTitle);
        var prev = Theme.Pressable(Theme.Glyph("\uE76B", 11), () => { _month = _month.AddMonths(-1); DrawCalendar(); }, new Thickness(8, 4, 8, 4));
        var today = Theme.Pressable(new TextBlock { Text = "Hôm nay", FontSize = 11.5, Foreground = Theme.Muted }, () => { _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); DrawCalendar(); }, new Thickness(8, 4, 8, 4));
        var next = Theme.Pressable(Theme.Glyph("\uE76C", 11), () => { _month = _month.AddMonths(1); DrawCalendar(); }, new Thickness(8, 4, 8, 4));
        Grid.SetColumn(prev, 1);
        Grid.SetColumn(today, 2);
        Grid.SetColumn(next, 3);
        header.Children.Add(prev);
        header.Children.Add(today);
        header.Children.Add(next);
        _calendar.Children.Add(header);
        var names = new UniformGrid { Columns = 7 };
        foreach (var name in new[] { "T2", "T3", "T4", "T5", "T6", "T7", "CN" })
            names.Children.Add(new TextBlock { Text = name, FontSize = 11, Foreground = Theme.Muted, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) });
        Grid.SetRow(names, 1);
        _calendar.Children.Add(names);
        Grid.SetRow(_days, 2);
        _calendar.Children.Add(_days);
        Grid.SetRow(_calendar, 1);
        _expanded.Children.Add(_calendar);
        DrawCalendar();
    }

    private void DrawCalendar()
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("vi-VN");
        var title = _month.ToString("MMMM yyyy", culture);
        _calendarTitle.Text = char.ToUpper(title[0], culture) + title[1..];
        _days.Children.Clear();
        // Weeks start on Monday, as Vietnamese calendars do.
        var offset = ((int)_month.DayOfWeek + 6) % 7;
        var start = _month.AddDays(-offset);
        for (var i = 0; i < 42; i++)
        {
            var day = start.AddDays(i);
            if (i >= 35 && day.Month != _month.Month)
                break;
            var isToday = day == DateTime.Today;
            var inMonth = day.Month == _month.Month;
            _days.Children.Add(new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = isToday ? Theme.Accent : Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 1),
                Child = new TextBlock
                {
                    Text = day.Day.ToString(),
                    FontSize = 12,
                    FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = !inMonth ? Theme.Hover : day.DayOfWeek == DayOfWeek.Sunday && !isToday ? new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x7A)) : Theme.Text,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
        }
        if (_mode == Mode.Expanded && IsCalendar)
            Animate(HeightProperty, ExpandedHeight());
    }

    private double ExpandedHeight()
    {
        var page = ViewHeight();
        if (page > 0)
            return page;
        if (IsCalendar)
            return 96 + 30 * (_days.Children.Count / 7);
        return (_hasMedia ? 232.0 : 226.0) + StripHeight();
    }

    /// <summary>CPU/RAM or network speed in the resting pill, like menu-bar readouts on a Mac.</summary>
    private void UpdateIdleText()
    {
        var text = "";
        if (SystemMonitor.Last is { } s)
        {
            if (Store.S.IslandMetrics == "system")
                text = $"CPU {s.Cpu:0}%   RAM {(s.MemoryTotal == 0 ? 0 : 100.0 * s.MemoryUsed / s.MemoryTotal):0}%";
            else if (Store.S.IslandMetrics == "network")
                text = $"↓ {SystemMonitor.Speed(s.DownloadPerSecond)}   ↑ {SystemMonitor.Speed(s.UploadPerSecond)}";
        }
        SystemMonitor.IslandWatching = Store.S.IslandMetrics != "off" && _enabled;
        var hadText = _idleText.Text.Length > 0;
        _idleText.Text = text;
        _miniMetrics.Text = text.Replace("   ", "  ");
        // A readout turned on or off shows at once, at rest and in the compact pill.
        var wanted = Wanted();
        if (_mode is Mode.Hidden or Mode.Idle or Mode.Compact && wanted != _mode)
            SetMode(wanted);
        else if (_mode == Mode.Idle && hadText != (text.Length > 0))
            SetMode(Mode.Idle);
        else if (_mode == Mode.Compact)
        {
            UpdateCompact();
            if (Math.Abs(_pill.Width - CompactWidth()) > 0.5)
                Animate(WidthProperty, CompactWidth());
        }
    }

    private double IdleWidth() => Store.S.IslandMetrics switch
    {
        "system" when _idleText.Text.Length > 0 => 190,
        "network" when _idleText.Text.Length > 0 => 236,
        _ => 140,
    };

    private void BuildMessage()
    {
        _message.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _message.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _message.Margin = new Thickness(16, 0, 18, 0);
        _messageGlyph.Foreground = Theme.Accent;
        _messageGlyph.Margin = new Thickness(0, 0, 10, 0);
        _message.Children.Add(_messageGlyph);
        Grid.SetColumn(_messageText, 1);
        _message.Children.Add(_messageText);
    }

    private static Border Cover(double size, double radius) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(radius),
        Background = Theme.Hover,
    };

    private Border SideButton(string glyph, string tooltip, Action onClick)
    {
        var icon = Theme.Glyph(glyph, 16);
        // Plain Unicode symbols (like ✦) are not in the icon font; draw them with Segoe UI Symbol.
        if (glyph.Length > 0 && glyph[0] < '\uE000')
        {
            icon.FontFamily = new FontFamily("Segoe UI Symbol");
            icon.FontSize = 17;
            icon.Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x57));
        }
        var button = Theme.Pressable(icon, onClick, new Thickness(0));
        button.Width = 42;
        button.Height = 42;
        button.CornerRadius = new CornerRadius(21);
        button.Margin = new Thickness(0, 0, 0, 10);
        button.ToolTip = tooltip;
        // Pressable resets to transparent on leave; this keeps the black disc.
        var disc = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(21), Background = PillBrush, BorderBrush = RimBrush, BorderThickness = new Thickness(1) };
        var holder = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        button.Margin = new Thickness(0);
        holder.Children.Add(disc);
        holder.Children.Add(button);
        var wrapper = new Border { Child = holder, Tag = disc };
        return wrapper;
    }

    private void ShowSideButtons(bool show)
    {
        foreach (var panel in new[] { _leftButtons, _rightButtons })
        {
            panel.Visibility = show ? Visibility.Visible : Visibility.Hidden;
            if (show)
                panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        }
    }

    // ---------- hover ----------

    private bool IsHovered() => _pill.IsMouseOver || _leftButtons.IsMouseOver || _rightButtons.IsMouseOver;

    private void HoverStarted()
    {
        _collapse.Stop();
        if (_mode is Mode.Idle or Mode.Compact)
            _expandDelay.Start();
    }

    private void HoverEnded()
    {
        _expandDelay.Stop();
        _collapse.Start();
    }

    // ---------- state ----------

    private Mode Wanted()
    {
        if (!_enabled)
            return Mode.Hidden;
        if (_hasMedia && _playing || AgentShown())
            return Mode.Compact;
        // A readout the person chose keeps the pill on screen at rest.
        return _alwaysShow || Store.S.IslandMetrics != "off" ? Mode.Idle : Mode.Hidden;
    }

    private void SetMode(Mode mode)
    {
        if (!_enabled)
            mode = Mode.Hidden;
        var changed = mode != _mode;
        if (mode != Mode.Expanded)
            // The AI Agents page is sticky: it stays until the back arrow is pressed.
            if (_view != ViewAgents)
                _view = "";
        _mode = mode;
        _compact.Visibility = mode == Mode.Compact ? Visibility.Visible : Visibility.Collapsed;
        _expanded.Visibility = mode == Mode.Expanded ? Visibility.Visible : Visibility.Collapsed;
        _idleText.Visibility = mode == Mode.Idle ? Visibility.Visible : Visibility.Collapsed;
        _level.Visibility = mode == Mode.Level ? Visibility.Visible : Visibility.Collapsed;
        _message.Visibility = mode == Mode.Message ? Visibility.Visible : Visibility.Collapsed;
        if (changed)
            ShowSideButtons(mode == Mode.Expanded);
        UpdateCompact();

        if (mode == Mode.Expanded)
        {
            OnExpanded();
            _live.Start();
        }
        else
        {
            _live.Stop();
        }

        if (mode == Mode.Hidden)
        {
            _pill.Visibility = Visibility.Collapsed;
            return;
        }
        var (width, height, radius) = mode switch
        {
            Mode.Idle => (IdleWidth(), _idleText.Text.Length > 0 ? 28.0 : 24.0, 14.0),
            Mode.Compact => (CompactWidth(), 34.0, 17.0),
            Mode.Expanded => (ExpandedWidth, ExpandedHeight(), 30.0),
            Mode.Message => (_messageWidth, 38.0, 19.0),
            _ => (300.0, 38.0, 19.0),
        };
        var wasHidden = _pill.Visibility == Visibility.Collapsed;
        _pill.Visibility = Visibility.Visible;
        _pill.CornerRadius = new CornerRadius(0, 0, radius, radius);
        if (wasHidden)
        {
            // Grows out of the top edge.
            _pill.Width = 90;
            _pill.Height = 6;
        }
        Animate(WidthProperty, width);
        Animate(HeightProperty, height);
    }

    private void Animate(DependencyProperty property, double to)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new BackEase { Amplitude = 0.22, EasingMode = EasingMode.EaseOut },
        };
        animation.Completed += (_, _) =>
        {
            // Hand the value back to the property so later animations start from it.
            _pill.BeginAnimation(property, null);
            _pill.SetValue(property, to);
        };
        _pill.BeginAnimation(property, animation);
    }

    private static bool AgentShown() => Store.S.AgentOnIsland && AgentMonitor.WorkingCount > 0;

    private double CompactWidth()
    {
        double width = _hasMedia && _playing ? 250 : 80;
        if (AgentShown()) width += AgentMonitor.WorkingCount > 1 ? 44 : 30;
        // The readout keeps its room in the compact pill too, not only at rest.
        if (_miniMetrics.Text.Length > 0) width += Store.S.IslandMetrics == "network" ? 166 : 120;
        return width;
    }

    private void UpdateCompact()
    {
        var music = _hasMedia && _playing;
        _miniTitle.Visibility = music ? Visibility.Visible : Visibility.Collapsed;
        _miniCover.Visibility = _miniTitle.Visibility;
        _bars.Visibility = _miniTitle.Visibility;
        var agents = AgentShown() ? AgentMonitor.WorkingCount : 0;
        _miniAgent.Visibility = agents > 0 ? Visibility.Visible : Visibility.Collapsed;
        _miniAgent.Text = agents > 1 ? $"✳ {agents}" : "✳";
        _miniMetrics.Visibility = _miniMetrics.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_mode == Mode.Compact && music)
            _meter.Start();
        else
            _meter.Stop();
    }

    /// <summary>The agents changed: the compact pill shows how many are working.</summary>
    private void OnToolsChanged()
    {
        UpdateTiles();
        if (_mode is Mode.Hidden or Mode.Idle or Mode.Compact)
        {
            var wanted = Wanted();
            if (wanted != _mode)
                SetMode(wanted);
            else if (_mode == Mode.Compact)
            {
                UpdateCompact();
                if (Math.Abs(_pill.Width - CompactWidth()) > 0.5)
                    Animate(WidthProperty, CompactWidth());
            }
        }
    }

    private void UpdateTiles()
    {
        _tileMixer.Set(false, "Từng app");
        _tileSnip.Set(false, "Scratchpad");
        if (_armed == null)
        {
            _tileShutdown.Set(false, "Windows");
            _tileRestart.Set(false, "Windows");
        }
        SetSideActive(_sideAgents, _view == ViewAgents);
    }

    private static void SetSideActive(Border side, bool active)
    {
        if (side.Tag is Border disc)
            disc.Background = active ? ActiveBrush : PillBrush;
    }

    private void OnExpanded()
    {
        ApplyView();
        UpdateTiles();
        UpdateLive();
        try
        {
            var current = Audio.DefaultId();
            _outputName.Text = Audio.Outputs().FirstOrDefault(o => o.Id == current)?.Name ?? "Không có loa";
        }
        catch (Exception ex)
        {
            Store.Log("island output read failed: " + ex.Message);
        }
        if (!_hasMedia)
        {
            Brightness.Read(monitors => Dispatcher.BeginInvoke(() =>
            {
                _brightnessKeys.Clear();
                _brightnessKeys.AddRange(monitors.Select(m => m.Key));
                _brightness.Show(monitors.Count == 0 ? null : (monitors.Average(m => m.Level), false));
                _brightness.ToolTip = monitors.Count == 0 ? "Màn hình không hỗ trợ DDC/CI" : "Độ sáng mọi màn hình: " + string.Join(", ", monitors.Select(m => m.Name));
            }));
        }
    }

    /// <summary>Refreshes what moves while the panel is open: clock, progress, volume.</summary>
    private void UpdateLive()
    {
        _clock.Text = DateTime.Now.ToString("HH:mm");
        UpdateAgentElapsed();
        _playerVolume.Refresh();
        _controlsVolume.Refresh();
        if (_session == null || !_hasMedia)
            return;
        try
        {
            var timeline = _session.GetTimelineProperties();
            var duration = timeline.EndTime - timeline.StartTime;
            if (duration <= TimeSpan.Zero)
            {
                _progress.Visibility = _times.Visibility = Visibility.Collapsed;
                return;
            }
            _progress.Visibility = _times.Visibility = Visibility.Visible;
            var position = timeline.Position - timeline.StartTime;
            if (_playing)
                position += DateTimeOffset.Now - timeline.LastUpdatedTime;
            if (position > duration) position = duration;
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            _progressFill.Width = _progress.ActualWidth * (position.TotalSeconds / duration.TotalSeconds);
            _elapsed.Text = Time(position);
            _remaining.Text = "-" + Time(duration - position);
        }
        catch
        {
            _progress.Visibility = _times.Visibility = Visibility.Collapsed;
        }
    }

    private static string Time(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private void Seek(double fraction)
    {
        if (_session == null)
            return;
        try
        {
            var timeline = _session.GetTimelineProperties();
            var duration = timeline.EndTime - timeline.StartTime;
            if (duration <= TimeSpan.Zero)
                return;
            var target = timeline.StartTime + TimeSpan.FromSeconds(duration.TotalSeconds * Math.Clamp(fraction, 0, 1));
            _ = _session.TryChangePlaybackPositionAsync(target.Ticks).AsTask();
        }
        catch (Exception ex)
        {
            Store.Log("seek failed: " + ex.Message);
        }
    }

    private void NextOutput()
    {
        try
        {
            _outputName.Text = Audio.NextOutput() ?? "Không có loa";
        }
        catch (Exception ex)
        {
            Store.Log("island output switch failed: " + ex.Message);
        }
    }

    // ---------- events from outside ----------

    /// <summary>Shows a short line of text for a few seconds (download finished, link cleaned...).</summary>
    public void Notify(string glyph, string text, double seconds = 2.5)
    {
        if (!_enabled || _mode == Mode.Expanded || ForegroundIsFullScreen())
            return;
        _messageGlyph.Text = glyph;
        _messageText.Text = text;
        _messageText.ToolTip = text;
        var measure = new TextBlock { Text = text, FontSize = 12.5, FontWeight = FontWeights.SemiBold, FontFamily = Theme.Font };
        measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _messageWidth = Math.Clamp(measure.DesiredSize.Width + 70, 160, 460);
        SetMode(Mode.Message);
        _transient.Stop();
        _transient.Interval = TimeSpan.FromSeconds(seconds);
        _transient.Start();
    }

    private void ShowLevel(string glyph, float level, bool muted)
    {
        // While the panel is open its own bars show the level.
        if (!_enabled || _mode == Mode.Expanded || ForegroundIsFullScreen())
            return;
        _levelGlyph.Text = glyph;
        var shown = muted ? 0 : level;
        _levelFill.Tag = (double)shown;
        if (_levelFill.Parent is FrameworkElement track)
            _levelFill.Width = track.ActualWidth * shown;
        _levelText.Text = muted ? "Tắt" : $"{Math.Round(level * 100)}%";
        SetMode(Mode.Level);
        _transient.Stop();
        _transient.Interval = TimeSpan.FromSeconds(1.6);
        _transient.Start();
    }

    private void Poll()
    {
        // A full-screen video or game owns the top of the screen.
        if (ForegroundIsFullScreen())
        {
            if (_mode != Mode.Hidden)
                SetMode(Mode.Hidden);
            return;
        }
        if (_mode == Mode.Hidden && Wanted() != Mode.Hidden)
            SetMode(Wanted());

        try
        {
            if (Audio.Volume() is { } volume)
            {
                if (_lastVolume is { } last && (Math.Abs(last - volume.Level) > 0.004 || _lastMuted != volume.Muted))
                    ShowLevel(volume.Muted || volume.Level == 0 ? VolumeBar.MutedGlyph : VolumeBar.VolumeGlyph, volume.Level, volume.Muted);
                _lastVolume = volume.Level;
                _lastMuted = volume.Muted;
            }
        }
        catch (Exception ex)
        {
            Store.Log("island volume read failed: " + ex.Message);
            _poll.Interval = TimeSpan.FromSeconds(2);
        }
    }

    /// <summary>Moves the bars with the level of what is actually playing.</summary>
    private void FollowMusic()
    {
        var peak = Math.Clamp(Audio.Peak() * 1.6, 0, 1);
        foreach (Rectangle bar in _bars.Children)
        {
            var scale = (ScaleTransform)bar.RenderTransform;
            var target = 0.2 + 0.8 * peak * (0.6 + 0.4 * _random.NextDouble());
            // Rise quickly, fall gently.
            scale.ScaleY = target > scale.ScaleY ? target : scale.ScaleY * 0.8 + target * 0.2;
        }
    }

    private static bool ForegroundIsFullScreen()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out var rect))
            return false;
        var name = new System.Text.StringBuilder(64);
        GetClassName(hwnd, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd")
            return false;
        var screen = System.Windows.Forms.Screen.FromHandle(hwnd).Bounds;
        return rect.Left <= screen.Left && rect.Top <= screen.Top && rect.Right >= screen.Right && rect.Bottom >= screen.Bottom;
    }

    // ---------- media ----------

    private async Task ConnectAsync()
    {
        try
        {
            _manager = await MediaManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => Dispatcher.Invoke(() => Attach(_manager.GetCurrentSession()));
            Attach(_manager.GetCurrentSession());
        }
        catch (Exception ex)
        {
            Store.Log("island media unavailable: " + ex.Message);
        }
    }

    private void Attach(MediaSession? session)
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaChanged;
            _session.PlaybackInfoChanged -= OnPlaybackChanged;
        }
        _session = session;
        _hasMedia = session != null;
        if (session == null)
        {
            _playing = false;
            MediaStateChanged();
            return;
        }
        session.MediaPropertiesChanged += OnMediaChanged;
        session.PlaybackInfoChanged += OnPlaybackChanged;
        _ = RefreshMediaAsync(session);
        RefreshPlayback(session);
    }

    private void OnMediaChanged(MediaSession sender, MediaPropertiesChangedEventArgs args) =>
        Dispatcher.InvokeAsync(() => RefreshMediaAsync(sender));

    private void OnPlaybackChanged(MediaSession sender, PlaybackInfoChangedEventArgs args) =>
        Dispatcher.InvokeAsync(() => RefreshPlayback(sender));

    private async Task RefreshMediaAsync(MediaSession session)
    {
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (session != _session)
                return;
            _title.Text = _miniTitle.Text = string.IsNullOrWhiteSpace(props.Title) ? "Không rõ tên bài" : props.Title;
            _artist.Text = string.IsNullOrWhiteSpace(props.Artist) ? MediaInfo.AppName(session) : props.Artist;
            var image = props.Thumbnail == null ? null : await MediaInfo.LoadThumbnailAsync(props.Thumbnail);
            foreach (var cover in new[] { _cover, _miniCover })
            {
                cover.Background = image == null ? Theme.Hover : new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                cover.Child = image == null ? Theme.Glyph("", cover.Width / 3) : null;
            }
        }
        catch (Exception ex)
        {
            Store.Log("island media properties failed: " + ex.Message);
        }
    }

    private void RefreshPlayback(MediaSession session)
    {
        if (session != _session)
            return;
        var info = session.GetPlaybackInfo();
        _playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        _playGlyph.Text = _playing ? "" : "";
        _prev.IsEnabled = info.Controls.IsPreviousEnabled;
        _next.IsEnabled = info.Controls.IsNextEnabled;
        MediaStateChanged();
    }

    private void MediaStateChanged()
    {
        if (_mode == Mode.Expanded)
        {
            // Pausing keeps the open panel as it is; a session coming or going redraws it.
            var wantPlayer = _hasMedia && _view == "";
            if ((_player.Visibility == Visibility.Visible) != wantPlayer)
                SetMode(Mode.Expanded);
            UpdateCompact();
            return;
        }
        if (_mode is Mode.Level or Mode.Message)
            return;
        var wanted = Wanted();
        if (wanted != _mode)
            SetMode(wanted);
        else if (_mode == Mode.Compact)
        {
            UpdateCompact();
            Animate(WidthProperty, CompactWidth());
        }
    }

    private void Send(Func<MediaSession, Windows.Foundation.IAsyncOperation<bool>> command)
    {
        if (_session is { } session)
            _ = command(session).AsTask();
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    /// <summary>A small square button in the open panel: icon, label and a state line.</summary>
    private Tile? _armed;
    private DispatcherTimer? _disarm;

    /// <summary>
    /// A tile that shuts the PC down or restarts it. The first press only arms it, saying so on the tile; a second
    /// press within four seconds runs Windows' own shutdown, which closes the apps as a Start menu shutdown does.
    /// </summary>
    private Tile ConfirmTile(string glyph, string label, string ask, string mode)
    {
        Tile? tile = null;
        tile = new Tile(glyph, label, () =>
        {
            if (_armed == tile)
            {
                _disarm?.Stop();
                _armed = null;
                Store.SaveNow();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown.exe", $"{mode} /t 0") { UseShellExecute = false, CreateNoWindow = true });
                return;
            }
            Disarm();
            _armed = tile;
            tile!.Set(true, ask);
            _disarm ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _disarm.Tick -= OnDisarm;
            _disarm.Tick += OnDisarm;
            _disarm.Stop();
            _disarm.Start();
        });
        return tile;
    }

    private void OnDisarm(object? sender, EventArgs e) => Disarm();

    private void Disarm()
    {
        _disarm?.Stop();
        _armed = null;
        _tileShutdown?.Set(false, "Windows");
        _tileRestart?.Set(false, "Windows");
    }

    private sealed class Tile : Border
    {
        private readonly TextBlock _detail = new() { FontSize = 10, Foreground = Theme.Muted, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        private bool _active;

        public Tile(string glyph, string label, Action onClick)
        {
            Margin = new Thickness(4);
            Padding = new Thickness(4, 8, 4, 7);
            CornerRadius = new CornerRadius(12);
            Background = CardBrush;
            Cursor = Cursors.Hand;
            var stack = new StackPanel();
            var icon = Theme.Glyph(glyph, 16);
            icon.Margin = new Thickness(0, 0, 0, 4);
            stack.Children.Add(icon);
            stack.Children.Add(new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            stack.Children.Add(_detail);
            Child = stack;

            MouseEnter += (_, _) => Background = _active ? ActiveBrush : Theme.Press;
            MouseLeave += (_, _) => Background = _active ? ActiveBrush : CardBrush;
            MouseLeftButtonUp += (_, e) =>
            {
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
        }

        public void Set(bool active, string detail)
        {
            _active = active;
            _detail.Text = detail;
            Background = active ? ActiveBrush : IsMouseOver ? Theme.Press : CardBrush;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int size);
}
