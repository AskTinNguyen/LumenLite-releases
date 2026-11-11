using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LumenLite;

internal static class Theme
{
    public static readonly FontFamily Font = new("Segoe UI Variable Display, Segoe UI");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    // Values follow Lumen's "obsidian" plate.
    public static readonly Brush Text = Freeze(new SolidColorBrush(Color.FromArgb(0xF2, 0xFA, 0xF9, 0xFF)));
    public static readonly Brush Muted = Freeze(new SolidColorBrush(Color.FromArgb(0x99, 0xF0, 0xEE, 0xFC)));
    public static readonly Brush Hover = Freeze(new SolidColorBrush(Color.FromArgb(0x13, 0xFF, 0xFF, 0xFF)));
    public static readonly Brush Press = Freeze(new SolidColorBrush(Color.FromArgb(0x21, 0xFF, 0xFF, 0xFF)));

    /// <summary>Selected device and switched-on toggles; matches the tray icon.</summary>
    public static readonly Brush Accent = Freeze(new SolidColorBrush(Color.FromRgb(0x8B, 0x7C, 0xF6)));

    /// <summary>Dashed outline and resize strokes shown in edit mode.</summary>
    public static readonly Brush EditOutline = Freeze(new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)));

    /// <summary>Corner radius of every widget, in device-independent pixels.</summary>
    public const double Radius = 30;

    /// <summary>Shown when there is no wallpaper image to frost (solid colour backgrounds).</summary>
    public static readonly Brush FrostFallback = Freeze(Linear(150,
        (Color.FromRgb(0x19, 0x13, 0x29), 0), (Color.FromRgb(0x0A, 0x08, 0x12), 0.65), (Color.FromRgb(0x12, 0x0C, 0x22), 1)));

    /// <summary>Dark veil over the frost (Lumen's veil 48%).</summary>
    public static readonly Brush Veil = Freeze(Linear(162,
        (Color.FromArgb(0x80, 20, 17, 32), 0), (Color.FromArgb(0x7A, 10, 9, 16), 0.52), (Color.FromArgb(0x85, 7, 6, 12), 1)));

    /// <summary>Soft light falling on the top-left of the glass.</summary>
    public static readonly Brush Glow = Freeze(new RadialGradientBrush(
        new GradientStopCollection { new(Color.FromArgb(0x42, 0xFF, 0xFF, 0xFF), 0), new(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.55) })
    {
        Center = new Point(0.16, -0.18),
        GradientOrigin = new Point(0.16, -0.18),
        RadiusX = 1.2,
        RadiusY = 0.7,
    });

    /// <summary>Faint highlight along the top and bottom edges.</summary>
    public static readonly Brush Sheen = Freeze(Linear(180,
        (Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF), 0), (Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.24),
        (Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.76), (Color.FromArgb(0x09, 0xFF, 0xFF, 0xFF), 1)));

    /// <summary>1px rim, bright at the top-left and bottom-right corners.</summary>
    public static readonly Brush Rim = Freeze(Linear(155,
        (Color.FromArgb(0x9E, 0xFF, 0xFF, 0xFF), 0), (Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF), 0.30),
        (Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF), 0.64), (Color.FromArgb(0x57, 0xFF, 0xFF, 0xFF), 1)));

    public static TextBlock Header(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = Muted,
        Margin = new Thickness(0, 0, 0, 8),
    };

    public static TextBlock Glyph(string glyph, double size) => new()
    {
        Text = glyph,
        FontFamily = Icons,
        FontSize = size,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// A Border that behaves like a flat button. It swallows the mouse-down so
    /// the click does not start dragging the widget.
    /// </summary>
    public static Border Pressable(UIElement child, Action onClick, Thickness padding)
    {
        var border = new Border
        {
            Child = child,
            Padding = padding,
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        var pressed = false;
        border.MouseEnter += (_, _) => { if (border.IsEnabled) border.Background = Hover; };
        border.MouseLeave += (_, _) => { border.Background = Brushes.Transparent; pressed = false; };
        border.MouseLeftButtonDown += (_, e) => { pressed = true; border.Background = Press; e.Handled = true; };
        border.MouseLeftButtonUp += (_, e) =>
        {
            if (!pressed) return;
            pressed = false;
            border.Background = Hover;
            e.Handled = true;
            onClick();
        };
        border.IsEnabledChanged += (_, _) => border.Opacity = border.IsEnabled ? 1 : 0.35;
        return border;
    }

    /// <summary>A linear gradient using CSS angle semantics (0deg points up, 90deg points right).</summary>
    private static LinearGradientBrush Linear(double cssDegrees, params (Color Color, double Offset)[] stops)
    {
        var angle = cssDegrees * Math.PI / 180;
        double dx = Math.Sin(angle) / 2, dy = -Math.Cos(angle) / 2;
        var brush = new LinearGradientBrush { StartPoint = new Point(0.5 - dx, 0.5 - dy), EndPoint = new Point(0.5 + dx, 0.5 + dy) };
        foreach (var (color, offset) in stops)
            brush.GradientStops.Add(new GradientStop(color, offset));
        return brush;
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
