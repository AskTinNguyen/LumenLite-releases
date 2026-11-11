using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace LumenLite;

/// <summary>
/// Lumen's glass, bottom to top: blurred wallpaper, dark veil, light, rim.
/// The optional child sits inside the rim with a label shadow.
/// </summary>
public sealed class GlassPanel : Grid
{
    private readonly Border _frost;
    private readonly Border[] _shapes;

    public GlassPanel(FrameworkElement? child, Thickness padding)
    {
        _frost = new Border { Background = Theme.FrostFallback };
        var rim = new Border { BorderBrush = Theme.Rim, BorderThickness = new Thickness(1), Padding = padding };
        if (child != null)
        {
            // Lumen's label shadow keeps text readable over bright wallpapers.
            child.Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Direction = 270, Opacity = 0.55 };
            rim.Child = child;
        }
        _shapes = new[]
        {
            _frost,
            new Border { Background = Theme.Veil },
            new Border { Background = Theme.Glow },
            new Border { Background = Theme.Sheen },
            rim,
        };
        foreach (var shape in _shapes)
            Children.Add(shape);
        SetRadius(Theme.Radius);
    }

    public void SetRadius(double radius)
    {
        var corners = new CornerRadius(radius);
        foreach (var shape in _shapes)
            shape.CornerRadius = corners;
    }

    /// <summary>Shows the part of the blurred wallpaper that lies behind the window.</summary>
    public void UpdateFrost(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out var rect))
            return;
        var frost = Wallpaper.For(hwnd, VisualTreeHelper.GetDpi(window).DpiScaleX);
        if (frost == null)
        {
            _frost.Background = Theme.FrostFallback;
            return;
        }

        double scale = frost.Scale;
        var slice = new Rect(
            (rect.Left - frost.Monitor.Left) / scale,
            (rect.Top - frost.Monitor.Top) / scale,
            (rect.Right - rect.Left) / scale,
            (rect.Bottom - rect.Top) / scale);
        if (_frost.Background is ImageBrush brush && brush.ImageSource == frost.Image)
            brush.Viewbox = slice;
        else
            _frost.Background = new ImageBrush(frost.Image) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = slice, Stretch = Stretch.Fill };
    }
}
