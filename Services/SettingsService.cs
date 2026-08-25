using System.IO;
using System.Text.Json;

namespace Mp3Player.Services;

public class AppSettings
{
    public float Volume { get; set; } = 0.8f;
    public int PlayMode { get; set; }
    public List<string> Playlist { get; set; } = new();
    public int LastIndex { get; set; } = -1;
    public double LastPosition { get; set; }
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 1200;
    public double WindowHeight { get; set; } = 760;
    public float[] Equalizer { get; set; } = new float[EqualizerSampleProvider.BandCount];
}

public static class SettingsService
{
    private static string DataDir => System.IO.Path.Combine(AppContext.BaseDirectory, "data");
    private static string SettingsPath => System.IO.Path.Combine(DataDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var json = File.ReadAllText(SettingsPath);
            var s = JsonSerializer.Deserialize<AppSettings>(json);
            return s ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings s)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var json = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
        }
    }
}
