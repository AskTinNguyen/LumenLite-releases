using System.Windows;
using System.Windows.Controls;

namespace LumenLite;

/// <summary>
/// A widget's surface, from <see cref="Material.Widget"/>: a see-through dark fill with a rim lit from above,
/// the content inside. No blur: a widget is never the active window, and Windows only blurs behind those.
/// </summary>
public sealed class GlassPanel : Grid
{
    private readonly Border _surface;

    public GlassPanel(FrameworkElement? child, Thickness padding)
    {
        _surface = new Border
        {
            Background = Material.Widget.Fill,
            BorderBrush = Material.Widget.Rim,
            BorderThickness = new Thickness(1),
            Padding = padding,
            Child = child,
        };
        Children.Add(_surface);
        SetRadius(Material.Widget.Radius);
    }

    public void SetRadius(double radius) => _surface.CornerRadius = new CornerRadius(radius);
}
