using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediaSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace LumenLite;

/// <summary>Helpers for Windows' media session: the playing app's name and the cover image.</summary>
internal static class MediaInfo
{
    internal static string AppName(MediaSession session)
    {
        var id = session.SourceAppUserModelId ?? "";
        var name = id.Split('!')[0];
        name = Path.GetFileNameWithoutExtension(name);
        return name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : "";
    }

    internal static async Task<ImageSource?> LoadThumbnailAsync(Windows.Storage.Streams.IRandomAccessStreamReference reference)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            using var source = stream.AsStreamForRead();
            var memory = new MemoryStream();
            await source.CopyToAsync(memory);
            memory.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 144;
            bitmap.StreamSource = memory;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
