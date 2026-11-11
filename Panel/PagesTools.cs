using System.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace LumenLite.Panel;

/// <summary>Utilities, as Vorssaint lists them: one card per tool with a short description.</summary>
internal sealed class ToolsPage : Page
{
    public ToolsPage(PanelWindow host, Action openCommandBar, Action openScratchpad, IslandWindow island, Background background, Action openRadialSettings) : base(host)
    {
        Children.Add(Kit.NavRow("", "Command Bar", "Tìm và mở app, tính toán, đổi đơn vị, tìm emoji và chạy mọi lệnh của LumenLite từ một ô.",
            () => { Host.HidePanel(); openCommandBar(); }, Kit.KeyHint(background.CommandBarHotkey ?? "—")));
        Children.Add(Kit.NavRow("", "Quick panel", "Bảng này: mở từ bất cứ đâu bằng phím tắt.", () => { }, Kit.KeyHint(background.PanelHotkey ?? "—"), chevron: false));
        Children.Add(Kit.NavRow("", "Menu vòng tròn", "Vòng nút quanh con trỏ chuột: mở trang AI Agents hoặc màn hình từng máy RustDesk.", openRadialSettings, Kit.KeyHint(background.RadialHotkey ?? "—")));
        Children.Add(Kit.NavRow("", "Scratchpad", "Ghi chú nhanh nhiều tab, có xem trước Markdown, tự lưu.", () => { Host.HidePanel(); openScratchpad(); }));
        Children.Add(Kit.NavRow("", "Cleaning Mode", "Khóa bàn phím và tắt sáng màn hình để lau máy an toàn.", () => { Host.HidePanel(); CleaningMode.Start(); }));
        Children.Add(Kit.Section("Tự động"));
        Children.Add(Kit.CardOf(true,
            Kit.ToggleRow("Làm sạch link", "Bỏ mã theo dõi (utm, fbclid, si…) khỏi link khi bạn copy.", Store.S.CleanLinks, on => { Store.S.CleanLinks = on; Store.SaveSoon(); }),
            Kit.ToggleRow("Báo tải xong", "Dynamic Island báo khi một file tải về xong trong Downloads.", Store.S.DownloadAlerts, on =>
            {
                background.DownloadAlerts = on;
                Store.S.DownloadAlerts = on;
                Store.SaveSoon();
            })));
        Children.Add(Kit.Section("Dynamic Island"));
        Children.Add(Kit.CardOf(true,
            Kit.ToggleRow("Dynamic Island", "Nhạc, điều khiển và thông báo ở giữa mép trên màn hình.", Store.S.Island, on =>
            {
                island.Enabled = on;
                Store.S.Island = on;
                Store.SaveSoon();
            }),
            Kit.ToggleRow("Luôn hiện viên nhỏ", "Tắt đi thì Island chỉ hiện khi có nhạc, hẹn giờ hay thông báo.", Store.S.IslandAlwaysShow, on =>
            {
                island.AlwaysShow = on;
                Store.S.IslandAlwaysShow = on;
                Store.SaveSoon();
            })));
    }

    public override string Title => "Tiện ích";
}

/// <summary>One-click switches, in a grid.</summary>
internal sealed class TogglesPage : Page
{
    private readonly UniformGrid _grid = new() { Columns = 3, Margin = new Thickness(-4) };

    public TogglesPage(PanelWindow host) : base(host)
    {
        Children.Add(Kit.CardOf(_grid));
    }

    public override string Title => "Bật/tắt nhanh";

    public override void Shown() => Build();

    private void Build()
    {
        _grid.Children.Clear();
        var light = QuickActions.LightTheme();
        var icons = QuickActions.DesktopIconsVisible();
        var (binCount, binBytes) = QuickActions.RecycleBin();
        Add("", light ? "Giao diện sáng" : "Giao diện tối", "Bấm để đổi", !light, () => QuickActions.SetLightTheme(!light));
        Add("", "Icon desktop", icons ? "Đang hiện" : "Đang ẩn", !icons, QuickActions.ToggleDesktopIcons);
        Add("", "Thùng rác", binCount == 0 ? "Trống" : SystemMonitor.Size(binBytes), false, () => QuickActions.EmptyRecycleBin(IntPtr.Zero));
        Add("", "Khóa máy", "Win + L", false, () => { Host.HidePanel(); QuickActions.Lock(); });
        Add("", "Tắt màn hình", "Rê chuột để bật", false, () => { Host.HidePanel(); QuickActions.ScreensOff(IntPtr.Zero); });
        Add("", "Lau phím", "Khóa bàn phím", false, () => { Host.HidePanel(); CleaningMode.Start(); });
    }

    private void Add(string glyph, string title, string detail, bool active, Action action)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var icon = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(Kit.ControlRadius),
            Background = active ? Kit.Accent : Kit.Inset,
            Child = Kit.Glyph(glyph, 17, active ? Kit.OnAccent : Kit.Text),
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        stack.Children.Add(icon);
        var t = Kit.Label(title, 12, true);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(t);
        var d = Kit.Label(detail, 10.5, false, Kit.Secondary);
        d.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(d);
        var tile = Kit.Clickable(stack, () =>
        {
            action();
            Dispatcher.BeginInvoke(Build, System.Windows.Threading.DispatcherPriority.Background);
        }, new Thickness(4, 10, 4, 10));
        tile.Margin = new Thickness(4);
        _grid.Children.Add(tile);
    }
}

