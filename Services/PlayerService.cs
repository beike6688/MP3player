using NAudio.Wave;

namespace Mp3Player.Services;

public class PlayerService : IDisposable
{
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;
    private EqualizerSampleProvider? _eq;
    private bool _manualStop;

    public event EventHandler? PlaybackCompleted;

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public bool HasTrack => _reader != null;
    public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan Length => _reader?.TotalTime ?? TimeSpan.Zero;
    public EqualizerSampleProvider? Equalizer => _eq;
    public float CurrentRms => _eq?.CurrentRms ?? 0f;
    public int SampleRate => _eq?.SampleRate ?? 44100;

    public float Volume
    {
        get => _output?.Volume ?? 0.8f;
        set
        {
            if (_output != null) _output.Volume = Math.Clamp(value, 0f, 1f);
        }
    }

    public void Play(string path)
    {
        StopInternal();
        _reader = new AudioFileReader(path);
        _eq = new EqualizerSampleProvider(_reader);
        _output = new WaveOutEvent { DesiredLatency = 200, NumberOfBuffers = 2 };
        _output.PlaybackStopped += OnPlaybackStopped;
        _output.Init(_eq);
        _output.Play();
    }

    public void Pause()
    {
        if (_output?.PlaybackState == PlaybackState.Playing)
            _output.Pause();
    }

    public void Resume()
    {
        if (_output?.PlaybackState == PlaybackState.Paused)
            _output.Play();
    }

    public void TogglePlayPause()
    {
        if (IsPlaying) Pause();
        else Resume();
    }

    public void Stop()
    {
        _manualStop = true;
        try { _output?.Stop(); } catch { }
        _manualStop = false;
        Cleanup();
    }

    public void Seek(TimeSpan pos)
    {
        if (_reader == null) return;
        try
        {
            long maxTicks = Math.Max(0, _reader.TotalTime.Ticks - 100_000);
            var clamped = TimeSpan.FromTicks(Math.Clamp(pos.Ticks, 0, maxTicks));
            _reader.CurrentTime = clamped;
        }
        catch
        {
        }
    }

    public void SetEqualizerBand(int band, float gainDb) => _eq?.SetBand(band, gainDb);

    public void CopyRecentSamples(float[] dest) => _eq?.CopyRecentSamples(dest);

    private void StopInternal()
    {
        _manualStop = true;
        try { _output?.Stop(); } catch { }
        _manualStop = false;
        Cleanup();
    }

    private void Cleanup()
    {
        if (_output != null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            _output.Dispose();
            _output = null;
        }
        _reader?.Dispose();
        _reader = null;
        _eq = null;
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (_manualStop) return;
        PlaybackCompleted?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => StopInternal();
}
