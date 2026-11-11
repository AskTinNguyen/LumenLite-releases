using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LumenLite.Panel;

/// <summary>A fader: a thin track, an orange fill up to the level and a small upright knob.</summary>
internal sealed class PanelSlider : Grid
{
    private const double TrackHeight = 2;

    private readonly Border _fill = new() { Height = TrackHeight, Background = Kit.Accent, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _knob = new()
    {
        Width = 8,
        Height = 16,
        CornerRadius = new CornerRadius(2),
        Background = Kit.Text,
        HorizontalAlignment = HorizontalAlignment.Left,
    };
    private readonly Action<float> _changed;
    private float _value;

    public PanelSlider(float value, Action<float> changed, Brush? fill = null)
    {
        _value = value;
        _changed = changed;
        Height = 22;
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        if (fill != null)
            _fill.Background = fill;
        Children.Add(new Border { Height = TrackHeight, Background = Kit.Track });
        Children.Add(_fill);
        Children.Add(_knob);
        SizeChanged += (_, _) => Draw();
        MouseLeftButtonDown += (_, e) =>
        {
            CaptureMouse();
            FromMouse(e);
            e.Handled = true;
        };
        MouseMove += (_, e) =>
        {
            if (IsMouseCaptured)
                FromMouse(e);
        };
        MouseLeftButtonUp += (_, e) =>
        {
            ReleaseMouseCapture();
            e.Handled = true;
        };
        MouseWheel += (_, e) =>
        {
            Set(_value + Math.Sign(e.Delta) * 0.02f, notify: true);
            e.Handled = true;
        };
    }

    public bool IsDragging => IsMouseCaptured;

    public float Value
    {
        get => _value;
        set
        {
            if (!IsDragging)
                Set(value, notify: false);
        }
    }

    private void FromMouse(MouseEventArgs e)
    {
        var usable = Math.Max(1, ActualWidth - _knob.Width);
        Set((float)((e.GetPosition(this).X - _knob.Width / 2) / usable), notify: true);
    }

    private void Set(float value, bool notify)
    {
        _value = Math.Clamp(value, 0f, 1f);
        Draw();
        if (notify)
        {
            try
            {
                _changed(_value);
            }
            catch (Exception ex)
            {
                Store.Log("slider failed: " + ex.Message);
            }
        }
    }

    private void Draw()
    {
        var usable = Math.Max(0, ActualWidth - _knob.Width);
        _knob.Margin = new Thickness(usable * _value, 0, 0, 0);
        _fill.Width = usable * _value + _knob.Width / 2;
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
        Margin = new Thickness(0, 6, 0, 4);
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

/// <summary>A readout tile: a small mono label over a big mono figure.</summary>
internal sealed class StatTile : Border
{
    private readonly TextBlock _value = new() { FontFamily = Theme.Mono, FontSize = 19, Foreground = Kit.Text, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 0) };

    public StatTile(string glyph, string label)
    {
        Background = Kit.Inset;
        CornerRadius = new CornerRadius(Kit.ControlRadius);
        Padding = new Thickness(10, 8, 8, 9);
        Margin = new Thickness(3);
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Kit.Glyph(glyph, 10.5, Kit.Secondary);
        icon.Margin = new Thickness(0, 0, 6, 0);
        top.Children.Add(icon);
        top.Children.Add(Kit.Mono(label.ToUpperInvariant(), 10, false, Kit.Secondary));
        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(_value);
        Child = stack;
    }

    public string Value
    {
        set => _value.Text = value;
    }
}

/// <summary>A usage line: label, bar and percentage, with a sparkline under it.</summary>
internal sealed class UsageRow : StackPanel
{
    private readonly Grid _bar;
    private readonly TextBlock _value = Kit.Mono("", 12.5);
    private readonly Sparkline? _graph;

    public UsageRow(string label, Brush fill, Color? graph, bool fixedMax = true)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
        grid.Children.Add(Kit.Mono(label.ToUpperInvariant(), 11, false, Kit.Secondary));
        _bar = Kit.Bar(0, fill);
        Grid.SetColumn(_bar, 1);
        grid.Children.Add(_bar);
        _value.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_value, 2);
        grid.Children.Add(_value);
        Children.Add(grid);
        if (graph is { } color)
        {
            _graph = new Sparkline(color, fixedMax);
            Children.Add(_graph);
        }
    }

    public void Update(double fraction, string text, IReadOnlyCollection<double>? history = null)
    {
        Kit.SetBar(_bar, fraction);
        _value.Text = text;
        if (history != null)
            _graph?.SetValues(history);
    }
}
