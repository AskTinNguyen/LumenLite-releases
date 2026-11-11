using System.Windows;
using System.Windows.Controls;

namespace LumenLite;

/// <summary>
/// A widget's surface: the display's flat black with a hairline rim, the content inside.
/// (The name is from when widgets were frosted glass over a blurred wallpaper.)
/// </summary>
public sealed class GlassPanel : Grid
{
    private readonly Border _surface;

    public GlassPanel(FrameworkElement? child, Thickness padding)
    {
        _surface = new Border
        {
            Background = Theme.Black,
            BorderBrush = Theme.Hairline,
            BorderThickness = new Thickness(1),
            Padding = padding,
            Child = child,
        };
        Children.Add(_surface);
        SetRadius(Theme.Radius);
    }

    public void SetRadius(double radius) => _surface.CornerRadius = new CornerRadius(radius);
}
