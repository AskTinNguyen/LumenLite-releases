using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LumenLite.Panel;
using LumenLite.Widgets;

namespace LumenLite;

public sealed class App : Application
{
    private readonly List<WidgetWindow> _widgets = new();
    private Tray? _tray;
    private IslandWindow? _island;
    private Background? _background;
    private PanelWindow? _panel;
    private CommandBar? _commandBar;
    private Scratchpad? _scratchpad;

    private const string ShowSignal = "LumenLite.ShowPanel";
    private const string TabFile = "open-tab.txt";

    /// <summary>Starting LumenLite again while it runs opens the panel ("--tab N" picks a tab).</summary>
    [STAThread]
    public static void Main(string[] args)
    {
        // A copy started by an update waits for the one it replaces to exit.
        Updater.WaitForPrevious(args);
        // Started inside a terminal's or an agent's job, LumenLite would go when that tool closes: run outside it.
        if (Detach.Relaunched(args))
            return;
        using var mutex = new Mutex(true, "LumenLite.SingleInstance", out var first);
        if (!first)
        {
            try
            {
                var tab = Array.IndexOf(args, "--tab") is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : "";
                System.IO.Directory.CreateDirectory(Store.Folder);
                System.IO.File.WriteAllText(System.IO.Path.Combine(Store.Folder, TabFile), tab);
                // Lets the running copy bring its panel to the front (Windows only allows that when asked).
                AllowSetForegroundWindow(-1);
                if (EventWaitHandle.TryOpenExisting(ShowSignal, out var signal))
                    signal.Set();
            }
            catch
            {
            }
            return;
        }
        new App().Run();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    private void ListenForSecondStart()
    {
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignal);
        var thread = new Thread(() =>
        {
            while (signal.WaitOne())
            {
                Dispatcher.BeginInvoke(() =>
                {
                    int? tab = null;
                    try
                    {
                        var path = System.IO.Path.Combine(Store.Folder, TabFile);
                        if (System.IO.File.Exists(path) && int.TryParse(System.IO.File.ReadAllText(path).Trim(), out var t))
                            tab = t;
                    }
                    catch
                    {
                    }
                    // No tab given: behave like clicking the tray icon (open, or close when open).
                    if (tab == null)
                        Panel.Toggle();
                    else
                        Panel.ShowPanel(tab);
                });
            }
        }) { IsBackground = true, Name = "LumenLite second start" };
        thread.Start();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnUnhandled;
        Store.Load();
        // Drawing on the GPU keeps the animations smooth: on the CPU every frame of a transparent window, its shadows
        // included, is drawn and copied pixel by pixel. Software drawing saves the driver's per-process memory (tens
        // of MB on NVIDIA) and stays available as a setting for machines short on memory.
        if (Store.S.SoftwareRendering)
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        Store.Log($"start {Updater.CurrentText} pid {Environment.ProcessId} from {Environment.ProcessPath}");
        // Whatever ends LumenLite leaves a line: an exception no handler caught, or Windows ending the session.
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => Store.Log("crash: " + ev.ExceptionObject);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            Store.Log("unobserved task error: " + ev.Exception.GetBaseException().Message);
            ev.SetObserved();
        };
        SessionEnding += (_, ev) => Store.Log("Windows session ending: " + ev.ReasonSessionEnding);
        Autostart.Migrate();
        Wallpaper.Start();
        SystemMonitor.Start();
        AgentMonitor.Start();
        // The Notes widget is gone; its text moves into a Scratchpad tab.
        if (!string.IsNullOrWhiteSpace(Store.S.Notes))
        {
            Store.S.Pads.Add(new ScratchPad { Title = "Ghi chú cũ", Text = Store.S.Notes });
            Store.S.Notes = "";
            Store.SaveSoon();
        }
        Cleaner.RunScheduled();

        // Default layout: a column on the right of the primary screen, notes beside it.
        var area = SystemParameters.WorkArea;
        var x = area.Right - 380;
        _widgets.Add(new WidgetWindow("launcher", "Launcher", new LauncherView(), new Size(340, 210), new Point(x, area.Top + 344), resizable: true));

        foreach (var widget in _widgets)
        {
            if (Store.Widget(widget.Id).Visible)
                widget.Show();
        }

