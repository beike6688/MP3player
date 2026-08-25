using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Shapes;
using Mp3Player.Models;
using Mp3Player.Services;
using NAudio.Dsp;
using WinForms = System.Windows.Forms;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;
using Path = System.IO.Path;

namespace Mp3Player;

public partial class MainWindow : Window
{
    private const int WM_NCHITTEST = 0x84;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCLIENT = 1;
    private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
    private const int RESIZE_EDGE = 7;
    private const int HTCAPTION = 2;
    private const uint AW_HIDE = 0x00010000;
    private const uint AW_ACTIVATE = 0x00020000;
    private const uint AW_BLEND = 0x00080000;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool AnimateWindow(IntPtr hWnd, int dwTime, uint dwFlags);

    private IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    private const string PathRepeat = "M7,7 H17 V10 L21,6 L17,2 V5 H5 V11 H7 Z M17,17 H7 V14 L3,18 L7,22 V19 H19 V13 H17 Z";
    private const string PathRepeatOne = "M7,7 H17 V10 L21,6 L17,2 V5 H5 V11 H7 Z M17,17 H7 V14 L3,18 L7,22 V19 H19 V13 H17 Z";
    private const string PathShuffle = "M10.59,9.17 L5.41,4 L4,5.41 L9.17,10.58 L10.59,9.17 Z M14.5,4 L17,4 L17,6.5 L19,4.5 L21,6.5 L17,10.5 L15,8.5 L15,9.5 L14.5,9.5 L14.5,4 Z M14.83,13.83 L19.17,18.17 L17,20.5 L21,20.5 L21,18.5 L19,18.5 L17.83,17.34 L17.83,17.34 C17.83,17.34 14.83,14.83 14.83,14.83 L13.42,16.24 C13.42,16.24 16.24,18.5 16.24,18.5 Z M4,18.5 L6,18.5 L10.5,14 L12,15.5 L6.5,21 L4,21 L4,18.5 Z M4,6.5 L6,6.5 L10.5,11 L9,12.5 L4,7.5 L4,6.5 Z";

    private readonly PlayerService _player = new();
    private readonly ObservableCollection<SongItem> _songs = new();
    private readonly List<LyricLine> _lyrics = new();
    private readonly DispatcherTimer _timer;
    private readonly Random _rng = new();

    private const int VisBarCount = 64;
    private const int FftSize = 2048;
    private readonly Complex[] _fftBuffer = new Complex[FftSize];
    private readonly float[] _sampleBuffer = new float[FftSize];
    private readonly float[] _spectrum = new float[FftSize / 2];
    private System.Windows.Shapes.Rectangle[] _visBars = Array.Empty<System.Windows.Shapes.Rectangle>();
    private System.Windows.Shapes.Rectangle[] _visReflectionBars = Array.Empty<System.Windows.Shapes.Rectangle>();
    private float[] _visLevels = Array.Empty<float>();
    private float[] _visTargets = Array.Empty<float>();

    private bool _settingsDirty;
    private DateTime _lastAutoSave = DateTime.Now;
    private bool _toneArmDown;
    private double _restorePosition = -1;
    private double _specTime2;

    private HotkeyService? _hotkeys;
    private WinForms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _appIcon;
    private AppSettings _settings = new();

    private int _currentIndex = -1;
    private int _playMode;
    private bool _seeking;
    private bool _closing;
    private bool _isMaximized;
    private Rect _normalBounds;
    private MiniPlayerWindow? _miniWindow;
    private LyricWindow? _lyricWindow;
    private readonly List<string>? _startFiles;

    public MainWindow(List<string>? startFiles = null)
    {
        InitializeComponent();
        RootBorder.SizeChanged += (_, _) =>
        {
            if (RootClipGeometry != null)
                RootClipGeometry.Rect = new Rect(0, 0, RootBorder.ActualWidth, RootBorder.ActualHeight);
            UpdateFramePath();
            PositionStars();
        };
        _startFiles = startFiles;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += Timer_Tick;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewMouseDown += Window_PreviewMouseDown;
        StateChanged += MainWindow_StateChanged;
        SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WndProc);
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsService.Load();
        _player.PlaybackCompleted += OnPlaybackCompleted;

        ApplyWindowBounds();
        BuildVisualizer();
        BuildEqSliders();
        InitTray();
        InitHotkeys();
        BuildPlaylistContextMenu();

        VolumeSlider.Value = _settings.Volume;
        VolumeBar.Value = _settings.Volume;
        _player.Volume = _settings.Volume;
        _playMode = _settings.PlayMode % 3;
        UpdateModeUI();
        PlaylistBox.ItemsSource = _songs;
        FadeInWindow();

        _restorePosition = _startFiles is { Count: > 0 } ? -1 : _settings.LastPosition;
        var files = _settings.Playlist.Where(File.Exists).ToList();
        if (files.Count > 0)
        {
            AddFiles(files, _settings.LastIndex >= 0 && _settings.LastIndex < files.Count ? _settings.LastIndex : 0, autoPlay: false);
        }

        if (_startFiles is { Count: > 0 })
        {
            AddFiles(_startFiles, autoPlay: true);
        }
        else if (files.Count == 0)
        {
            UpdateEmptyState();
        }

        UpdateSeekUiFromPlayer();
        BuildStars();
        StartHaloBreath();

