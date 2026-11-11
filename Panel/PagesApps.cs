using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LumenLite.Panel;

/// <summary>Drives with their free space, plus the cleaner and messaging downloads.</summary>
internal sealed class StoragePage : Page
{
    private readonly StackPanel _drives = new();
    private readonly TextBlock _binDetail = Kit.Label("", 12, false, Kit.Secondary);

    public StoragePage(PanelWindow host) : base(host)
    {
        Children.Add(Kit.CardOf(_drives));
        Children.Add(Kit.Section("Dọn dẹp"));
        Children.Add(Kit.NavRow("", "Dọn dẹp", "File tạm, cache trình duyệt, cache đồ họa và báo lỗi", () => Host.Push(new CleanerPage(Host))));
        Children.Add(Kit.NavRow("", "File từ app nhắn tin", "Zalo, Telegram, Messenger… gom ảnh và file nhận được qua thời gian", () => Host.Push(new MessagingPage(Host))));
        Children.Add(Kit.NavRow("", "Thùng rác", null, () =>
        {
            QuickActions.EmptyRecycleBin(IntPtr.Zero);
            Shown();
        }, _binDetail, chevron: false));
    }

    public override string Title => "Lưu trữ";

    public override void Shown()
    {
        _drives.Children.Clear();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable))
        {
            var used = drive.TotalSize - drive.AvailableFreeSpace;
            var fraction = (double)used / drive.TotalSize;
            var top = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Ổ đĩa" : drive.VolumeLabel;
            top.Children.Add(Kit.Label($"{name} ({drive.Name.TrimEnd('\\')})", 13.5, true));
            var free = Kit.Label($"còn {SystemMonitor.Size(drive.AvailableFreeSpace)} / {SystemMonitor.Size(drive.TotalSize)}", 12.5, false, Kit.Secondary);
            free.FontFamily = Theme.Mono;
            free.FontSize = 11.5;
            Grid.SetColumn(free, 1);
            top.Children.Add(free);
            _drives.Children.Add(top);
            var bar = Kit.Bar(fraction, fraction > 0.9 ? Kit.Red : fraction > 0.75 ? Kit.Amber : Kit.Text, 4);
            bar.Margin = new Thickness(0, 0, 0, 8);
            _drives.Children.Add(bar);
        }
        var (count, bytes) = QuickActions.RecycleBin();
        _binDetail.Text = count == 0 ? "Trống" : $"{count} mục · {SystemMonitor.Size(bytes)} · bấm để dọn";
    }
}

internal sealed class CleanerPage : Page
{
    private readonly StackPanel _list = new();
    private readonly Border _cleanButton;
    private readonly HashSet<string> _selected = new() { "temp", "browsers", "crash", "shaders" };
    private List<CleanCategory> _categories = Cleaner.Categories();
    private bool _busy;

    public CleanerPage(PanelWindow host) : base(host)
    {
        Children.Add(Kit.CardOf(_list));
        _cleanButton = Kit.Button("", "Dọn", Clean, Kit.Ink, Kit.OnInk);
        Children.Add(_cleanButton);
        Children.Add(Kit.Section("Tự động"));
        Children.Add(Kit.CardOf(Kit.ToggleRow("Tự dọn mỗi tuần", "File tạm của bạn và báo lỗi, chạy nền khi mở LumenLite", Store.S.CleanerAuto, on =>
        {
            Store.S.CleanerAuto = on;
            Store.SaveSoon();
            Cleaner.RunScheduled();
        })));
        var note = Kit.Wrap("File đang được dùng sẽ được bỏ qua. Trình duyệt đang mở có thể giữ lại một phần cache.", 11);
        note.Margin = new Thickness(4, -4, 4, 0);
        Children.Add(note);
    }

    public override string Title => "Dọn dẹp";

    public override void Shown() => Scan();

    private async void Scan()
    {
        if (_busy) return;
        _busy = true;
        _list.Children.Clear();
        _list.Children.Add(Kit.Wrap("Đang quét…", 13));
        SetButton("Đang quét…", false);
        _categories = Cleaner.Categories();
        await Task.Run(() => Cleaner.Measure(_categories));
        _busy = false;
        Draw();
    }

