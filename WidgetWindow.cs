using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace LumenLite;

/// <summary>
/// Borderless frosted-glass window that sits behind other windows, stays out
/// of the taskbar and Alt+Tab, and remembers where the user dragged it.
/// </summary>
public sealed class WidgetWindow : Window
{
    public static bool Exiting;

    /// <summary>
    /// Widgets stay fixed in place. Edit mode, toggled from the tray, is the only
    /// time they can be dragged or resized; it always starts off.
    /// </summary>
    public static bool Editing { get; private set; }
    private static event Action? EditingChanged;

    private readonly WidgetState _state;
    private readonly bool _resizable;
    private readonly GlassPanel _glass;
    private readonly Grid _editOverlay;

    public string Id { get; }

    public WidgetWindow(string id, string title, FrameworkElement content, Size size, Point defaultPosition, bool resizable)
    {
        Id = id;
        _state = Store.Widget(id);
        _resizable = resizable;

        Title = title;
        WindowStyle = WindowStyle.None;
        // Per-pixel transparency gives smooth rounded corners. Without a sizing
        // frame, resizing goes through the corner grip below.
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FontFamily = Theme.Font;
        Foreground = Theme.Text;
        MinWidth = 160;
        MinHeight = 80;

        var surface = _glass = new GlassPanel(content, new Thickness(22, 18, 22, 20));
        // In edit mode this layer covers the content, so a press anywhere drags the
        // widget instead of clicking a button or typing a note.
        _editOverlay = new Grid { Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        _editOverlay.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Stroke = Theme.EditOutline,
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            RadiusX = Theme.Radius - 3,
            RadiusY = Theme.Radius - 3,
            Margin = new Thickness(3),
            IsHitTestVisible = false,
        });
        if (resizable)
            _editOverlay.Children.Add(CreateResizeGrip());
        surface.Children.Add(_editOverlay);
        Content = surface;
        ApplyEditing();

        if (resizable)
        {
            Width = _state.Width ?? size.Width;
            Height = _state.Height ?? size.Height;
        }
        else
        {
            SizeToContent = SizeToContent.WidthAndHeight;
        }

        var position = new Point(_state.Left ?? defaultPosition.X, _state.Top ?? defaultPosition.Y);
        if (!IsOnScreen(position))
            position = defaultPosition;
        Left = position.X;
        Top = position.Y;

        SourceInitialized += (_, _) => HideFromAltTab();
        Loaded += (_, _) => { SendToBack(); UpdateFrost(); };
        Deactivated += (_, _) => SendToBack();
        LocationChanged += (_, _) => { Remember(); UpdateFrost(); };
        SizeChanged += (_, _) => { Remember(); UpdateFrost(); };
        DpiChanged += (_, _) => UpdateFrost();
        Wallpaper.Changed += UpdateFrost;
        EditingChanged += ApplyEditing;
        MouseLeftButtonDown += OnDragStart;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && Editing)
                SetEditing(false);
        };
    }

    public static void SetEditing(bool editing)
    {
        if (Editing == editing)
            return;
        Editing = editing;
        EditingChanged?.Invoke();
    }

    private void ApplyEditing() => _editOverlay.Visibility = Editing ? Visibility.Visible : Visibility.Collapsed;

    public void SetShown(bool shown)
    {
        _state.Visible = shown;
        if (shown) Show(); else Hide();
        Store.SaveSoon();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Alt+F4 on a widget hides it; it comes back from the tray menu.
        if (!Exiting)
        {
            e.Cancel = true;
            SetShown(false);
        }
        base.OnClosing(e);
    }

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (!Editing || e.ButtonState != MouseButtonState.Pressed)
            return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void Remember()
    {
        if (!IsLoaded || WindowState != WindowState.Normal)
            return;
        _state.Left = Left;
        _state.Top = Top;
        if (_resizable)
        {
            _state.Width = ActualWidth;
            _state.Height = ActualHeight;
        }
        Store.SaveSoon();
    }

    private Thumb CreateResizeGrip()
    {
        // Two short diagonal strokes in the bottom-right corner, inside the rounded edge.
        var hitArea = new FrameworkElementFactory(typeof(Border));
        hitArea.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var strokes = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        strokes.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 16,6 L 6,16 M 16,11 L 11,16"));
        strokes.SetValue(System.Windows.Shapes.Shape.StrokeProperty, Theme.EditOutline);
        strokes.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.5);
        strokes.SetValue(System.Windows.Shapes.Shape.StrokeStartLineCapProperty, PenLineCap.Round);
        strokes.SetValue(System.Windows.Shapes.Shape.StrokeEndLineCapProperty, PenLineCap.Round);
        hitArea.AppendChild(strokes);
        var grip = new Thumb
        {
            Width = 22,
            Height = 22,
            Margin = new Thickness(0, 0, 10, 10),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.SizeNWSE,
            Template = new ControlTemplate(typeof(Thumb)) { VisualTree = hitArea },
        };
        grip.DragDelta += (_, e) =>
        {
            Width = Math.Max(MinWidth, ActualWidth + e.HorizontalChange);
            Height = Math.Max(MinHeight, ActualHeight + e.VerticalChange);
        };
        return grip;
    }

    private void SendToBack()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            Native.SetWindowPos(hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    private void HideFromAltTab()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
        exStyle = (exStyle | Native.WS_EX_TOOLWINDOW) & ~(long)Native.WS_EX_APPWINDOW;
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(exStyle));
    }

    private void UpdateFrost() => _glass.UpdateFrost(this);

    private static bool IsOnScreen(Point p)
    {
        // Guards against a saved position on a monitor that is no longer connected.
        return p.X >= SystemParameters.VirtualScreenLeft - 40
            && p.Y >= SystemParameters.VirtualScreenTop - 10
            && p.X <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80
            && p.Y <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 60;
    }
}
