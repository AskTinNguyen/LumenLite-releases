using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LumenLite;

/// <summary>What a surface is made of.</summary>
internal enum Surface
{
    /// <summary>A sheet over Windows' own blurred backdrop: the panel, Command Bar and Scratchpad on Windows 11.</summary>
    Glass,
    /// <summary>The same sheet, opaque: where the backdrop is not available.</summary>
    Panel,
    /// <summary>The always-black display: island, radial menu, widgets.</summary>
    Display,
}

/// <summary>
/// The one place that knows how a surface looks: its fill, rim, radii and the brushes for what sits on it.
/// The island and the panel both draw from here, so they cannot drift apart; <see cref="Palette"/> and
/// <see cref="Theme"/> only store the raw colours underneath.
/// </summary>
internal sealed class Material
{
    public required Surface Kind { get; init; }
    public required bool IsDark { get; init; }

    /// <summary>The outer sheet.</summary>
    public required Brush Fill { get; init; }
    public required Brush Rim { get; init; }
    public required double Radius { get; init; }

    /// <summary>A group of rows, a tile or a card on the sheet. On glass the rim is lit from above.</summary>
    public required Brush Group { get; init; }
    public required Brush GroupHover { get; init; }
    public required Brush GroupRim { get; init; }
    public required double GroupRadius { get; init; }

    /// <summary>Always opaque: drop-down lists, the confirmation card, tooltips.</summary>
    public required Brush Menu { get; init; }
    public required Brush MenuRim { get; init; }

    /// <summary>A well: key caps, search boxes, unselected segments.</summary>
    public required Brush Inset { get; init; }
    public required Brush Divider { get; init; }
    public required Brush Track { get; init; }
    public required Brush Knob { get; init; }

    public required Brush Text { get; init; }
    public required Brush Secondary { get; init; }
    public required Brush Faint { get; init; }
    public required Brush Hover { get; init; }
    public required Brush Press { get; init; }

    /// <summary>On, selected, live: the only colour that is not a status.</summary>
    public required Brush Accent { get; init; }
    public required Brush AccentSoft { get; init; }
    public required Brush OnAccent { get; init; }

    /// <summary>The fill of a primary button, and the text on it.</summary>
    public required Brush Ink { get; init; }
    public required Brush OnInk { get; init; }

    public required Brush Green { get; init; }
    public required Brush GreenSoft { get; init; }
    public required Brush Amber { get; init; }
    public required Brush AmberSoft { get; init; }
    public required Brush Red { get; init; }
    public required Brush RedSoft { get; init; }
    public Brush OnRed { get; } = Palette.B(0xFF, 0xFF, 0xFF, 0xFF);

    /// <summary>Dims the sheet behind a confirmation.</summary>
    public required Brush Scrim { get; init; }

    /// <summary>Buttons and chips; key caps and tags.</summary>
    public double ControlRadius => 6;
    public double KeyRadius => 4;

    public Color TextColor => ((SolidColorBrush)Text).Color;

    // ---------- the surfaces ----------

    private static readonly Material GlassLight = Sheet(Palette.Light, true), GlassDark = Sheet(Palette.Dark, true);
    private static readonly Material PanelLight = Sheet(Palette.Light, false), PanelDark = Sheet(Palette.Dark, false);

    public static Material Glass => Palette.Current.IsDark ? GlassDark : GlassLight;
    public static Material Panel => Palette.Current.IsDark ? PanelDark : PanelLight;

    /// <summary>What the panel, Command Bar and Scratchpad are drawn in: glass where Windows can blur behind them.</summary>
    public static Material Current => Backdrop.Available ? Glass : Panel;

    /// <summary>The island, the radial menu, Cleaning Mode: solid black.</summary>
    public static readonly Material Display = OnDisplay(Theme.Black, Theme.Hairline);