    private void Draw()
    {
        _list.Children.Clear();
        foreach (var category in _categories)
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = Kit.Glyph(category.Glyph, 16, Kit.Text);
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            grid.Children.Add(icon);
            var text = new StackPanel();
            text.Children.Add(Kit.Label(category.Title, 13.5, true));
            text.Children.Add(Kit.Wrap(category.Description, 11.5));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var size = Kit.Label(category.Files == 0 ? "Sạch" : SystemMonitor.Size(category.Bytes), 13, true, category.Files == 0 ? Kit.Secondary : Kit.Text);
            size.Margin = new Thickness(8, 0, 10, 0);
            Grid.SetColumn(size, 2);
            grid.Children.Add(size);
            var id = category.Id;
            var toggle = Kit.Toggle(_selected.Contains(id), on =>
            {
                if (on) _selected.Add(id); else _selected.Remove(id);
                UpdateTotal();
            });
            toggle.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(toggle, 3);
            grid.Children.Add(toggle);
            _list.Children.Add(grid);
        }
        UpdateTotal();
    }

    private void UpdateTotal()
    {
        var total = _categories.Where(c => _selected.Contains(c.Id)).Sum(c => c.Bytes);
        SetButton(total == 0 ? "Không có gì để dọn" : $"Dọn {SystemMonitor.Size(total)}", total > 0);
    }

    private void SetButton(string text, bool enabled)
    {
        var row = (StackPanel)_cleanButton.Child;
        ((TextBlock)row.Children[1]).Text = text;
        _cleanButton.IsEnabled = enabled;
    }

    private void Clean()
    {
        var chosen = _categories.Where(c => _selected.Contains(c.Id) && c.Files > 0).ToList();
        var total = chosen.Sum(c => c.Bytes);
        Host.Confirm($"Xóa {SystemMonitor.Size(total)}?", "Các file này bị xóa hẳn, không vào Thùng rác: " + string.Join(", ", chosen.Select(c => c.Title.ToLowerInvariant())) + ".", "Xóa", async () =>
        {
            SetButton("Đang dọn…", false);
            var freed = await Task.Run(() => Cleaner.Clean(chosen));
            Host.Toast($"Đã giải phóng {SystemMonitor.Size(freed)}.");
            Scan();
        });
    }
}

internal sealed class MessagingPage : Page
{
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(-3, 0, -3, 0) };
    private readonly StackPanel _list = new();
    private int _days = 90;

    public MessagingPage(PanelWindow host) : base(host)
    {
        Children.Add(Kit.CardOf(Kit.Label("Chọn file cũ hơn", 13.5, true), _chips));
        Children.Add(_list);
        var note = Kit.Wrap("File được chuyển vào Thùng rác, nên vẫn khôi phục được.", 11);
        note.Margin = new Thickness(4, 0, 4, 0);
        Children.Add(note);
    }

    public override string Title => "File từ app nhắn tin";

    public override void Shown() => Load();

    private async void Load()
    {
        _chips.Children.Clear();
        foreach (var days in new[] { 30, 90, 180, 365 })
            _chips.Children.Add(Kit.Chip(days == 365 ? "1 năm" : $"{days} ngày", days == _days, () =>
            {
                _days = days;
                Load();
            }));
        _list.Children.Clear();
        _list.Children.Add(Kit.CardOf(Kit.Wrap("Đang quét…", 13)));
        var folders = await Task.Run(() => Cleaner.MessagingFolders(_days));
        _list.Children.Clear();
        if (folders.Count == 0)
            _list.Children.Add(Kit.CardOf(Kit.Wrap("Không thấy thư mục nhận file của Zalo, Telegram, Messenger, Discord hay Viber.", 13)));
        foreach (var folder in folders)
        {
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(Kit.Label(folder.App, 14.5, true));
            var total = Kit.Label($"{SystemMonitor.Size(folder.Bytes)} · {folder.Files} file", 12.5, false, Kit.Secondary);
            Grid.SetColumn(total, 1);
            header.Children.Add(total);
            var path = Kit.Wrap(folder.Path, 11);
            var old = Kit.ValueRow($"Cũ hơn {(_days == 365 ? "1 năm" : _days + " ngày")}", folder.OldFiles == 0 ? "Không có" : $"{SystemMonitor.Size(folder.OldBytes)} · {folder.OldFiles} file");
            var buttons = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            buttons.ColumnDefinitions.Add(new ColumnDefinition());
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition());
            buttons.Children.Add(Kit.Button("", "Mở thư mục", () => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder.Path}\"") { UseShellExecute = true })));
            var recycle = Kit.Button("", "Bỏ file cũ", () => Host.Confirm($"Chuyển {folder.OldFiles} file vào Thùng rác?",
                $"{SystemMonitor.Size(folder.OldBytes)} file {folder.App} cũ hơn {(_days == 365 ? "1 năm" : _days + " ngày")} trong {folder.Path}.", "Chuyển", async () =>
                {
                    var moved = await Task.Run(() => Cleaner.RecycleOld(folder.Path, _days));
                    Host.Toast($"Đã chuyển {moved} file vào Thùng rác.");
                    Load();
                }));
            recycle.IsEnabled = folder.OldFiles > 0;
            Grid.SetColumn(recycle, 2);
            buttons.Children.Add(recycle);
            _list.Children.Add(Kit.CardOf(header, path, old, buttons));
        }
    }
}

