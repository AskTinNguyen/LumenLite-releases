using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace LumenLite.Panel;

/// <summary>How much an action button asks for attention.</summary>
internal enum Tone
{
    Plain,
    /// <summary>The one thing a screen is for ("Dọn", "Bắt đầu đo").</summary>
    Primary,
    /// <summary>Removes or stops something.</summary>
    Destructive,
}

/// <summary>
/// Everything a panel screen is made of. A screen is a list of <see cref="Group"/>s, a group is a list of
/// <see cref="Row"/>s, and a row ends in one of a few controls (<see cref="Switch"/>, <see cref="Slider"/>,
/// <see cref="Value"/>, <see cref="Choice"/>, <see cref="Segments"/>, <see cref="Nav"/>, <see cref="Action"/>,
/// <see cref="Keys"/>). Spacing, dividers, hover, tooltips and error logging live in here, and the colours come
/// from <see cref="Material.Current"/> each time something is built, so a theme change shows on the next open.
/// </summary>
internal static class Ui
{
    /// <summary>The material the panel surfaces are drawn in right now.</summary>
    public static Material M => Material.Current;

    // ---------- containers ----------

    /// <summary>A titled, rounded group of rows with hairlines between them: the one container for every list.</summary>
    public static Group Group(string? title, params UIElement[] rows) => new(title, rows);

    /// <summary>
    /// The one row: icon, title and a trailing control. <paramref name="hint"/> is the row's tooltip;
    /// <paramref name="onClick"/> makes the whole row a button.
    /// </summary>
    public static Row Row(string? icon, string title, string? hint = null, UIElement? control = null, Action? onClick = null) =>
        new(icon == null ? null : Glyph(icon, 15), true, title, hint, control, onClick);

    /// <summary>A row led by a picture (an app's icon) instead of a glyph.</summary>
    public static Row PictureRow(ImageSource? icon, string title, string? hint = null, UIElement? control = null, Action? onClick = null) =>
        new(icon == null ? Glyph("\uE71D", 15, M.Secondary) : new Image { Source = icon, Width = 22, Height = 22 }, true, title, hint, control, onClick);

    /// <summary>A row led by anything else: a status dot, a port number.</summary>
    public static Row LeadRow(FrameworkElement lead, string title, string? hint = null, UIElement? control = null, Action? onClick = null) =>
        new(lead, false, title, hint, control, onClick);

    // ---------- the controls a row can end in ----------

