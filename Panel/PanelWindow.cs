using System;
using System.Collections.Generic;
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

namespace LumenLite.Panel;

/// <summary>A screen of the panel. Pages are kept and reused; Shown/Hidden start and stop their timers.</summary>
internal abstract class Page : StackPanel
{
    protected Page(PanelWindow host)
    {
        Host = host;
    }

    protected PanelWindow Host { get; }

    /// <summary>Section title for tabs; heading next to the back arrow for sub-pages.</summary>
    public abstract string Title { get; }

    public virtual void Shown() { }

    public virtual void Hidden() { }

    private readonly List<(Action Subscribe, Action Unsubscribe)> _live = new();
    private bool _subscribed;

    /// <summary>
    /// Hooks a static event only while the page is shown, so a closed panel is not
    /// kept alive by SystemMonitor and the like.
    /// </summary>
    protected void WhileShown(Action subscribe, Action unsubscribe) => _live.Add((subscribe, unsubscribe));

    internal void Attach()
    {
        if (_subscribed) return;
        _subscribed = true;
        foreach (var (subscribe, _) in _live) subscribe();
    }

    internal void Detach()
    {
        if (!_subscribed) return;
        _subscribed = false;
        foreach (var (_, unsubscribe) in _live) unsubscribe();
    }
}

/// <summary>
/// LumenLite's control panel: a nameplate, scrolling content in cards, a flat row
/// of tab icons and Settings / Quit at the bottom. Opens above the tray and hides
/// when it loses focus.
/// </summary>
internal sealed class PanelWindow : Window
{
    private const double PanelWidth = 400, SheetRadius = 12;