/// <summary>Open apps to quit, plus updates, the uninstaller and ports.</summary>
internal sealed class AppsPage : Page
{
    private readonly StackPanel _apps = new();

    public AppsPage(PanelWindow host) : base(host)
    {
        var all = Kit.Button("", "Thoát tất cả", () => QuitMany(exceptFront: false));
        var others = Kit.Button("", "Trừ app đang dùng", () => QuitMany(exceptFront: true));
        var buttons = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.Children.Add(all);
        Grid.SetColumn(others, 2);
        buttons.Children.Add(others);
        Children.Add(Kit.CardOf(_apps));
        Children.Add(buttons);
        Children.Add(Kit.Section("Quản lý app"));
        Children.Add(Kit.NavRow("", "Cập nhật app", "Tìm bản mới cho app đã cài qua winget, cập nhật từng app hoặc tất cả", () => Host.Push(new UpdatesPage(Host))));
        Children.Add(Kit.NavRow("", "Gỡ cài đặt", "Gỡ app rồi dọn thư mục nó để lại trong AppData và ProgramData", () => Host.Push(new UninstallPage(Host))));
        Children.Add(Kit.NavRow("", "Cổng mạng", "Xem tiến trình nào đang mở port và dừng nó", () => Host.Push(new PortsPage(Host))));
    }

    public override string Title => "Ứng dụng đang mở";

    public override void Shown() => Load();

    private List<RunningApp> _running = new();

    private void Load()
    {
        _apps.Children.Clear();
        _running = AppManager.Running();
        foreach (var app in _running)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = AppInfo.Of(app.Pid).Icon;
            if (icon != null)
                grid.Children.Add(new Image { Source = icon, Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Left });
            var name = Kit.Label(app.Name, 13.5, app.Pid == Host.PreviousForegroundPid);
            if (app.Pid == Host.PreviousForegroundPid)
                name.Text += "  · đang dùng";
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);
            var quit = Kit.SmallButton("Thoát", () =>
            {
                AppManager.Quit(app);
                Host.Toast($"Đã yêu cầu {app.Name} đóng.");
                Delay(Load);
            });
            Grid.SetColumn(quit, 2);
            grid.Children.Add(quit);
            _apps.Children.Add(grid);
        }
        if (_running.Count == 0)
            _apps.Children.Add(Kit.Wrap("Không có app nào đang mở cửa sổ.", 13));
    }

    private void QuitMany(bool exceptFront)
    {
        var targets = _running.Where(a => !exceptFront || a.Pid != Host.PreviousForegroundPid).ToList();
        if (targets.Count == 0) return;
        Host.Confirm(exceptFront ? $"Thoát {targets.Count} app, giữ app đang dùng?" : $"Thoát {targets.Count} app?",
            "Mỗi app được yêu cầu đóng như khi bấm dấu X, nên app nào có việc chưa lưu sẽ hỏi bạn trước.", "Thoát", () =>
            {
                foreach (var app in targets)
                    AppManager.Quit(app);
                Delay(Load);
            });
    }

    private static async void Delay(Action action)
    {
        await Task.Delay(1200);
        action();
    }
}

internal sealed class UpdatesPage : Page
{
    private readonly StackPanel _list = new();
    private readonly Border _all;

