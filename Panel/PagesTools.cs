using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace LumenLite.Panel;

/// <summary>The tools LumenLite adds to Windows, and what runs by itself.</summary>
internal sealed class ToolsPage : Page
{
    public ToolsPage(PanelWindow host, Action openCommandBar, Action openScratchpad, IslandWindow island, Background background, Action openRadialSettings) : base(host)
    {
        Add(Ui.Group(Title,
                Ui.Row("\uE721", "Command Bar", "Tìm và mở app, tính toán, đổi đơn vị, tìm emoji và chạy mọi lệnh của LumenLite từ một ô.",
                    Ui.Trail(Ui.Keys(background.CommandBarHotkey ?? "—"), Ui.Nav()), () => { Host.HidePanel(); openCommandBar(); }),
                Ui.Row("\uF0E2", "Quick panel", "Bảng này: mở từ bất cứ đâu bằng phím tắt.", Ui.Keys(background.PanelHotkey ?? "—")),
                Ui.Row("\uE8B9", "Menu vòng tròn", "Vòng nút quanh con trỏ chuột: mở trang AI Agents hoặc màn hình từng máy RustDesk.",
                    Ui.Trail(Ui.Keys(background.RadialHotkey ?? "—"), Ui.Nav()), openRadialSettings),
                Ui.Row("\uE70B", "Scratchpad", "Ghi chú nhanh nhiều tab, có xem trước Markdown, tự lưu.", Ui.Nav(), () => { Host.HidePanel(); openScratchpad(); }),
                Ui.Row("\uE765", "Cleaning Mode", "Khóa bàn phím và tắt sáng màn hình để lau máy an toàn.", Ui.Nav(), () => { Host.HidePanel(); CleaningMode.Start(); })),
            Ui.Group("Tự động",
                Ui.Row("\uE71B", "Làm sạch link", "Bỏ mã theo dõi (utm, fbclid, si…) khỏi link khi bạn copy.", Ui.Switch(Store.S.CleanLinks, on => { Store.S.CleanLinks = on; Store.SaveSoon(); })),
                Ui.Row("\uE896", "Báo tải xong", "Dynamic Island báo khi một file tải về xong trong Downloads.", Ui.Switch(Store.S.DownloadAlerts, on =>
                {
                    background.DownloadAlerts = on;
                    Store.S.DownloadAlerts = on;
                    Store.SaveSoon();
                }))),
            Ui.Group("Dynamic Island",
                Ui.Row(null, "Dynamic Island", "Nhạc, điều khiển và thông báo ở giữa mép trên màn hình.", Ui.Switch(Store.S.Island, on =>
                {
                    island.Enabled = on;
                    Store.S.Island = on;
                    Store.SaveSoon();
                })),
                Ui.Row(null, "Luôn hiện viên nhỏ", "Tắt đi thì Island chỉ hiện khi có nhạc, hẹn giờ hay thông báo.", Ui.Switch(Store.S.IslandAlwaysShow, on =>
                {
                    island.AlwaysShow = on;
                    Store.S.IslandAlwaysShow = on;
                    Store.SaveSoon();
                })),
                Ui.Row(null, "Vị trí", "Mép màn hình Island bám vào. Cũng đổi được bằng cách giữ và kéo Island tới mép trái, phải hoặc trên.",
                    Ui.Segments(new[] { (IslandWindow.IslandDock.Top, "Trên"), (IslandWindow.IslandDock.Left, "Trái"), (IslandWindow.IslandDock.Right, "Phải") }
                        .Select(o => (o.Item2, Store.S.IslandDock == o.Item1.ToString().ToLowerInvariant(), (Action)(() => island.SetDock(o.Item1)))).ToList(), compact: true))));
    }

    public override string Title => "Tiện ích";
}

/// <summary>One-click switches, in a grid of tiles.</summary>
internal sealed class TogglesPage : Page
{
    private readonly UniformGrid _grid = new() { Columns = 3, Margin = new Thickness(-4, -4, -4, 6) };

    public TogglesPage(PanelWindow host) : base(host)
    {
        Add(Ui.Title(Title), _grid);
    }

    public override string Title => "Bật/tắt nhanh";

    public override void Shown() => Build();

