using System.ComponentModel;
using System.Windows.Media;

namespace Mp3Player.Models;

public class SongItem : INotifyPropertyChanged
{
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public ImageSource? Cover { get; set; }
    public int Number { get; set; }
    public string Format { get; set; } = "";
    public int SampleRate { get; set; }
    public int Bitrate { get; set; }

    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying != value)
            {
                _isPlaying = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaying)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? System.IO.Path.GetFileNameWithoutExtension(Path) : Title;
    public string DisplayArtist => string.IsNullOrWhiteSpace(Artist) ? "未知歌手" : Artist;
    public string DurationText => Duration <= TimeSpan.Zero ? "--:--" : $"{Duration.Minutes:00}:{Duration.Seconds:00}";
}
