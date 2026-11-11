using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LumenLite.Panel;

/// <summary>Volume mixer with per-app output, output devices and the microphone.</summary>
internal sealed class SoundPage : Page
{
    private readonly StackPanel _apps = new();
    private readonly StackPanel _outputs = new();
    private readonly StackPanel _mic = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private readonly Dictionary<int, (PanelSlider Slider, TextBlock Percent, TextBlock Mute)> _rows = new();
    private string _appsSignature = "", _outputSignature = "", _micSignature = "";
    private PanelSlider? _micSlider;
    private TextBlock? _micPercent, _micMute;

    public SoundPage(PanelWindow host) : base(host)
    {
        Children.Add(Kit.CardOf(_apps));
        Children.Add(Kit.Section("Đầu ra"));
        Children.Add(Kit.CardOf(_outputs));
        Children.Add(Kit.Section("Micro", Kit.KeyHint("Ctrl+Alt+M: tắt/bật")));
        Children.Add(Kit.CardOf(_mic));
        _timer.Tick += (_, _) => Refresh();
    }

    public override string Title => "Trộn âm lượng";

    public override void Shown()
    {
        _appsSignature = _outputSignature = _micSignature = "";
        Refresh();
        _timer.Start();
    }

    public override void Hidden() => _timer.Stop();

    private List<AudioOutput> _outputList = new();

    private void Refresh()
    {
        try
        {
            _outputList = Audio.Outputs().OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            RefreshApps();
            RefreshOutputs();
            RefreshMic();
        }
        catch (Exception ex)
        {
            Store.Log("sound page failed: " + ex.Message);
        }
    }

    private void RefreshApps()
    {
        var apps = Audio.Apps().OrderBy(a => a.IsSystem).ThenBy(a => a.Pid).ToList();
        var signature = string.Join("|", apps.Select(a => a.Pid)) + "#" + string.Join("|", _outputList.Select(o => o.Id))
            + "#" + string.Join("|", apps.Select(a => AudioPolicy.GetOutput(a.Pid)));
        if (signature != _appsSignature)
        {
            _appsSignature = signature;
            _apps.Children.Clear();
            _rows.Clear();
            foreach (var app in apps)
                _apps.Children.Add(AppRow(app));
            if (apps.Count == 0)
                _apps.Children.Add(Kit.Wrap("Chưa có app nào phát âm thanh.", 13));
            else if (AudioPolicy.Available)
            {
                var note = Kit.Wrap("Đổi đầu ra của một app có hiệu lực khi app phát lại (nhiều app cần tạm dừng rồi phát tiếp).", 11);
                note.Margin = new Thickness(0, 6, 0, 0);
                _apps.Children.Add(note);
            }
        }
        foreach (var app in apps)
        {
            if (!_rows.TryGetValue(app.Pid, out var row) || row.Slider.IsDragging)
                continue;
            row.Slider.Value = app.Level;
            row.Percent.Text = app.Muted ? "Tắt" : $"{Math.Round(app.Level * 100)}%";
            row.Mute.Text = app.Muted ? VolumeBarGlyph(true) : VolumeBarGlyph(false);
        }
    }

    private static string VolumeBarGlyph(bool muted) => muted ? "" : "";

    private UIElement AppRow(AppVolume app)
    {
        string name;
        BitmapSource? icon = null;
        if (app.IsSystem)
            name = "Âm thanh hệ thống";
        else
        {
            (name, icon) = AppInfo.Of(app.Pid);
            if (!string.IsNullOrWhiteSpace(app.DisplayName) && !app.DisplayName.StartsWith('@'))
                name = app.DisplayName;
        }

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());