    /// <summary>A rectangular switch with a square knob, orange when on. The callback receives the new state.</summary>
    public static Border Switch(bool on, Action<bool> changed)
    {
        var m = M;
        var knob = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(2), Background = m.Knob, HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left, Margin = new Thickness(3) };
        var track = new Border { Width = 36, Height = 20, CornerRadius = new CornerRadius(4), Background = on ? m.Accent : m.Track, Child = knob, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        var state = on;
        track.MouseLeftButtonDown += (_, e) => e.Handled = true;
        track.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            state = !state;
            knob.HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            track.Background = state ? m.Accent : m.Track;
            try
            {
                changed(state);
            }
            catch (Exception ex)
            {
                Store.Log("switch failed: " + ex.Message);
            }
        };
        return track;
    }

    /// <summary>A fader with its percentage beside it. Give it a width when it is a row's control.</summary>
    public static Fader Slider(float value, Action<float> changed, double width = double.NaN) => new(value, changed) { Width = width };

    /// <summary>A reading: mono, right-aligned, quiet unless coloured.</summary>
    public static TextBlock Value(string text, Brush? color = null) => new()
    {
        Text = text,
        FontFamily = Theme.Mono,
        FontSize = 12,
        Foreground = color ?? M.Secondary,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
        MaxWidth = 210,
    };

    /// <summary>A drop-down ("Mặc định ⌄") that opens the list of options under it.</summary>
    public static Border Choice(string current, IReadOnlyList<(string Label, Action Pick)> options, double maxWidth = 150)
    {
        var m = M;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = current, FontSize = 12, Foreground = m.Text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var arrows = Glyph("\uE70D", 8, m.Secondary);
        arrows.Margin = new Thickness(8, 1, 0, 0);
        Grid.SetColumn(arrows, 1);
        grid.Children.Add(arrows);

        Border? choice = null;
        choice = m.Pressable(grid, () =>
        {
            var list = new StackPanel();
            var popup = new Popup { PlacementTarget = choice, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
            foreach (var (label, pick) in options)
            {
                list.Children.Add(m.Pressable(new TextBlock { Text = label, FontSize = 12.5, Foreground = m.Text, TextTrimming = TextTrimming.CharacterEllipsis }, () =>
                {
                    popup.IsOpen = false;
                    pick();
                }, new Thickness(10, 6, 10, 6), m.KeyRadius));
            }
            popup.Child = new Border
            {
                Background = m.Menu,
                BorderBrush = m.MenuRim,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4),
                MinWidth = choice!.ActualWidth,
                MaxWidth = 300,
                Child = new ScrollViewer { Content = list, MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            };
            popup.IsOpen = true;
        }, new Thickness(10, 4, 8, 5), m.ControlRadius, m.Inset);
        choice.MaxWidth = maxWidth;
        choice.VerticalAlignment = VerticalAlignment.Center;
        return choice;
    }

    /// <summary>
    /// A choice among a few, all visible side by side; the chosen one is orange. It fills the width it is given,
    /// or with <paramref name="compact"/> is only as wide as its labels (as a row's control).
    /// </summary>
    public static FrameworkElement Segments(IReadOnlyList<(string Label, bool Selected, Action Pick)> options, bool compact = false) =>
        Segmented(options, compact, M.Accent, M.OnAccent);

    /// <summary>
    /// The same control switching between views of one tab. Which view is showing is not a setting, so the
    /// chosen one is ink, not orange.
    /// </summary>
    public static FrameworkElement Views(IReadOnlyList<(string Label, bool Selected, Action Pick)> options) =>
        Segmented(options, false, M.Ink, M.OnInk);

    private static FrameworkElement Segmented(IReadOnlyList<(string Label, bool Selected, Action Pick)> options, bool compact, Brush chosen, Brush onChosen)
    {
        var m = M;
        var cells = new Grid();
        foreach (var (label, selected, pick) in options)
        {
            var text = new TextBlock { Text = label, FontSize = 12, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal, Foreground = selected ? onChosen : m.Text, HorizontalAlignment = HorizontalAlignment.Center };
            var cell = m.Pressable(text, pick, new Thickness(10, 4, 10, 5), 5, selected ? chosen : null);
            cells.ColumnDefinitions.Add(new ColumnDefinition { Width = compact ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(cell, cells.Children.Count);
            cells.Children.Add(cell);
        }
        return new Border { Background = m.Inset, CornerRadius = new CornerRadius(7), Padding = new Thickness(2), Child = cells, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>The chevron of a row that opens something.</summary>
    public static TextBlock Nav() => Glyph("\uE76C", 10, M.Secondary);

    /// <summary>A button. Small beside a row's title; <paramref name="wide"/> for a bar under a group.</summary>
    public static ActionButton Action(string text, Action onClick, Tone tone = Tone.Plain, string? glyph = null, bool wide = false) =>
        new(M, text, onClick, tone, glyph, wide);

    /// <summary>A key-cap hint such as "Ctrl+Alt+V".</summary>
    public static Border Keys(string keys)
    {
        var m = M;
        return new Border
        {
            Background = m.Inset,
            CornerRadius = new CornerRadius(m.KeyRadius),
            Padding = new Thickness(6, 2, 6, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = keys, FontFamily = Theme.Mono, FontSize = 10.5, Foreground = m.Secondary },
        };
    }

    /// <summary>A button that is only an icon: mute, back, close.</summary>
    public static Border Icon(string glyph, Action onClick, string? tip = null, double size = 14, Brush? color = null)
    {
        var button = M.Pressable(Glyph(glyph, size, color), onClick, new Thickness(7, 5, 7, 5));
        button.VerticalAlignment = VerticalAlignment.Center;
        if (tip != null)
            button.ToolTip = Tip(tip);
        return button;
    }

    /// <summary>Two or three controls at the end of one row, e.g. a reading and a switch.</summary>
    public static FrameworkElement Trail(params UIElement[] controls)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < controls.Length; i++)
        {
            if (i > 0 && controls[i] is FrameworkElement element)
                element.Margin = new Thickness(10, 0, 0, 0);
            stack.Children.Add(controls[i]);
        }
        return stack;
    }

    // ---------- what is not a row ----------

    /// <summary>One line across a group: the fader takes the room the other parts leave.</summary>
    public static Grid Line(params FrameworkElement[] parts)
    {
        var grid = new Grid();
        for (var i = 0; i < parts.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = parts[i] is Fader ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            Grid.SetColumn(parts[i], i);
            grid.Children.Add(parts[i]);
        }
        return grid;
    }

    /// <summary>Wide buttons side by side, under the group they act on.</summary>
    public static Grid Bar(params FrameworkElement[] buttons)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        for (var i = 0; i < buttons.Length; i++)
        {
            if (i > 0)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(buttons[i], i * 2);
            grid.Children.Add(buttons[i]);
        }
        return grid;
    }

    /// <summary>
    /// Text that stays visible: a warning, a live status, an empty list. (What merely describes a row goes in
    /// the row's hint instead.)
    /// </summary>
    public static TextBlock Note(string text, Brush? color = null) => new()
    {
        Text = text,
        FontSize = 12.5,
        Foreground = color ?? M.Secondary,
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>The small mono label above a group.</summary>
    public static TextBlock Title(string text)
    {
        var title = M.Caption(text);
        title.Margin = new Thickness(4, 8, 4, 7);
        return title;
    }

    public static TextBlock Text(string text, double size = 13.5, bool bold = false, Brush? color = null) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = color ?? M.Text,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock Mono(string text, double size = 12, Brush? color = null)
    {
        var label = Text(text, size, false, color);
        label.FontFamily = Theme.Mono;
        return label;
    }

    public static TextBlock Glyph(string glyph, double size, Brush? color = null) => M.Glyph(glyph, size, color ?? M.Text);

    /// <summary>A tooltip in the panel's colours that wraps long text.</summary>
    public static ToolTip Tip(string text)
    {
        var m = M;
        return new ToolTip
        {
            Content = new TextBlock { Text = text, FontFamily = Theme.Font, FontSize = 12, Foreground = m.Text, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 },
            Background = m.Menu,
            BorderBrush = m.MenuRim,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 7, 10, 8),
        };
    }

    /// <summary>A search box: a magnifier, a placeholder and the field.</summary>
    public static (Border Box, TextBox Input) Search(string placeholder, double size = 13.5, bool bare = false)
    {
        var m = M;
        var input = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = m.Text,
            CaretBrush = m.Accent,
            SelectionBrush = m.Accent,
            FontSize = size,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var hint = new TextBlock { Text = placeholder, Foreground = m.Secondary, FontSize = size, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
        input.TextChanged += (_, _) => hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = Glyph("\uE721", size - 0.5, m.Secondary);
        icon.Margin = new Thickness(0, 0, 8, 0);
        grid.Children.Add(icon);
        var field = new Grid();
        field.Children.Add(hint);
        field.Children.Add(input);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
        var box = new Border
        {
            Background = bare ? Brushes.Transparent : m.Inset,
            CornerRadius = new CornerRadius(m.ControlRadius),
            Padding = bare ? new Thickness(14, 10, 14, 10) : new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, bare ? 0 : 10),
            Child = grid,
        };
        return (box, input);
    }
}

/// <summary>See <see cref="Ui.Group"/>.</summary>
internal sealed class Group : StackPanel
{
    private readonly Grid _header = new() { Visibility = Visibility.Collapsed };
    private readonly StackPanel _titleLine = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _title;
    private readonly StackPanel _rows = new();
    private bool _titled, _extras;

    internal Group(string? title, UIElement[] rows)
    {
        Margin = new Thickness(0, 0, 0, 10);
        _title = Ui.Title(title ?? "");
        _titleLine.Children.Add(_title);
        _header.Children.Add(_titleLine);
        Children.Add(_header);
        Children.Add(Ui.M.Raised(_rows));
        Title = title;
        Set(rows);
    }

    /// <summary>The label above the group; none hides it.</summary>
    public string? Title
    {
        set
        {
            _title.Text = (value ?? "").ToUpperInvariant();
            _title.Visibility = value == null ? Visibility.Collapsed : Visibility.Visible;
            _titled = value != null;
            _header.Visibility = _titled || _extras ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>Replaces the rows: how a live list redraws.</summary>
    public Group Set(params UIElement[] rows)
    {
        // A live list hands some of its elements back in: let go of them first.
        foreach (var child in _rows.Children)
            if (child is Border wrapper and not Row)
                wrapper.Child = null;
        _rows.Children.Clear();
        foreach (var row in rows)
            Add(row);
        return this;
    }

    public Group Add(UIElement row)
    {
        var m = Ui.M;
        if (_rows.Children.Count > 0)
            _rows.Children.Add(new Border { Height = 1, Background = m.Divider, Margin = new Thickness(14, 0, 14, 0) });
        // A row brings its own inset (its hover shape); anything else gets the same edges.
        _rows.Children.Add(row is Row ? row : new Border { Padding = new Thickness(14, 10, 14, 11), Child = row });
        return this;
    }

    /// <summary>Something beside the title, at the right: a count, a shortcut.</summary>
    public Group Trailing(FrameworkElement element)
    {
        element.HorizontalAlignment = HorizontalAlignment.Right;
        element.VerticalAlignment = VerticalAlignment.Center;
        element.Margin = new Thickness(0, 2, 4, 0);
        _header.Children.Add(element);
        _extras = true;
        _header.Visibility = Visibility.Visible;
        return this;
    }

    /// <summary>What the whole group is about, as a tooltip on its title and a small "i" after it.</summary>
    public Group Hint(string text)
    {
        var info = Ui.Glyph("\uE946", 11, Ui.M.Secondary);
        info.Margin = new Thickness(_titled ? 0 : 4, 2, 0, 0);
        _titleLine.Children.Add(info);
        _titleLine.Background = Brushes.Transparent;
        _titleLine.HorizontalAlignment = HorizontalAlignment.Left;
        _titleLine.ToolTip = Ui.Tip(text);
        ToolTipService.SetShowDuration(_titleLine, 30000);
        _extras = true;
        _header.Visibility = Visibility.Visible;
        return this;
    }
}

/// <summary>See <see cref="Ui.Row(string?, string, string?, UIElement?, Action?)"/>.</summary>
internal sealed class Row : Border
{
    private readonly TextBlock _title;
    private readonly FrameworkElement? _lead;
    private readonly StackPanel _text = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _stack = new();
    private TextBlock? _detail;

    internal Row(FrameworkElement? lead, bool boxed, string title, string? hint, UIElement? control, Action? onClick)
    {
        var m = Ui.M;
        Margin = new Thickness(3);
        Padding = new Thickness(11, 5, 11, 5);
        CornerRadius = new CornerRadius(7);
        Background = Brushes.Transparent;

        var line = new Grid { MinHeight = 28 };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _lead = lead;
        if (lead != null)
        {
            lead.HorizontalAlignment = HorizontalAlignment.Left;
            lead.VerticalAlignment = VerticalAlignment.Center;
            // Icons share one column width, so titles line up down the group.
            line.Children.Add(boxed ? new Border { Width = 32, Child = lead } : new Border { Child = lead, Margin = new Thickness(0, 0, 12, 0) });
        }
        _title = Ui.Text(title);
        _text.Children.Add(_title);
        Grid.SetColumn(_text, 1);
        line.Children.Add(_text);
        if (control is FrameworkElement trailing)
        {
            var holder = new Border { Child = trailing, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(holder, 2);
            line.Children.Add(holder);
        }
        _stack.Children.Add(line);
        Child = _stack;

        if (hint is { Length: > 0 })
        {
            ToolTip = Ui.Tip(hint);
            ToolTipService.SetShowDuration(this, 30000);
        }
        if (onClick != null)
            m.MakePressable(this, onClick);
    }

    public string Title
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    /// <summary>Marks the current one of a list: a heavier title and the icon in the accent.</summary>
    public Row Current(bool on = true)
    {
        _title.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        if (_lead is TextBlock icon)
            icon.Foreground = on ? Ui.M.Accent : Ui.M.Secondary;
        return this;
    }

    /// <summary>
    /// A second line that stays visible. Only for what must be read before acting (what a cleaner category
    /// deletes) or for live state; a description belongs in the hint.
    /// </summary>
    public Row Detail(string text, Brush? color = null)
    {
        if (_detail == null)
        {
            _detail = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) };
            _text.Children.Add(_detail);
        }
        _detail.Text = text;
        _detail.Foreground = color ?? Ui.M.Secondary;
        return this;
    }

    /// <summary>Content under the title line, the full width of the row: a fader.</summary>
    public Row Below(FrameworkElement element)
    {
        element.Margin = new Thickness(0, 2, 0, 2);
        _stack.Children.Add(element);
        return this;
    }

    /// <summary>Greyed out and not clickable.</summary>
    public Row Disabled(bool disabled = true)
    {
        IsEnabled = !disabled;
        Opacity = disabled ? 0.5 : 1;
        // A disabled element shows no tooltip of its own, and this one says why it is disabled.
        ToolTipService.SetShowOnDisabled(this, true);
        return this;
    }
}

/// <summary>See <see cref="Ui.Action"/>. The label and icon can change while it is on screen.</summary>
internal sealed class ActionButton : Border
{
    private readonly TextBlock _label;
    private readonly TextBlock? _icon;

    internal ActionButton(Material m, string text, Action onClick, Tone tone, string? glyph, bool wide)
    {
        var (fill, ink) = tone switch
        {
            Tone.Primary => (m.Ink, m.OnInk),
            // Solid red only on the wide button of a confirmation: the press that cannot be undone.
            Tone.Destructive => wide ? (m.Red, m.OnRed) : (m.RedSoft, m.Red),
            _ => (wide ? m.Group : m.Inset, m.Text),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (glyph != null)
        {
            _icon = m.Glyph(glyph, 12.5, ink);
            _icon.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(_icon);
        }
        _label = new TextBlock { Text = text, FontSize = wide ? 13 : 12, FontWeight = FontWeights.SemiBold, Foreground = ink };
        row.Children.Add(_label);
        Child = row;
        Padding = wide ? new Thickness(12, 8, 12, 9) : new Thickness(10, 4, 10, 5);
        CornerRadius = new CornerRadius(wide ? 8 : m.ControlRadius);
        VerticalAlignment = VerticalAlignment.Center;
        if (wide && tone == Tone.Plain)
        {
            BorderBrush = m.GroupRim;
            BorderThickness = new Thickness(1);
            Padding = new Thickness(11, 7, 11, 8);
        }
        m.MakePressable(this, onClick, fill);
    }

    public string Text
    {
        set => _label.Text = value;
    }

    public string Glyph
    {
        set
        {
            if (_icon != null) _icon.Text = value;
        }
    }
}

/// <summary>A fader: a thin track, an orange fill up to the level, a small upright knob, and the percentage.</summary>
internal sealed class Fader : Grid
{
    private const double TrackHeight = 2, KnobWidth = 8;

    private readonly Grid _track = new() { Height = 22, Background = Brushes.Transparent, Cursor = Cursors.Hand };
    private readonly Border _fill;
    private readonly Border _knob;
    private readonly TextBlock _readout;
    private readonly Action<float> _changed;
    private float _value;
    private string? _label;

    internal Fader(float value, Action<float> changed)
    {
        var m = Ui.M;
        _value = value;
        _changed = changed;
        VerticalAlignment = VerticalAlignment.Center;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        _fill = new Border { Height = TrackHeight, Background = m.Accent, HorizontalAlignment = HorizontalAlignment.Left };
        _knob = new Border { Width = KnobWidth, Height = 16, CornerRadius = new CornerRadius(2), Background = m.Text, HorizontalAlignment = HorizontalAlignment.Left };
        _track.Children.Add(new Border { Height = TrackHeight, Background = m.Track });
        _track.Children.Add(_fill);
        _track.Children.Add(_knob);
        Children.Add(_track);
        _readout = Ui.Value("");
        _readout.HorizontalAlignment = HorizontalAlignment.Right;
        SetColumn(_readout, 1);
        Children.Add(_readout);

        _track.SizeChanged += (_, _) => Draw();
        _track.MouseLeftButtonDown += (_, e) =>
        {
            _track.CaptureMouse();
            FromMouse(e);
            e.Handled = true;
        };
        _track.MouseMove += (_, e) =>
        {
            if (_track.IsMouseCaptured)
                FromMouse(e);
        };
        _track.MouseLeftButtonUp += (_, e) =>
        {
            _track.ReleaseMouseCapture();
            e.Handled = true;
        };
        _track.MouseWheel += (_, e) =>
        {
            Set(_value + Math.Sign(e.Delta) * 0.02f, notify: true);
            e.Handled = true;
        };
        Draw();
    }

    public bool IsDragging => _track.IsMouseCaptured;

    /// <summary>The level, 0 to 1. Setting it while the user drags is ignored.</summary>
    public float Value
    {
        get => _value;
        set
        {
            if (!IsDragging)
                Set(value, notify: false);
        }
    }

    /// <summary>Shown in place of the percentage ("Tắt" while muted); null for the percentage.</summary>
    public string? Label
    {
        set
        {
            _label = value;
            Draw();
        }
    }

    private void FromMouse(MouseEventArgs e)
    {
        var usable = Math.Max(1, _track.ActualWidth - KnobWidth);
        Set((float)((e.GetPosition(_track).X - KnobWidth / 2) / usable), notify: true);
    }

    private void Set(float value, bool notify)
    {
        _value = Math.Clamp(value, 0f, 1f);
        Draw();
        if (!notify)
            return;
        try
        {
            _changed(_value);
        }
        catch (Exception ex)
        {
            Store.Log("slider failed: " + ex.Message);
        }
    }

    private void Draw()
    {
        var usable = Math.Max(0, _track.ActualWidth - KnobWidth);
        _knob.Margin = new Thickness(usable * _value, 0, 0, 0);
        _fill.Width = usable * _value + KnobWidth / 2;
        _readout.Text = _label ?? $"{Math.Round(_value * 100)}%";
    }
}
