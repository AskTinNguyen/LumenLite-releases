using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Rectangle = System.Drawing.Rectangle;
using Screen = System.Windows.Forms.Screen;

namespace LumenLite;

/// <summary>A blurred copy of one monitor's wallpaper, <see cref="Scale"/> times smaller than the monitor.</summary>
public sealed record Frost(BitmapSource Image, Rectangle Monitor, int Scale);

/// <summary>
/// Frosted glass the way Lumen draws it: rather than asking Windows to blur
/// whatever is behind the window, each widget shows the slice of a pre-blurred
/// wallpaper that sits behind it. That keeps large corners smooth and the look
/// the same whatever windows are open.
/// </summary>
internal static class Wallpaper
{
    private const int Scale = 4;
    private const double BlurCssPixels = 44;   // Lumen's default blur size
    private const double Saturation = 1.2;
    private const double WallOpacity = 0.70;   // Lumen's frost strength 62%

    private static readonly Color Base = Color.FromRgb(0x0C, 0x0A, 0x14);
    private static readonly Dictionary<(Rectangle, int), Frost?> Cache = new();
    private static string? _file;
    private static DateTime _stamp;

    public static event Action? Changed;

    public static void Start()
    {
        Check(force: true);
        // Slideshows rewrite the wallpaper file without always raising an event, so it is also polled.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => Check(force: false);
        timer.Start();
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.Desktop)
                Application.Current.Dispatcher.BeginInvoke(() => Check(force: true));
        };
        SystemEvents.DisplaySettingsChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(() => Check(force: true));
    }

    /// <summary>The frost for the monitor the window is on, or null when there is no wallpaper image.</summary>
    public static Frost? For(IntPtr hwnd, double dpiScale)
    {
        var monitor = Screen.FromHandle(hwnd).Bounds;
        var key = (monitor, (int)Math.Round(dpiScale * 100));
        if (!Cache.TryGetValue(key, out var frost))
        {
            Cache[key] = frost = Build(monitor, dpiScale);
            // The full-resolution wallpaper (tens of MB at 4K) is only needed while blurring.
            _decoded = null;
            Memory.TrimSoon();
        }
        return frost;
    }

    private static void Check(bool force)
    {
        var file = ImageFile();
        var stamp = file != null ? File.GetLastWriteTimeUtc(file) : default;
        if (!force && file == _file && stamp == _stamp)
            return;
        _file = file;
        _stamp = stamp;
        Cache.Clear();
        Changed?.Invoke();
    }

    private static string? ImageFile()
    {
        // TranscodedWallpaper is the image Windows actually shows, including the current slideshow picture.
        var transcoded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Themes\TranscodedWallpaper");
        if (File.Exists(transcoded))
            return transcoded;

        var path = new StringBuilder(260);
        Native.SystemParametersInfo(Native.SPI_GETDESKWALLPAPER, (uint)path.Capacity, path, 0);
        return File.Exists(path.ToString()) ? path.ToString() : null;
    }

    private static Frost? Build(Rectangle monitor, double dpiScale)
    {
        var file = ImageFile();
        if (file == null)
            return null;
        try
        {
            var source = Decode(file);
            int width = Math.Max(1, monitor.Width / Scale), height = Math.Max(1, monitor.Height / Scale);
            var target = Render(source, monitor, 1.0 / Scale, new Int32Rect(0, 0, width, height));
            var pixels = new byte[width * height * 4];
            target.CopyPixels(pixels, width * 4, 0);

            // CSS blur(44px) is a Gaussian with sigma 44; three box passes of radius ~sigma approximate it.
            var radius = (int)Math.Round(BlurCssPixels * dpiScale / Scale);
            for (var pass = 0; pass < 3; pass++)
                BoxBlur(pixels, width, height, radius);
            Finish(pixels);

            var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            image.Freeze();
            return new Frost(image, monitor, Scale);
        }
        catch (Exception ex)
        {
            Store.Log("wallpaper frost failed: " + ex.Message);
            return null;
        }
    }

    private static BitmapSource? _decoded;
    private static (string?, DateTime) _decodedKey;

    /// <summary>The wallpaper file decoded once per change and reused for every monitor.</summary>
    private static BitmapSource Decode(string file)
    {
        var key = (file, File.GetLastWriteTimeUtc(file));
        if (_decoded == null || _decodedKey != key)
        {
            using var stream = File.OpenRead(file);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            frame.Freeze();
            _decoded = frame;
            _decodedKey = key;
        }
        return _decoded;
    }

    /// <summary>
    /// Draws the wallpaper for a monitor shrunk by <paramref name="factor"/> and
    /// returns the <paramref name="region"/> of that drawing, in drawn pixels.
    /// </summary>
    private static RenderTargetBitmap Render(BitmapSource source, Rectangle monitor, double factor, Int32Rect region)
    {
        double width = monitor.Width * factor, height = monitor.Height * factor;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(-region.X, -region.Y));
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
            if (IsTiled())
            {
                var tile = new Rect(0, 0, source.PixelWidth * factor, source.PixelHeight * factor);
                dc.DrawRectangle(new ImageBrush(source) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = tile }, null, new Rect(0, 0, width, height));
            }
            else
            {
                dc.DrawImage(source, Placement(source.PixelWidth, source.PixelHeight, width, height, factor));
            }
        }
        var target = new RenderTargetBitmap(region.Width, region.Height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    /// <summary>Where Windows draws the picture for the current WallpaperStyle.</summary>
    private static Rect Placement(int imageWidth, int imageHeight, double width, double height, double factor)
    {
        double fitX = width / imageWidth, fitY = height / imageHeight;
        var scale = ReadDesktop("WallpaperStyle") switch
        {
            "2" => double.NaN,                 // stretch
            "6" => Math.Min(fitX, fitY),       // fit
            "0" => factor,                     // centre, actual size
            _ => Math.Max(fitX, fitY),         // fill (10); span (22) is treated the same
        };
        if (double.IsNaN(scale))
            return new Rect(0, 0, width, height);
        double w = imageWidth * scale, h = imageHeight * scale;
        return new Rect((width - w) / 2, (height - h) / 2, w, h);
    }

    private static bool IsTiled() => ReadDesktop("WallpaperStyle") == "0" && ReadDesktop("TileWallpaper") == "1";

    private static string? ReadDesktop(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        return key?.GetValue(name) as string;
    }

    /// <summary>Saturate like CSS saturate(1.2), then lay the wall at Lumen's opacity over its dark glass base.</summary>
    private static void Finish(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += 4)
        {
            double b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
            var gray = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            pixels[i] = Mix(gray + (b - gray) * Saturation, Base.B);
            pixels[i + 1] = Mix(gray + (g - gray) * Saturation, Base.G);
            pixels[i + 2] = Mix(gray + (r - gray) * Saturation, Base.R);
            pixels[i + 3] = 255;
        }
    }

    private static byte Mix(double wall, byte baseValue) =>
        (byte)Math.Clamp(Math.Clamp(wall, 0, 255) * WallOpacity + baseValue * (1 - WallOpacity), 0, 255);

    /// <summary>One horizontal and one vertical box pass over the BGR channels, clamping at the edges.</summary>
    private static void BoxBlur(byte[] pixels, int width, int height, int radius)
    {
        if (radius < 1)
            return;
        var temp = new byte[pixels.Length];
        BoxPass(pixels, temp, width, height, 4, width * 4, radius);
        BoxPass(temp, pixels, height, width, width * 4, 4, radius);
    }

    /// <summary>Blurs each line of <paramref name="lines"/> pixels that are <paramref name="step"/> bytes apart.</summary>
    private static void BoxPass(byte[] src, byte[] dst, int length, int lines, int step, int lineStep, int radius)
    {
        var divisor = radius * 2 + 1;
        for (var line = 0; line < lines; line++)
        {
            var start = line * lineStep;
            for (var channel = 0; channel < 3; channel++)
            {
                var offset = start + channel;
                var sum = 0;
                for (var i = -radius; i <= radius; i++)
                    sum += src[offset + Math.Clamp(i, 0, length - 1) * step];
                for (var i = 0; i < length; i++)
                {
                    dst[offset + i * step] = (byte)(sum / divisor);
                    sum += src[offset + Math.Min(i + radius + 1, length - 1) * step]
                         - src[offset + Math.Max(i - radius, 0) * step];
                }
            }
            for (var i = 0; i < length; i++)
                dst[start + 3 + i * step] = 255;
        }
    }
}
