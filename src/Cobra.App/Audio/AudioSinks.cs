namespace Cobra.App.Audio;

/// <summary>Where the machine's sound goes: mono float samples in -1..1 at <see cref="SampleRate"/>.</summary>
public interface IAudioSink : IDisposable
{
    int SampleRate { get; }

    /// <summary>Samples queued and not yet played (a sink that does not play reports 0).</summary>
    int QueuedSamples { get; }

    void Queue(ReadOnlySpan<float> samples);
}

/// <summary>Writes the sound to a 16-bit mono WAV file (the App's --sound-dump; audio is proven by its file).</summary>
public sealed class WavFileSink(string path, int sampleRate = 48000) : IAudioSink
{
    private readonly List<short> _samples = [];

    public int SampleRate { get; } = sampleRate;

    public int QueuedSamples => 0;

    public IReadOnlyList<short> Samples => _samples;

    public void Queue(ReadOnlySpan<float> samples)
    {
        foreach (var s in samples) _samples.Add((short)Math.Clamp(s * 32767f, -32768f, 32767f));
    }

    public void Dispose()
    {
        using var file = File.Create(path);
        using var w = new BinaryWriter(file);
        w.Write("RIFF"u8); w.Write(36 + _samples.Count * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(SampleRate); w.Write(SampleRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(_samples.Count * 2);
        foreach (var s in _samples) w.Write(s);
    }
}

/// <summary>Plays to one sink and copies to another (the device plus a dump file).</summary>
public sealed class TeeSink(IAudioSink play, IAudioSink copy) : IAudioSink
{
    public int SampleRate => play.SampleRate;

    public int QueuedSamples => play.QueuedSamples;

    public void Queue(ReadOnlySpan<float> samples)
    {
        play.Queue(samples);
        copy.Queue(samples);
    }

    /// <summary>Unpauses the playing sink when it is the SDL device.</summary>
    public void Start()
    {
        if (play is SdlQueuedAudioSink sdl) sdl.Pause(false);
    }

    public void Dispose()
    {
        play.Dispose();
        copy.Dispose();
    }
}