        FrameworkElement image = icon != null
            ? new Image { Source = icon, Width = 34, Height = 34 }
            : new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(Kit.ControlRadius), Background = Kit.Inset, Child = Kit.Glyph(app.IsSystem ? "" : "", 15, Kit.Secondary) };
        image.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetRowSpan(image, 2);
        grid.Children.Add(image);

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Kit.Label(name, 14.5, true));
        if (!app.IsSystem && AudioPolicy.Available)
        {
            var pinned = AudioPolicy.GetOutput(app.Pid);
            var current = pinned == null ? "Mặc định" : _outputList.FirstOrDefault(o => o.Id == pinned)?.Name ?? "Mặc định";
            var choices = new List<(string, Action)> { ("Mặc định (theo Windows)", () => SetAppOutput(app.Pid, null)) };
            choices.AddRange(_outputList.Select(o => (o.Name, (Action)(() => SetAppOutput(app.Pid, o.Id)))));
            var dropdown = Kit.Dropdown(current, choices, 160);
            Grid.SetColumn(dropdown, 1);
            top.Children.Add(dropdown);
        }
        Grid.SetColumn(top, 1);
        grid.Children.Add(top);

        var bottom = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var pid = app.Pid;
        var slider = new PanelSlider(app.Level, level => Audio.SetAppVolume(pid, level));
        bottom.Children.Add(slider);
        var percent = Kit.Mono("", 12, false, Kit.Secondary);
        percent.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(percent, 1);
        bottom.Children.Add(percent);
        var muteGlyph = Kit.Glyph(VolumeBarGlyph(app.Muted), 14, Kit.Secondary);
        var muted = app.Muted;
        var mute = Kit.Clickable(muteGlyph, () =>
        {
            muted = !muted;
            Audio.SetAppMute(pid, muted);
            muteGlyph.Text = VolumeBarGlyph(muted);
            percent.Text = muted ? "Tắt" : $"{Math.Round(slider.Value * 100)}%";
        }, new Thickness(8, 4, 4, 4));
        Grid.SetColumn(mute, 2);
        bottom.Children.Add(mute);
        Grid.SetColumn(bottom, 1);
        Grid.SetRow(bottom, 1);
        grid.Children.Add(bottom);

        _rows[app.Pid] = (slider, percent, muteGlyph);
        return grid;
    }

    private void SetAppOutput(int pid, string? deviceId)
    {
        if (!AudioPolicy.SetOutput(pid, deviceId))
            Host.Toast("Windows không cho đổi đầu ra của app này.");
        _appsSignature = "";
        Refresh();
    }

    private void RefreshOutputs()
    {
        var current = Audio.DefaultId();
        var signature = current + string.Join("|", _outputList.Select(o => o.Id)) + Store.S.LowerOnUnplug;
        if (signature == _outputSignature)
            return;
        _outputSignature = signature;
        _outputs.Children.Clear();
        foreach (var output in _outputList)
        {
            var selected = output.Id == current;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(Kit.Glyph(output.FormFactor is AudioFormFactor.Headphones or AudioFormFactor.Headset ? "" : output.FormFactor == AudioFormFactor.Display ? "" : "", 15, selected ? Kit.Accent : Kit.Secondary));
            var label = Kit.Label(output.Name, 13.5, selected);
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);
            if (selected)
            {
                var check = Kit.Glyph("", 12, Kit.Accent);
                Grid.SetColumn(check, 2);
                grid.Children.Add(check);
            }
            var row = Kit.Clickable(grid, () =>
            {
                Audio.SetDefault(output.Id);
                _outputSignature = "";
                Refresh();
            }, new Thickness(6, 7, 6, 7));
            row.Margin = new Thickness(-6, 0, -6, 0);
            _outputs.Children.Add(row);
        }
        var divider = new Border { Height = 1, Background = Kit.Divider, Margin = new Thickness(0, 8, 0, 8) };
        _outputs.Children.Add(divider);
        _outputs.Children.Add(Kit.ToggleRow("Giảm âm lượng khi rút tai nghe", "Hạ về 25% để loa ngoài không phát quá to", Store.S.LowerOnUnplug, on =>
        {
            Store.S.LowerOnUnplug = on;
            Store.SaveSoon();
        }));
        var hint = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        hint.Children.Add(Kit.Label("Chuyển nhanh: ", 12, false, Kit.Secondary));
        hint.Children.Add(Kit.KeyHint("Ctrl+Alt+O"));
        _outputs.Children.Add(hint);
    }

    private void RefreshMic()
    {
        var inputs = Audio.Inputs().OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var current = Audio.DefaultInputId();
        var signature = current + string.Join("|", inputs.Select(i => i.Id)) + Store.S.PreferredMic;
        if (signature != _micSignature)
        {
            _micSignature = signature;
            _mic.Children.Clear();
            if (inputs.Count == 0)
            {
                _mic.Children.Add(Kit.Wrap("Không có micro nào đang cắm.", 13));
                _micSlider = null;
                return;
            }
            var preferredName = Store.S.PreferredMic is { } p ? inputs.FirstOrDefault(i => i.Id == p)?.Name ?? "(đang rút)" : "Không";
            var choices = new List<(string, Action)> { ("Không ưu tiên (theo Windows)", () => SetPreferred(null)) };
            choices.AddRange(inputs.Select(i => (i.Name, (Action)(() => SetPreferred(i.Id)))));
            var prefer = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            prefer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            prefer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(Kit.Label("Micro ưu tiên", 13.5, true));
            text.Children.Add(Kit.Wrap("Tự dùng micro này mỗi khi nó được cắm", 11.5));
            prefer.Children.Add(text);
            var dropdown = Kit.Dropdown(preferredName, choices, 170);
            dropdown.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(dropdown, 1);
            prefer.Children.Add(dropdown);
            _mic.Children.Add(prefer);

            var currentName = inputs.FirstOrDefault(i => i.Id == current)?.Name ?? "—";
            _mic.Children.Add(Kit.ValueRow("Đang dùng", currentName));

            var levelRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            levelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            levelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            levelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            _micMute = Kit.Glyph("", 15, Kit.Text);
            var mute = Kit.Clickable(_micMute, () =>
            {
                if (Audio.MicVolume() is { } v)
                    Audio.SetMicMute(!v.Muted);
                RefreshMicLevel();
            }, new Thickness(4, 4, 10, 4));
            mute.ToolTip = "Tắt/bật micro";
            levelRow.Children.Add(mute);
            _micSlider = new PanelSlider(0, Audio.SetMicVolume);
            Grid.SetColumn(_micSlider, 1);
            levelRow.Children.Add(_micSlider);
            _micPercent = Kit.Mono("", 12, false, Kit.Secondary);
            _micPercent.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(_micPercent, 2);
            levelRow.Children.Add(_micPercent);
            _mic.Children.Add(levelRow);
        }
        RefreshMicLevel();
    }

    private void RefreshMicLevel()
    {
        if (_micSlider == null || Audio.MicVolume() is not { } mic)
            return;
        _micSlider.Value = mic.Level;
        _micPercent!.Text = mic.Muted ? "Tắt" : $"{Math.Round(mic.Level * 100)}%";
        _micMute!.Text = mic.Muted ? "" : "";
        _micMute.Foreground = mic.Muted ? Kit.Red : Kit.Text;
    }

    private void SetPreferred(string? id)
    {
        Store.S.PreferredMic = id;
        Store.SaveSoon();
        if (id != null)
            Audio.SetDefault(id);
        _micSignature = "";
        Refresh();
    }
}

