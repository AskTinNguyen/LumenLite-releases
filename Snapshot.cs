using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LumenLite;

/// <summary>
/// Developer aid: "LumenLite.exe --snapshot &lt;folder&gt;" opens each screen in turn, saves it as a PNG and quits,
/// so a change to the look can be checked without clicking through the app. "--theme light|dark" picks the panel
/// theme and "--no-glass" the opaque fallback, for this run only.
/// </summary>
internal static class Snapshot
{
    public const string Flag = "--snapshot";

    public static string? Folder(string[] args)
    {
        var at = Array.IndexOf(args, Flag);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    /// <summary>"--theme light" or "--theme dark" beside --snapshot: the panel theme to draw, whatever the settings say.</summary>
    public static string? Theme(string[] args)
    {
        var at = Array.IndexOf(args, "--theme");
        return at >= 0 && at + 1 < args.Length && args[at + 1] is "light" or "dark" ? args[at + 1] : null;
    }

    /// <summary>
    /// Saves what a window draws. A bitmap of the window cannot include what Windows composes behind it, so
    /// something stands in: for a window on glass a soft light or dark gradient, about what the blurred backdrop
    /// looks like over a plain wallpaper; otherwise a mid grey, as a desktop behind a transparent window.
    /// </summary>
    public static void Save(Window window, string folder, string name, bool onGlass = false)
    {
        if (window.Content is not FrameworkElement element || element.ActualWidth < 1 || element.ActualHeight < 1)
            return;
        var dpi = VisualTreeHelper.GetDpi(element);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        Brush behind = !onGlass
            ? new SolidColorBrush(Color.FromRgb(0x6E, 0x76, 0x80))
            : Palette.Current.IsDark
                ? new LinearGradientBrush(Color.FromRgb(0x3A, 0x34, 0x30), Color.FromRgb(0x24, 0x2A, 0x33), 35)
                : new LinearGradientBrush(Color.FromRgb(0xF4, 0xEC, 0xE2), Color.FromRgb(0xD9, 0xE3, 0xF0), 35);
        var backdrop = new DrawingVisual();
        using (var dc = backdrop.RenderOpen())
            dc.DrawRectangle(behind, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        bitmap.Render(backdrop);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(folder);
        using var file = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(file);
    }

    public static Task Settle(int ms = 700) => Task.Delay(ms);
}
