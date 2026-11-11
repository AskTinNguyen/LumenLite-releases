using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LumenLite;

/// <summary>The TD logo, loaded once from the app's resources.</summary>
internal static class Branding
{
    private static ImageSource? _logo, _icon;

    public static ImageSource Logo => _logo ??= Load("Assets/logo.png");

    public static ImageSource Icon => _icon ??= Load("Assets/LumenLite.ico");

    private static ImageSource Load(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri("pack://application:,,,/" + path, UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