        _timer.Start();
    }

    private void UpdateFramePath()
    {
        double w = RootBorder.ActualWidth;
        double h = RootBorder.ActualHeight;
        if (w <= 0 || h <= 0 || FramePath == null) return;
        double r = 8;
        double i = 0.5;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(i + r, i), false, true);
            ctx.LineTo(new Point(w - i - r, i), true, false);
            ctx.ArcTo(new Point(w - i, i + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(w - i, h - i - r), true, false);
            ctx.ArcTo(new Point(w - i - r, h - i), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(i + r, h - i), true, false);
            ctx.ArcTo(new Point(i, h - i - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(i, i + r), true, false);
            ctx.ArcTo(new Point(i + r, i), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        FramePath.Data = geo;
    }

    private Ellipse[] _stars = Array.Empty<Ellipse>();
    private const int StarCount = 100;

    private void StartHaloBreath()
    {
        if (CdHalo == null) return;
        var breath = new DoubleAnimation(0.6, 1.0, TimeSpan.FromSeconds(2.2))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        CdHalo.BeginAnimation(OpacityProperty, breath);

        CdHalo.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(0.965, 0.965);
        CdHalo.RenderTransform = scale;
        var sc = new DoubleAnimation(0.965, 1.0, TimeSpan.FromSeconds(2.2))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, sc);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, sc);

        if (CdHalo.Effect is DropShadowEffect dse)
        {
            var glow = new DoubleAnimation(0.4, 0.9, TimeSpan.FromSeconds(2.2))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            dse.BeginAnimation(DropShadowEffect.OpacityProperty, glow);
            var blur = new DoubleAnimation(20, 32, TimeSpan.FromSeconds(2.2))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            dse.BeginAnimation(DropShadowEffect.BlurRadiusProperty, blur);
        }
    }

    private void BuildStars()
    {
        if (StarCanvas == null) return;
        StarCanvas.Children.Clear();
        _stars = new Ellipse[StarCount];
        for (int i = 0; i < StarCount; i++)
        {
            var starBrush = new RadialGradientBrush();
            starBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0));
            starBrush.GradientStops.Add(new GradientStop(Color.FromArgb(180, 220, 235, 255), 0.35));
            starBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 200, 220, 255), 1));
            var star = new Ellipse
            {
                Width = 2.5 + _rng.NextDouble() * 3.5,
                Height = 2.5 + _rng.NextDouble() * 3.5,
                Fill = starBrush,
                IsHitTestVisible = false
            };
            if (_rng.NextDouble() < 0.55)
            {
                star.Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(140, 170, 255),
                    BlurRadius = 7,
                    ShadowDepth = 0,
                    Opacity = 0.9
                };
            }
            StarCanvas.Children.Add(star);
            _stars[i] = star;
            var op = new DoubleAnimation(0.12, 1.0, TimeSpan.FromSeconds(2 + _rng.NextDouble() * 4))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(_rng.NextDouble() * 3)
            };
            star.BeginAnimation(OpacityProperty, op);
        }
        PositionStars();
    }

    private void PositionStars()
    {
        double w = StarCanvas.ActualWidth;
        double h = StarCanvas.ActualHeight;
        if (w <= 0 || _stars.Length == 0) return;
        for (int i = 0; i < _stars.Length; i++)
        {
            Canvas.SetLeft(_stars[i], _rng.NextDouble() * w);
            Canvas.SetTop(_stars[i], _rng.NextDouble() * h);
        }
    }

    private void ApplyWindowBounds()
    {
        if (double.IsNaN(_settings.WindowLeft)) return;
        var wa = SystemParameters.WorkArea;
        double w = Math.Clamp(_settings.WindowWidth, MinWidth, wa.Width);
        double h = Math.Clamp(_settings.WindowHeight, MinHeight, wa.Height);
        double l = Math.Clamp(_settings.WindowLeft, wa.Left - w + 60, wa.Right - 60);
        double t = Math.Clamp(_settings.WindowTop, wa.Top, wa.Bottom - 40);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = l; Top = t; Width = w; Height = h;
        _normalBounds = new Rect(l, t, w, h);
    }

    #region 托盘与图标

    private void InitTray()
    {
        try
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream;
            _appIcon = new System.Drawing.Icon(stream, 32, 32);
        }
        catch
        {
            _appIcon = CreateAppIcon();
        }


        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = _appIcon,
            Text = "炫音播放器",
            Visible = true
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("播放 / 暂停", null, (_, _) => Dispatcher.BeginInvoke(PlayPause));
        menu.Items.Add("上一曲", null, (_, _) => Dispatcher.BeginInvoke(Prev));
        menu.Items.Add("下一曲", null, (_, _) => Dispatcher.BeginInvoke(Next));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("显示主窗口", null, (_, _) => Dispatcher.BeginInvoke(ShowMainWindow));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.BeginInvoke(() =>
        {
            _closing = true;
            Application.Current.Shutdown();
        }));
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
            {
                if (!IsVisible || WindowState == WindowState.Minimized)
                    Dispatcher.BeginInvoke(ShowMainWindow);
                else if (!IsActive)
                    Dispatcher.BeginInvoke(Activate);
            }
        };
    }

    private static System.Drawing.Icon CreateAppIcon()
    {
        using var bmp = new System.Drawing.Bitmap(64, 64);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                new System.Drawing.Point(0, 0), new System.Drawing.Point(64, 64),
                System.Drawing.Color.FromArgb(78, 140, 255), System.Drawing.Color.FromArgb(155, 107, 255));
            g.FillEllipse(brush, 1, 1, 62, 62);
            using var white = new System.Drawing.SolidBrush(System.Drawing.Color.White);
            g.FillPolygon(white, new[]
            {
                new System.Drawing.PointF(25f, 16f), new System.Drawing.PointF(25f, 48f), new System.Drawing.PointF(47f, 32f)
            });
        }
        IntPtr h = bmp.GetHicon();
        return System.Drawing.Icon.FromHandle(h);
    }

    private void ShowMainWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void HideToTray()
    {
        FadeOutThen(() =>
        {
            Hide();
            _trayIcon?.ShowBalloonTip(1500, "炫音播放器", "已最小化到系统托盘，播放继续。", WinForms.ToolTipIcon.Info);
        });
    }

    #endregion

    #region 全局快捷键

    private void InitHotkeys()
    {
        _hotkeys = new HotkeyService();
        _hotkeys.Pressed += action => Dispatcher.BeginInvoke(() =>
        {
            switch (action)
            {
                case HotkeyAction.PlayPause: PlayPause(); break;
                case HotkeyAction.Next: Next(); break;
                case HotkeyAction.Prev: Prev(); break;
            }
        });
        _hotkeys.Register(this);
    }

    #endregion

    #region 播放列表

    private void AddSongs_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "添加歌曲",
            Filter = "音频文件|*.mp3;*.wav;*.m4a;*.flac|MP3 歌曲|*.mp3|所有文件|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog(this) == true)
            AddFiles(dlg.FileNames);
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "添加歌曲文件夹"
        };
        if (dlg.ShowDialog(this) != true) return;
        var folder = dlg.FolderName;
        var files = await Task.Run(() =>
            Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(IsAudioFile)
                .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase)
                .ToList());
        if (files.Count == 0)
        {
            MessageBox.Show(this, "该文件夹下没有找到音频文件。", "炫音播放器",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        AddFiles(files);
    }

    private async void AddFiles(IEnumerable<string> paths, int playIndex = -1, bool autoPlay = true)
    {
        var added = new List<SongItem>();
        var existing = new HashSet<string>(_songs.Select(s => s.Path), StringComparer.OrdinalIgnoreCase);
        var list = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var p in list)
        {
            if (existing.Contains(p)) continue;
            existing.Add(p);
            var info = await Task.Run(() => TagReader.Read(p));
            var cover = TagReader.CoverToImage(info.Cover);
            added.Add(new SongItem
            {
                Path = p,
                Title = info.Title,
                Artist = info.Artist,
                Album = info.Album,
                Duration = info.Duration,
                Cover = cover,
                Number = _songs.Count + added.Count + 1,
                Format = info.Format,
                SampleRate = info.SampleRate,
                Bitrate = info.Bitrate
            });
        }

        if (added.Count == 0)
        {
            if (autoPlay && _currentIndex < 0)
            {
                foreach (var p in list)
                {
                    int idx = -1;
                    for (int i = 0; i < _songs.Count; i++)
                    {
                        if (string.Equals(_songs[i].Path, p, StringComparison.OrdinalIgnoreCase))
                        {
                            idx = i;
                            break;
                        }
                    }
                    if (idx >= 0)
                    {
                        PlaySong(idx);
                        break;
                    }
                }
            }
            return;
        }
        foreach (var s in added) _songs.Add(s);
        UpdatePlaylistHeader();
        _settingsDirty = true;
        SaveSettings();

        if (playIndex >= 0 && playIndex < _songs.Count)
            PlaySong(playIndex, autoPlay);
        else if (_currentIndex < 0)
            PlaySong(0, autoPlay);

        if (_restorePosition > 3 && _currentIndex >= 0)
        {
            try
            {
                _player.Play(_songs[_currentIndex].Path);
                _player.Pause();
                var pos = TimeSpan.FromSeconds(_restorePosition);
                if (pos < _player.Length - TimeSpan.FromSeconds(2))
                    _player.Seek(pos);
                _settingsDirty = true;
                SaveSettings();
            }
            catch
            {
            }
            _restorePosition = -1;
        }
    }

    private void UpdatePlaylistHeader() => PlaylistCountText.Text = $"播放列表 ({_songs.Count})";

    private void UpdateEmptyState()
    {
        TitleText.Text = "未选择歌曲";
        TopSongText.Text = "未播放";
        UpdatePlaylistHeader();
    }

    private void BuildPlaylistContextMenu()
    {
        var menu = new ContextMenu();
        var play = new MenuItem { Header = "播放" };
        play.Click += (_, _) => { if (PlaylistBox.SelectedItem is SongItem s) PlaySong(_songs.IndexOf(s)); };
        var remove = new MenuItem { Header = "从列表移除" };
        remove.Click += (_, _) => RemoveSelectedSong();
        var openFolder = new MenuItem { Header = "打开文件位置" };
        openFolder.Click += (_, _) =>
        {
            if (PlaylistBox.SelectedItem is SongItem s && File.Exists(s.Path))
            {
                var arg = $"/select,\"{s.Path}\"";
                System.Diagnostics.Process.Start("explorer.exe", arg);
            }
        };
        menu.Items.Add(play);
        menu.Items.Add(remove);
        menu.Items.Add(openFolder);
        PlaylistBox.ContextMenu = menu;
    }

    private void RemoveSelectedSong()
    {
        if (PlaylistBox.SelectedItem is not SongItem song) return;
        int idx = _songs.IndexOf(song);
        _songs.Remove(song);
        UpdatePlaylistHeader();
        if (idx == _currentIndex)
        {
            _player.Stop();
            _currentIndex = -1;
            StopCdAnimation();
            UpdatePlaybackButtons();
            UpdateEmptyState();
        }
        else if (idx < _currentIndex)
        {
            _currentIndex--;
        }
        for (int i = 0; i < _songs.Count; i++)
            _songs[i].Number = i + 1;
        _settingsDirty = true;
        SaveSettings();
    }

    private void PlaylistBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PlaylistBox.SelectedItem is SongItem s)
            PlaySong(_songs.IndexOf(s));
    }

    private void PlaylistBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && PlaylistBox.SelectedItem != null)
            RemoveSelectedSong();
    }

    private void PlaylistBox_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void PlaylistBox_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var songs = files.Where(f => File.Exists(f) && IsAudioFile(f)).ToList();
        if (songs.Count > 0) AddFiles(songs);
    }

    private static bool IsAudioFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp3" or ".wav" or ".m4a" or ".flac" or ".aac" or ".wma" or ".ogg";
    }

    #endregion

    #region 播放核心

    private void PlaySong(int index, bool autoPlay = true)
    {
        if (_songs.Count == 0) return;
        index = Math.Clamp(index, 0, _songs.Count - 1);
        var song = _songs[index];
        _currentIndex = index;
        _settingsDirty = true;
        SaveSettings();

        for (int i = 0; i < _songs.Count; i++)
            _songs[i].IsPlaying = i == index;

        TitleText.Text = song.DisplayTitle;
        TopSongText.Text = song.DisplayTitle;
        CdImageBrush.ImageSource = song.Cover ?? CreateDefaultCover();
        BgCoverImage.Source = song.Cover;

        if (song.SampleRate > 0)
        {
            FormatTagText1.Text = song.Format;
            FormatTagText2.Text = $"{song.SampleRate / 1000.0:0.#}kHz";
            bool hasBitrate = song.Bitrate > 0;
            FormatTag3.Visibility = hasBitrate ? Visibility.Visible : Visibility.Collapsed;
            FormatTagText3.Text = hasBitrate ? $"{song.Bitrate} kbps" : "";
            FormatTagsPanel.Visibility = Visibility.Visible;
        }
        else
        {
            FormatTagsPanel.Visibility = Visibility.Collapsed;
        }

        LoadLyricsData(song.Path);
        PlaylistBox.ScrollIntoView(song);

        if (autoPlay)
        {
            try
            {
                _player.Play(song.Path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"无法播放该文件：\n{ex.Message}", "炫音播放器",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        UpdatePlaybackButtons();
        _miniWindow?.UpdateSong(song);
        _lyricWindow?.UpdateSong(song.DisplayTitle, _lyrics);
    }

    private void PlayPause()
    {
        if (_songs.Count == 0) return;
        if (!_player.HasTrack)
        {
            PlaySong(_currentIndex >= 0 ? _currentIndex : 0);
            return;
        }
        if (_player.Position >= _player.Length - TimeSpan.FromMilliseconds(600))
        {
            PlaySong(_currentIndex >= 0 ? _currentIndex : 0);
            return;
        }
        _player.TogglePlayPause();
        UpdatePlaybackButtons();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => PlayPause();

    private void Prev_Click(object sender, RoutedEventArgs e) => Prev();

    private void Next_Click(object sender, RoutedEventArgs e) => Next();

    private void Prev()
    {
        if (_songs.Count == 0) return;
        int idx = _playMode == 2
            ? (_songs.Count > 1 ? NextRandom() : 0)
            : (_currentIndex <= 0 ? _songs.Count - 1 : _currentIndex - 1);
        PlaySong(idx);
    }

    private void Next()
    {
        if (_songs.Count == 0) return;
        int idx = _playMode == 2
            ? (_songs.Count > 1 ? NextRandom() : 0)
            : (_currentIndex >= _songs.Count - 1 ? 0 : _currentIndex + 1);
        PlaySong(idx);
    }

    private int NextRandom()
    {
        int next;
        do { next = _rng.Next(_songs.Count); } while (_songs.Count > 1 && next == _currentIndex);
        return next;
    }

    private void OnPlaybackCompleted(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_playMode == 1 && _songs.Count > 0)
            {
                try { _player.Play(_songs[_currentIndex].Path); } catch { }
                UpdatePlaybackButtons();
                return;
            }
            if (_playMode == 2)
            {
                if (_songs.Count > 0) PlaySong(NextRandom());
                return;
            }
            if (_currentIndex < _songs.Count - 1)
                PlaySong(_currentIndex + 1);
            else
                UpdatePlaybackButtons();
        });
    }

    private void UpdatePlaybackButtons()
    {
        bool playing = _player.IsPlaying;
        PlayIcon.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        PauseIcon.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;

        if (playing) StartCdAnimation();
        else StopCdAnimation();
        UpdateToneArm(playing);
        UpdatePlayButtonBreath(playing);
    }

    private void StartCdAnimation()
    {
        if (CdSpinTarget.Resources["CdSpinStoryboard"] is Storyboard sb)
            sb.Begin(CdSpinTarget, true);
    }

    private void StopCdAnimation()
    {
        if (CdSpinTarget.Resources["CdSpinStoryboard"] is Storyboard sb)
            sb.Pause(CdSpinTarget);
    }

    private void UpdateToneArm(bool playing)
    {
        if (_toneArmDown == playing) return;
        _toneArmDown = playing;
        var anim = new DoubleAnimation(
            playing ? -28 : 8,
            playing ? 8 : -28,
            TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        ToneArmRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
    }

    private void UpdatePlayButtonBreath(bool playing)
    {
        if (PlayPauseButton.RenderTransform is not ScaleTransform st)
        {
            st = new ScaleTransform(1, 1);
            PlayPauseButton.RenderTransform = st;
            PlayPauseButton.RenderTransformOrigin = new Point(0.5, 0.5);
        }
        if (playing)
        {
            var anim = new DoubleAnimation(1, 1.06, TimeSpan.FromMilliseconds(900))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }
        else
        {
            st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            st.ScaleX = 1;
            st.ScaleY = 1;
        }
    }

    private void SeekRelative(double seconds)
    {
        if (!_player.HasTrack) return;
        _player.Seek(_player.Position + TimeSpan.FromSeconds(seconds));
        _settingsDirty = true;
        SaveSettings();
    }

    private void ChangeVolume(float delta)
    {
        double v = Math.Clamp(VolumeSlider.Value + delta, 0, 1);
        VolumeSlider.Value = v;
    }

    private double _lastVolume = 0.8;

    private void VolumeIcon_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (VolumeSlider.Value > 0.001)
        {
            _lastVolume = VolumeSlider.Value;
            VolumeSlider.Value = 0;
        }
        else
        {
            VolumeSlider.Value = _lastVolume > 0.001 ? _lastVolume : 0.8;
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        VolumeBar.Value = e.NewValue;
        _player.Volume = (float)e.NewValue;
        if (VolumePercentText != null)
            VolumePercentText.Text = $"{(int)Math.Round(e.NewValue * 100)}%";
        if (VolumeIcon != null)
        {
            if (e.NewValue <= 0.001)
            {
                VolumeIcon.Data = Geometry.Parse("M4,9 V15 H8 L13,19 V5 L8,9 Z M15.5,9 L19.5,15 M19.5,9 L15.5,15");
            }
            else if (e.NewValue < 0.5)
            {
                VolumeIcon.Data = Geometry.Parse("M4,9 V15 H8 L13,19 V5 L8,9 Z M15.5,10.5 C16.6,11.9 16.6,12.1 15.5,13.5");
            }
            else
            {
                VolumeIcon.Data = Geometry.Parse("M4,9 V15 H8 L13,19 V5 L8,9 Z M15.5,7.5 C17.6,9.9 17.6,14.1 15.5,16.5 M17.8,4.8 C21.2,8.3 21.2,15.7 17.8,19.2");
            }
            VolumeIcon.StrokeThickness = 1.5;
        }
        _settingsDirty = true;
    }

    private void SeekArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _seeking = true;
        SeekArea.CaptureMouse();
        UpdateSeekFromPoint(e.GetPosition(SeekArea));
        e.Handled = true;
    }

    private void SeekArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_seeking) return;
        UpdateSeekFromPoint(e.GetPosition(SeekArea));
        e.Handled = true;
    }

    private void SeekArea_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_seeking) return;
        _seeking = false;
        SeekArea.ReleaseMouseCapture();
        ApplySeek();
        e.Handled = true;
    }

    private void UpdateSeekFromPoint(Point p)
    {
        double w = SeekArea.ActualWidth;
        if (w <= 0) return;
        double frac = Math.Clamp(p.X / w, 0, 1);
        SeekBar.Value = frac;
        UpdateSeekThumb(frac);
    }

    private void ApplySeek()
    {
        if (_player.HasTrack)
        {
            _player.Seek(TimeSpan.FromSeconds(SeekBar.Value * _player.Length.TotalSeconds));
            _settingsDirty = true;
            SaveSettings();
        }
    }

    private void UpdateSeekThumb(double frac)
    {
        double w = SeekArea.ActualWidth;
        if (w <= 0) return;
        SeekThumb.Margin = new Thickness(frac * w - 9, 0, 0, 0);
    }

    private void UpdateSeekUiFromPlayer()
    {
        if (!_player.HasTrack) return;
        double len = _player.Length.TotalSeconds;
        if (len <= 0) return;
        double frac = Math.Clamp(_player.Position.TotalSeconds / len, 0, 1);
        SeekBar.Value = frac;
        UpdateSeekThumb(frac);
        CurrentTimeText.Text = FormatTime(_player.Position);
        TotalTimeText.Text = FormatTime(_player.Length);
    }

    #endregion

    #region 播放模式 / 均衡器

    private void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        ShowModeMenu();
    }

    private void ModeChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out int mode))
            SetPlayMode(mode);
        ModeMenuPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowModeMenu()
    {
        ModeMenuPanel.Visibility = Visibility.Visible;
        ModeMenuPanel.UpdateLayout();
        double menuHeight = ModeMenuPanel.ActualHeight;
        var p = ModeButton.TranslatePoint(new Point(0, 0), this);
        ModeMenuPanel.Margin = new Thickness(p.X, Math.Max(0, p.Y - menuHeight - 6), 0, 0);
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ModeMenuPanel.Visibility != Visibility.Visible) return;
        var p = e.GetPosition(ModeMenuPanel);
        if (p.X < 0 || p.Y < 0 || p.X > ModeMenuPanel.ActualWidth || p.Y > ModeMenuPanel.ActualHeight)
            ModeMenuPanel.Visibility = Visibility.Collapsed;
    }

    private void SetPlayMode(int mode)
    {
        _playMode = mode;
        UpdateModeUI();
        _settingsDirty = true;
    }

    private void UpdateModeUI()
    {
        var accent = new SolidColorBrush(Color.FromRgb(255, 138, 179));
        var dim = (Brush)FindResource("TextSecondaryBrush");
        ModeOrderButton.Foreground = _playMode == 0 ? accent : dim;
        ModeRepeatOneButton.Foreground = _playMode == 1 ? accent : dim;
        ModeShuffleButton.Foreground = _playMode == 2 ? accent : dim;

        switch (_playMode)
        {
            case 1:
                ModeIcon.Data = Geometry.Parse(PathRepeatOne);
                ModeBadge.Visibility = Visibility.Visible;
                ModeButton.ToolTip = "播放模式：单曲循环";
                break;
            case 2:
                ModeIcon.Data = Geometry.Parse(PathShuffle);
                ModeBadge.Visibility = Visibility.Collapsed;
                ModeButton.ToolTip = "播放模式：随机播放";
                break;
            default:
                ModeIcon.Data = Geometry.Parse(PathRepeat);
                ModeBadge.Visibility = Visibility.Collapsed;
                ModeButton.ToolTip = "播放模式：顺序播放";
                break;
        }
    }

    private void BuildEqSliders()
    {
        EqSlidersGrid.Children.Clear();
        for (int i = 0; i < EqualizerSampleProvider.BandCount; i++)
        {
            var band = i;
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            var name = new TextBlock
            {
                Text = EqualizerSampleProvider.BandNames[i],
                FontSize = 11,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            const double canvasW = 96, canvasH = 28;
            var canvas = new Canvas { Width = canvasW, Height = canvasH, VerticalAlignment = VerticalAlignment.Center };

            var track = new Border
            {
                Width = canvasW - 16,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255))
            };
            Canvas.SetLeft(track, 8);
            Canvas.SetTop(track, canvasH / 2 - 2);
            canvas.Children.Add(track);

            var fill = new Rectangle
            {
                Height = 4,
                RadiusX = 2,
                RadiusY = 2,
                Fill = new LinearGradientBrush(new GradientStopCollection
                {
                    new(Color.FromRgb(0, 102, 255), 0),
                    new(Color.FromRgb(160, 32, 240), 0.5),
                    new(Color.FromRgb(255, 85, 221), 1)
                }, 0)
            };
            Canvas.SetTop(fill, canvasH / 2 - 2);
            canvas.Children.Add(fill);

            var knob = new Ellipse
            {
                Width = 15,
                Height = 15,
                Fill = Brushes.White,
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(160, 32, 240),
                    BlurRadius = 10,
                    ShadowDepth = 0,
                    Opacity = 0.9
                }
            };
            Canvas.SetTop(knob, canvasH / 2 - 7.5);
            canvas.Children.Add(knob);

            var slider = new Slider
            {
                Minimum = -12,
                Maximum = 12,
                Value = _settings.Equalizer.Length > i ? _settings.Equalizer[i] : 0,
                Width = canvasW,
                Height = canvasH,
                IsMoveToPointEnabled = true,
                Opacity = 0,
                Background = Brushes.Transparent
            };
            canvas.Children.Add(slider);

            var valueText = new TextBlock
            {
                FontSize = 9.5,
                Foreground = (Brush)FindResource("TextDimBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Text = slider.Value.ToString("+#;-#;0") + " dB"
            };

            void UpdateEqUi()
            {
                double v = slider.Value;
                double centerX = canvasW / 2;
                double half = canvasW / 2 - 8;
                double ratio = v / 12.0;
                double w = Math.Abs(ratio) * half;
                fill.Width = w;
                if (v >= 0)
                {
                    Canvas.SetLeft(fill, centerX);
                }
                else
                {
                    Canvas.SetLeft(fill, centerX - w);
                }
                Canvas.SetLeft(knob, centerX + ratio * half - 7.5);
                valueText.Text = slider.Value.ToString("+#;-#;0") + " dB";
            }

            slider.ValueChanged += (_, _) =>
            {
                _player.SetEqualizerBand(band, (float)slider.Value);
                _settingsDirty = true;
                UpdateEqUi();
            };
            _player.SetEqualizerBand(band, (float)slider.Value);
            UpdateEqUi();
            stack.Children.Add(name);
            stack.Children.Add(canvas);
            stack.Children.Add(valueText);
            EqSlidersGrid.Children.Add(stack);
        }
    }

    private void EqToggle_Click(object sender, RoutedEventArgs e)
    {
        EqPanel.Visibility = EqPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        EqToggleButton.Foreground = EqPanel.Visibility == Visibility.Visible
            ? new SolidColorBrush(Color.FromRgb(255, 138, 179))
            : (Brush)FindResource("TextSecondaryBrush");
    }

    private void ResetEq_Click(object sender, RoutedEventArgs e)
    {
        foreach (var child in EqSlidersGrid.Children)
        {
            if (child is StackPanel sp)
            {
                foreach (var c in sp.Children)
                {
                    if (c is Canvas cv)
                    {
                        foreach (var cc in cv.Children)
                        {
                            if (cc is Slider s)
                            {
                                s.Value = 0;
                                break;
                            }
                        }
                    }
                }
            }
        }
    }

    #endregion

    #region 歌词（桌面歌词）

    private void LoadLyricsData(string songPath)
    {
        _lyrics.Clear();
        var lrc = LrcParser.FindLrcFile(songPath);
        if (!string.IsNullOrEmpty(lrc))
            _lyrics.AddRange(LrcParser.Parse(lrc));
    }

    private void UpdateDesktopLyric()
    {
        if (_lyricWindow == null || _lyrics.Count == 0 || !_player.HasTrack) return;
        var pos = _player.Position;
        int idx = -1;
        for (int i = 0; i < _lyrics.Count; i++)
        {
            if (_lyrics[i].Time <= pos) idx = i;
            else break;
        }
        _lyricWindow.UpdatePosition(pos, idx);
    }

    #endregion

    #region 迷你窗口 / 桌面歌词

    private void MiniMode_Click(object sender, RoutedEventArgs e)
    {
        if (_miniWindow == null)
        {
            _miniWindow = new MiniPlayerWindow();
            _miniWindow.ExpandRequested += () =>
            {
                _miniWindow.Close();
                ShowMainWindow();
            };
            _miniWindow.Closed += (_, _) => _miniWindow = null;
            _miniWindow.Show();
            if (_currentIndex >= 0 && _currentIndex < _songs.Count)
                _miniWindow.UpdateSong(_songs[_currentIndex]);
            Hide();
        }
        else
        {
            _miniWindow.Close();
            ShowMainWindow();
        }
    }

    private void LyricToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_lyricWindow == null)
        {
            _lyricWindow = new LyricWindow();
            _lyricWindow.Closed += (_, _) =>
            {
                _lyricWindow = null;
                LyricToggleButton.Foreground = (Brush)FindResource("TextSecondaryBrush");
            };
            _lyricWindow.Show();
            if (_currentIndex >= 0 && _currentIndex < _songs.Count)
                _lyricWindow.UpdateSong(_songs[_currentIndex].DisplayTitle, _lyrics);
            LyricToggleButton.Foreground = new SolidColorBrush(Color.FromRgb(255, 138, 179));
        }
        else
        {
            _lyricWindow.Close();
            LyricToggleButton.Foreground = (Brush)FindResource("TextSecondaryBrush");
        }
    }

    #endregion

    #region 计时器与窗口交互

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_player.HasTrack)
        {
            double len = _player.Length.TotalSeconds;
            if (len > 0)
            {
                double frac = Math.Clamp(_player.Position.TotalSeconds / len, 0, 1);
                if (!_seeking)
                {
                    SeekBar.Value = frac;
                }
                UpdateSeekThumb(frac);
                CurrentTimeText.Text = FormatTime(_player.Position);
                TotalTimeText.Text = FormatTime(_player.Length);
            }
            UpdateVisualizer(_player.CurrentRms);
            UpdateDesktopLyric();
        }
        else
        {
            UpdateVisualizer(0);
        }
        _miniWindow?.Sync(_player.IsPlaying, _player.Position, _player.Length);

        if (_settingsDirty && DateTime.Now - _lastAutoSave > TimeSpan.FromSeconds(5))
        {
            _lastAutoSave = DateTime.Now;
            SaveSettings();
        }
    }

    private void BuildVisualizer()
    {
        VisGrid.Children.Clear();
        VisReflectionGrid.Children.Clear();
        _visBars = new System.Windows.Shapes.Rectangle[VisBarCount];
        _visReflectionBars = new System.Windows.Shapes.Rectangle[VisBarCount];
        _visLevels = new float[VisBarCount];
        _visTargets = new float[VisBarCount];

        for (int i = 0; i < VisBarCount; i++)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0, 102, 255), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(160, 32, 240), 0.5));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 85, 221), 1));
            var glow = new DropShadowEffect
            {
                Color = Color.FromRgb(160, 32, 240),
                BlurRadius = 7,
                ShadowDepth = 0,
                Opacity = 0.4
            };

            var bar = new System.Windows.Shapes.Rectangle
            {
                Fill = brush,
                Effect = glow,
                Width = 4,
                Height = 6,
                RadiusX = 2.5,
                RadiusY = 2.5,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _visBars[i] = bar;
            VisGrid.Children.Add(bar);

            var refl = new System.Windows.Shapes.Rectangle
            {
                Fill = brush,
                Width = 4,
                Height = 6,
                RadiusX = 2.5,
                RadiusY = 2.5,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _visReflectionBars[i] = refl;
            VisReflectionGrid.Children.Add(refl);

}
    }

    private void UpdateVisualizer(float rms)
    {
        if (_player.HasTrack && rms > 0.002f)
        {
            _player.CopyRecentSamples(_sampleBuffer);
            for (int i = 0; i < FftSize; i++)
            {
                _fftBuffer[i].X = _sampleBuffer[i];
                _fftBuffer[i].Y = 0;
            }
            FastFourierTransform.FFT(true, 11, _fftBuffer);

            int bins = FftSize / 2;
            int sampleRate = _player.SampleRate;
            double fMin = 30;
            double fMax = Math.Min(18000, sampleRate / 2.0 * 0.95);
            for (int i = 0; i < bins; i++)
            {
                float re = _fftBuffer[i].X;
                float im = _fftBuffer[i].Y;
                _spectrum[i] = MathF.Sqrt(re * re + im * im);
            }

            var raw = new float[VisBarCount];
            float total = 0f;
            for (int i = 0; i < VisBarCount; i++)
            {
                double f0 = fMin * Math.Pow(fMax / fMin, (double)i / VisBarCount);
                double f1 = fMin * Math.Pow(fMax / fMin, (double)(i + 1) / VisBarCount);
                int bin0 = Math.Max(1, (int)(f0 / sampleRate * FftSize));
                int bin1 = Math.Min(bins, (int)(f1 / sampleRate * FftSize) + 1);
                if (bin1 <= bin0) bin1 = bin0 + 1;
                float sum = 0f;
                for (int b = bin0; b < bin1; b++) sum += _spectrum[b];
                float avg = sum / (bin1 - bin0);
                float db = 20f * MathF.Log10(avg + 1e-8f);
                float norm = Math.Clamp((db + 68f) / 52f, 0f, 1f);
                norm = MathF.Pow(norm, 0.7f);
                float wobble = 0.86f + 0.14f * (float)Math.Sin(_specTime2 * 2.3 + i * 0.85);
                raw[i] = Math.Max(norm * wobble, rms * 0.12f);
                total += raw[i];
            }
            float avgAll = total / VisBarCount;
            for (int i = 0; i < VisBarCount; i++)
            {
                float balanced = raw[i] * 0.45f + avgAll * 0.55f;
                float centerW = 0.55f + 0.45f * (float)Math.Sin(Math.PI * i / (VisBarCount - 1));
                _visTargets[i] = Math.Min(1f, balanced * centerW);
            }
            _specTime2 += 0.1;
        }
        else
        {
            for (int i = 0; i < VisBarCount; i++)
                _visTargets[i] = 0f;
        }

        bool playing = _player.IsPlaying;
        for (int i = 0; i < VisBarCount; i++)
        {
            float cur = _visLevels[i];
            float next;
            if (!playing)
            {
                next = cur * 0.82f;
            }
            else if (_visTargets[i] > cur)
            {
                next = cur + (_visTargets[i] - cur) * 0.42f;
            }
            else
            {
                float release = 0.84f + 0.05f * (float)Math.Sin(i * 0.7);
                next = cur * release;
            }
            _visLevels[i] = next;
            double maxH = Math.Max(20, VisGrid.ActualHeight - 8);
            _visBars[i].Height = Math.Max(6, next * maxH);
            double reflH = Math.Max(20, VisReflectionGrid.ActualHeight - 8);
            _visReflectionBars[i].Height = Math.Max(6, next * reflH);
        }
    }

    private static string FormatTime(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            PlayPause();
            e.Handled = true;
        }
        else if (e.Key == Key.Left && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Prev();
            e.Handled = true;
        }
        else if (e.Key == Key.Right && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Next();
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            SeekRelative(-5);
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            SeekRelative(5);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            ChangeVolume(0.05f);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            ChangeVolume(-0.05f);
            e.Handled = true;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            if (e.OriginalSource is DependencyObject d2 && !IsInteractiveElement(d2))
            {
                Maximize_Click(sender, e);
                e.Handled = true;
            }
            return;
        }
        if (e.OriginalSource is DependencyObject d && IsInteractiveElement(d)) return;
        try { DragMove(); } catch { }
    }

    private static bool IsInteractiveElement(DependencyObject node)
    {
        while (node != null)
        {
            if (node is System.Windows.Controls.Primitives.ButtonBase
                or System.Windows.Controls.Primitives.Thumb
                or System.Windows.Controls.Slider
                or System.Windows.Controls.ProgressBar
                or System.Windows.Controls.Primitives.ScrollBar
                or System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.ListBoxItem
                or System.Windows.Controls.ListBox
                or System.Windows.Controls.ComboBoxItem
                or System.Windows.Controls.MenuItem
                or System.Windows.Controls.ContextMenu
                or System.Windows.Controls.CheckBox
                or System.Windows.Controls.RadioButton)
                return true;
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        WindowState = WindowState.Minimized;
    }

    private void FadeInWindow()
    {
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
    }

    private void FadeOutThen(Action after)
    {
        var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
        anim.Completed += (_, _) =>
        {
            Opacity = 1;
            after();
        };
        BeginAnimation(OpacityProperty, anim);
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState != WindowState.Maximized)
        {
            _normalBounds = new Rect(Left, Top, Width, Height);
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowState = WindowState.Normal;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => HideToTray();

        private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        _isMaximized = WindowState == WindowState.Maximized;
        if (_isMaximized)
        {
            var wa = SystemParameters.WorkArea;
            Dispatcher.BeginInvoke(() =>
            {
                Left = wa.Left;
                Top = wa.Top;
                Width = wa.Width;
                Height = wa.Height;
            });
            RootBorder.CornerRadius = new CornerRadius(0);
            RootBorder.BorderThickness = new Thickness(0);
            if (RootClipGeometry != null)
            {
                RootClipGeometry.RadiusX = 0;
                RootClipGeometry.RadiusY = 0;
                RootClipGeometry.Rect = new Rect(0, 0, RootBorder.ActualWidth, RootBorder.ActualHeight);
            }
            FramePath.Visibility = Visibility.Collapsed;
            MaximizeIcon.Data = Geometry.Parse("M5,8 H16 V19 H5 Z M8,5 H19 V16 H17 V7 H8 Z");
        }
        else
        {
            if (WindowState == WindowState.Normal)
            {
                BeginAnimation(OpacityProperty, null);
                Opacity = 1;
                var nb = _normalBounds;
                if (nb.Width > 0 && !_isMaximized)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        Left = nb.X;
                        Top = nb.Y;
                        Width = nb.Width;
                        Height = nb.Height;
                    });
                }
            }
            RootBorder.CornerRadius = new CornerRadius(8);
            RootBorder.BorderThickness = new Thickness(1);
            if (RootClipGeometry != null)
            {
                RootClipGeometry.RadiusX = 8;
                RootClipGeometry.RadiusY = 8;
                RootClipGeometry.Rect = new Rect(0, 0, RootBorder.ActualWidth, RootBorder.ActualHeight);
            }
            FramePath.Visibility = Visibility.Visible;
            UpdateFramePath();
            MaximizeIcon.Data = Geometry.Parse("M6,6 H18 V18 H6 Z M8,8 H16 V16 H8 Z");
        }
    }
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST && !_isMaximized)
        {
            long lp = lParam.ToInt64();
            int x = (short)(lp & 0xFFFF);
            int y = (short)((lp >> 16) & 0xFFFF);
            var p = PointFromScreen(new System.Windows.Point(x, y));

            double left = p.X;
            double top = p.Y;
            double right = ActualWidth - p.X;
            double bottom = ActualHeight - p.Y;

            int hit = HTCLIENT;
            if (left <= RESIZE_EDGE) hit = HTLEFT;
            if (right <= RESIZE_EDGE) hit = HTRIGHT;
            if (top <= RESIZE_EDGE) hit = HTTOP;
            if (bottom <= RESIZE_EDGE) hit = HTBOTTOM;
            if (left <= RESIZE_EDGE && top <= RESIZE_EDGE) hit = HTTOPLEFT;
            if (right <= RESIZE_EDGE && top <= RESIZE_EDGE) hit = HTTOPRIGHT;
            if (left <= RESIZE_EDGE && bottom <= RESIZE_EDGE) hit = HTBOTTOMLEFT;
            if (right <= RESIZE_EDGE && bottom <= RESIZE_EDGE) hit = HTBOTTOMRIGHT;

            if (hit != HTCLIENT)
            {
                handled = true;
                return new IntPtr(hit);
            }
        }
        return IntPtr.Zero;
    }

    private static ImageSource CreateDefaultCover()
    {
        var bmp = new RenderTargetBitmap(300, 300, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromRgb(30, 40, 70), 0),
                new(Color.FromRgb(55, 45, 95), 0.55),
                new(Color.FromRgb(80, 45, 90), 1)
            }, new Point(0, 0), new Point(1, 1));
            dc.DrawRectangle(brush, null, new Rect(0, 0, 300, 300));

            var note = new PathGeometry();
            var f = new StreamGeometry();
            using (var ctx = f.Open())
            {
                ctx.BeginFigure(new Point(120, 70), true, true);
                ctx.LineTo(new Point(120, 175), true, false);
                ctx.BezierTo(new Point(111, 168), new Point(99, 164), new Point(86, 164), true, false);
                ctx.BezierTo(new Point(64, 164), new Point(46, 182), new Point(46, 204), true, false);
                ctx.BezierTo(new Point(46, 226), new Point(64, 244), new Point(86, 244), true, false);
                ctx.BezierTo(new Point(108, 244), new Point(126, 226), new Point(126, 204), true, false);
                ctx.LineTo(new Point(126, 120), true, false);
                ctx.LineTo(new Point(196, 98), true, false);
                ctx.LineTo(new Point(196, 172), true, false);
                ctx.BezierTo(new Point(187, 165), new Point(175, 161), new Point(162, 161), true, false);
                ctx.BezierTo(new Point(140, 161), new Point(122, 179), new Point(122, 201), true, false);
                ctx.BezierTo(new Point(122, 223), new Point(140, 241), new Point(162, 241), true, false);
                ctx.BezierTo(new Point(184, 241), new Point(202, 223), new Point(202, 201), true, false);
                ctx.LineTo(new Point(202, 70), true, false);
                ctx.Close();
            }
            f.Freeze();
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), null, f);
        }
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    #endregion

    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (!_closing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _timer.Stop();
        SaveSettings();
        _hotkeys?.Dispose();
        _trayIcon?.Dispose();
        _player.Dispose();
    }

    private void SaveSettings()
    {
        if (WindowState == WindowState.Normal)
            _normalBounds = new Rect(Left, Top, Width, Height);
        _settings.Volume = (float)VolumeSlider.Value;
        _settings.PlayMode = _playMode;
        _settings.Playlist = _songs.Select(s => s.Path).ToList();
        _settings.LastIndex = _currentIndex;
        _settings.LastPosition = _player.HasTrack ? _player.Position.TotalSeconds : 0;
        _settings.WindowLeft = _normalBounds.X;
        _settings.WindowTop = _normalBounds.Y;
        _settings.WindowWidth = _normalBounds.Width;
        _settings.WindowHeight = _normalBounds.Height;
        _settings.Equalizer = new float[EqualizerSampleProvider.BandCount];
        for (int i = 0; i < EqualizerSampleProvider.BandCount; i++)
        {
            if (EqSlidersGrid.Children[i] is StackPanel sp)
            {
                foreach (var c in sp.Children)
                {
                    if (c is Canvas cv)
                    {
                        foreach (var cc in cv.Children)
                        {
                            if (cc is Slider s)
                            {
                                _settings.Equalizer[i] = (float)s.Value;
                                break;
                            }
                        }
                    }
                }
            }
        }
        SettingsService.Save(_settings);
        _settingsDirty = false;
    }
}
