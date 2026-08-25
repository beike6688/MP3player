using NAudio.Dsp;
using NAudio.Wave;

namespace Mp3Player.Services;

/// <summary>
/// 在播放链上叠加 5 段均衡器，同时统计实时音量用于频谱动画。
/// </summary>
public class EqualizerSampleProvider : ISampleProvider
{
    public const int BandCount = 5;
    public static readonly float[] BandFrequencies = { 80f, 250f, 1000f, 4000f, 12000f };
    public static readonly string[] BandNames = { "低音", "中低", "中音", "中高", "高音" };

    private readonly ISampleProvider _source;
    private readonly float _sampleRate;
    private readonly int _channels;
    private volatile BiQuadFilter[] _eq;
    private readonly float[] _gains = new float[BandCount];
    private volatile float _currentRms;
    private readonly float[] _ring = new float[16384];
    private int _ringPos;
    private readonly object _ringLock = new();

    public WaveFormat WaveFormat => _source.WaveFormat;
    public float CurrentRms => _currentRms;
    public int SampleRate => (int)_sampleRate;

    public EqualizerSampleProvider(ISampleProvider source)
    {
        _source = source;
        _sampleRate = source.WaveFormat.SampleRate;
        _channels = source.WaveFormat.Channels;
        _eq = BuildFilters();
    }

    private BiQuadFilter[] BuildFilters()
    {
        var filters = new BiQuadFilter[_channels * BandCount];
        for (int c = 0; c < _channels; c++)
            for (int b = 0; b < BandCount; b++)
                filters[c * BandCount + b] = BiQuadFilter.PeakingEQ(_sampleRate, BandFrequencies[b], 1.0f, _gains[b]);
        return filters;
    }

    public void SetBand(int band, float gainDb)
    {
        if (band < 0 || band >= BandCount) return;
        _gains[band] = Math.Clamp(gainDb, -12f, 12f);
        _eq = BuildFilters();
    }

    public float GetBand(int band) => band >= 0 && band < BandCount ? _gains[band] : 0f;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        var filters = _eq;
        for (int i = 0; i < read; i++)
        {
            int idx = offset + i;
            int channel = idx % _channels;
            for (int b = 0; b < BandCount; b++)
                buffer[idx] = filters[channel * BandCount + b].Transform(buffer[idx]);
        }
        if (read > 0)
        {
            float sum = 0f;
            for (int i = 0; i < read; i++)
            {
                float v = buffer[offset + i];
                sum += v * v;
            }
            _currentRms = MathF.Sqrt(sum / read);

            lock (_ringLock)
            {
                for (int i = 0; i < read; i++)
                {
                    _ring[_ringPos] = buffer[offset + i];
                    _ringPos = (_ringPos + 1) % _ring.Length;
                }
            }
        }
        return read;
    }

    /// <summary>
    /// 复制最近 n 个样本（单声道混合）到目标数组，用于频谱分析。
    /// </summary>
    public void CopyRecentSamples(float[] dest)
    {
        lock (_ringLock)
        {
            int n = dest.Length;
            int channels = _channels;
            for (int i = 0; i < n; i++)
            {
                // 只取第一个声道，避免混叠
                int idx = (_ringPos - n * channels + i * channels + _ring.Length) % _ring.Length;
                dest[i] = _ring[idx];
            }
        }
    }
}
