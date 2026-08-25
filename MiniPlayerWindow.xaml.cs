using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Mp3Player.Models;

namespace Mp3Player;

public partial class MiniPlayerWindow : Window
{
    public event Action? PlayPauseRequested;
    public event Action? NextRequested;
    public event Action? PrevRequested;
    public event Action? ExpandRequested;

    public MiniPlayerWindow()
    {
        InitializeComponent();
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 24;
        Top = wa.Bottom - Height - 24;
    }

    public void UpdateSong(SongItem song)
    {
        TitleText.Text = song.DisplayTitle;
        ArtistText.Text = song.DisplayArtist;
        CoverImage.Source = song.Cover;
    }

    public void Sync(bool isPlaying, TimeSpan position, TimeSpan length)
    {
        PlayIcon.Visibility = isPlaying ? Visibility.Collapsed : Visibility.Visible;
        PauseIcon.Visibility = isPlaying ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => PlayPauseRequested?.Invoke();
    private void Next_Click(object sender, RoutedEventArgs e) => NextRequested?.Invoke();
    private void Prev_Click(object sender, RoutedEventArgs e) => PrevRequested?.Invoke();
    private void Expand_Click(object sender, RoutedEventArgs e) => ExpandRequested?.Invoke();

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button) return;
        try { DragMove(); } catch { }
    }
}
