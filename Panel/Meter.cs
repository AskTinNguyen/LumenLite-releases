using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace LumenLite.Panel;

/// <summary>
/// One measured thing: its name, a reading at the right, a thin bar for how full it is and, when asked, a line
/// chart of the last minute. CPU, memory, a drive and network traffic are all this.
/// </summary>
internal sealed class Meter : StackPanel
{
    private readonly Material _m = Ui.M;
    private readonly TextBlock _readout = Ui.Mono("", 12.5);
    private readonly Border _badge;
    private readonly TextBlock _badgeText = Ui.Text("", 11.5, true);
    private readonly System.Windows.Shapes.Ellipse _badgeDot = new() { Width = 6, Height = 6, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid? _bar;
    private readonly Border? _fill;
    private readonly Sparkline? _graph;
    private double _fraction;

    /// <param name="bar">Show the bar (a fraction of a whole); off for a rate such as download speed.</param>
    /// <param name="graph">Show the last minute as a line under it.</param>
    /// <param name="fixedMax">The graph's top is 100; otherwise it scales to the largest value.</param>
    public Meter(string label, bool bar = true, bool graph = false, bool fixedMax = true)
    {
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.Children.Add(Ui.Text(label));
        var badge = new StackPanel { Orientation = Orientation.Horizontal };
        badge.Children.Add(_badgeDot);
        badge.Children.Add(_badgeText);
        _badge = new Border { CornerRadius = new CornerRadius(_m.KeyRadius), Padding = new Thickness(7, 1, 8, 2), Margin = new Thickness(10, 0, 0, 0), Child = badge, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_badge, 1);
        head.Children.Add(_badge);
        _readout.HorizontalAlignment = HorizontalAlignment.Right;
        _readout.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(_readout, 2);
        head.Children.Add(_readout);
        Children.Add(head);

        if (bar)
        {
            _fill = new Border { CornerRadius = new CornerRadius(1), Background = _m.Text, HorizontalAlignment = HorizontalAlignment.Left };
            _bar = new Grid { Height = 3, Margin = new Thickness(0, 7, 0, 1) };
            _bar.Children.Add(new Border { CornerRadius = new CornerRadius(1), Background = _m.Track });
            _bar.Children.Add(_fill);
            _bar.SizeChanged += (_, _) => DrawBar();
            Children.Add(_bar);
        }
        if (graph)
        {
            _graph = new Sparkline(_m.TextColor, fixedMax);
            Children.Add(_graph);
        }
    }

    /// <summary>A new reading. <paramref name="fill"/> colours the bar when the value means something (nearly full).</summary>
    public void Update(double fraction, string readout, IReadOnlyCollection<double>? history = null, Brush? fill = null)
    {
        _fraction = fraction;
        _readout.Text = readout;
        if (_fill != null)
        {
            _fill.Background = fill ?? _m.Text;
            DrawBar();
        }
        if (history != null)
            _graph?.SetValues(history);
    }

    /// <summary>A small coloured tag after the name: "Bình thường", "Cao".</summary>
    public void Badge(string text, Brush color, Brush soft)
    {
        _badgeText.Text = text;
        _badgeText.Foreground = color;
        _badgeDot.Fill = color;
        _badge.Background = soft;
        _badge.Visibility = Visibility.Visible;
    }

    private void DrawBar()
    {
        if (_bar != null && _fill != null)
            _fill.Width = Math.Max(2, _bar.ActualWidth * Math.Clamp(_fraction, 0, 1));
    }
}

/// <summary>A thin line chart of the last minute: one line over a faint flat fill.</summary>
internal sealed class Sparkline : FrameworkElement
{
    private readonly Pen _line;
    private readonly Brush _area;
    private readonly bool _fixedMax;
    private IReadOnlyCollection<double> _values = Array.Empty<double>();

    public Sparkline(Color color, bool fixedMax = true, double height = 30)
    {
        _fixedMax = fixedMax;
        Height = height;
        var stroke = new SolidColorBrush(color);
        stroke.Freeze();
        _line = new Pen(stroke, 1.2) { LineJoin = PenLineJoin.Round };
        _line.Freeze();
        var area = new SolidColorBrush(Color.FromArgb(0x16, color.R, color.G, color.B));
        area.Freeze();
        _area = area;
        Margin = new Thickness(0, 6, 0, 0);
    }

    public void SetValues(IReadOnlyCollection<double> values)
    {
        _values = values.ToArray();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (_values.Count < 2 || w <= 0)
            return;
        var max = _fixedMax ? 100 : Math.Max(65536, _values.Max());
        var step = w / (SystemMonitor.HistoryLength - 1);
        var start = w - step * (_values.Count - 1);
        var points = _values.Select((v, i) => new Point(start + i * step, h - 2 - Math.Clamp(v / max, 0, 1) * (h - 4))).ToList();
        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(points[0].X, h), true, true);
            ctx.PolyLineTo(points, false, true);
            ctx.LineTo(new Point(points[^1].X, h), false, false);
        }
        area.Freeze();
        dc.DrawGeometry(_area, null, area);
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            ctx.PolyLineTo(points.Skip(1).ToList(), true, true);
        }
        line.Freeze();
        dc.DrawGeometry(null, _line, line);
    }
}

/// <summary>A big figure under a small mono label: tokens today, ping.</summary>
internal sealed class Readout : Border
{
    private readonly TextBlock _value;

    public Readout(string glyph, string label)
    {
        var m = Ui.M;
        Background = m.Inset;
        CornerRadius = new CornerRadius(m.ControlRadius);
        Padding = new Thickness(10, 8, 8, 9);
        Margin = new Thickness(3);
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Ui.Glyph(glyph, 10.5, m.Secondary);
        icon.Margin = new Thickness(0, 0, 6, 0);
        top.Children.Add(icon);
        top.Children.Add(Ui.Mono(label.ToUpperInvariant(), 10, m.Secondary));
        _value = new TextBlock { FontFamily = Theme.Mono, FontSize = 19, Foreground = m.Text, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 0) };
        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(_value);
        Child = stack;
    }

    public string Value
    {
        set => _value.Text = value;
    }

    /// <summary>Several readouts side by side, edge to edge with the rows around them.</summary>
    public static UniformGrid Strip(params Readout[] readouts)
    {
        var grid = new UniformGrid { Columns = readouts.Length, Margin = new Thickness(-3) };
        foreach (var readout in readouts)
            grid.Children.Add(readout);
        return grid;
    }
}