    private readonly Border _sheet;
    private readonly UniformGrid _tabs = new() { Rows = 1 };
    private readonly ContentControl _content = new();
    private readonly ScrollViewer _scroll;
    private readonly Grid _subHeader = new() { Margin = new Thickness(-6, 12, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _subTitle = Kit.Label("", 15, true);
    private readonly Grid _overlay = new() { Visibility = Visibility.Collapsed };
    /// <summary>A tab's page is only built the first time the tab is opened.</summary>
    private sealed class Tab
    {
        public required Func<Page> Create;
        public Page? Page;
        public required Border Button;
        public required TextBlock Icon;
        public required Border Mark;
        public Page Get() => Page ??= Create();
    }

    private readonly List<Tab> _tabPages = new();
    private readonly Stack<Page> _stack = new();
    private Page? _current;
    private int _tabIndex;
    // Shared by every panel instance: a hidden panel is discarded and a new one built on the next open.
    private static DateTime _hiddenAt;
    private DateTime _shownAt;
    private readonly DispatcherTimer _outsideClick = new() { Interval = TimeSpan.FromMilliseconds(120) };

    /// <summary>A mouse button is held with the pointer outside the panel.</summary>
    private bool MouseDownOutside()
    {
        if ((GetAsyncKeyState(0x01) & 0x8000) == 0 && (GetAsyncKeyState(0x02) & 0x8000) == 0 && (GetAsyncKeyState(0x04) & 0x8000) == 0)
            return false;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!GetCursorPos(out var cursor) || !Native.GetWindowRect(hwnd, out var rect))
            return false;
        return cursor.X < rect.Left || cursor.X >= rect.Right || cursor.Y < rect.Top || cursor.Y >= rect.Bottom;
    }

    /// <summary>Alt (Alt+Tab), the Windows key or Esc: the user moved on with the keyboard.</summary>
    private static bool SwitchKeyDown() =>
        (GetAsyncKeyState(0x12) & 0x8000) != 0 || (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    public Action? OpenSettings { get; set; }

    /// <summary>Builds the panel again in the theme just chosen.</summary>
    public Action? Retheme { get; set; }

    /// <summary>Raised when the panel hides; the app then discards it to free its memory.</summary>
    public event Action? Dismissed;

    /// <summary>The app that was in front before the panel opened ("all but the current app").</summary>
    public int PreviousForegroundPid { get; private set; }
    public Action? Quit { get; set; }

    public PanelWindow()
    {
        Title = "LumenLite";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = PanelWidth + 24;
        SizeToContent = SizeToContent.Height;
        FontFamily = Theme.Font;
        Foreground = Kit.Text;
        UseLayoutRounding = true;

        var logo = Nameplate();

        // Flat: a hairline above the icons, nothing around them.
        var tabBar = new Border
        {
            BorderBrush = Kit.Divider,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 6, 0, 0),
            Child = _tabs,
        };

        var back = Kit.Clickable(Kit.Glyph("", 13, Kit.Text), Back, new Thickness(6, 4, 8, 4));
        _subHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _subHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _subHeader.Children.Add(back);
        Grid.SetColumn(_subTitle, 1);
        _subHeader.Children.Add(_subTitle);

        _scroll = new ScrollViewer
        {
            Content = _content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 640,
            PanningMode = PanningMode.VerticalOnly,
        };

        var footer = new Grid { Margin = new Thickness(0, 4, 0, -6) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.Children.Add(Kit.Quiet("", "Cài đặt", () => OpenSettings?.Invoke()));
        var quit = Kit.Quiet("", "Thoát", () => Confirm("Thoát LumenLite?", "Mọi widget, Dynamic Island và phím tắt sẽ tắt cho tới lần mở sau.", "Thoát", () => Quit?.Invoke()));
        Grid.SetColumn(quit, 2);
        footer.Children.Add(quit);

        // The panel sits on the bottom edge and grows upwards, so the tab bar lives at the
        // bottom: switching tabs then never moves it from under the mouse.
        tabBar.Margin = new Thickness(0, 8, 0, 0);
        var body = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
        body.Children.Add(logo);
        body.Children.Add(_subHeader);
        body.Children.Add(_scroll);
        body.Children.Add(tabBar);
        body.Children.Add(footer);

        var layers = new Grid();
        layers.Children.Add(body);
        layers.Children.Add(_overlay);

        _sheet = new Border
        {
            Background = Kit.Sheet,
            BorderBrush = Kit.SheetBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(SheetRadius),
            Child = layers,
            Margin = new Thickness(12),
        };
        Content = _sheet;

        // Closes like a flyout: when the user clicks outside it or switches away with the
        // keyboard. Losing focus alone is not enough: Windows sometimes hands the focus back
        // to the previous window right after the panel opens, with nobody touching anything.
        Deactivated += (_, _) =>
        {
            if (_overlay.Visibility == Visibility.Visible || Mouse.Captured != null)
                return;
            Dispatcher.BeginInvoke(() =>
            {
                if (!IsActive && !HasOpenPopup() && (MouseDownOutside() || SwitchKeyDown()))
                {
                    HidePanel();
                }
            }, DispatcherPriority.Background);
        };
        // Clicks elsewhere also close it while it is not the active window (focus was taken away).
        _outsideClick.Tick += (_, _) =>
        {
            if (IsVisible && !IsActive && !HasOpenPopup() && _overlay.Visibility != Visibility.Visible && MouseDownOutside())
            {
                HidePanel();
            }
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _outsideClick.Start();
            else _outsideClick.Stop();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (_overlay.Visibility == Visibility.Visible)
                    _overlay.Visibility = Visibility.Collapsed;
                else if (_stack.Count > 0)
                    Back();
                else
                    HidePanel();
                e.Handled = true;
            }
        };
        // After a size change the native window catches up a moment later; place it then.
        SizeChanged += (_, _) => Dispatcher.BeginInvoke(Place, DispatcherPriority.Render);
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            style |= Native.WS_EX_TOOLWINDOW;
            style &= ~Native.WS_EX_APPWINDOW;
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(style));
        };
    }

    /// <summary>Adds a tab with its icon; the first one added is shown first.</summary>
    public void AddTab(string glyph, string title, Func<Page> create)
    {
        var icon = Kit.Glyph(glyph, 16, Kit.Secondary);
        icon.Margin = new Thickness(0, 9, 0, 11);
        // The selected tab: its icon in ink, over a short orange bar.
        var mark = new Border { Width = 14, Height = 2, Background = Kit.Accent, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Hidden };
        var cell = new Grid();
        cell.Children.Add(icon);
        cell.Children.Add(mark);
        var index = _tabPages.Count;
        var button = Kit.Clickable(cell, () => SelectTab(index), new Thickness(0));
        button.ToolTip = title;
        button.Margin = new Thickness(1, 0, 1, 0);
        _tabs.Children.Add(button);
        _tabs.Columns = _tabs.Children.Count;
        _tabPages.Add(new Tab { Create = create, Button = button, Icon = icon, Mark = mark });
    }

    public void SelectTab(int index)
    {
        _stack.Clear();
        _tabIndex = index;
        for (var i = 0; i < _tabPages.Count; i++)
        {
            var tab = _tabPages[i];
            var selected = i == index;
            tab.Icon.Foreground = selected ? Kit.Text : Kit.Secondary;
            tab.Mark.Visibility = selected ? Visibility.Visible : Visibility.Hidden;
        }
        Show(_tabPages[index].Get(), sub: false);
    }

    /// <summary>Opens a sub-page with a back arrow.</summary>
    public void Push(Page page)
    {
        if (_current != null)
            _stack.Push(_current);
        Show(page, sub: true);
    }

    private void Back()
    {
        if (_stack.Count == 0)
            return;
        var previous = _stack.Pop();
        Show(previous, sub: _stack.Count > 0 || !_tabPages.Exists(t => t.Page == previous));
    }

    private void Show(Page page, bool sub)
    {
        if (_current != null)
        {
            _current.Hidden();
            _current.Detach();
        }
        _current = page;
        page.Attach();
        _subHeader.Visibility = sub ? Visibility.Visible : Visibility.Collapsed;
        _subTitle.Text = page.Title;
        if (!sub)
        {
            // Tabs carry their own uppercase title like "VOLUME MIXER".
            if (page.Children.Count == 0 || page.Children[0] is not FrameworkElement { Tag: "title" })
            {
                var title = Kit.Section(page.Title);
                title.Tag = "title";
                title.Margin = new Thickness(2, 14, 2, 8);
                page.Children.Insert(0, title);
            }
        }
        _content.Content = page;
        _scroll.ScrollToTop();
        page.Shown();
        page.Opacity = 0;
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
    }

    /// <summary>An in-panel confirmation sheet; a MessageBox would close the panel behind it.</summary>
    public void Confirm(string title, string message, string action, Action onConfirm, bool destructive = true)
    {
        _overlay.Children.Clear();
        _overlay.Background = Palette.Current.IsDark ? Kit.Solid(0xB8, 0x08, 0x08, 0x08) : Kit.Solid(0x70, 0x17, 0x17, 0x16);
        var stack = new StackPanel();
        stack.Children.Add(Kit.Label(title, 16, true));
        var text = Kit.Wrap(message, 13);
        text.Margin = new Thickness(0, 6, 0, 16);
        stack.Children.Add(text);
        var buttons = new Grid();
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.Children.Add(Kit.Button("", "Hủy", () => _overlay.Visibility = Visibility.Collapsed));
        var ok = Kit.Button("", action, () =>
        {
            _overlay.Visibility = Visibility.Collapsed;
            onConfirm();
        }, destructive ? Kit.Red : Kit.Ink, destructive ? Kit.OnRed : Kit.OnInk);
        Grid.SetColumn(ok, 2);
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);
        var card = new Border
        {
            Background = Kit.Card,
            BorderBrush = Kit.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Margin = new Thickness(24),
            VerticalAlignment = VerticalAlignment.Center,
            Child = stack,
        };
        _overlay.Children.Add(card);
        _overlay.Visibility = Visibility.Visible;
    }

    /// <summary>Shows a short note at the bottom of the panel.</summary>
    public void Toast(string text)
    {
        var toast = new Border
        {
            Background = Kit.Ink,
            CornerRadius = new CornerRadius(Kit.ControlRadius),
            Padding = new Thickness(14, 8, 14, 9),
            Margin = new Thickness(30, 0, 30, 70),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = Kit.Wrap(text, 12.5, Kit.OnInk),
            IsHitTestVisible = false,
        };
        var layers = (Grid)_sheet.Child;
        layers.Children.Add(toast);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            layers.Children.Remove(toast);
        };
        timer.Start();
    }

    public void Toggle()
    {
        if (IsVisible)
            HidePanel();
        // A click on the tray icon first takes focus away (hiding the panel); don't reopen it at once.
        else if (DateTime.UtcNow - _hiddenAt > TimeSpan.FromMilliseconds(300))
            ShowPanel();
    }

    public void ShowPanel(int? tab = null)
    {
        if (_tabPages.Count == 0)
            return;
        if (!IsVisible)
            PreviousForegroundPid = AppManager.ForegroundPid();
        if (tab is { } t)
            SelectTab(t);
        else if (_current == null)
            SelectTab(0);
        else
        {
            _current.Attach();
            _current.Shown();
        }
        _shownAt = DateTime.UtcNow;
        Show();
        Activate();
        Place();
        _sheet.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
    }

    public void HidePanel()
    {
        if (!IsVisible)
            return;
        _current?.Hidden();
        _current?.Detach();
        _overlay.Visibility = Visibility.Collapsed;
        _hiddenAt = DateTime.UtcNow;
        Hide();
        _stack.Clear();
        Dismissed?.Invoke();
    }

    /// <summary>Bottom-right of the main screen's work area, above the tray, in physical pixels.</summary>
    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
            return;
        // Size from WPF's own layout, scaled to pixels: GetWindowRect can still report the old size.
        var width = (int)Math.Ceiling(ActualWidth * target.TransformToDevice.M11);
        var height = (int)Math.Ceiling(ActualHeight * target.TransformToDevice.M22);
        var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        Native.SetWindowPos(hwnd, new IntPtr(-1), area.Right - width - 4, Math.Max(area.Top, area.Bottom - height - 4), 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    private static bool HasOpenPopup()
    {
        foreach (PresentationSource source in PresentationSource.CurrentSources)
            if (source.RootVisual is FrameworkElement { Parent: Popup { IsOpen: true } } || source.RootVisual?.GetType().Name == "PopupRoot")
                return true;
        return false;
    }

    /// <summary>
    /// The nameplate: the TD logo on a small black plate (it is drawn for a dark ground), and the product name
    /// and version in mono, as on the back of a device.
    /// </summary>
    private static FrameworkElement Nameplate()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        grid.Children.Add(new Border
        {
            Background = Theme.Black,
            // A hairline keeps the plate visible on the graphite sheet in dark mode.
            BorderBrush = Theme.Hairline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Kit.KeyRadius),
            Padding = new Thickness(8, 5, 8, 5),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new Image { Source = Branding.Logo, Height = 16 },
        });
        var name = Kit.Mono("LUMENLITE " + Updater.CurrentText, 10.5, false, Kit.Secondary);
        name.HorizontalAlignment = HorizontalAlignment.Right;
        grid.Children.Add(name);
        return grid;
    }
}
