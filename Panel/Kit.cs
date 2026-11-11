using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LumenLite.Panel;

/// <summary>
/// Building blocks for the control panel, following the look of Vorssaint's
/// menu-bar panel: a near-black sheet, grey cards with a hairline border,
/// blue accents, uppercase section titles and rows that open sub-pages.
/// </summary>
internal static class Kit
{
    public static readonly Brush Sheet = Solid(0xFF, 0x1C, 0x1C, 0x1E);
    public static readonly Brush SheetBorder = Solid(0xFF, 0x3A, 0x3A, 0x3C);
    public static readonly Brush Card = Solid(0xFF, 0x2A, 0x2A, 0x2C);
    public static readonly Brush CardBorder = Solid(0xFF, 0x38, 0x38, 0x3A);
    public static readonly Brush Inset = Solid(0xFF, 0x33, 0x33, 0x36);
    public static readonly Brush Divider = Solid(0xFF, 0x3A, 0x3A, 0x3C);
    public static readonly Brush Text = Solid(0xFF, 0xF2, 0xF2, 0xF7);
    public static readonly Brush Secondary = Solid(0xFF, 0x98, 0x98, 0x9F);
    public static readonly Brush Blue = Solid(0xFF, 0x0A, 0x84, 0xFF);
    public static readonly Brush BlueSoft = Solid(0xFF, 0x1B, 0x35, 0x55);
    public static readonly Brush Green = Solid(0xFF, 0x30, 0xD1, 0x58);
    public static readonly Brush GreenSoft = Solid(0xFF, 0x1E, 0x3B, 0x27);
    public static readonly Brush Orange = Solid(0xFF, 0xFF, 0x9F, 0x0A);
    public static readonly Brush Red = Solid(0xFF, 0xFF, 0x45, 0x3A);
    public static readonly Brush Track = Solid(0xFF, 0x45, 0x45, 0x48);
    public static readonly Brush HoverFill = Solid(0x14, 0xFF, 0xFF, 0xFF);
    public static readonly Brush PressFill = Solid(0x24, 0xFF, 0xFF, 0xFF);

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
        var grid = new Grid { Margin = new Thickness(2, 16, 2, 8) };
        grid.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
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

    /// <summary>A grey rounded card; children are stacked with thin dividers when asked.</summary>
    public static Border CardOf(params UIElement[] children) => CardOf(false, children);