    private void Build()
    {
        _grid.Children.Clear();
        var light = QuickActions.LightTheme();
        var icons = QuickActions.DesktopIconsVisible();
        var (binCount, binBytes) = QuickActions.RecycleBin();
        Add("\uE793", light ? "Giao diện sáng" : "Giao diện tối", "Bấm để đổi", !light, () => QuickActions.SetLightTheme(!light));
        Add("\uE8FC", "Icon desktop", icons ? "Đang hiện" : "Đang ẩn", !icons, QuickActions.ToggleDesktopIcons);
        Add("\uE74D", "Thùng rác", binCount == 0 ? "Trống" : SystemMonitor.Size(binBytes), false, () => QuickActions.EmptyRecycleBin(IntPtr.Zero));
        Add("\uE72E", "Khóa máy", "Win + L", false, () => { Host.HidePanel(); QuickActions.Lock(); });
        Add("\uE7F4", "Tắt màn hình", "Rê chuột để bật", false, () => { Host.HidePanel(); QuickActions.ScreensOff(IntPtr.Zero); });
        Add("\uE765", "Lau phím", "Khóa bàn phím", false, () => { Host.HidePanel(); CleaningMode.Start(); });
    }

    private void Add(string glyph, string title, string detail, bool active, Action action)
    {
        var tile = new Tile(Ui.M, glyph, title, () =>
        {
            action();
            Dispatcher.BeginInvoke(Build, System.Windows.Threading.DispatcherPriority.Background);
        }, detail, roomy: true) { Height = 84 };
        tile.Set(active, detail);
        _grid.Children.Add(tile);
    }
}

/// <summary>Widgets, startup, updates and the shortcuts, opened from the gear in the header.</summary>
internal sealed class SettingsPage : Page
{
    public SettingsPage(PanelWindow host, IReadOnlyList<WidgetWindow> widgets, Background background) : base(host)
    {
        var widgetRows = widgets.Select(widget => (UIElement)Ui.Row(null, widget.Title, control: Ui.Switch(widget.IsVisible, on => widget.SetShown(on)))).ToList();
        widgetRows.Add(Ui.Row(null, "Chỉnh sửa bố cục widget", control: Ui.Nav(), onClick: () =>
        {
            Host.HidePanel();
            WidgetWindow.SetEditing(true);
            foreach (var w in widgets)
                if (w.IsVisible) w.Activate();
        }));

        var themes = Ui.Segments(new[] { ("system", "Theo Windows"), ("light", "Sáng"), ("dark", "Tối") }.Select(o => (o.Item2, Store.S.ThemeMode == o.Item1, (Action)(() =>
        {
            if (Store.S.ThemeMode == o.Item1)
                return;
            Store.S.ThemeMode = o.Item1;
            Store.SaveSoon();
            Host.Retheme?.Invoke();
        }))).ToList(), compact: true);

        // Stays visible: the version in use and what the updater is doing right now.
        var status = Ui.Note("");
        var check = Ui.Row("\uE777", "Kiểm tra ngay", onClick: () =>
        {
            if (Updater.Status == Updater.State.Ready)
                Updater.Restart(() => Host.Quit?.Invoke());
            else
                _ = Updater.CheckAsync();
        });
        void Show()
        {
            status.Text = $"Phiên bản {Updater.CurrentText}. " + (Updater.Message.Length > 0 ? Updater.Message : "Tự kiểm tra 1 phút sau khi mở app, rồi 6 giờ một lần.");
            check.Title = Updater.Status == Updater.State.Ready ? "Khởi động lại để cập nhật" : "Kiểm tra ngay";
            check.Disabled(Updater.Status is Updater.State.Checking or Updater.State.Downloading);
        }

        Add(Ui.Group("Widget", widgetRows.ToArray()),
            Ui.Group("Chung",
                Ui.Row(null, "Giao diện", control: themes),
                Ui.Row(null, "Khởi động cùng Windows", "Mở LumenLite khi bạn đăng nhập.", Ui.Switch(Autostart.IsEnabled, on =>
                {
                    try { Autostart.Set(on); } catch (Exception ex) { Store.Log("autostart failed: " + ex.Message); }
                })),
                Ui.Row(null, "Tiết kiệm bộ nhớ", "Vẽ bằng CPU thay vì GPU: ít RAM hơn nhưng hiệu ứng kém mượt. Có hiệu lực từ lần mở app tiếp theo.", Ui.Switch(Store.S.SoftwareRendering, on =>
                {
                    Store.S.SoftwareRendering = on;
                    Store.SaveSoon();
                }))),
            Ui.Group("Cập nhật",
                Ui.Row("\uE896", "Tự cập nhật", Updater.Enabled ? $"Tải bản mới từ GitHub ({Updater.Repository}) và dùng từ lần mở app tiếp theo." : "Bản tự build không tự cập nhật. Phát hành bằng release.ps1 để bật.", Ui.Switch(Store.S.AutoUpdate, on =>
                {
                    Store.S.AutoUpdate = on;
                    Store.SaveSoon();
                })),
                check,
                status),
            Ui.Group("Phím tắt",
                Shortcut("Mở bảng này", background.PanelHotkey),
                Shortcut("Command Bar", background.CommandBarHotkey),
                Shortcut("Menu vòng tròn", background.RadialHotkey),
                Shortcut("Tắt/bật micro", background.MicHotkey),
                Shortcut("Đổi loa/tai nghe", background.OutputHotkey))
                .Hint("Khi một phím đã bị app khác giữ, LumenLite dùng thêm Shift. Cài đặt lưu ở %APPDATA%\\LumenLite."),
            About());
        Show();
        WhileShown(() => Updater.Changed += Show, () => Updater.Changed -= Show);
    }