    public UpdatesPage(PanelWindow host) : base(host)
    {
        Children.Add(Kit.CardOf(_list));
        _all = Kit.Button("", "Cập nhật tất cả", () => Host.Confirm("Cập nhật tất cả?", "winget sẽ tải và cài từng bản mới trong một cửa sổ riêng. Một số app cần bạn bấm đồng ý quyền admin.", "Cập nhật", () => AppManager.Upgrade(null), destructive: false), Kit.Ink, Kit.OnInk);
        Children.Add(_all);
        var refresh = Kit.Button("", "Kiểm tra lại", Load);
        refresh.Margin = new Thickness(0, 8, 0, 0);
        Children.Add(refresh);
    }

    public override string Title => "Cập nhật app";

    public override void Shown() => Load();

    private async void Load()
    {
        _list.Children.Clear();
        _all.IsEnabled = false;
        if (!AppManager.WingetAvailable)
        {
            _list.Children.Add(Kit.Wrap("Máy chưa có winget. Cài \"App Installer\" từ Microsoft Store để dùng mục này.", 13));
            return;
        }
        _list.Children.Add(Kit.Wrap("Đang hỏi winget (có thể mất 10–30 giây)…", 13));
        List<UpgradeItem> items;
        try
        {
            items = await AppManager.UpgradesAsync();
        }
        catch (Exception ex)
        {
            _list.Children.Clear();
            _list.Children.Add(Kit.Wrap("Không kiểm tra được: " + ex.Message, 13));
            return;
        }
        _list.Children.Clear();
        if (items.Count == 0)
        {
            _list.Children.Add(Kit.Wrap("Mọi app winget biết đều đã mới nhất.", 13));
            return;
        }
        var count = Kit.Label($"{items.Count} app có bản mới", 13, true, Kit.Secondary);
        count.Margin = new Thickness(0, 0, 0, 6);
        _list.Children.Add(count);
        foreach (var item in items)
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(Kit.Label(item.Name, 13.5, true));
            text.Children.Add(Kit.Label($"{item.Version}  →  {item.Available}", 12, false, Kit.Secondary));
            grid.Children.Add(text);
            var update = Kit.SmallButton("Cập nhật", () => AppManager.Upgrade(item.Id), Kit.Ink, Kit.OnInk);
            update.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(update, 1);
            grid.Children.Add(update);
            _list.Children.Add(grid);
        }
        _all.IsEnabled = true;
    }
}

internal sealed class UninstallPage : Page
{
    private readonly StackPanel _list = new();
    private readonly TextBox _search;
    private List<InstalledApp> _apps = new();

    public UninstallPage(PanelWindow host) : base(host)
    {
        var (box, input) = Kit.SearchBox("Tìm app đã cài…");
        _search = input;
        _search.TextChanged += (_, _) => Draw();
        Children.Add(box);
        Children.Add(Kit.CardOf(_list));
    }

    public override string Title => "Gỡ cài đặt";

    public override async void Shown()
    {
        _list.Children.Clear();
        _list.Children.Add(Kit.Wrap("Đang đọc danh sách app…", 13));
        _apps = await Task.Run(AppManager.Installed);
        Draw();
        _search.Focus();
    }

    private void Draw()
    {
        _list.Children.Clear();
        var query = _search.Text.Trim();
        var matches = _apps.Where(a => query.Length == 0 || a.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || (a.Publisher?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)).ToList();
        foreach (var app in matches.Take(60))
            _list.Children.Add(Row(app));
        var footer = matches.Count > 60 ? $"Còn {matches.Count - 60} app nữa, gõ tên để tìm." : matches.Count == 0 ? "Không có app nào khớp." : $"{matches.Count} app";
        var note = Kit.Label(footer, 12, false, Kit.Secondary);
        note.Margin = new Thickness(0, 6, 0, 0);
        _list.Children.Add(note);
    }