    /// <summary>
    /// Desktop widgets: the display seen through glass. A widget is never the active window, and Windows only
    /// blurs behind active ones, so this is the treatment without the blur: a see-through dark fill, lit from above.
    /// </summary>
    public static readonly Material Widget = OnDisplay(Palette.B(0xC4, 0x0A, 0x0A, 0x0A), Lit(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)));

    private static Material Sheet(Palette p, bool glass) => new()
    {
        Kind = glass ? Surface.Glass : Surface.Panel,
        IsDark = p.IsDark,
        Fill = glass ? p.GlassTint : p.Sheet,
        Rim = glass ? Brushes.Transparent : p.SheetBorder,
        Radius = glass ? 0 : 12,
        Group = glass ? p.GlassGroup : p.Card,
        GroupHover = glass ? p.GlassGroupHover : p.CardHover,
        GroupRim = glass ? Lit(p.GlassHighlight, p.GlassEdge) : p.CardBorder,
        GroupRadius = 10,
        Menu = p.Card,
        MenuRim = p.SheetBorder,
        Inset = glass ? p.GlassInset : p.Inset,
        Divider = glass ? p.GlassDivider : p.Divider,
        Track = glass ? p.GlassTrack : p.Track,
        Knob = p.Knob,
        Text = p.Text,
        Secondary = glass ? p.GlassSecondary : p.Secondary,
        Faint = p.Track,
        Hover = p.HoverFill,
        Press = p.PressFill,
        Accent = Theme.Accent,
        AccentSoft = p.AccentSoft,
        OnAccent = Theme.OnAccent,
        Ink = p.Selected,
        OnInk = p.OnSelected,
        Green = p.IsDark ? Palette.B(0xFF, 0x4C, 0xC9, 0x74) : Palette.B(0xFF, 0x1F, 0x8F, 0x46),
        GreenSoft = p.IsDark ? Palette.B(0xFF, 0x1B, 0x33, 0x23) : Palette.B(0xFF, 0xDD, 0xEE, 0xDF),
        Amber = p.IsDark ? Theme.Warn : Palette.B(0xFF, 0xA8, 0x7A, 0x00),
        AmberSoft = p.IsDark ? Palette.B(0xFF, 0x3A, 0x30, 0x14) : Palette.B(0xFF, 0xF3, 0xE8, 0xC6),
        Red = p.IsDark ? Theme.Bad : Palette.B(0xFF, 0xD1, 0x36, 0x2A),
        RedSoft = p.IsDark ? Palette.B(0xFF, 0x40, 0x1D, 0x1A) : Palette.B(0xFF, 0xF7, 0xDD, 0xD9),
        Scrim = p.IsDark ? Palette.B(0xB8, 0x08, 0x08, 0x08) : Palette.B(0x70, 0x17, 0x17, 0x16),
    };

    private static Material OnDisplay(Brush fill, Brush rim) => new()
    {
        Kind = Surface.Display,
        IsDark = true,
        Fill = fill,
        Rim = rim,
        Radius = Theme.Radius,
        Group = Theme.Surface,
        GroupHover = Theme.SurfaceHover,
        GroupRim = Theme.Hairline,
        GroupRadius = Theme.TileRadius,
        Menu = Theme.Surface,
        MenuRim = Theme.Hairline,
        Inset = Theme.Surface,
        Divider = Theme.Hairline,
        Track = Theme.Track,
        Knob = Theme.Text,
        Text = Theme.Text,
        Secondary = Theme.Muted,
        Faint = Theme.Faint,
        Hover = Theme.Hover,
        Press = Theme.Press,
        Accent = Theme.Accent,
        AccentSoft = Theme.AccentSoft,
        OnAccent = Theme.OnAccent,
        Ink = Theme.Text,
        OnInk = Theme.Black,
        Green = Palette.B(0xFF, 0x4C, 0xC9, 0x74),
        GreenSoft = Palette.B(0xFF, 0x1B, 0x33, 0x23),
        Amber = Theme.Warn,
        AmberSoft = Palette.B(0xFF, 0x3A, 0x30, 0x14),
        Red = Theme.Bad,
        RedSoft = Palette.B(0xFF, 0x40, 0x1D, 0x1A),
        Scrim = Palette.B(0xB8, 0x08, 0x08, 0x08),
    };

    /// <summary>A rim that is bright along the top edge and fades down the sides, like light on the edge of glass.</summary>
    private static Brush Lit(Color top, Color rest)
    {
        var brush = new LinearGradientBrush(top, rest, new Point(0, 0), new Point(0, 14)) { MappingMode = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }

    private static Brush Lit(Brush top, Brush rest) => Lit(((SolidColorBrush)top).Color, ((SolidColorBrush)rest).Color);

    // ---------- what is built from them ----------

    /// <summary>The outer sheet of a window, around its content.</summary>
    public Border Frame(UIElement content) => new()
    {
        Background = Fill,
        BorderBrush = Rim,
        BorderThickness = new Thickness(Kind == Surface.Glass ? 0 : 1),
        CornerRadius = new CornerRadius(Radius),
        Child = content,
    };

    /// <summary>A raised area on the sheet: a group of rows, a card.</summary>
    public Border Raised(UIElement? content, Thickness padding = default) => new()
    {
        Background = Group,
        BorderBrush = GroupRim,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(GroupRadius),
        Padding = padding,
        Child = content,
    };

    /// <summary>An icon from the icon font. Without a colour it takes the one around it.</summary>
    public TextBlock Glyph(string glyph, double size, Brush? color = null)
    {
        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = Theme.Icons,
            FontSize = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (color != null)
            icon.Foreground = color;
        return icon;
    }

    /// <summary>"VOLUME MIXER": the small mono label above a group or a widget.</summary>
    public TextBlock Caption(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontFamily = Theme.Mono,
        FontSize = 10.5,
        Foreground = Secondary,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// A flat area that behaves like a button: hover and press shades, a hand cursor, dimmed when disabled.
    /// It swallows the press, so nothing behind it reacts (a widget does not start dragging), and a failing
    /// action is logged instead of taking the window down.
    /// </summary>
    public Border Pressable(UIElement child, Action onClick, Thickness padding, double? radius = null, Brush? rest = null)
    {
        var border = new Border { Child = child, Padding = padding, CornerRadius = new CornerRadius(radius ?? ControlRadius) };
        MakePressable(border, onClick, rest);
        return border;
    }

    /// <summary>Gives an existing border the behaviour of <see cref="Pressable"/>.</summary>
    public void MakePressable(Border border, Action onClick, Brush? rest = null)
    {
        var restBrush = rest ?? Brushes.Transparent;
        var hover = Shade(restBrush, Hover);
        var press = Shade(restBrush, Press);
        border.Background = restBrush;
        border.Cursor = Cursors.Hand;
        var pressed = false;
        border.MouseEnter += (_, _) => { if (border.IsEnabled) border.Background = hover; };
        border.MouseLeave += (_, _) => { border.Background = restBrush; pressed = false; };
        border.MouseLeftButtonDown += (_, e) => { pressed = true; border.Background = press; e.Handled = true; };
        border.MouseLeftButtonUp += (_, e) =>
        {
            if (!pressed) return;
            pressed = false;
            border.Background = hover;
            e.Handled = true;
            try
            {
                onClick();
            }
            catch (Exception ex)
            {
                Store.Log("action failed: " + ex);
            }
        };
        border.IsEnabledChanged += (_, _) => border.Opacity = border.IsEnabled ? 1 : 0.4;
    }

    /// <summary>
    /// The hover or press shade of a fill. Over nothing it is the overlay itself; a see-through fill gets denser;
    /// a solid one steps towards white when dark and towards black when light.
    /// </summary>
    private static Brush Shade(Brush rest, Brush overlay)
    {
        if (rest == Brushes.Transparent || rest is not SolidColorBrush s || overlay is not SolidColorBrush o)
            return overlay;
        var c = s.Color;
        if (c.A < 0xFF)
            return Palette.B((byte)Math.Min(255, c.A + o.Color.A * 2), c.R, c.G, c.B);
        var dark = (c.R * 299 + c.G * 587 + c.B * 114) / 1000 < 110;
        var amount = Math.Min(0.4, o.Color.A / 255.0 * 1.6);
        byte Mix(byte v) => (byte)Math.Round(v + ((dark ? 255 : 0) - v) * amount);
        return Palette.B(0xFF, Mix(c.R), Mix(c.G), Mix(c.B));
    }
}

/// <summary>
/// A square button with an icon over a label and an optional small line of state: the island's quick actions and
/// tool grid, and the panel's quick switches. Orange-rimmed while it is on or armed.
/// </summary>
internal sealed class Tile : Border
{
    private readonly Material _m;
    private readonly TextBlock _detail;
    private bool _active;

    /// <param name="roomy">The larger tile of a grid; otherwise the compact one of a strip.</param>
    /// <param name="symbol">The glyph is a plain Unicode symbol (like ✦), not from the icon font.</param>
    public Tile(Material m, string glyph, string label, Action onClick, string? detail = null, bool roomy = false, bool symbol = false)
    {
        _m = m;
        Margin = new Thickness(4);
        Padding = roomy ? new Thickness(4) : new Thickness(4, 8, 4, 7);
        CornerRadius = new CornerRadius(m.GroupRadius);
        Background = m.Group;
        BorderBrush = m.GroupRim;
        BorderThickness = new Thickness(1);
        Cursor = Cursors.Hand;

        var icon = m.Glyph(glyph, roomy ? 20 : 16, m.Text);
        if (symbol)
        {
            icon.FontFamily = new FontFamily("Segoe UI Symbol");
            icon.FontSize = roomy ? 22 : 17;
        }
        icon.Margin = new Thickness(0, 0, 0, roomy ? 8 : 4);
        _detail = new TextBlock
        {
            Text = detail ?? "",
            FontSize = roomy ? 10.5 : 10,
            Foreground = m.Secondary,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, roomy ? 3 : 1, 0, 0),
            Visibility = roomy && detail == null ? Visibility.Collapsed : Visibility.Visible,
        };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(icon);
        stack.Children.Add(new TextBlock { Text = label, FontSize = roomy ? 12 : 11, FontWeight = FontWeights.SemiBold, Foreground = m.Text, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        stack.Children.Add(_detail);
        Child = stack;

        var pressed = false;
        MouseEnter += (_, _) => Paint();
        MouseLeave += (_, _) => { pressed = false; Paint(); };
        MouseLeftButtonDown += (_, e) => { pressed = true; e.Handled = true; };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!pressed) return;
            pressed = false;
            e.Handled = true;
            try
            {
                onClick();
            }
            catch (Exception ex)
            {
                Store.Log($"tile '{label}' failed: " + ex.Message);
            }
        };
    }

    /// <summary>The small line under the label.</summary>
    public string Detail
    {
        set
        {
            _detail.Text = value;
            _detail.Visibility = Visibility.Visible;
        }
    }

    public void Set(bool active, string detail)
    {
        _active = active;
        Detail = detail;
        Paint();
    }

    private void Paint()
    {
        Background = _active ? _m.AccentSoft : IsMouseOver ? _m.GroupHover : _m.Group;
        BorderBrush = _active ? _m.Accent : _m.GroupRim;
    }
}