/// <summary>Usage with history graphs, memory pressure, battery, uptime, alerts and the Island readout.</summary>
internal sealed class SystemPage : Page
{
    private readonly UsageRow _cpu = new("CPU", Kit.Text, Kit.TextColor);
    private readonly UsageRow _gpu = new("GPU", Kit.Text, Kit.TextColor);
    private readonly UsageRow _battery = new("Pin", Kit.Green, null) { Visibility = Visibility.Collapsed };
    private readonly Border _pressure = new() { CornerRadius = new CornerRadius(Kit.KeyRadius), Padding = new Thickness(8, 2, 9, 3), Margin = new Thickness(10, 0, 0, 0) };
    private readonly TextBlock _pressureText = Kit.Label("", 12, true);
    private readonly TextBlock _memoryText = Kit.Mono("", 12.5);
    private readonly Sparkline _memoryGraph = new(Kit.TextColor);
    private readonly TextBlock _uptime = Kit.Label("", 13, false, Kit.Secondary);
    private readonly StackPanel _islandChips = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(-3, 0, -3, 0) };

    public SystemPage(PanelWindow host) : base(host)
    {
        var memoryRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        memoryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        memoryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        memoryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        memoryRow.Children.Add(Kit.Label("Áp lực RAM", 13.5));
        var dot = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 6, 0) };
        var badge = new StackPanel { Orientation = Orientation.Horizontal };
        badge.Children.Add(dot);
        badge.Children.Add(_pressureText);
        _pressure.Child = badge;
        _pressure.Tag = dot;
        Grid.SetColumn(_pressure, 1);
        memoryRow.Children.Add(_pressure);
        _memoryText.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_memoryText, 2);
        memoryRow.Children.Add(_memoryText);

        var uptime = new StackPanel { Orientation = Orientation.Horizontal };
        var clock = Kit.Glyph("", 13, Kit.Secondary);
        clock.Margin = new Thickness(0, 0, 8, 0);
        uptime.Children.Add(clock);
        uptime.Children.Add(_uptime);

        Children.Add(Kit.CardOf(true,
            Titled("Mức sử dụng phần cứng", _cpu, _gpu, _battery),
            Titled("Bộ nhớ", memoryRow, _memoryGraph),
            uptime));

        Children.Add(Kit.Section("Cảnh báo"));
        Children.Add(Kit.CardOf(true,
            Kit.ToggleRow("CPU tải cao", "Trên 90% liên tục 1 phút", Store.S.AlertCpu, on => { Store.S.AlertCpu = on; Store.SaveSoon(); }),
            Kit.ToggleRow("Thiếu RAM", "Dùng trên 92% bộ nhớ", Store.S.AlertMemory, on => { Store.S.AlertMemory = on; Store.SaveSoon(); }),
            Kit.ToggleRow("Ổ C sắp đầy", "Còn dưới 10 GB hoặc dưới 5%", Store.S.AlertDisk, on => { Store.S.AlertDisk = on; Store.SaveSoon(); }),
            Kit.ToggleRow("Pin yếu", "Còn 20% và không cắm sạc", Store.S.AlertBattery, on => { Store.S.AlertBattery = on; Store.SaveSoon(); })));
        var alertNote = Kit.Wrap("Cảnh báo hiện trên Dynamic Island và thông báo Windows, mỗi loại tối đa 30 phút một lần. Windows không cho đọc nhiệt độ hay tốc độ quạt nếu không cài driver riêng.", 11);
        alertNote.Margin = new Thickness(4, -4, 4, 0);
        Children.Add(alertNote);

        Children.Add(Kit.Section("Chỉ số trên Dynamic Island"));
        Children.Add(Kit.CardOf(_islandChips, Kit.Wrap("Hiện ngay trên viên Island lúc rảnh, giống chỉ số trên thanh menu của Mac.", 11.5)));
        BuildChips();

        WhileShown(() => { SystemMonitor.Sampled += Show; SystemMonitor.Watch(true); },
                   () => { SystemMonitor.Sampled -= Show; SystemMonitor.Watch(false); });
    }

    public override string Title => "Hệ thống";

    public override void Shown()
    {
        if (SystemMonitor.Last is { } s)
            Show(s);
    }

    private static StackPanel Titled(string title, params UIElement[] children)
    {
        var stack = new StackPanel();
        var heading = Kit.Label(title, 14, true);
        heading.Margin = new Thickness(0, 0, 0, 4);
        stack.Children.Add(heading);
        foreach (var child in children)
            stack.Children.Add(child);
        return stack;
    }

    private void BuildChips()
    {
        _islandChips.Children.Clear();
        foreach (var (id, label) in new[] { ("off", "Tắt"), ("system", "CPU · RAM"), ("network", "Mạng ↓↑") })
        {
            var chip = Kit.Chip(label, Store.S.IslandMetrics == id, () =>
            {
                Store.S.IslandMetrics = id;
                Store.SaveSoon();
                BuildChips();
            });
            chip.MinWidth = 100;
            _islandChips.Children.Add(chip);
        }
        _islandChips.Margin = new Thickness(-3, 0, -3, 6);
    }

    private void Show(SystemSample s)
    {
        _cpu.Update(s.Cpu / 100, $"{s.Cpu:0}%", SystemMonitor.Cpu);
        _gpu.Update((s.Gpu ?? 0) / 100, s.Gpu is { } g ? $"{g:0}%" : "—", SystemMonitor.Gpu);
        if (s.Battery is { } battery)
        {
            _battery.Visibility = Visibility.Visible;
            _battery.Update(battery, $"{battery * 100:0}%" + (s.Charging ? " ⚡" : ""));
        }
        var percent = s.MemoryTotal == 0 ? 0 : 100.0 * s.MemoryUsed / s.MemoryTotal;
        var (label, color, soft) = percent switch
        {
            < 75 => ("Bình thường", Kit.Green, Kit.GreenSoft),
            < 90 => ("Cao", Kit.Amber, Kit.AmberSoft),
            _ => ("Rất cao", Kit.Red, Kit.RedSoft),
        };
        _pressureText.Text = label;
        _pressureText.Foreground = color;
        _pressure.Background = soft;
        ((System.Windows.Shapes.Ellipse)_pressure.Tag).Fill = color;
        _memoryText.Text = $"{s.MemoryUsed / 1073741824.0:0.0} GB / {s.MemoryTotal / 1073741824.0:0} GB";
        _memoryGraph.SetValues(SystemMonitor.Memory);
        _uptime.Text = "Máy đã chạy " + SystemMonitor.Uptime();
    }
}

