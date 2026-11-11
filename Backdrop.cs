using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace LumenLite;

/// <summary>
/// Windows 11's own blurred backdrop behind a borderless window. The desktop compositor draws the blur, the
/// rounded corners and the shadow on the GPU; WPF only draws what sits on top, so there is no blur effect to pay
/// for. Where that is not possible the window falls back to an opaque sheet.
/// </summary>
internal static class Backdrop
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2, DWMSBT_TRANSIENTWINDOW = 3;

    /// <summary>Windows 11 22H2: the first build with system backdrops for any window.</summary>
    private const int FirstBuild = 22621;

    private static bool _failed;

    /// <summary>Developer aid ("--no-glass"): draw the opaque fallback, as on Windows 10.</summary>
    public static bool Off;

    /// <summary>
    /// Whether windows get the backdrop: Windows 11 22H2 or later, drawing on the GPU, and no earlier attempt has
    /// failed. The "save memory" setting draws on the CPU, where WPF cannot hand the compositor a see-through
    /// window cheaply; it counts both as saved and as this run was started, since it only takes effect on restart.
    /// </summary>
    public static bool Available => !Off && !_failed && Environment.OSVersion.Version.Build >= FirstBuild
        && !Store.S.SoftwareRendering && RenderOptions.ProcessRenderMode != RenderMode.SoftwareOnly && RenderCapability.Tier >> 16 > 0;

    /// <summary>
    /// Makes <paramref name="window"/> borderless and gives it the backdrop; call it before the window is shown.
    /// The window's handle exists afterwards. Returns whether the backdrop is on. When it is not, the window is
    /// still right: per-pixel transparent on systems without backdrops, so the sheet inside can round its own
    /// corners, or a plain opaque rectangle if Windows refused the backdrop just now.
    /// </summary>
    /// <param name="resizeBorder">Width of the invisible edge that resizes the window; 0 for a fixed window.</param>
    public static bool Apply(Window window, bool dark, double resizeBorder = 0)
    {
        window.WindowStyle = WindowStyle.None;
        window.Background = Brushes.Transparent;
        if (!Available)
        {
            window.AllowsTransparency = true;
            new WindowInteropHelper(window).EnsureHandle();
            return false;
        }

        window.AllowsTransparency = false;
        WindowChrome.SetWindowChrome(window, Chrome(-1, resizeBorder));
        try
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is not { } target)
                throw new InvalidOperationException("no composition target");
            // WPF otherwise clears the window to black before drawing, which would cover the backdrop.
            target.BackgroundColor = Colors.Transparent;
            Set(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);
            Set(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
            var result = Set(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_TRANSIENTWINDOW);
            if (result != 0)
                throw new COMException("DwmSetWindowAttribute", result);
            return true;
        }
        catch (Exception ex)
        {
            Store.Log("backdrop failed, using the opaque sheet: " + ex.Message);
        }
        // The window now exists and can no longer be made per-pixel transparent: it stays an opaque rectangle in
        // the sheet's colour, and windows opened later take the transparent path above.
        _failed = true;
        WindowChrome.SetWindowChrome(window, Chrome(0, resizeBorder));
        window.Background = Palette.Current.Sheet;
        if (PresentationSource.FromVisual(window)?.CompositionTarget is HwndTarget opaque)
            opaque.BackgroundColor = ((SolidColorBrush)Palette.Current.Sheet).Color;
        return false;
    }

    /// <summary>
    /// Shows <paramref name="window"/> without the empty frame flashing: the compositor draws the backdrop the
    /// moment a window appears, before WPF has drawn anything or the window has moved to its place. The window is
    /// cloaked (shown, but kept off screen by the compositor) until it is placed and its first frame is drawn.
    /// </summary>
    public static void ShowCloaked(Window window, Action? place = null)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var cloaked = !window.IsVisible && Set(hwnd, DWMWA_CLOAK, 1) == 0;
        window.Show();
        window.UpdateLayout();
        place?.Invoke();
        if (!cloaked)
            return;
        // Loaded runs after the render pass that draws the first frame.
        window.Dispatcher.BeginInvoke(() => Set(hwnd, DWMWA_CLOAK, 0), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private const int DWMWA_CLOAK = 13;

    private static WindowChrome Chrome(double glass, double resizeBorder) => new()
    {
        GlassFrameThickness = new Thickness(glass),
        CaptionHeight = 0,
        ResizeBorderThickness = new Thickness(resizeBorder),
        UseAeroCaptionButtons = false,
        CornerRadius = default,
    };

    private static int Set(IntPtr hwnd, int attribute, int value) => DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