    public static Border CardOf(bool dividers, params UIElement[] children)
    {
        var stack = new StackPanel();
        for (var i = 0; i < children.Length; i++)
        {
            if (dividers && i > 0)
                stack.Children.Add(new Border { Height = 1, Background = Divider, Margin = new Thickness(0, 8, 0, 8) });
            stack.Children.Add(children[i]);
        }
        return new Border
        {
            Background = Card,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 12, 14, 12),
            Child = stack,
            Margin = new Thickness(0, 0, 0, 10),
        };
    }

    /// <summary>A flat clickable area; swallows the press so nothing behind it reacts.</summary>
    public static Border Clickable(UIElement child, Action onClick, Thickness padding, double radius = 8, Brush? rest = null)
    {
        var restBrush = rest ?? Brushes.Transparent;
        var border = new Border { Child = child, Padding = padding, CornerRadius = new CornerRadius(radius), Background = restBrush, Cursor = Cursors.Hand };
        var pressed = false;
        border.MouseEnter += (_, _) => { if (border.IsEnabled) border.Background = Blend(restBrush, HoverFill); };
        border.MouseLeave += (_, _) => { border.Background = restBrush; pressed = false; };
        border.MouseLeftButtonDown += (_, e) => { pressed = true; border.Background = Blend(restBrush, PressFill); e.Handled = true; };
        border.MouseLeftButtonUp += (_, e) =>
        {
            if (!pressed) return;
            pressed = false;
            border.Background = Blend(restBrush, HoverFill);
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

    private static Brush Blend(Brush rest, Brush overlay) => rest == Brushes.Transparent ? overlay : rest is SolidColorBrush s && overlay is SolidColorBrush o
        ? Solid(0xFF,
            (byte)Math.Min(255, s.Color.R + o.Color.A * (255 - s.Color.R) / 600),
            (byte)Math.Min(255, s.Color.G + o.Color.A * (255 - s.Color.G) / 600),
            (byte)Math.Min(255, s.Color.B + o.Color.A * (255 - s.Color.B) / 600))
        : overlay;

    /// <summary>Utilities-style row: blue icon, bold title, grey description, trailing bits and a chevron.</summary>
    public static Border NavRow(string glyph, string title, string? description, Action onClick, UIElement? trailing = null, bool chevron = true)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Glyph(glyph, 20, Blue);
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Label(title, 14.5, true));
        if (description != null)
            text.Children.Add(Wrap(description, 12.5));
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
            var arrow = Glyph("", 11, Secondary);
            arrow.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(arrow, 3);
            grid.Children.Add(arrow);
        }
        var row = Clickable(grid, onClick, new Thickness(14, 12, 14, 12), 14, Card);
        row.BorderBrush = CardBorder;
        row.BorderThickness = new Thickness(1);
        row.Margin = new Thickness(0, 0, 0, 10);
        return row;
    }

    /// <summary>A key-cap style hint such as "Ctrl+Alt+V".</summary>
    public static Border KeyHint(string keys) => new()
    {
        Background = Inset,
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(7, 2, 7, 3),
        Child = new TextBlock { Text = keys, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Secondary },
    };

    /// <summary>Rounded chip, filled blue when selected.</summary>
    public static Border Chip(string text, bool selected, Action onClick)
    {
        var label = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Text, HorizontalAlignment = HorizontalAlignment.Center };
        var chip = Clickable(label, onClick, new Thickness(8, 6, 8, 7), 10, selected ? Blue : Inset);
        chip.Margin = new Thickness(3);
        return chip;
    }

    /// <summary>A footer-style wide button with an icon ("Settings", "Quit").</summary>
    public static Border Button(string glyph, string text, Action onClick, Brush? fill = null, Brush? foreground = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (glyph.Length > 0)
        {
            var icon = Glyph(glyph, 13, foreground ?? Secondary);
            icon.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(icon);
        }
        row.Children.Add(new TextBlock { Text = text, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = foreground ?? Secondary });
        var button = Clickable(row, onClick, new Thickness(12, 8, 12, 9), 10, fill ?? Card);
        button.BorderBrush = CardBorder;
        button.BorderThickness = new Thickness(1);
        return button;
    }

    /// <summary>Small inline button such as "Thoát" or "Cập nhật".</summary>
    public static Border SmallButton(string text, Action onClick, Brush? fill = null, Brush? foreground = null)
    {
        var button = Clickable(new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = foreground ?? Text },
            onClick, new Thickness(10, 4, 10, 5), 8, fill ?? Inset);
        return button;
    }

    /// <summary>iOS-style switch. The callback receives the new state.</summary>
    public static Border Toggle(bool on, Action<bool> changed)
    {
        var knob = new Ellipse { Width = 18, Height = 18, Fill = Brushes.White, HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left, Margin = new Thickness(2) };
        var track = new Border { Width = 40, Height = 22, CornerRadius = new CornerRadius(11), Background = on ? Green : Track, Child = knob, Cursor = Cursors.Hand };
        var state = on;
        track.MouseLeftButtonDown += (_, e) => e.Handled = true;
        track.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            state = !state;
            knob.HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            track.Background = state ? Green : Track;
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
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        text.Children.Add(Label(title, 13.5, true));
        if (description != null)
            text.Children.Add(Wrap(description, 12));
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
        var v = Label(value, 13, true, valueColor);
        Grid.SetColumn(v, 1);
        grid.Children.Add(v);
        return grid;
    }

    /// <summary>A horizontal usage bar with a coloured fill.</summary>
    public static Grid Bar(double fraction, Brush fill, double height = 6)
    {
        var grid = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = Track });
        var bar = new Border { CornerRadius = new CornerRadius(height / 2), Background = fill, HorizontalAlignment = HorizontalAlignment.Left };
        grid.Children.Add(bar);
        grid.Tag = fraction;
        grid.SizeChanged += (_, _) => bar.Width = Math.Max(height, grid.ActualWidth * Math.Clamp((double)grid.Tag, 0, 1));
        return grid;
    }

    public static void SetBar(Grid bar, double fraction)
    {
        bar.Tag = fraction;
        if (bar.Children.Count > 1 && bar.Children[1] is Border fill)
            fill.Width = Math.Max(bar.Height, bar.ActualWidth * Math.Clamp(fraction, 0, 1));
    }

    /// <summary>A search box in the panel's style.</summary>
    public static (Border Box, TextBox Input) SearchBox(string placeholder)
    {
        var input = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Text,
            CaretBrush = Text,
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
        var box = new Border { Background = Inset, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 6, 10, 6), Child = grid, Margin = new Thickness(0, 0, 0, 10) };
        return (box, input);
    }

    /// <summary>A dark drop-down ("Default ⌃⌄") that opens a list of choices under it.</summary>
    public static Border Dropdown(string current, IReadOnlyList<(string Label, Action Pick)> choices, double maxWidth = 150)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = current, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var arrows = Glyph("", 9, Secondary);
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
                    }, new Thickness(10, 6, 10, 6), 6);
                list.Children.Add(item);
            }
            popup.Child = new Border
            {
                Background = Card,
                BorderBrush = CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(4),
                MinWidth = dropdown!.ActualWidth,
                MaxWidth = 300,
                Child = new ScrollViewer { Content = list, MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            };
            popup.IsOpen = true;
        }, new Thickness(10, 4, 10, 5), 8, Inset);
        dropdown.MaxWidth = maxWidth;
        return dropdown;
    }
}