/// <summary>Live traffic, addresses and a speed test.</summary>
internal sealed class NetworkPage : Page
{
    private readonly StatTile _down = new("", "Tải xuống");
    private readonly StatTile _up = new("", "Tải lên");
    private readonly Sparkline _graph = new(Kit.TextColor, fixedMax: false, height: 40);
    private readonly StackPanel _details = new();
    private readonly StatTile _ping = new("", "Độ trễ");
    private readonly StatTile _speedDown = new("", "Xuống");
    private readonly StatTile _speedUp = new("", "Lên");
    private readonly TextBlock _speedStatus = Kit.Wrap("Đo qua speed.cloudflare.com, tốn khoảng 50–150 MB dữ liệu.", 12);
    private readonly Border _speedButton;
    private CancellationTokenSource? _speedCancel;

    public NetworkPage(PanelWindow host) : base(host)
    {
        var tiles = new UniformGrid { Columns = 2, Margin = new Thickness(-3) };
        tiles.Children.Add(_down);
        tiles.Children.Add(_up);
        Children.Add(Kit.CardOf(tiles, _graph, _details));

        Children.Add(Kit.Section("Đo tốc độ mạng"));
        var results = new UniformGrid { Columns = 3, Margin = new Thickness(-3, 0, -3, 8) };
        results.Children.Add(_ping);
        results.Children.Add(_speedDown);
        results.Children.Add(_speedUp);
        _ping.Value = _speedDown.Value = _speedUp.Value = "—";
        _speedButton = Kit.Button("", "Bắt đầu đo", RunSpeedTest, Kit.Ink, Kit.OnInk);
        _speedButton.Margin = new Thickness(0, 8, 0, 0);
        Children.Add(Kit.CardOf(results, _speedStatus, _speedButton));

        WhileShown(() => { SystemMonitor.Sampled += Show; SystemMonitor.Watch(true); },
                   () => { SystemMonitor.Sampled -= Show; SystemMonitor.Watch(false); });
    }

