using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mp3Player.Services;

public static class TagReader
{
    public static (string Title, string Artist, string Album, TimeSpan Duration, byte[]? Cover, string Format, int SampleRate, int Bitrate) Read(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var tag = file.Tag;
            string title = string.IsNullOrWhiteSpace(tag.Title) ? System.IO.Path.GetFileNameWithoutExtension(path) : tag.Title;
            string artist = string.IsNullOrWhiteSpace(tag.FirstPerformer) ? "未知歌手" : tag.FirstPerformer;
            string album = string.IsNullOrWhiteSpace(tag.Album) ? "未知专辑" : tag.Album;
            var duration = file.Properties?.Duration ?? TimeSpan.Zero;
            byte[]? cover = null;
            var pics = tag.Pictures;
            if (pics != null && pics.Length > 0 && pics[0].Data?.Data is { Length: > 0 } d)
                cover = d;
            string format = (System.IO.Path.GetExtension(path) ?? ".mp3").TrimStart('.').ToUpperInvariant();
            int sampleRate = file.Properties?.AudioSampleRate ?? 0;
            int bitrate = file.Properties?.AudioBitrate ?? 0;
            return (title, artist, album, duration, cover, format, sampleRate, bitrate);
        }
        catch
        {
            string format = (System.IO.Path.GetExtension(path) ?? ".mp3").TrimStart('.').ToUpperInvariant();
            return (System.IO.Path.GetFileNameWithoutExtension(path), "未知歌手", "未知专辑", TimeSpan.Zero, null, format, 0, 0);
        }
    }

    public static ImageSource? CoverToImage(byte[]? data)
    {
        if (data == null || data.Length == 0) return null;
        try
        {
            using var ms = new MemoryStream(data);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