    public override string Title => "Cài đặt";

    private static Row Shortcut(string label, string? keys) => Ui.Row(null, label, control: Ui.Keys(keys ?? "bị app khác giữ"));

    /// <summary>
    /// Who made this: the TD logo on a small black plate (it is drawn for a dark ground) and the product name and
    /// version in mono, as on the back of a device.
    /// </summary>
    private static FrameworkElement About()
    {
        var m = Ui.M;
        var plate = new Border
        {
            Background = Material.Display.Fill,
            BorderBrush = Material.Display.Rim,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(m.KeyRadius),
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 0, 10, 0),
            Child = new Image { Source = Branding.Logo, Height = 16 },
        };
        var about = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 10) };
        about.Children.Add(plate);
        about.Children.Add(Ui.Mono("LUMENLITE " + Updater.CurrentText, 10.5, m.Secondary));
        return about;
    }
}

/// <summary>
/// The radial menu's settings: how to open it, and what it holds now. The ring
/// is the AI Agents page and one button per RustDesk machine, so there is
/// nothing to choose; this page only shows it.
/// </summary>
internal sealed class RadialPage : Page
{
    private readonly Func<IReadOnlyList<RadialAction>> _items;
    private readonly TextBlock _count = Ui.Value("");
    private readonly Group _list;

    public RadialPage(PanelWindow host, Func<IReadOnlyList<RadialAction>> items, string? hotkey, Action<bool> setMiddleHold) : base(host)
    {
        _items = items;
        _list = Ui.Group("Nút trên vòng").Trailing(_count)
            .Hint("Vòng gồm trang AI Agents trên Island và từng máy RustDesk đã lưu (tối đa 7). Bấm một máy để mở màn hình của máy đó trong RustDesk.");
        Add(Ui.Group("Cách dùng",
                // Stays visible: the instructions are what this page is for.
                Ui.Note($"Giữ chuột giữa (hoặc nhấn {hotkey ?? "phím tắt"}), vòng nút hiện quanh con trỏ. Rê về phía một nút rồi thả chuột giữa, hoặc bấm vào nút. Phím 1–8 chạy nút theo thứ tự. Esc hay bấm giữa vòng để đóng."),
                Ui.Row(null, "Giữ chuột giữa để mở", "Bấm chuột giữa nhanh vẫn hoạt động như cũ (mở tab mới, cuộn). Tự tắt khi chơi game hay xem video toàn màn hình.", Ui.Switch(Store.S.RadialMiddleHold, setMiddleHold))),
            _list);
    }

    public override string Title => "Menu vòng tròn";

    public override void Shown()
    {
        var items = _items();
        _count.Text = $"{items.Count}/8";
        _list.Set(items.Select((item, i) =>
        {
            var title = $"{i + 1}. {item.Label}";
            if (!item.Symbol)
                return (UIElement)Ui.Row(item.Glyph, title);
            // Plain Unicode symbols (like ✦) are not in the icon font.
            var symbol = new TextBlock { Text = item.Glyph, FontSize = 16, FontFamily = new FontFamily("Segoe UI Symbol"), Foreground = Ui.M.Text, Width = 20 };
            return Ui.LeadRow(symbol, title);
        }).ToArray());
    }
}
