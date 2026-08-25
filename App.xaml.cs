using System.Text;
using System.Windows;
using Application = System.Windows.Application;

namespace Mp3Player;

public partial class App : Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        bool createdNew;
        _mutex = new Mutex(true, "XuanYinMp3Player_SingleInstance", out createdNew);
        if (!createdNew)
        {
            MessageBox.Show("播放器已经在运行了，请查看任务栏或系统托盘。", "炫音播放器",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        var startFiles = e.Args.Where(arg =>
            System.IO.File.Exists(arg) && IsAudioArg(arg)).ToList();
        var win = new MainWindow(startFiles);
        MainWindow = win;
        win.Show();
    }

    private static bool IsAudioArg(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp3" or ".wav" or ".m4a" or ".flac" or ".aac" or ".wma" or ".ogg";
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
        catch
        {
        }
        base.OnExit(e);
    }
}
