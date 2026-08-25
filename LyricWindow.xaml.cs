using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mp3Player.Services;

namespace Mp3Player;

public partial class LyricWindow : Window
{
    private List<LyricLine> _lyrics = new();
    private int _currentIndex = -1;

    public LyricWindow()
    {
        InitializeComponent();
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Bottom - Height - 40;
    }

    public void UpdateSong(string title, List<LyricLine> lyrics)
    {
        SongTitleText.Text = string.IsNullOrWhiteSpace(title) ? "未知歌曲" : title;
        _lyrics = lyrics;
        _currentIndex = -1;
        CurrentText.Text = "♪";
        NextText.Text = _lyrics.Count > 0 ? "" : "暂无歌词";
    }

    public void UpdatePosition(TimeSpan pos, int index)
    {
        if (index == _currentIndex || _lyrics.Count == 0) return;
        _currentIndex = index;

        if (index >= 0 && index < _lyrics.Count)
        {
            var line = _lyrics[index];
            CurrentText.Text = string.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text;
            CurrentText.Foreground = Brushes.White;
            CurrentText.FontSize = 22;
            CurrentText.FontWeight = FontWeights.SemiBold;
            if (index + 1 < _lyrics.Count)
            {
                var next = _lyrics[index + 1];
                NextText.Text = string.IsNullOrWhiteSpace(next.Text) ? "♪" : next.Text;
                NextText.Visibility = Visibility.Visible;
            }
            else
            {
                NextText.Text = "—— 完 ——";
                NextText.Visibility = Visibility.Visible;
            }
        }
        else
        {
            CurrentText.Text = "♪";
            CurrentText.FontSize = 16;
            CurrentText.FontWeight = FontWeights.Normal;
            CurrentText.Foreground = new SolidColorBrush(Color.FromRgb(200, 208, 228));
            NextText.Text = "";
            NextText.Visibility = Visibility.Collapsed;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button) return;
        try { DragMove(); } catch { }
    }
}