    public override string Title => "Mạng";

    public override void Shown()
    {
        if (SystemMonitor.Last is { } s)
            Show(s);
    }

    private void Show(SystemSample s)
    {
        _down.Value = SystemMonitor.Speed(s.DownloadPerSecond);
        _up.Value = SystemMonitor.Speed(s.UploadPerSecond);
        _graph.SetValues(SystemMonitor.Download);
        _details.Children.Clear();
        _details.Children.Add(Kit.ValueRow("IP nội bộ", s.LocalIp ?? "Không có mạng"));
        _details.Children.Add(Kit.ValueRow("Đã tải xuống (từ khi mở app)", SystemMonitor.Size(SystemMonitor.SessionDown)));
        _details.Children.Add(Kit.ValueRow("Đã tải lên (từ khi mở app)", SystemMonitor.Size(SystemMonitor.SessionUp)));
    }

    private async void RunSpeedTest()
    {
        if (_speedCancel != null)
        {
            _speedCancel.Cancel();
            return;
        }
        _speedCancel = new CancellationTokenSource();
        SetButton("", "Dừng");
        _ping.Value = _speedDown.Value = _speedUp.Value = "…";
        try
        {
            var progress = new Progress<string>(text => _speedStatus.Text = text);
            var result = await SpeedTest.RunAsync(progress, _speedCancel.Token);
            _ping.Value = $"{result.PingMs:0} ms";
            _speedDown.Value = $"{result.DownloadMbps:0}";
            _speedUp.Value = $"{result.UploadMbps:0}";
            _speedStatus.Text = $"Tải xuống {result.DownloadMbps:0} Mbps, tải lên {result.UploadMbps:0} Mbps (đơn vị ô: Mbps). Đo lúc {DateTime.Now:HH:mm}.";
        }
        catch (OperationCanceledException)
        {
            _speedStatus.Text = "Đã dừng.";
            _ping.Value = _speedDown.Value = _speedUp.Value = "—";
        }
        catch (Exception ex)
        {
            _speedStatus.Text = "Không đo được: " + ex.Message;
            _ping.Value = _speedDown.Value = _speedUp.Value = "—";
        }
        finally
        {
            _speedCancel = null;
            SetButton("", "Đo lại");
        }
    }

    private void SetButton(string glyph, string text)
    {
        var row = (StackPanel)_speedButton.Child;
        ((TextBlock)row.Children[0]).Text = glyph;
        ((TextBlock)row.Children[1]).Text = text;
    }
}