        _island = new IslandWindow();
        _island.AlwaysShow = Store.S.IslandAlwaysShow;
        _island.OpenPanel = tab => Panel.ShowPanel(tab switch
        {
            "sound" => 0,
            "devices" => 1,
            "system" => 2,
            "agents" => 6,
            "tools" => 7,
            "toggles" => 8,
            _ => null,
        });
        _island.OpenCommandBar = () => Bar.Open();
        AgentOpen.Unknown += text => _island?.Notify("", text, 4);
        _island.OpenRadial = OpenRadial;
        _island.OpenNotes = () => Pad.Open();
        _island.Enabled = Store.S.Island;
        _background = new Background(_island);
        // The panel, Command Bar and Scratchpad are built the first time they are opened.
        _background.OpenPanel = () => Panel.Toggle();
        _background.OpenCommandBar = () => Bar.Open();
        _background.OpenRadial = OpenRadial;
        _middle = new MiddleHold
        {
            Open = () => OpenRadial(toggle: false),
            Release = () => _radial?.ReleaseSelect(),
            Enabled = Store.S.RadialMiddleHold,
        };
        _tray = new Tray(_widgets, _island, _background, Quit, () => Panel.Toggle());
        Memory.Start();
        Updater.Start();
        Updater.Ready += version =>
        {
            var text = $"Đã tải LumenLite {version.Major}.{version.Minor}.{version.Build}: khởi động lại để dùng (Cài đặt hoặc Command Bar)";
            _island?.Notify("\uE777", text, 8);
            _tray?.Balloon(text);
        };
        ListenForSecondStart();
        AgentMonitor.Finished += session =>
        {
            if (Store.S.AgentAlerts)
                _island.Notify("\uE73E", $"{AgentMonitor.Name(session.Provider)} xong: {session.Title}", 5);
        };
        SystemMonitor.Alert += (glyph, text) =>
        {
            _island.Notify(glyph, text, 5);
            _tray?.Balloon(text);
        };
    }

    private PanelWindow Panel => _panel ??= BuildPanel(_background!, _island!);
    private CommandBar Bar => _commandBar ??= BuildCommandBar();

    /// <summary>A hidden panel is thrown away so its pages, icons and render buffers are freed.</summary>
    private void DiscardPanel(PanelWindow panel)
    {
        if (_panel != panel) return;
        _panel = null;
        panel.Close();
        Memory.TrimSoon();
    }

    private CommandBar BuildCommandBar()
    {
        var bar = new CommandBar(Commands);
        bar.IsVisibleChanged += (_, _) =>
        {
            if (bar.IsVisible || _commandBar != bar) return;
            _commandBar = null;
            bar.Dispatcher.BeginInvoke(bar.Close);
            Memory.TrimSoon();
        };
        return bar;
    }
    private Scratchpad Pad => _scratchpad ??= new Scratchpad();

    private static readonly System.Windows.Media.Brush RustDeskBlue = Freeze(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x24, 0x9C, 0xF6)));
    private static readonly System.Windows.Media.Brush ClaudeTint = Freeze(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD9, 0x77, 0x57)));

    private static System.Windows.Media.Brush Freeze(System.Windows.Media.Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// The ring: the AI Agents page at the top, then each RustDesk machine, with
    /// the Island's own marks (the spark; a PC or a Mac in RustDesk blue).
    /// </summary>
    private IReadOnlyList<RadialAction> RadialItems()
    {
        var items = new List<RadialAction>
        {
            new("agents", "✦", "AI Agents", () => _island?.ShowAgents(), ClaudeTint, Symbol: true),
        };
        if (RustDesk.Available)
        {
            foreach (var peer in RustDesk.List().Take(7))
            {
                var id = peer.Id;
                var mac = peer.Platform.Contains("Mac", StringComparison.OrdinalIgnoreCase);
                var name = peer.Host.Length > 0 ? peer.Host : peer.Id;
                items.Add(new("rustdesk:" + id, mac ? "\uE7F8" : "\uE7F4", peer.Showing ? name + " · đang xem" : name, () =>
                {
                    // Pressed again while that screen is in front: back to the desktop.
                    var minimized = RustDesk.Toggle(id);
                    _island?.Notify(mac ? "\uE7F8" : "\uE7F4", minimized ? "\u0110\u00E3 thu nh\u1ECF RustDesk" : "RustDesk: " + name);
                }, RustDeskBlue, Active: peer.Showing));
            }
        }
        return items;
    }

    private RadialMenu? _radial;
    private MiddleHold? _middle;

    /// <summary>The shortcut toggles the ring; holding the middle button only ever opens it.</summary>
    private void OpenRadial() => OpenRadial(toggle: true);

    private void OpenRadial(bool toggle)
    {
        if (_radial != null)
        {
            if (toggle) _radial.Close();
            return;
        }
        _radial = new RadialMenu(RadialItems());
        _radial.Closed += (_, _) =>
        {
            _radial = null;
            Memory.TrimSoon();
        };
        _radial.Open();
    }

    private PanelWindow BuildPanel(Background background, IslandWindow island)
    {
        var panel = new PanelWindow();
        panel.AddTab("\uE9E9", "Âm thanh", () => new SoundPage(panel));
        panel.AddTab("\uE7F4", "Thiết bị", () => new DevicesPage(panel));
        panel.AddTab("\uE9D9", "Hệ thống", () => new SystemPage(panel));
        panel.AddTab("\uE774", "Mạng", () => new NetworkPage(panel));
        panel.AddTab("\uEDA2", "Lưu trữ", () => new StoragePage(panel));
        panel.AddTab("\uE71D", "Ứng dụng", () => new AppsPage(panel));
        panel.AddTab("\uE99A", "AI agent", () => new AgentsPage(panel));
        panel.AddTab("\uE90F", "Tiện ích", () => new ToolsPage(panel, () => Bar.Open(), () => Pad.Open(), island, background, () => panel.Push(new RadialPage(panel, RadialItems, background.RadialHotkey, on =>
            {
                Store.S.RadialMiddleHold = on;
                Store.SaveSoon();
                if (_middle != null) _middle.Enabled = on;
            }))));
        panel.AddTab("\uF0E2", "Bật/tắt nhanh", () => new TogglesPage(panel));
        panel.OpenSettings = () => panel.Push(new SettingsPage(panel, _widgets, background));
        panel.Quit = Quit;
        // Closing waits a moment, so a click on the tray icon that hid the panel can still be told apart from a fresh open.
        panel.Dismissed += () => panel.Dispatcher.BeginInvoke(() => DiscardPanel(panel), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        return panel;
    }

    /// <summary>Everything LumenLite can do, for the Command Bar.</summary>
    private IEnumerable<CommandItem> Commands()
    {
        var panel = Panel;
        CommandItem Item(string title, string subtitle, string glyph, Action run) => new(title, subtitle, "Lệnh", glyph, run);
        CommandItem Settings(string title, string uri, string glyph) => new(title, "Cài đặt Windows", "Windows", glyph, () => CommandBar.Launch(uri));
        yield return Item("Mở bảng điều khiển LumenLite", "Âm thanh, thiết bị, hệ thống, mạng, ứng dụng…", "\uF0E2", () => panel.ShowPanel());
        yield return Item("Scratchpad", "Ghi chú nhanh nhiều tab", "\uE70B", () => Pad.Open());
        yield return Item("UniKey, màn hình, độ sáng", "Đổi kiểu gõ, bật/tắt màn hình, chỉnh độ sáng", "", () => panel.ShowPanel(1));
        yield return Item("Trộn âm lượng", "Âm lượng và đầu ra riêng từng app", "\uE9E9", () => panel.ShowPanel(0));
        yield return Item("Khóa máy", "Win + L", "\uE72E", QuickActions.Lock);
        yield return Item("Tắt màn hình", "Rê chuột hoặc bấm phím để bật lại", "\uE7F4", () => QuickActions.ScreensOff(IntPtr.Zero));
        yield return Item("Dọn thùng rác", "Windows hỏi lại trước khi xóa", "\uE74D", () => QuickActions.EmptyRecycleBin(IntPtr.Zero));
        yield return Item("Ẩn/hiện icon desktop", "", "\uE8FC", QuickActions.ToggleDesktopIcons);
        yield return Item("Đổi giao diện sáng/tối", QuickActions.LightTheme() ? "Đang sáng" : "Đang tối", "\uE793", () => QuickActions.SetLightTheme(!QuickActions.LightTheme()));
        yield return Item("Tắt/bật micro", "Ctrl+Alt+M", "\uE720", () => _background!.ToggleMic());
        yield return Item("Chuyển loa/tai nghe", "Ctrl+Alt+O", "\uE7F5", () => _background!.NextOutput());
        yield return Item("AI agent", "Token và phiên Claude Code", "\uE99A", () => panel.ShowPanel(6));
        yield return Item("Hệ thống", "CPU, GPU, RAM, cảnh báo", "\uE9D9", () => panel.ShowPanel(2));
        yield return Item("Đo tốc độ mạng", "Ping, tải xuống, tải lên", "\uE774", () => panel.ShowPanel(3));
        yield return Item("Dọn dẹp ổ đĩa", "File tạm, cache trình duyệt, báo lỗi", "\uE899", () => { panel.ShowPanel(4); panel.Push(new CleanerPage(panel)); });
        yield return Item("Dọn file app nhắn tin", "Zalo, Telegram, Messenger…", "\uE8BD", () => { panel.ShowPanel(4); panel.Push(new MessagingPage(panel)); });
        yield return Item("Thoát app", "Thoát một app, tất cả, hoặc tất cả trừ app đang dùng", "\uE711", () => panel.ShowPanel(5));
        yield return Item("Cập nhật app", "Qua winget", "\uE895", () => { panel.ShowPanel(5); panel.Push(new UpdatesPage(panel)); });
        yield return Item("Gỡ cài đặt app", "Kèm dọn thư mục còn sót", "\uE74D", () => { panel.ShowPanel(5); panel.Push(new UninstallPage(panel)); });
        yield return Item("Cổng mạng đang mở", "Port và tiến trình", "\uE968", () => { panel.ShowPanel(5); panel.Push(new PortsPage(panel)); });
        yield return Updater.Status == Updater.State.Ready
            ? Item("Khởi động lại để cập nhật LumenLite", Updater.Message, "\uE777", () => Updater.Restart(Quit))
            : Item("Kiểm tra cập nhật LumenLite", $"Đang dùng {Updater.CurrentText}", "\uE777", () => _ = Updater.CheckAsync());
        yield return Item("Lau phím (Cleaning Mode)", "Khóa bàn phím để lau", "\uE765", CleaningMode.Start);
        yield return Item("Chỉnh sửa bố cục widget", "Kéo để di chuyển, Esc để xong", "\uE70F", () => WidgetWindow.SetEditing(true));
        yield return Settings("Bluetooth", "ms-settings:bluetooth", "\uE702");
        yield return Settings("Wi-Fi", "ms-settings:network-wifi", "\uE701");
        yield return Settings("Âm thanh (Windows)", "ms-settings:sound", "\uE767");
        yield return Settings("Màn hình (Windows)", "ms-settings:display", "\uE7F4");
        yield return Settings("Cài đặt Windows", "ms-settings:", "\uE713");
        yield return Settings("Ứng dụng đã cài (Windows)", "ms-settings:appsfeatures", "\uE71D");
        foreach (var peer in RustDesk.List().Take(6))
        {
            var id = peer.Id;
            yield return new CommandItem("RustDesk: " + (peer.Host.Length > 0 ? peer.Host : peer.Id), $"{peer.Id} · {peer.User} · {peer.Platform}" + (peer.Showing ? " · đang xem" : ""), "RustDesk", "\uE703", () => RustDesk.Toggle(id));
        }
        yield return new CommandItem("Task Manager", "Quản lý tác vụ", "Windows", "\uE9D9", () => CommandBar.Launch("taskmgr.exe"));
        yield return new CommandItem("Thư mục Downloads", "", "Thư mục", "\uE896", () => CommandBar.Launch(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")));
    }

    /// <summary>Lets a widget's content scroll when the user makes it smaller than its lists.</summary>
    private static ScrollViewer Scrolling(FrameworkElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };

    private void Quit()
    {
        Store.Log("quit requested: " + new System.Diagnostics.StackTrace(1, false).GetFrame(0)?.GetMethod()?.Name);
        Store.SaveNow();
        WidgetWindow.Exiting = true;
        _tray?.Dispose();
        _middle?.Dispose();
        _panel?.Close();
        _commandBar?.Close();
        _scratchpad?.Close();
        CleaningMode.Stop();
        _background?.Dispose();
        _island?.Close();
        foreach (var widget in _widgets)
            widget.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Store.Log($"exit {e.ApplicationExitCode}");
        Store.SaveNow();
        base.OnExit(e);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A widget error is logged instead of taking every widget down with it.
        Store.Log("unhandled: " + e.Exception);
        e.Handled = true;
    }
}
