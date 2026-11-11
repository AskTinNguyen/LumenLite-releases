using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LumenLite.Panel;

/// <summary>UniKey's typing mode, turning monitors on or off, and monitor brightness.</summary>
internal sealed class DevicesPage : Page
{
    private readonly StackPanel _uniKey = new();
    private readonly StackPanel _displays = new();
    private readonly StackPanel _brightness = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly List<PanelSlider> _brightnessSliders = new();
    private string _displaySignature = "";
    private bool? _lastMode;
    private bool _uniKeyBusy, _displayBusy;

    public DevicesPage(PanelWindow host) : base(host)
    {
        Children.Add(Kit.CardOf(_uniKey));
        Children.Add(Kit.Section("Màn hình"));
        Children.Add(Kit.CardOf(_displays));
        Children.Add(Kit.Section("Độ sáng"));
        Children.Add(Kit.CardOf(_brightness));
        var settings = Kit.Button("", "Cài đặt màn hình của Windows", () =>
        {
            Host.HidePanel();
            CommandBar.Launch("ms-settings:display");
        });
        Children.Add(settings);
        _timer.Tick += (_, _) =>
        {
            RefreshUniKey();
            RefreshDisplays();
        };
        WhileShown(() => Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaysChanged,
                   () => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged);
    }

    public override string Title => "Thiết bị";

