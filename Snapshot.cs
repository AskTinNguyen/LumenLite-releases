using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LumenLite;

/// <summary>
/// Developer aid: "LumenLite.exe --snapshot &lt;folder&gt;" opens each screen in turn, saves it as a PNG and quits,
/// so a change to the look can be checked without clicking through the app.
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

    public static void Save(Visual visual, string folder, string name)
    {
        if (visual is not FrameworkElement element || element.ActualWidth < 1 || element.ActualHeight < 1)
            return;
        var dpi = VisualTreeHelper.GetDpi(visual);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        // A backdrop, so transparent windows read as they would on a desktop.
        var backdrop = new DrawingVisual();
        using (var dc = backdrop.RenderOpen())
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x6E, 0x76, 0x80)), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        bitmap.Render(backdrop);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(folder);
        using var file = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(file);
    }

    public static Task Settle(int ms = 700) => Task.Delay(ms);
}
