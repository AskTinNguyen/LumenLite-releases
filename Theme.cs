using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LumenLite;

/// <summary>
/// LumenLite's colours. Two surfaces, one accent:
/// the "display" (island, radial menu, widgets) is always black, like a device's screen;
/// the "panel" (control panel, Command Bar, Scratchpad) is warm aluminium grey, or graphite in dark mode.
/// Orange marks what is on or selected; nothing else is coloured unless it means something.
/// </summary>
internal sealed class Palette
{
    public required Brush Sheet, SheetBorder, Card, CardBorder, Inset, Divider, Text, Secondary, AccentSoft, Track, Knob, HoverFill, PressFill, Selected, OnSelected;
    public required bool IsDark;

    public static readonly Color AccentColor = Color.FromRgb(0xFF, 0x52, 0x0E);

    public static readonly Palette Light = new()
    {
        IsDark = false,
        Sheet = B(0xFF, 0xE9, 0xE8, 0xE4),
        SheetBorder = B(0xFF, 0xC9, 0xC7, 0xC1),
        Card = B(0xFF, 0xF6, 0xF5, 0xF2),
        CardBorder = B(0xFF, 0xD9, 0xD7, 0xD1),
        Inset = B(0xFF, 0xE2, 0xE0, 0xDB),
        Divider = B(0xFF, 0xE1, 0xDF, 0xDA),
        Text = B(0xFF, 0x17, 0x17, 0x16),
        Secondary = B(0xFF, 0x76, 0x74, 0x6F),
        AccentSoft = B(0xFF, 0xFC, 0xDE, 0xCF),
        Track = B(0xFF, 0xD0, 0xCE, 0xC8),
        Knob = B(0xFF, 0xFF, 0xFF, 0xFF),
        HoverFill = B(0x0E, 0x00, 0x00, 0x00),
        PressFill = B(0x1C, 0x00, 0x00, 0x00),
        Selected = B(0xFF, 0x17, 0x17, 0x16),
        OnSelected = B(0xFF, 0xF6, 0xF5, 0xF2),
    };

    public static readonly Palette Dark = new()
    {
        IsDark = true,
        Sheet = B(0xFF, 0x12, 0x12, 0x12),
        SheetBorder = B(0xFF, 0x2E, 0x2E, 0x2D),
        Card = B(0xFF, 0x1B, 0x1B, 0x1A),
        CardBorder = B(0xFF, 0x29, 0x29, 0x28),
        Inset = B(0xFF, 0x26, 0x26, 0x25),
        Divider = B(0xFF, 0x26, 0x26, 0x25),
        Text = B(0xFF, 0xEE, 0xED, 0xE9),
        Secondary = B(0xFF, 0x8E, 0x8C, 0x87),
        AccentSoft = B(0xFF, 0x3A, 0x1F, 0x12),
        Track = B(0xFF, 0x33, 0x33, 0x32),
        Knob = B(0xFF, 0xEE, 0xED, 0xE9),
        HoverFill = B(0x12, 0xFF, 0xFF, 0xFF),
        PressFill = B(0x22, 0xFF, 0xFF, 0xFF),
        Selected = B(0xFF, 0xEE, 0xED, 0xE9),
        OnSelected = B(0xFF, 0x12, 0x12, 0x12),
    };

    /// <summary>The panel palette in use: Settings' choice, or Windows' app mode. Read each time a screen is built.</summary>
    public static Palette Current => (Override ?? Store.S.ThemeMode) switch
    {
        "light" => Light,
        "dark" => Dark,
        _ => WindowsUsesDark() ? Dark : Light,
    };

    /// <summary>Developer aid ("--theme light|dark" with --snapshot): a theme for this run only, never saved.</summary>
    public static string? Override;

    private static bool? _windowsDark;
    private static DateTime _windowsRead;

    private static bool WindowsUsesDark()
    {
        if (_windowsDark is { } known && DateTime.UtcNow - _windowsRead < TimeSpan.FromSeconds(5))
            return known;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            _windowsDark = key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            _windowsDark = true;
        }
        _windowsRead = DateTime.UtcNow;
        return _windowsDark.Value;
    }

    internal static Brush B(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}

internal static class Theme
{
    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    /// <summary>For numbers, times, key names and labels: fixed-width figures read like an instrument.</summary>
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    // The display: black glass, off-white type, one orange.
    public static readonly Brush Text = Freeze(new SolidColorBrush(Color.FromRgb(0xEE, 0xED, 0xE9)));
    public static readonly Brush Muted = Freeze(new SolidColorBrush(Color.FromRgb(0x8E, 0x8C, 0x87)));
    public static readonly Brush Hover = Freeze(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)));
    public static readonly Brush Press = Freeze(new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)));
    /// <summary>The display's raised surface: tiles and buttons on the black.</summary>
    public static readonly Brush Surface = Freeze(new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x19)));
    public static readonly Brush SurfaceHover = Freeze(new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x24)));
    public static readonly Brush Hairline = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x29)));
    public static readonly Brush Black = Freeze(new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0A)));
    /// <summary>The empty part of a bar or a fader.</summary>
    public static readonly Brush Track = Freeze(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x32)));
    /// <summary>Quieter than <see cref="Muted"/>: days outside the month, what is switched off.</summary>
    public static readonly Brush Faint = Freeze(new SolidColorBrush(Color.FromRgb(0x56, 0x55, 0x52)));

    /// <summary>On, selected, live. The only colour on the display.</summary>
    public static readonly Brush Accent = Freeze(new SolidColorBrush(Palette.AccentColor));
    /// <summary>Behind something that is on without shouting: a chosen tile, a "watching" tag.</summary>
    public static readonly Brush AccentSoft = Freeze(new SolidColorBrush(Color.FromRgb(0x3A, 0x1F, 0x12)));
    /// <summary>Text and icons on the accent.</summary>
    public static readonly Brush OnAccent = Freeze(new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x13)));

    // Status, used only where a reading is close to a limit or past it.
    public static readonly Brush Warn = Freeze(new SolidColorBrush(Color.FromRgb(0xE3, 0xB3, 0x41)));
    public static readonly Brush Bad = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x5F, 0x52)));

    /// <summary>Dashed outline and resize strokes shown in edit mode.</summary>
    public static readonly Brush EditOutline = Freeze(new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)));

    /// <summary>Corner radius of every widget, in device-independent pixels.</summary>
    public const double Radius = 14;
    /// <summary>Tiles and cards on the display, and the buttons and tags inside them.</summary>
    public const double TileRadius = 8, ControlRadius = 6, TagRadius = 4;

    public static TextBlock Header(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontFamily = Mono,
        FontSize = 10.5,
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
            CornerRadius = new CornerRadius(ControlRadius),
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

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
