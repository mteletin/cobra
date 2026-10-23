namespace Cobra.Machine;

/// The cassette interface: 8255 port C bit 3 is TO, the tape output, and port A bit 6 is TI, the
/// tape input (Hardware Manual pp.16-17; main board v.0 page 15). Those are the bits the ZX
/// Spectrum ROM uses for MIC and EAR, and the COBRA's BASIC is that ROM, so its SAVE and LOAD
/// drive this deck directly. All COBRA tape storage is the standard Spectrum block format
/// (the CoBra tape format).
///
/// The deck records every change of TO with its CPU T-state, decodes a recording into blocks
/// (flag, data, checksum) and plays blocks back as TI levels with the standard ROM timing:
/// pilot pulses of 2168 T-states (8063 before a header, 3223 before data), sync 667 + 735,
/// a 0 bit as two 855 pulses and a 1 bit as two 1710 pulses, most significant bit first, and
/// a second of silence after each block.
public sealed class TapeDeck
{
    public const int TapeOutBit = 0x08;
    public const int PilotPulse = 2168;
    public const int HeaderPilotPulses = 8063;
    public const int DataPilotPulses = 3223;
    public const int Sync1Pulse = 667;
    public const int Sync2Pulse = 735;
    public const int ZeroPulse = 855;
    public const int OnePulse = 1710;
    public const int PauseTStates = 3_500_000;

    private readonly List<(long TState, bool High)> _recording = [];
    private readonly Func<long> _clock;
    private bool _out;
    private long[] _edges = [];
    private long _playStart;

    public TapeDeck(Port8255 ppi, Func<long> clock)
    {
        ArgumentNullException.ThrowIfNull(ppi);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ppi.PortCWritten += value =>
        {
            var high = (value & TapeOutBit) != 0;
            if (high == _out) return;
            _out = high;
            _recording.Add((_clock(), high));
        };
    }

    /// <summary>Every change of TO since the last <see cref="ClearRecording"/>.</summary>
    public IReadOnlyList<(long TState, bool High)> Recording => _recording;

    public void ClearRecording() => _recording.Clear();

    public bool Playing { get; private set; }

    /// <summary>Starts playing <paramref name="blocks"/> (each flag + data + checksum, as in a .tap file) now.</summary>
    public void Play(IEnumerable<byte[]> blocks)
    {
        var edges = new List<long>();
        long t = 0;
        foreach (var block in blocks)
        {
            void Pulse(int length)
            {
                t += length;
                edges.Add(t);
            }

            var pilot = block.Length > 0 && block[0] < 0x80 ? HeaderPilotPulses : DataPilotPulses;
            for (var i = 0; i < pilot; i++) Pulse(PilotPulse);
            Pulse(Sync1Pulse);
            Pulse(Sync2Pulse);
            foreach (var b in block)
                for (var bit = 7; bit >= 0; bit--)
                {
                    var length = (b >> bit & 1) != 0 ? OnePulse : ZeroPulse;
                    Pulse(length);
                    Pulse(length);
                }

            t += PauseTStates;
        }

        _edges = [.. edges];
        _playStart = _clock();
        Playing = true;
    }

    public void Stop() => Playing = false;

    /// <summary>TI now: the level toggles at each edge of the playing tape; idle high.</summary>
    public bool InputLevel(long tStates)
    {
        if (!Playing) return true;
        var at = tStates - _playStart;
        if (_edges.Length > 0 && at > _edges[^1]) Playing = false;
        var index = Array.BinarySearch(_edges, at);
        var passed = index >= 0 ? index + 1 : ~index;
        return passed % 2 == 0;
    }

    /// <summary>Decodes a TO recording into Spectrum tape blocks (flag + data + checksum); a block
    /// is a pilot tone, the two sync pulses, then bit pulse pairs until the signal stops.</summary>
    public static List<byte[]> Decode(IReadOnlyList<(long TState, bool High)> recording)
    {
        var pulses = new List<long>();
        for (var i = 1; i < recording.Count; i++) pulses.Add(recording[i].TState - recording[i - 1].TState);
        static bool Near(long pulse, int nominal) => Math.Abs(pulse - nominal) <= nominal / 5;

        var blocks = new List<byte[]>();
        var p = 0;
        while (p < pulses.Count)
        {
            var pilot = 0;
            while (p < pulses.Count && Near(pulses[p], PilotPulse)) { pilot++; p++; }
            if (pilot < 256 || p + 1 >= pulses.Count || !Near(pulses[p], Sync1Pulse) || !Near(pulses[p + 1], Sync2Pulse))
            {
                p = Math.Max(p + 1, p);
                continue;
            }

            p += 2;
            var bytes = new List<byte>();
            int value = 0, bits = 0;
            while (p + 1 < pulses.Count)
            {
                bool one;
                if (Near(pulses[p], ZeroPulse) && Near(pulses[p + 1], ZeroPulse)) one = false;
                else if (Near(pulses[p], OnePulse) && Near(pulses[p + 1], OnePulse)) one = true;
                else break;
                value = value << 1 | (one ? 1 : 0);
                p += 2;
                if (++bits == 8)
                {
                    bytes.Add((byte)value);
                    value = bits = 0;
                }
            }

            if (bytes.Count > 0) blocks.Add([.. bytes]);
        }

        return blocks;
    }

    /// <summary>A .tap file: each block as a 2-byte little-endian length, then the block.</summary>
    public static byte[] ToTap(IEnumerable<byte[]> blocks)
    {
        var tap = new List<byte>();
        foreach (var block in blocks)
        {
            tap.Add((byte)block.Length);
            tap.Add((byte)(block.Length >> 8));
            tap.AddRange(block);
        }

        return [.. tap];
    }

    /// <summary>The blocks of a .tap file.</summary>
    public static List<byte[]> FromTap(byte[] tap)
    {
        ArgumentNullException.ThrowIfNull(tap);
        var blocks = new List<byte[]>();
        for (var at = 0; at + 2 <= tap.Length;)
        {
            var length = tap[at] | tap[at + 1] << 8;
            if (at + 2 + length > tap.Length) throw new InvalidDataException($"a .tap block at {at} runs past the end");
            blocks.Add(tap[(at + 2)..(at + 2 + length)]);
            at += 2 + length;
        }

        return blocks;
    }

    /// <summary>True when the block's last byte is the XOR of all the others (the ROM's check).</summary>
    public static bool ChecksumOk(byte[] block) => block.Length > 1 && block.Aggregate((byte)0, (x, b) => (byte)(x ^ b)) == 0;
}
