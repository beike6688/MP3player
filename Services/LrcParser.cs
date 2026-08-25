using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Mp3Player.Services;

public sealed record LyricLine(TimeSpan Time, string Text);

public static class LrcParser
{
    public static List<LyricLine> Parse(string path)
    {
        var list = new List<LyricLine>();
        try
        {
            if (!File.Exists(path)) return list;
            var enc = DetectEncoding(path);
            foreach (var raw in File.ReadAllLines(path, enc))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                var matches = Regex.Matches(line, @"\[(\d{1,2}):(\d{1,2})(?:[.:](\d{1,3}))?\]");
                if (matches.Count == 0) continue;
                int lastBracket = line.LastIndexOf(']');
                var text = lastBracket >= 0 && lastBracket + 1 < line.Length ? line.Substring(lastBracket + 1).Trim() : "";
                if (text.StartsWith("ti:", StringComparison.OrdinalIgnoreCase)
                    || text.StartsWith("ar:", StringComparison.OrdinalIgnoreCase)
                    || text.StartsWith("al:", StringComparison.OrdinalIgnoreCase)
                    || text.StartsWith("by:", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (Match m in matches)
                {
                    int min = int.Parse(m.Groups[1].Value);
                    int sec = int.Parse(m.Groups[2].Value);
                    double frac = 0;
                    if (m.Groups[3].Success)
                    {
                        var fs = m.Groups[3].Value;
                        frac = double.Parse(fs) / Math.Pow(10, fs.Length);
                    }
                    var t = TimeSpan.FromMinutes(min) + TimeSpan.FromSeconds(sec) + TimeSpan.FromSeconds(frac);
                    list.Add(new LyricLine(t, text));
                }
            }
            list.Sort((a, b) => a.Time.CompareTo(b.Time));
        }
        catch
        {
            // 忽略无法解析的歌词文件
        }
        return list;
    }

    public static string FindLrcFile(string songPath)
    {
        string dir = System.IO.Path.GetDirectoryName(songPath) ?? "";
        string name = System.IO.Path.GetFileNameWithoutExtension(songPath);
        foreach (var ext in new[] { ".lrc", ".LRC" })
        {
            string p = System.IO.Path.Combine(dir, name + ext);
            if (File.Exists(p)) return p;
        }
        return "";
    }

    private static Encoding DetectEncoding(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var bom = new byte[3];
            int n = fs.Read(bom, 0, 3);
            if (n >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return Encoding.UTF8;
        }
        catch
        {
        }
        try
        {
            return Encoding.GetEncoding(936); // GBK
        }
        catch
        {
            return Encoding.UTF8;
        }
    }
}
