namespace Cobra.Machine;

/// The speaker: 8255 port C bit 4 (LS, "LOUDSPEAKER") drives the keyboard's speaker through
/// C45 (Hardware Manual p.17; main board v.0 page 15). The beeper records every change of that
/// bit with the CPU T-state it happened at, and renders the level as PCM on request; nothing
/// here plays sound (audio is proven by writing it to a file).
public sealed class Beeper
{
    public const int SpeakerBit = 0x10;

    private readonly List<(long TState, bool High)> _edges = [];
    private readonly Func<long> _clock;
    private bool _level;

    public Beeper(Port8255 ppi, Func<long> clock)
    {
        ArgumentNullException.ThrowIfNull(ppi);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ppi.PortCWritten += value =>
        {
            var high = (value & SpeakerBit) != 0;
            if (high == _level) return;
            _level = high;
            _edges.Add((_clock(), high));
        };
    }

    /// <summary>Every level change, in order: (CPU T-state, new level).</summary>
    public IReadOnlyList<(long TState, bool High)> Edges => _edges;

    public void Clear() => _edges.Clear();

    /// <summary>Forgets the level changes before <paramref name="tState"/> that a later <see cref="Render"/>
    /// from <paramref name="tState"/> on no longer needs, keeping the one that sets the level there.</summary>
    public void Discard(long tState)
    {
        var keepFrom = 0;
        while (keepFrom < _edges.Count && _edges[keepFrom].TState <= tState) keepFrom++;
        if (keepFrom > 1) _edges.RemoveRange(0, keepFrom - 1);
    }

    /// <summary>The speaker level as 16-bit mono PCM from <paramref name="fromTState"/> for
    /// <paramref name="sampleCount"/> samples at <paramref name="sampleRate"/> Hz (3.5 MHz CPU clock).</summary>
    public short[] Render(long fromTState, int sampleCount, int sampleRate = 44_100)
    {
        var samples = new short[sampleCount];
        var edge = 0;
        var level = false;
        while (edge < _edges.Count && _edges[edge].TState <= fromTState) level = _edges[edge++].High;
        for (var i = 0; i < sampleCount; i++)
        {
            var t = fromTState + (long)i * 3_500_000 / sampleRate;
            while (edge < _edges.Count && _edges[edge].TState <= t) level = _edges[edge++].High;
            samples[i] = level ? (short)8000 : (short)-8000;
        }

        return samples;
    }
}

/// <summary>The Kempston joystick on 8255 port B (J6; main board v.0 page 15): PB0 right,
/// PB1 left, PB2 down, PB3 up, PB4 fire, active high with 8.2K pull-downs, so idle reads 0.
/// Port B answers at 1FH (A0 = 1, A5 = 0), the Kempston address.</summary>
[Flags]
public enum Joystick : byte
{
    None = 0,
    Right = 0x01,
    Left = 0x02,
    Down = 0x04,
    Up = 0x08,
    Fire = 0x10,
}