/// <summary>Widgets, startup and the shortcuts, opened from the footer.</summary>
internal sealed class SettingsPage : Page
{
    public SettingsPage(PanelWindow host, IReadOnlyList<WidgetWindow> widgets, Background background) : base(host)
    {
        var widgetRows = new List<UIElement>();
        foreach (var widget in widgets)
            widgetRows.Add(Kit.ToggleRow(widget.Title, null, widget.IsVisible, on => widget.SetShown(on)));
        Children.Add(Kit.Section("Widget"));
        Children.Add(Kit.CardOf(true, widgetRows.ToArray()));
        var edit = Kit.Button("", "Chỉnh sửa bố cục widget", () =>
        {
            Host.HidePanel();
            WidgetWindow.SetEditing(true);
            foreach (var w in widgets)
                if (w.IsVisible) w.Activate();
        });
        edit.Margin = new Thickness(0, 0, 0, 10);
        Children.Add(edit);

        Children.Add(Kit.Section("Chung"));
        var themes = new UniformGrid { Rows = 1, Margin = new Thickness(-3, 8, -3, -3) };
        foreach (var (mode, label) in new[] { ("system", "Theo Windows"), ("light", "Sáng"), ("dark", "Tối") })
        {
            themes.Children.Add(Kit.Chip(label, Store.S.ThemeMode == mode, () =>
            {
                if (Store.S.ThemeMode == mode)
                    return;
                Store.S.ThemeMode = mode;
                Store.SaveSoon();
                Host.Retheme?.Invoke();
            }));
        }
        var theme = new StackPanel();
        theme.Children.Add(Kit.Label("Giao diện", 13.5, true));
        theme.Children.Add(themes);
        Children.Add(Kit.CardOf(true,
            theme,
            Kit.ToggleRow("Khởi động cùng Windows", "Mở LumenLite khi bạn đăng nhập.", Autostart.IsEnabled, on =>
            {
                try { Autostart.Set(on); } catch (Exception ex) { Store.Log("autostart failed: " + ex.Message); }
            }),
            Kit.ToggleRow("Tiết kiệm bộ nhớ", "Vẽ bằng CPU thay vì GPU: ít RAM hơn nhưng hiệu ứng kém mượt. Có hiệu lực từ lần mở app tiếp theo.", Store.S.SoftwareRendering, on =>
            {
                Store.S.SoftwareRendering = on;
                Store.SaveSoon();
            })));

        Children.Add(Kit.Section("Cập nhật"));
        var status = Kit.Wrap("", 12, Kit.Secondary);
        var action = Kit.Button("\uE777", "Kiểm tra ngay", () =>
        {
            // A Kit button swallows its own click, so the action goes in the button itself.
            if (Updater.Status == Updater.State.Ready)
                Updater.Restart(() => Host.Quit?.Invoke());
            else
                _ = Updater.CheckAsync();
        });
        void Show()
        {
            status.Text = $"Phiên bản {Updater.CurrentText}. " + (Updater.Message.Length > 0 ? Updater.Message : "Tự kiểm tra 1 phút sau khi mở app, rồi 6 giờ một lần.");
            if (FindText(action) is { } label)
                label.Text = Updater.Status == Updater.State.Ready ? "Khởi động lại để cập nhật" : "Kiểm tra ngay";
            action.IsEnabled = Updater.Status is not (Updater.State.Checking or Updater.State.Downloading);
        }
        Children.Add(Kit.CardOf(true,
            Kit.ToggleRow("Tự cập nhật", Updater.Enabled ? $"Tải bản mới từ GitHub ({Updater.Repository}) và dùng từ lần mở app tiếp theo." : "Bản tự build không tự cập nhật. Phát hành bằng release.ps1 để bật.", Store.S.AutoUpdate, on =>
            {
                Store.S.AutoUpdate = on;
                Store.SaveSoon();
            }),
            status, action));
        Show();
        WhileShown(() => Updater.Changed += Show, () => Updater.Changed -= Show);

        Children.Add(Kit.Section("Phím tắt"));
        Children.Add(Kit.CardOf(
            Shortcut("Mở bảng này", background.PanelHotkey),
            Shortcut("Command Bar", background.CommandBarHotkey),
            Shortcut("Menu vòng tròn", background.RadialHotkey),
            Shortcut("Tắt/bật micro", background.MicHotkey),
            Shortcut("Đổi loa/tai nghe", background.OutputHotkey)));
        var note = Kit.Wrap("Khi một phím đã bị app khác giữ, LumenLite dùng thêm Shift. Cài đặt lưu ở %APPDATA%\\LumenLite.", 11);
        note.Margin = new Thickness(4, -4, 4, 0);
        Children.Add(note);
    }

    public override string Title => "Cài đặt";

    /// <summary>The label of a Kit button: the last text in its row, after the icon.</summary>
    private static TextBlock? FindText(Border button) =>
        (button.Child as System.Windows.Controls.Panel)?.Children.OfType<TextBlock>().LastOrDefault();

    private static Grid Shortcut(string label, string? keys)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Kit.Label(label, 13.5));
        var hint = Kit.KeyHint(keys ?? "bị app khác giữ");
        Grid.SetColumn(hint, 1);
        grid.Children.Add(hint);
        return grid;
    }
}