    private UIElement Row(InstalledApp app)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (app.Icon is { Length: > 0 } iconPath && File.Exists(iconPath) && AppInfo.FileIcon(iconPath) is { } icon)
            grid.Children.Add(new Image { Source = icon, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left });
        else
            grid.Children.Add(Kit.Glyph("", 16, Kit.Secondary));
        var text = new StackPanel();
        text.Children.Add(Kit.Label(app.Name, 13.5, true));
        var details = string.Join(" · ", new[] { app.Publisher, app.Version, app.SizeBytes > 0 ? SystemMonitor.Size(app.SizeBytes) : null }.Where(x => !string.IsNullOrWhiteSpace(x)));
        text.Children.Add(Kit.Label(details, 11.5, false, Kit.Secondary));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var remove = Kit.SmallButton("Gỡ", () => Uninstall(app), Kit.RedSoft, Kit.Red);
        remove.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(remove, 2);
        grid.Children.Add(remove);
        return grid;
    }

    private void Uninstall(InstalledApp app)
    {
        Host.Confirm($"Gỡ {app.Name}?", "Trình gỡ của chính app sẽ mở ra. Gỡ xong, LumenLite tìm thư mục nó để lại và hỏi bạn có muốn bỏ vào Thùng rác không.", "Gỡ", async () =>
        {
            Process? process;
            try
            {
                process = AppManager.StartUninstall(app);
            }
            catch (Exception ex)
            {
                Host.Toast("Không mở được trình gỡ: " + ex.Message);
                return;
            }
            if (process == null) return;
            await process.WaitForExitAsync();
            await Task.Delay(1500);
            _apps = await Task.Run(AppManager.Installed);
            Draw();
            if (_apps.Any(a => a.Key == app.Key && a.Name == app.Name))
                return; // still installed: cancelled or failed
            var leftovers = await Task.Run(() => AppManager.Leftovers(app));
            if (leftovers.Count == 0)
            {
                Host.Toast($"Đã gỡ {app.Name}, không còn thư mục sót lại.");
                return;
            }
            Host.ShowPanel();
            Host.Confirm($"Dọn {leftovers.Count} thư mục còn sót?", string.Join("\n", leftovers), "Bỏ vào Thùng rác", () =>
            {
                var removed = 0;
                foreach (var folder in leftovers)
                {
                    try
                    {
                        Cleaner.RecycleFolder(folder);
                        removed++;
                    }
                    catch (Exception ex)
                    {
                        Store.Log("leftover remove failed: " + ex.Message);
                    }
                }
                Host.Toast($"Đã chuyển {removed} thư mục vào Thùng rác.");
            });
        });
    }
}

internal sealed class PortsPage : Page
{
    private readonly StackPanel _list = new();

    public PortsPage(PanelWindow host) : base(host)
    {
        Children.Add(Kit.CardOf(_list));
        Children.Add(Kit.Button("", "Làm mới", Load));
    }

    public override string Title => "Cổng mạng đang mở";

    public override void Shown() => Load();

    private void Load()
    {
        _list.Children.Clear();
        List<ListeningPort> ports;
        try
        {
            ports = AppManager.Ports();
        }
        catch (Exception ex)
        {
            _list.Children.Add(Kit.Wrap("Không đọc được: " + ex.Message, 13));
            return;
        }
        foreach (var group in ports.GroupBy(p => (p.Port, p.Pid)))
        {
            var port = group.First();
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var number = Kit.Mono(port.Port.ToString(), 13.5, true);
            grid.Children.Add(number);
            var text = new StackPanel();
            text.Children.Add(Kit.Label(port.Process, 13.5, true));
            text.Children.Add(Kit.Label($"{string.Join(", ", group.Select(p => p.Protocol).Distinct())} · {port.Address} · PID {port.Pid}", 11.5, false, Kit.Secondary));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var stop = Kit.SmallButton("Dừng", () => Host.Confirm($"Dừng {port.Process}?", $"Tiến trình PID {port.Pid} đang mở cổng {port.Port} sẽ bị buộc tắt ngay, việc chưa lưu sẽ mất.", "Dừng", () =>
            {
                try
                {
                    Process.GetProcessById(port.Pid).Kill(entireProcessTree: true);
                    Host.Toast($"Đã dừng {port.Process}.");
                }
                catch (Exception ex)
                {
                    Host.Toast("Không dừng được (có thể cần quyền admin): " + ex.Message);
                }
                Load();
            }), Kit.RedSoft, Kit.Red);
            stop.IsEnabled = port.Pid > 4;
            stop.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(stop, 2);
            grid.Children.Add(stop);
            _list.Children.Add(grid);
        }
        if (ports.Count == 0)
            _list.Children.Add(Kit.Wrap("Không có cổng nào đang lắng nghe.", 13));
    }
}
