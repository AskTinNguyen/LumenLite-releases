using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace LumenLite.Panel;

/// <summary>
/// Building blocks for the panel surfaces (control panel, Command Bar, Scratchpad): a flat sheet, cards with a
/// hairline border, small radii, mono labels and one orange for what is on or selected. The colours come from
/// <see cref="Palette.Current"/> each time something is built, so a theme change shows on the next open.
/// </summary>
internal static class Kit
{
    /// <summary>Corner radii: cards and sheets-in-sheets, buttons and chips, key caps.</summary>
    public const double CardRadius = 8, ControlRadius = 6, KeyRadius = 4;

    public static Brush Sheet => Palette.Current.Sheet;
    public static Brush SheetBorder => Palette.Current.SheetBorder;
    public static Brush Card => Palette.Current.Card;
    public static Brush CardBorder => Palette.Current.CardBorder;
    public static Brush Inset => Palette.Current.Inset;
    public static Brush Divider => Palette.Current.Divider;
    public static Brush Text => Palette.Current.Text;
    public static Brush Secondary => Palette.Current.Secondary;
    public static Brush Track => Palette.Current.Track;
    public static Brush Knob => Palette.Current.Knob;
    public static Brush HoverFill => Palette.Current.HoverFill;
    public static Brush PressFill => Palette.Current.PressFill;

    /// <summary>On, selected, live.</summary>
    public static Brush Accent => Theme.Accent;
    public static Brush AccentSoft => Palette.Current.AccentSoft;
    public static readonly Brush OnAccent = Solid(0xFF, 0x14, 0x14, 0x13);

    /// <summary>The ink fill of a primary button, and the text on it.</summary>
    public static Brush Ink => Palette.Current.Selected;
    public static Brush OnInk => Palette.Current.OnSelected;

    // Status colours: only where a state is good, close to a limit, or bad.
    private static readonly Brush GreenLight = Solid(0xFF, 0x1F, 0x8F, 0x46), GreenDark = Solid(0xFF, 0x4C, 0xC9, 0x74);
    private static readonly Brush GreenSoftLight = Solid(0xFF, 0xDD, 0xEE, 0xDF), GreenSoftDark = Solid(0xFF, 0x1B, 0x33, 0x23);
    private static readonly Brush AmberLight = Solid(0xFF, 0xA8, 0x7A, 0x00), AmberDark = Solid(0xFF, 0xE3, 0xB3, 0x41);
    private static readonly Brush AmberSoftLight = Solid(0xFF, 0xF3, 0xE8, 0xC6), AmberSoftDark = Solid(0xFF, 0x3A, 0x30, 0x14);
    private static readonly Brush RedLight = Solid(0xFF, 0xD1, 0x36, 0x2A), RedDark = Solid(0xFF, 0xFF, 0x5F, 0x52);
    private static readonly Brush RedSoftLight = Solid(0xFF, 0xF7, 0xDD, 0xD9), RedSoftDark = Solid(0xFF, 0x40, 0x1D, 0x1A);
    public static readonly Brush OnRed = Solid(0xFF, 0xFF, 0xFF, 0xFF);

    public static Brush Green => Palette.Current.IsDark ? GreenDark : GreenLight;
    public static Brush GreenSoft => Palette.Current.IsDark ? GreenSoftDark : GreenSoftLight;
    public static Brush Amber => Palette.Current.IsDark ? AmberDark : AmberLight;
    public static Brush AmberSoft => Palette.Current.IsDark ? AmberSoftDark : AmberSoftLight;
    public static Brush Red => Palette.Current.IsDark ? RedDark : RedLight;
    public static Brush RedSoft => Palette.Current.IsDark ? RedSoftDark : RedSoftLight;

    public static Color TextColor => ((SolidColorBrush)Text).Color;
    public static Color SecondaryColor => ((SolidColorBrush)Secondary).Color;

