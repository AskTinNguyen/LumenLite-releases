using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LumenLite;

/// <summary>
/// The Island's AI page, laid out like Vorssaint's: plan limits for Claude and
/// Codex side by side, what the use is worth and what runs now, then the
/// sessions that are working or waiting for the person. A session opens in the
/// Claude or Codex app with a click.
/// </summary>
public sealed partial class IslandWindow
{
    private static readonly Brush CodexTint = Freeze(new SolidColorBrush(Color.FromRgb(0x7D, 0x99, 0xFF)));
    private static readonly Brush Faintest = Freeze(new SolidColorBrush(Color.FromArgb(0x80, 0x8E, 0x8E, 0x93)));
    private static readonly Brush TickBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)));

    private readonly List<(TextBlock Text, DateTime Since)> _agentClocks = new();
    /// <summary>The spending card covers today, or the last seven days.</summary>
    private bool _spendWeek;

    private static Brush Tint(AgentProvider provider) => provider == AgentProvider.Claude ? Claude : CodexTint;

    private static Brush Soft(Brush tint, byte alpha = 0x26) =>
        Freeze(new SolidColorBrush(Color.FromArgb(alpha, ((SolidColorBrush)tint).Color.R, ((SolidColorBrush)tint).Color.G, ((SolidColorBrush)tint).Color.B)));

    /// <summary>Each agent keeps its own color; orange once a window runs low, red when it is nearly gone.</summary>
    private static Brush LimitTint(AgentProvider provider, double usedPercent) =>
        usedPercent >= 95 ? Red : usedPercent >= 80 ? Orange : Tint(provider);

    // ---------- building blocks ----------

    private static Border Card(UIElement child, Thickness? margin = null) => new()
    {
        Background = TileBrush,
        BorderBrush = TileBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(18),
        Padding = new Thickness(14, 12, 14, 12),
        Margin = margin ?? new Thickness(0, 0, 0, 10),
        Child = child,
    };

    /// <summary>The agent's mark: Claude's spark, Codex's brackets.</summary>
    private static TextBlock Mark(AgentProvider provider, double size)
    {
        var mark = provider == AgentProvider.Claude
            ? new TextBlock { Text = "✳", FontSize = size + 1, FontFamily = new FontFamily("Segoe UI Symbol") }
            : Theme.Glyph("", size);
        mark.Foreground = Tint(provider);
        mark.VerticalAlignment = VerticalAlignment.Center;
        mark.HorizontalAlignment = HorizontalAlignment.Left;
        return mark;
    }

    private static Grid CardHeader(UIElement icon, string title, params UIElement[] accessories)
    {
        var grid = new Grid { Height = 20, Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(icon);
        var label = new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var accessory in accessories)
        {
            if (accessory is FrameworkElement element) element.Margin = new Thickness(6, 0, 0, 0);
            right.Children.Add(accessory);
        }
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return grid;
    }

    private static TextBlock SymbolIcon(string glyph, Brush? color = null)
    {
        var icon = Theme.Glyph(glyph, 13);
        icon.Foreground = color ?? Theme.Muted;
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        return icon;
    }

    private static Border Chip(string text, Brush tint) => new()
    {
        Background = Soft(tint),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(7, 1, 7, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = tint },
    };

    private static Ellipse Pulse(Brush tint) => new() { Width = 7, Height = 7, Fill = tint, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>A thin capsule meter with a tick where use would be if it were spread evenly over the window.</summary>
    private static Grid Bar(double fraction, Brush fill, double? pace = null)
    {
        var track = new Grid { Height = 10 };
        track.Children.Add(new Border { Height = 5, CornerRadius = new CornerRadius(2.5), Background = TrackBrush, VerticalAlignment = VerticalAlignment.Center });
        var bar = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), Background = fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        track.Children.Add(bar);
        var tick = new Border { Width = 1.5, Height = 10, CornerRadius = new CornerRadius(0.75), Background = TickBrush, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
        track.Children.Add(tick);
        var share = Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1);
        track.SizeChanged += (_, _) =>
        {
            bar.Width = share > 0 ? Math.Max(5, track.ActualWidth * share) : 0;
            if (pace is > 0.02 and < 0.98)
            {
                tick.Margin = new Thickness(track.ActualWidth * pace.Value - 0.75, 0, 0, 0);
                tick.Visibility = Visibility.Visible;
            }
        };
        return track;
    }

    /// <summary>"2g 13p", "3n 5g" or "45p": how long until a window renews, never in seconds.</summary>
    private static string Renews(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1)) span = TimeSpan.FromMinutes(1);
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}n {span.Hours}g";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}g {span.Minutes}p";
        return $"{(int)Math.Ceiling(span.TotalMinutes)}p";
    }

    private static string Ago(DateTime when)
    {
        var span = DateTime.Now - when;
        if (span < TimeSpan.FromMinutes(1)) return "vừa xong";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} phút trước";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours} giờ trước";
        return $"{(int)span.TotalDays} ngày trước";
    }

    /// <summary>Two cards on one line, the same height.</summary>
    private static Grid Pair(UIElement left, UIElement? right)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        if (left is Border l) l.Margin = new Thickness(0);
        row.Children.Add(left);
        if (right != null)
        {
            if (right is Border r) r.Margin = new Thickness(0);
            Grid.SetColumn(right, 2);
            row.Children.Add(right);
        }
        else
            Grid.SetColumnSpan(left, 3);
        return row;
    }

    // ---------- the page ----------

    private void DrawAgents()
    {
        _agentsContent.Children.Clear();
        _agentClocks.Clear();
        var providers = AgentMonitor.Providers();
        if (!AgentMonitor.Available || providers.Count == 0)
        {
            _agentsContent.Children.Add(Card(new TextBlock { Text = "Chưa thấy Claude Code hay Codex trên máy này.", FontSize = 13, Foreground = Theme.Muted, TextWrapping = TextWrapping.Wrap }));
            return;
        }
        var sessions = AgentMonitor.Sessions();

        // What runs or waits for the person comes first; the plan's limits and the spending follow.
        _agentsContent.Children.Add(SessionsCard(sessions));
        var limits = providers.Select(p => LimitsCard(p, sessions)).ToList();
        _agentsContent.Children.Add(Pair(limits[0], limits.Count > 1 ? limits[1] : null));
        _agentsContent.Children.Add(Pair(SpendCard(providers), NowCard(providers, sessions)));

        var more = Theme.Pressable(new TextBlock { Text = "Xem đầy đủ trong bảng điều khiển  ›", FontSize = 12.5, Foreground = Theme.Accent }, () => OpenPanel?.Invoke("agents"), new Thickness(8, 4, 8, 8));
        more.HorizontalAlignment = HorizontalAlignment.Center;
        _agentsContent.Children.Add(more);
        UpdateAgentElapsed();
    }

    // ---------- limits ----------

    private Border LimitsCard(AgentProvider provider, List<AgentSession> sessions)
    {
        var stack = new StackPanel();
        var accessories = new List<UIElement>();
        if (AgentMonitor.Plan(provider) is { } plan)
            accessories.Add(Chip(plan, Tint(provider)));
        if (sessions.Any(s => s.Provider == provider && s.Working))
            accessories.Add(Pulse(Tint(provider)));
        stack.Children.Add(CardHeader(Mark(provider, 14), AgentMonitor.Name(provider), accessories.ToArray()));

        var limits = AgentMonitor.LimitsOf(provider);
        var now = DateTime.Now;
        // Two rows fit: the session and whichever longer window binds first.
        var windows = new List<LimitWindow>();
        if (limits != null)
        {
            if (limits.Windows.FirstOrDefault(w => w.Session) is { } session) windows.Add(session);
            if (limits.Windows.Where(w => !w.Session).OrderByDescending(w => w.Used).FirstOrDefault() is { } longer) windows.Add(longer);
        }
        if (windows.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = provider == AgentProvider.Claude ? "Mở app Claude để xem hạn mức của gói." : "Codex chưa báo hạn mức.",
                FontSize = 12, Foreground = Theme.Muted, TextWrapping = TextWrapping.Wrap,
            });
            if (AgentMonitor.LastActivity(provider) is { } last)
                stack.Children.Add(new TextBlock { Text = "Dùng lần cuối " + Ago(last), FontSize = 11.5, Foreground = Faint, Margin = new Thickness(0, 4, 0, 0) });
            return Card(stack);
        }

        // A reading from a while ago is still the latest known, shown quieter.
        var stale = now - limits!.Observed > TimeSpan.FromMinutes(provider == AgentProvider.Claude ? 30 : 60);
        var rows = new StackPanel { Opacity = stale ? 0.6 : 1 };
        foreach (var window in windows)
            rows.Children.Add(LimitRow(provider, window, now));
        stack.Children.Add(rows);
        if (stale)
            stack.Children.Add(new TextBlock { Text = "Cập nhật " + Ago(limits.Observed), FontSize = 11, Foreground = Faint, Margin = new Thickness(0, 2, 0, 0) });
        return Card(stack);
    }

    /// <summary>What is left of a window and when it renews; never a forecast, which would read as a promise.</summary>
    private static Grid LimitRow(AgentProvider provider, LimitWindow window, DateTime now)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = window.Session ? "Phiên" : window.Minutes is >= 8640 and <= 11520 ? "Tuần" : Renews(TimeSpan.FromMinutes(window.Minutes));
        row.Children.Add(new TextBlock { Text = label, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, VerticalAlignment = VerticalAlignment.Center });

        double? pace = null;
        if (window.ResetsAt is { } resets && resets > now)
        {
            var countdown = new TextBlock
            {
                Text = "⟳ " + (window.Estimated ? "≈ " : "") + Renews(resets - now),
                FontSize = 11.5,
                Foreground = Faint,
                Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(countdown, 1);
            row.Children.Add(countdown);
            if (window.Minutes > 0)
                pace = (resets - now).TotalMinutes / window.Minutes;
        }

        var left = 100 - window.Used;
        var tint = LimitTint(provider, window.Used);
        var share = new TextBlock
        {
            Text = $"{left:0}%",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = tint == Tint(provider) ? Theme.Text : tint,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(share, 2);
        row.Children.Add(share);

        var bar = Bar(left / 100, tint, pace);
        bar.Margin = new Thickness(0, 3, 0, 0);
        Grid.SetRow(bar, 1);
        Grid.SetColumnSpan(bar, 3);
        row.Children.Add(bar);

        var help = $"{label} · đã dùng {window.Used:0}% · còn {left:0}%";
        if (window.ResetsAt is { } at)
            help += $" · làm mới {at:HH:mm dd/MM}" + (window.Estimated ? " (ước tính)" : "");
        row.ToolTip = help;
        return row;
    }

    // ---------- spending ----------

    private Border SpendCard(List<AgentProvider> providers)
    {
        var from = _spendWeek ? DateTime.Today.AddDays(-6) : DateTime.Today;
        var spend = AgentMonitor.SpendBetween(from, DateTime.Today);
        var cost = spend.Sum(s => s.Cost);
        var priced = spend.All(s => s.Priced);
        var tokens = spend.Sum(s => s.Total);
        var read = spend.Sum(s => s.CacheRead);

        var period = new StackPanel { Orientation = Orientation.Horizontal };
        period.Children.Add(new TextBlock { Text = _spendWeek ? "7 ngày" : "Hôm nay", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Faint, VerticalAlignment = VerticalAlignment.Center });
        var chevron = Theme.Glyph("", 8);
        chevron.Foreground = Faint;
        chevron.Margin = new Thickness(4, 1, 0, 0);
        period.Children.Add(chevron);
        var periodButton = Theme.Pressable(period, () =>
        {
            _spendWeek = !_spendWeek;
            DrawAgents();
        }, new Thickness(6, 1, 4, 2));
        periodButton.ToolTip = "Đổi khoảng thời gian";

        var stack = new StackPanel();
        stack.Children.Add(CardHeader(SymbolIcon(""), "Chi tiêu", periodButton));

        var figure = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -2, 0, 4) };
        figure.Children.Add(new TextBlock { Text = (priced ? "" : "≥ ") + AgentPricing.Format(cost), FontSize = 26, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Bottom });
        figure.Children.Add(new TextBlock { Text = "giá trị API", FontSize = 11.5, Foreground = Faint, Margin = new Thickness(6, 0, 0, 5), VerticalAlignment = VerticalAlignment.Bottom });
        figure.ToolTip = priced
            ? "Số tiền nếu trả theo giá API. Gói thuê bao tính khác."
            : "Có model chưa biết giá nên chưa tính vào; con số thật cao hơn.";
        stack.Children.Add(figure);

        stack.Children.Add(SplitBar(providers.Select(p => spend.FirstOrDefault(s => s.Provider == p)).ToList(), priced));

        var footer = AgentMonitor.Tokens(tokens) + " token";
        var cacheable = spend.Sum(s => s.Input + s.CacheRead + s.CacheWrite);
        if (cacheable > 0 && read > 0)
            footer += $" · {100.0 * read / cacheable:0}% từ cache";
        stack.Children.Add(new TextBlock { Text = footer, FontSize = 11.5, Foreground = Faint, Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        return Card(stack);
    }

    /// <summary>One capsule split by agent; each keeps a visible sliver.</summary>
    private static Grid SplitBar(List<AgentSpend?> parts, bool byCost)
    {
        var grid = new Grid { Height = 5 };
        var weights = parts.Select(p => p == null ? 0 : byCost ? p.Cost : p.Total).ToList();
        var total = weights.Sum();
        if (total <= 0)
        {
            grid.Children.Add(new Border { CornerRadius = new CornerRadius(2.5), Background = TrackBrush });
            return grid;
        }
        for (var i = 0; i < parts.Count; i++)
        {
            if (weights[i] <= 0) continue;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.03 * total + weights[i], GridUnitType.Star) });
            var part = new Border { CornerRadius = new CornerRadius(2.5), Background = Tint(parts[i]!.Provider), Margin = new Thickness(grid.ColumnDefinitions.Count > 1 ? 2 : 0, 0, 0, 0) };
            part.ToolTip = $"{AgentMonitor.Name(parts[i]!.Provider)} · {AgentPricing.Format(parts[i]!.Cost)} · {AgentMonitor.Tokens(parts[i]!.Total)} token";
            Grid.SetColumn(part, grid.ColumnDefinitions.Count - 1);
            grid.Children.Add(part);
        }
        return grid;
    }

    // ---------- now ----------

    private static Border NowCard(List<AgentProvider> providers, List<AgentSession> sessions)
    {
        var stack = new StackPanel();
        var busy = providers.FirstOrDefault(p => sessions.Any(s => s.Provider == p && s.Working));
        var anyBusy = sessions.Any(s => s.Working);
        stack.Children.Add(CardHeader(SymbolIcon("", anyBusy ? Tint(busy) : null), "Bây giờ", anyBusy ? new UIElement[] { Pulse(Tint(busy)) } : Array.Empty<UIElement>()));
        foreach (var provider in providers)
        {
            var line = new Grid { Margin = new Thickness(0, 1, 0, 5) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.Children.Add(Mark(provider, 12));
            var name = new TextBlock { Text = AgentMonitor.Name(provider), FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(name, 1);
            line.Children.Add(name);

            var working = sessions.Count(s => s.Provider == provider && s.Working);
            var waiting = sessions.Count(s => s.Provider == provider && s.NeedsInput);
            var state = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (working > 0)
                state.Children.Add(new TextBlock { Text = $"{working} đang chạy", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Tint(provider) });
            if (waiting > 0)
                state.Children.Add(new TextBlock { Text = (working > 0 ? " · " : "") + $"{waiting} chờ bạn", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Orange });
            if (working == 0 && waiting == 0)
                state.Children.Add(new TextBlock { Text = AgentMonitor.LastActivity(provider) is { } last ? Ago(last) : "Rảnh", FontSize = 11.5, Foreground = Faint });
            Grid.SetColumn(state, 2);
            line.Children.Add(state);
            stack.Children.Add(line);
        }
        return Card(stack);
    }

    // ---------- sessions ----------

    private Border SessionsCard(List<AgentSession> sessions)
    {
        var shown = sessions.Where(s => s.Working || s.NeedsInput)
            .OrderByDescending(s => s.NeedsInput).ThenByDescending(s => s.LastActive).Take(6).ToList();
        var working = shown.Count(s => s.Working);
        var waiting = shown.Count(s => s.NeedsInput);
        var accessories = new List<UIElement>();
        if (waiting > 0) accessories.Add(Chip($"{waiting} chờ bạn", Orange));
        if (working > 0) accessories.Add(Chip($"{working} đang chạy", Theme.Accent));

        var stack = new StackPanel();
        stack.Children.Add(CardHeader(SymbolIcon(""), "Phiên", accessories.ToArray()));
        if (shown.Count == 0)
        {
            stack.Children.Add(new TextBlock { Text = "Không có phiên nào đang chạy hay chờ bạn.", FontSize = 12.5, Foreground = Faint });
            return Card(stack);
        }
        foreach (var session in shown)
            stack.Children.Add(SessionRow(session));
        return Card(stack);
    }

    /// <summary>A session as a row: click to open it in the agent's own app.</summary>
    private Border SessionRow(AgentSession session)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mark = Mark(session.Provider, 12);
        mark.VerticalAlignment = VerticalAlignment.Top;
        mark.Margin = new Thickness(0, 2, 0, 0);
        grid.Children.Add(mark);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = session.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var detail = string.Join(" · ", new[] { session.Machine.Length > 0 ? "Máy " + session.Machine : "", session.Project, session.Activity }.Where(x => x.Length > 0));
        text.Children.Add(new TextBlock { Text = detail.Length > 0 ? detail : AgentMonitor.Name(session.Provider), FontSize = 11.5, Foreground = Faint, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        FrameworkElement state;
        if (session.NeedsInput)
            state = Chip("Cần bạn", Orange);
        else
        {
            var clock = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Tint(session.Provider) };
            _agentClocks.Add((clock, session.WorkingSince));
            state = clock;
        }
        state.VerticalAlignment = VerticalAlignment.Center;
        state.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(state, 2);
        grid.Children.Add(state);

        var app = AgentMonitor.Name(session.Provider);
        var row = new Border
        {
            Child = grid,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(-8, 0, -8, 2),
            CornerRadius = new CornerRadius(10),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = AgentOpen.Describe(session),
        };
        var pressed = false;
        row.MouseEnter += (_, _) => row.Background = TileHover;
        row.MouseLeave += (_, _) => { row.Background = Brushes.Transparent; pressed = false; };
        row.MouseLeftButtonDown += (_, e) => { pressed = true; e.Handled = true; };
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (!pressed) return;
            pressed = false;
            e.Handled = true;
            if (!AgentOpen.Open(session))
            {
                Notify("", $"Không mở được app {app}", 4);
                return;
            }
            // Collapse so the app is not covered.
            SetMode(Wanted());
        };
        return row;
    }

    /// <summary>How long each running session has been at its current turn, like "4:12".</summary>
    private void UpdateAgentElapsed()
    {
        foreach (var (text, since) in _agentClocks)
        {
            if (since == default)
            {
                text.Text = "";
                continue;
            }
            var span = DateTime.Now - since;
            text.Text = span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
        }
    }
}