    private void OnDisplaysChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        _displaySignature = "";
        RefreshDisplays();
        RefreshBrightness();
    });

    public override void Shown()
    {
        _displaySignature = "";
        RefreshUniKey();
        RefreshDisplays();
        RefreshBrightness();
        _timer.Start();
    }

    public override void Hidden() => _timer.Stop();

    // ---------- UniKey ----------

    private void RefreshUniKey()
    {
        if (_uniKeyBusy) return;
        _uniKeyBusy = true;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var running = false;
            bool? vietnamese = null;
            try
            {
                running = UniKey.IsRunning;
                if (running) vietnamese = UniKey.Vietnamese();
            }
            catch (Exception ex)
            {
                Store.Log("unikey read failed: " + ex.Message);
            }
            Dispatcher.BeginInvoke(() =>
            {
                _uniKeyBusy = false;
                ShowUniKey(running, vietnamese);
            });
        });
    }

    private void ShowUniKey(bool running, bool? vietnamese)
    {
        if (vietnamese != null) _lastMode = vietnamese;
        var mode = running ? vietnamese ?? _lastMode : null;
        _uniKey.Children.Clear();
        if (!running)
        {
            _uniKey.Children.Add(Kit.ToggleRow("UniKey chưa chạy", "Mở UniKey để đổi kiểu gõ ở đây.", false, _ => RefreshUniKey()));
            return;
        }
        var title = mode switch { true => "UniKey: Tiếng Việt", false => "UniKey: English", null => "Kiểu gõ UniKey" };
        var description = mode == null
            ? "Biểu tượng UniKey đang ở khay ẩn; bấm để đổi, LumenLite sẽ biết kiểu mới."
            : "Bật là gõ tiếng Việt, tắt là gõ English. Giống bấm biểu tượng UniKey.";
        _uniKey.Children.Add(Kit.ToggleRow(title, description, mode == true, _ => Toggle()));
    }

    private void Toggle()
    {
        if (_uniKeyBusy) return;
        _uniKeyBusy = true;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            bool? vietnamese = null;
            try
            {
                vietnamese = UniKey.Toggle();
            }
            catch (Exception ex)
            {
                Store.Log("unikey toggle failed: " + ex.Message);
            }
            Dispatcher.BeginInvoke(() =>
            {
                _uniKeyBusy = false;
                ShowUniKey(true, vietnamese);
            });
        });
    }

    // ---------- monitors ----------

    private void RefreshDisplays()
    {
        if (_displayBusy) return;
        List<DisplayTarget> targets;
        try
        {
            targets = Displays.Targets();
        }
        catch (Exception ex)
        {
            Store.Log("display list failed: " + ex.Message);
            return;
        }
        var signature = string.Join("|", targets.Select(t => $"{t.Adapter}:{t.TargetId}:{t.Name}:{t.Active}:{t.Primary}"));
        if (signature == _displaySignature) return;
        _displaySignature = signature;

        _displays.Children.Clear();
        var active = targets.Count(t => t.Active);
        var counts = targets.GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.Count());
        var seen = new Dictionary<string, int>();
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            var label = target.Name;
            if (counts[target.Name] > 1)
            {
                seen[target.Name] = seen.GetValueOrDefault(target.Name) + 1;
                label += $" {seen[target.Name]}";
            }
            var description = !target.Active ? "Đang tắt" : target.Primary ? "Màn hình chính" : "Đang bật";
            var last = target.Active && active == 1;
            if (last) description += " · không thể tắt màn hình cuối cùng";
            var row = Kit.ToggleRow(label, description, target.Active, on => SetDisplay(target, on));
            row.IsEnabled = !last;
            row.Opacity = last ? 0.55 : 1;
            if (i > 0)
                _displays.Children.Add(new Border { Height = 1, Background = Kit.Divider, Margin = new Thickness(0, 8, 0, 8) });
            _displays.Children.Add(row);
        }
        if (targets.Count == 0)
            _displays.Children.Add(Kit.Wrap("Không tìm thấy màn hình.", 13));
    }

    private void SetDisplay(DisplayTarget target, bool on)
    {
        _displayBusy = true;
        var error = 0;
        try
        {
            error = Displays.SetActive(target, on);
        }
        catch (Exception ex)
        {
            Store.Log("display toggle failed: " + ex.Message);
            error = -1;
        }
        finally
        {
            _displayBusy = false;
        }
        if (error != 0)
            Host.Toast($"Windows không {(on ? "bật" : "tắt")} được {target.Name} (mã {error}). Thử trong Cài đặt màn hình.");
        _displaySignature = "";
        RefreshDisplays();
    }

    // ---------- brightness ----------

    private void RefreshBrightness()
    {
        if (_brightnessSliders.Any(s => s.IsDragging)) return;
        _brightness.Children.Clear();
        _brightness.Children.Add(Kit.Wrap("Đang hỏi màn hình…", 13));
        Brightness.Read(monitors => Dispatcher.BeginInvoke(() => ShowBrightness(monitors)));
    }

    private void ShowBrightness(List<BrightnessMonitor> monitors)
    {
        _brightness.Children.Clear();
        _brightnessSliders.Clear();
        if (monitors.Count == 0)
        {
            _brightness.Children.Add(Kit.Wrap("Không màn hình nào nhận lệnh độ sáng. Bật DDC/CI trong menu của màn hình rời; màn hình laptop chưa hỗ trợ.", 12.5));
            return;
        }
        var counts = monitors.GroupBy(m => m.Name).ToDictionary(g => g.Key, g => g.Count());
        var seen = new Dictionary<string, int>();
        foreach (var monitor in monitors)
        {
            var label = monitor.Name;
            if (counts[monitor.Name] > 1)
            {
                seen[monitor.Name] = seen.GetValueOrDefault(monitor.Name) + 1;
                label += $" {seen[monitor.Name]}";
            }
            var name = Kit.Label(label, 13.5, true);
            name.Margin = new Thickness(0, 4, 0, 2);
            _brightness.Children.Add(name);
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            var icon = Kit.Glyph("", 15, Kit.Secondary);
            icon.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(icon);
            var percent = Kit.Mono($"{Math.Round(monitor.Level * 100)}%", 12, false, Kit.Secondary);
            percent.HorizontalAlignment = HorizontalAlignment.Right;
            var key = monitor.Key;
            var slider = new PanelSlider(monitor.Level, level =>
            {
                Brightness.Set(key, level);
                percent.Text = $"{Math.Round(level * 100)}%";
            });
            Grid.SetColumn(slider, 1);
            row.Children.Add(slider);
            Grid.SetColumn(percent, 2);
            row.Children.Add(percent);
            _brightness.Children.Add(row);
            _brightnessSliders.Add(slider);
        }
    }
}