    public static Brush Solid(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    public static TextBlock Glyph(string glyph, double size, Brush? color = null) => new()
    {
        Text = glyph,
        FontFamily = Theme.Icons,
        FontSize = size,
        Foreground = color ?? Text,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock Label(string text, double size = 13, bool bold = false, Brush? color = null) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = color ?? Text,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A number, a time or a key name: fixed-width, like an instrument's readout.</summary>
    public static TextBlock Mono(string text, double size = 12, bool bold = false, Brush? color = null)
    {
        var label = Label(text, size, bold, color);
        label.FontFamily = Theme.Mono;
        return label;
    }

    public static TextBlock Wrap(string text, double size = 12, Brush? color = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = color ?? Secondary,
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>"VOLUME MIXER" style title above a card, with optional trailing content.</summary>
    public static FrameworkElement Section(string title, UIElement? trailing = null)
    {
        var grid = new Grid { Margin = new Thickness(2, 18, 2, 8) };
        grid.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontFamily = Theme.Mono,
            FontSize = 10.5,
            Foreground = Secondary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (trailing is FrameworkElement element)
        {
            element.HorizontalAlignment = HorizontalAlignment.Right;
            grid.Children.Add(element);
        }
        return grid;
    }

    /// <summary>A flat card with a hairline border; children are stacked with thin dividers when asked.</summary>
    public static Border CardOf(params UIElement[] children) => CardOf(false, children);

    public static Border CardOf(bool dividers, params UIElement[] children)
    {
        var stack = new StackPanel();
        for (var i = 0; i < children.Length; i++)
        {
            if (dividers && i > 0)
                stack.Children.Add(new Border { Height = 1, Background = Divider, Margin = new Thickness(0, 10, 0, 10) });
            stack.Children.Add(children[i]);
        }
        return new Border
        {
            Background = Card,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(CardRadius),
            Padding = new Thickness(14, 12, 14, 12),
            Child = stack,
            Margin = new Thickness(0, 0, 0, 8),
        };
    }

    /// <summary>A flat clickable area; swallows the press so nothing behind it reacts.</summary>
    public static Border Clickable(UIElement child, Action onClick, Thickness padding, double radius = ControlRadius, Brush? rest = null)
    {
        var restBrush = rest ?? Brushes.Transparent;
        var hover = Blend(restBrush, HoverFill);
        var press = Blend(restBrush, PressFill);
        var border = new Border { Child = child, Padding = padding, CornerRadius = new CornerRadius(radius), Background = restBrush, Cursor = Cursors.Hand };
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
                Store.Log("panel action failed: " + ex);
            }
        };
        border.IsEnabledChanged += (_, _) => border.Opacity = border.IsEnabled ? 1 : 0.4;
        return border;
    }

    /// <summary>The hover or press shade of a fill: a step towards white on a dark fill, towards black on a light one.</summary>
    private static Brush Blend(Brush rest, Brush overlay)
    {
        if (rest == Brushes.Transparent || rest is not SolidColorBrush s || overlay is not SolidColorBrush o)
            return overlay;
        var c = s.Color;
        var dark = (c.R * 299 + c.G * 587 + c.B * 114) / 1000 < 110;
        var amount = Math.Min(0.4, o.Color.A / 255.0 * 1.6);
        byte Mix(byte v) => (byte)Math.Round(v + ((dark ? 255 : 0) - v) * amount);
        return Solid(0xFF, Mix(c.R), Mix(c.G), Mix(c.B));
    }

    /// <summary>A row that opens something: icon, bold title, grey description, trailing bits and a chevron.</summary>
    public static Border NavRow(string glyph, string title, string? description, Action onClick, UIElement? trailing = null, bool chevron = true)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Glyph(glyph, 18, Text);
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Label(title, 14, true));
        if (description != null)
            text.Children.Add(Wrap(description, 12));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (trailing is FrameworkElement t)
        {
            t.Margin = new Thickness(8, 0, 0, 0);
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, 2);
            grid.Children.Add(t);
        }
        if (chevron)
        {
            var arrow = Glyph("", 10, Secondary);
            arrow.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(arrow, 3);
            grid.Children.Add(arrow);
        }
        var row = Clickable(grid, onClick, new Thickness(14, 12, 14, 12), CardRadius, Card);
        row.BorderBrush = CardBorder;
        row.BorderThickness = new Thickness(1);
        row.Margin = new Thickness(0, 0, 0, 8);
        return row;
    }

    /// <summary>A key-cap style hint such as "Ctrl+Alt+V".</summary>
    public static Border KeyHint(string keys) => new()
    {
        Background = Inset,
        BorderBrush = CardBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(KeyRadius),
        Padding = new Thickness(6, 2, 6, 2),
        Child = new TextBlock { Text = keys, FontFamily = Theme.Mono, FontSize = 10.5, Foreground = Secondary },
    };

    /// <summary>A choice among a few; the chosen one is orange.</summary>
    public static Border Chip(string text, bool selected, Action onClick)
    {
        var label = new TextBlock { Text = text, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = selected ? OnAccent : Text, HorizontalAlignment = HorizontalAlignment.Center };
        var chip = Clickable(label, onClick, new Thickness(8, 6, 8, 7), ControlRadius, selected ? Accent : Inset);
        chip.Margin = new Thickness(3);
        return chip;
    }

    /// <summary>A wide button with an optional icon. Without a fill it is an outlined secondary button.</summary>
    public static Border Button(string glyph, string text, Action onClick, Brush? fill = null, Brush? foreground = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (glyph.Length > 0)
        {
            var icon = Glyph(glyph, 12.5, foreground ?? Text);
            icon.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(icon);
        }
        row.Children.Add(new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = foreground ?? Text });
        var button = Clickable(row, onClick, new Thickness(12, 8, 12, 9), ControlRadius, fill ?? Card);
        if (fill == null)
        {
            button.BorderBrush = CardBorder;
            button.BorderThickness = new Thickness(1);
        }
        else
            button.Padding = new Thickness(13, 9, 13, 10);
        return button;
    }

    /// <summary>The one action a card is for ("Dọn", "Bắt đầu đo"): an ink-filled button.</summary>
    public static Border Primary(string glyph, string text, Action onClick) => Button(glyph, text, onClick, Ink, OnInk);

    /// <summary>A button that stays out of the way until wanted: no fill, no border, secondary text.</summary>
    public static Border Quiet(string glyph, string text, Action onClick)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var icon = Glyph(glyph, 12, Secondary);
        icon.Margin = new Thickness(0, 0, 7, 0);
        row.Children.Add(icon);
        row.Children.Add(new TextBlock { Text = text, FontSize = 12.5, Foreground = Secondary });
        return Clickable(row, onClick, new Thickness(12, 7, 12, 8));
    }

    /// <summary>Small inline button such as "Thoát" or "Cập nhật".</summary>
    public static Border SmallButton(string text, Action onClick, Brush? fill = null, Brush? foreground = null)
    {
        var button = Clickable(new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = foreground ?? Text },
            onClick, new Thickness(10, 4, 10, 5), ControlRadius, fill ?? Inset);
        return button;
    }

    /// <summary>A rectangular switch with a square knob, orange when on. The callback receives the new state.</summary>
    public static Border Toggle(bool on, Action<bool> changed)
    {
        var knob = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(2), Background = Knob, HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left, Margin = new Thickness(3) };
        var track = new Border { Width = 36, Height = 20, CornerRadius = new CornerRadius(4), Background = on ? Accent : Track, Child = knob, Cursor = Cursors.Hand };
        var off = Track;
        var state = on;
        track.MouseLeftButtonDown += (_, e) => e.Handled = true;
        track.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            state = !state;
            knob.HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            track.Background = state ? Accent : off;
            try
            {
                changed(state);
            }
            catch (Exception ex)
            {
                Store.Log("toggle failed: " + ex.Message);
            }
        };
        return track;
    }

    /// <summary>A setting line: title, optional description and a switch on the right.</summary>
    public static Grid ToggleRow(string title, string? description, bool on, Action<bool> changed)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        text.Children.Add(Label(title, 13.5, true));
        if (description != null)
        {
            var detail = Wrap(description, 12);
            detail.Margin = new Thickness(0, 1, 0, 0);
            text.Children.Add(detail);
        }
        grid.Children.Add(text);
        var toggle = Toggle(on, changed);
        toggle.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        return grid;
    }

    /// <summary>A plain "label ... value" line.</summary>
    public static Grid ValueRow(string label, string value, Brush? valueColor = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Label(label, 13, false, Secondary));
        var v = Mono(value, 12.5, false, valueColor);
        Grid.SetColumn(v, 1);
        grid.Children.Add(v);
        return grid;
    }

    /// <summary>A horizontal usage bar: a thin track with a square-ended fill.</summary>
    public static Grid Bar(double fraction, Brush fill, double height = 4)
    {
        var grid = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(new Border { CornerRadius = new CornerRadius(1), Background = Track });
        var bar = new Border { CornerRadius = new CornerRadius(1), Background = fill, HorizontalAlignment = HorizontalAlignment.Left };
        grid.Children.Add(bar);
        grid.Tag = fraction;
        grid.SizeChanged += (_, _) => bar.Width = Math.Max(2, grid.ActualWidth * Math.Clamp((double)grid.Tag, 0, 1));
        return grid;
    }

    public static void SetBar(Grid bar, double fraction)
    {
        bar.Tag = fraction;
        if (bar.Children.Count > 1 && bar.Children[1] is Border fill)
            fill.Width = Math.Max(2, bar.ActualWidth * Math.Clamp(fraction, 0, 1));
    }

    /// <summary>A search box in the panel's style.</summary>
    public static (Border Box, TextBox Input) SearchBox(string placeholder)
    {
        var input = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Text,
            CaretBrush = Accent,
            SelectionBrush = Accent,
            FontSize = 13.5,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var hint = new TextBlock { Text = placeholder, Foreground = Secondary, FontSize = 13.5, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
        input.TextChanged += (_, _) => hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = Glyph("", 13, Secondary);
        icon.Margin = new Thickness(0, 0, 8, 0);
        grid.Children.Add(icon);
        var field = new Grid();
        field.Children.Add(hint);
        field.Children.Add(input);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
        var box = new Border { Background = Inset, CornerRadius = new CornerRadius(ControlRadius), Padding = new Thickness(10, 6, 10, 6), Child = grid, Margin = new Thickness(0, 0, 0, 8) };
        return (box, input);
    }

    /// <summary>A drop-down ("Default ⌄") that opens a list of choices under it.</summary>
    public static Border Dropdown(string current, IReadOnlyList<(string Label, Action Pick)> choices, double maxWidth = 150)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = current, FontSize = 12, Foreground = Text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var arrows = Glyph("", 8, Secondary);
        arrows.Margin = new Thickness(8, 1, 0, 0);
        Grid.SetColumn(arrows, 1);
        grid.Children.Add(arrows);

        Border? dropdown = null;
        dropdown = Clickable(grid, () =>
        {
            var list = new StackPanel();
            var popup = new Popup
            {
                PlacementTarget = dropdown,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
            };
            foreach (var (label, pick) in choices)
            {
                var item = Clickable(new TextBlock { Text = label, FontSize = 12.5, Foreground = Text, TextTrimming = TextTrimming.CharacterEllipsis },
                    () =>
                    {
                        popup.IsOpen = false;
                        pick();
                    }, new Thickness(10, 6, 10, 6), KeyRadius);
                list.Children.Add(item);
            }
            popup.Child = new Border
            {
                Background = Card,
                BorderBrush = SheetBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(CardRadius),
                Padding = new Thickness(4),
                MinWidth = dropdown!.ActualWidth,
                MaxWidth = 300,
                Child = new ScrollViewer { Content = list, MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            };
            popup.IsOpen = true;
        }, new Thickness(10, 4, 8, 5), ControlRadius, Inset);
        dropdown.MaxWidth = maxWidth;
        return dropdown;
    }

    /// <summary>
    /// A floating sheet (Command Bar, Scratchpad) over a soft shadow. The shadow belongs to a plain shape behind the
    /// sheet, not to the sheet: an effect on the sheet would be drawn again whenever anything inside it changes.
    /// </summary>
    public static Grid Floating(Border sheet, double margin)
    {
        var shadow = new Border
        {
            Background = sheet.Background,
            CornerRadius = sheet.CornerRadius,
            Margin = new Thickness(margin),
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 5, Direction = 270, Opacity = 0.28, Color = Colors.Black, RenderingBias = RenderingBias.Performance },
        };
        sheet.Margin = new Thickness(margin);
        var host = new Grid();
        host.Children.Add(shadow);
        host.Children.Add(sheet);
        return host;
    }
}
