using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LumenLite.Panel;

/// <summary>Claude Code on this machine: tokens today and this week, per model, and the sessions running now.</summary>
internal sealed class AgentsPage : Page
{
    private readonly StatTile _today = new("", "Hôm nay");
    private readonly StatTile _week = new("", "7 ngày");
    private readonly StatTile _running = new("", "Đang chạy");
    private readonly Grid _chart = new() { Height = 70, Margin = new Thickness(0, 10, 0, 0) };
    private readonly StackPanel _models = new();
    private readonly StackPanel _sessions = new();

    public AgentsPage(PanelWindow host) : base(host)
    {
        if (!AgentMonitor.Available)
        {
            Children.Add(Kit.CardOf(Kit.Wrap("Chưa thấy Claude Code trên máy này (thư mục %USERPROFILE%\\.claude\\projects). Codex, Copilot hay OpenCode cũng chưa có log để đọc.", 13)));
            return;
        }
        var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(-3) };
        tiles.Children.Add(_today);
        tiles.Children.Add(_week);
        tiles.Children.Add(_running);
        Children.Add(Kit.CardOf(tiles, _chart));
        Children.Add(Kit.Section("Theo model · hôm nay"));
        Children.Add(Kit.CardOf(_models));
        Children.Add(Kit.Section("Phiên · bấm để mở"));
        Children.Add(Kit.CardOf(_sessions));
        Children.Add(Kit.Section("Thông báo"));
        Children.Add(Kit.CardOf(true,
            Kit.ToggleRow("Báo khi Claude xong việc", "Dynamic Island báo khi một phiên chạy hơn 1 phút rồi xong lượt.", Store.S.AgentAlerts, on => { Store.S.AgentAlerts = on; Store.SaveSoon(); }),
            Kit.ToggleRow("Hiện trên Island khi đang chạy", "Viên Island hiện biểu tượng ✳ và số phiên đang làm việc.", Store.S.AgentOnIsland, on => { Store.S.AgentOnIsland = on; Store.SaveSoon(); AgentMonitor.Nudge(); })));
        var note = Kit.Wrap("Đọc từ log Claude Code trên máy, không gửi dữ liệu đi đâu. Log không ghi giới hạn gói hay thời điểm reset, nên mục đó không có. Token gồm vào, ra, ghi cache và đọc cache.", 11);
        note.Margin = new Thickness(4, -4, 4, 0);
        Children.Add(note);
        WhileShown(() => AgentMonitor.Updated += Draw, () => AgentMonitor.Updated -= Draw);
    }

    public override string Title => "AI agent";

    public override void Shown() => Draw();

    private void Draw()
    {
        if (!AgentMonitor.Available)
            return;
        var today = AgentMonitor.UsageBetween(DateTime.Today, DateTime.Today);
        var week = AgentMonitor.UsageBetween(DateTime.Today.AddDays(-6), DateTime.Today);
        var sessions = AgentMonitor.Sessions();
        _today.Value = AgentMonitor.Tokens(today.Sum(m => m.Total));
        _week.Value = AgentMonitor.Tokens(week.Sum(m => m.Total));
        _running.Value = sessions.Count(s => s.Working).ToString();

        DrawChart();

        _models.Children.Clear();
        if (today.Count == 0)
            _models.Children.Add(Kit.Wrap("Hôm nay chưa dùng.", 13));
        foreach (var model in today)
        {
            var top = Kit.ValueRow(model.Model, AgentMonitor.Tokens(model.Total));
            _models.Children.Add(top);
            var detail = Kit.Label($"vào {AgentMonitor.Tokens(model.Input)} · ra {AgentMonitor.Tokens(model.Output)} · ghi cache {AgentMonitor.Tokens(model.CacheWrite)} · đọc cache {AgentMonitor.Tokens(model.CacheRead)}", 11.5, false, Kit.Secondary);
            detail.Margin = new Thickness(0, -2, 0, 6);
            _models.Children.Add(detail);
        }

        _sessions.Children.Clear();
        if (sessions.Count == 0)
            _sessions.Children.Add(Kit.Wrap("Không có phiên nào trong 12 giờ qua.", 13));
        foreach (var session in sessions.Take(12))
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var dot = new Ellipse { Width = 7, Height = 7, Fill = session.Working ? Kit.Accent : Kit.Track, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            grid.Children.Add(dot);
            var text = new StackPanel();
            text.Children.Add(Kit.Label(session.Title, 13.5, true));
            var where = string.Join(" · ", new[] { session.Machine.Length > 0 ? "Máy " + session.Machine : "", session.Project }.Where(x => x.Length > 0));
            text.Children.Add(Kit.Label(where.Length > 0 ? where : "—", 11.5, false, Kit.Secondary));
            var activity = Kit.Wrap(session.NeedsInput ? "Đang chờ bạn" : session.Working ? (session.Activity.Length > 0 ? session.Activity : "Đang chạy") : $"Xong {Ago(session.LastActive)}", 11.5);
            if (session.NeedsInput) activity.Foreground = Kit.Amber;
            text.Children.Add(activity);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var tokens = Kit.Mono(AgentMonitor.Tokens(session.Tokens), 11.5, false, Kit.Secondary);
            tokens.VerticalAlignment = VerticalAlignment.Top;
            tokens.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(tokens, 2);
            grid.Children.Add(tokens);
            // A click opens the session where it lives, as the Island's rows do.
            var row = Kit.Clickable(grid, () =>
            {
                if (AgentOpen.Open(session))
                    Host.HidePanel();
                else
                    Host.Toast($"Không mở được app {AgentMonitor.Name(session.Provider)}");
            }, new Thickness(8, 0, 8, 0));
            row.Margin = new Thickness(-8, 0, -8, 2);
            row.ToolTip = AgentOpen.Describe(session);
            _sessions.Children.Add(row);
        }
    }

    /// <summary>Seven bars, one per day, today on the right.</summary>
    private void DrawChart()
    {
        _chart.Children.Clear();
        _chart.ColumnDefinitions.Clear();
        var days = AgentMonitor.Daily(7);
        var max = Math.Max(1, days.Max(d => d.Tokens));
        for (var i = 0; i < days.Count; i++)
        {
            _chart.ColumnDefinitions.Add(new ColumnDefinition());
            var (day, tokens) = days[i];
            var column = new Grid { Margin = new Thickness(4, 0, 4, 0) };
            column.RowDefinitions.Add(new RowDefinition());
            column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var bar = new Border
            {
                Height = Math.Max(3, 50.0 * tokens / max),
                CornerRadius = new CornerRadius(1),
                Background = day == DateTime.Today ? Kit.Accent : Kit.Track,
                VerticalAlignment = VerticalAlignment.Bottom,
                ToolTip = $"{day:dd/MM}: {AgentMonitor.Tokens(tokens)} token",
            };
            column.Children.Add(bar);
            var label = Kit.Mono(day == DateTime.Today ? "Nay" : $"{day:dd}", 10, false, day == DateTime.Today ? Kit.Text : Kit.Secondary);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetRow(label, 1);
            column.Children.Add(label);
            Grid.SetColumn(column, i);
            _chart.Children.Add(column);
        }
    }

    private static string Ago(DateTime when)
    {
        var span = DateTime.Now - when;
        return span.TotalMinutes < 1 ? "vừa xong" : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} phút trước" : $"{(int)span.TotalHours} giờ trước";
    }
}
