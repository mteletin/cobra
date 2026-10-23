namespace Cobra.Machine.Media;

/// <summary>A COBRA disk format: geometry, recording and the physical order of sector IDs on a
/// track side (the CoBra disk formats). IDs run 1..SectorsPerTrack.</summary>
public sealed record FloppyFormat(string Name, int Cylinders, int Heads, int SectorsPerTrack, int SectorSize, IReadOnlyList<int> PhysicalOrder)
{
    /// <summary>CP/M 720K: 80 x 2 x 9 x 512, MFM, interleave 1:1 (cpm80.html section 7).</summary>
    public static FloppyFormat Cpm720 { get; } = new("CP/M 720K", 80, 2, 9, 512, Sequential(9));

    /// <summary>CP/M 860K: 86 x 2 x 10 x 512 (cpm860k-ro.html section 3).</summary>
    public static FloppyFormat Cpm860 { get; } = new("CP/M 860K", 86, 2, 10, 512, Sequential(10));

    /// <summary>DEVIL: 80 x 2 x 18 x 256 with the physical interleave 1, 10, 2, 11, ... 9, 18
    /// (devil.html section 1).</summary>
    public static FloppyFormat Devil { get; } = new("DEVIL", 80, 2, 18, 256, [1, 10, 2, 11, 3, 12, 4, 13, 5, 14, 6, 15, 7, 16, 8, 17, 9, 18]);

    public int SizeCode => SectorSize switch { 128 => 0, 256 => 1, 512 => 2, 1024 => 3, _ => throw new InvalidOperationException($"no 8272 size code for {SectorSize}") };

    public int RawBytes => Cylinders * Heads * SectorsPerTrack * SectorSize;

    private static int[] Sequential(int count) => Enumerable.Range(1, count).ToArray();
}

/// <summary>One sector as the floppy controller finds it: its ID field (C, H, R, N) and data.</summary>
public sealed record FloppySector(int Cylinder, int Head, int Id, int SizeCode, byte[] Data, bool Mfm = true, bool CrcOk = true);

/// <summary>A floppy disk as tracks of sectors in physical order, per cylinder and head. It is the
/// media model the floppy board's drives read; loaders fill it from raw images (any COBRA
/// format) and from HFE track recordings.</summary>
public sealed class FloppyImage
{
    private readonly List<FloppySector>[,] _tracks;

    public FloppyImage(int cylinders, int heads)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cylinders, 1);
        ArgumentOutOfRangeException.ThrowIfNotEqual(heads is 1 or 2, true);
        Cylinders = cylinders;
        Heads = heads;
        _tracks = new List<FloppySector>[cylinders, heads];
        for (var c = 0; c < cylinders; c++)
            for (var h = 0; h < heads; h++)
                _tracks[c, h] = [];
    }

    public int Cylinders { get; }

    public int Heads { get; }

    /// <summary>The sectors of one track side, in the order they pass the head.</summary>
    public IReadOnlyList<FloppySector> Track(int cylinder, int head) => _tracks[cylinder, head];

    /// <summary>Adds a sector on the track its ID field names.</summary>
    public void Add(FloppySector sector) => Add(sector.Cylinder, sector.Head, sector);

    /// <summary>Adds a sector on a physical track; its ID field may name another (a defect or a
    /// protection), and the controller sees the ID as recorded.</summary>
    public void Add(int cylinder, int head, FloppySector sector) => _tracks[cylinder, head].Add(sector);

    /// <summary>The first sector on that track side whose ID is <paramref name="id"/>, or null.</summary>
    public FloppySector? Find(int cylinder, int head, int id) => _tracks[cylinder, head].FirstOrDefault(s => s.Id == id);

    /// <summary>A raw image (sector data back to back: cylinder 0 head 0, head 1, cylinder 1 ...),
    /// each track side in the format's physical order, as the published .img files and
    /// TD02RAW's output are laid out.</summary>
    public static FloppyImage FromRaw(byte[] raw, FloppyFormat format)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(format);
        if (raw.Length != format.RawBytes)
            throw new ArgumentException($"{format.Name} raw images are {format.RawBytes} bytes; this one is {raw.Length}.", nameof(raw));
        var image = new FloppyImage(format.Cylinders, format.Heads);
        var at = 0;
        for (var c = 0; c < format.Cylinders; c++)
            for (var h = 0; h < format.Heads; h++)
                foreach (var id in format.PhysicalOrder)
                {
                    image.Add(new FloppySector(c, h, id, format.SizeCode, raw[at..(at + format.SectorSize)]));
                    at += format.SectorSize;
                }

        return image;
    }

    /// <summary>The raw image of this disk in <paramref name="format"/>'s layout.</summary>
    public byte[] ToRaw(FloppyFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        var raw = new byte[format.RawBytes];
        var at = 0;
        for (var c = 0; c < format.Cylinders; c++)
            for (var h = 0; h < format.Heads; h++)
                foreach (var id in format.PhysicalOrder)
                {
                    var sector = Find(c, h, id) ?? throw new InvalidDataException($"cylinder {c} head {h} has no sector {id}");
                    sector.Data.CopyTo(raw, at);
                    at += format.SectorSize;
                }

        return raw;
    }

    /// <summary>A disk from a CPC DSK or Extended DSK container (what the App saves a changed disk as): every
    /// track side's sectors as recorded, MFM (the container keeps no density; every COBRA format is MFM), a
    /// sector with the data-error bits (ST1 DE / ST2 DD) read as a bad CRC.</summary>
    public static FloppyImage FromDsk(Hc2000.Machine.Storage.Dsk dsk)
    {
        ArgumentNullException.ThrowIfNull(dsk);
        var image = new FloppyImage(dsk.Cylinders, dsk.Sides);
        foreach (var track in dsk.Tracks)
            foreach (var s in track.Sectors)
                image.Add(track.Cylinder, track.Side, new FloppySector(s.C, s.H, s.R, s.N, s.Data.ToArray(), Mfm: true, CrcOk: (s.ST1 & 0x20) == 0 && (s.ST2 & 0x20) == 0));
        return image;
    }

    /// <summary>The disk as an Extended DSK container, the media format of the copied drive model:
    /// every track side with its sectors in the order they pass the head and their own ID fields
    /// (so DEVIL's interleave and any odd ID survive). A sector read with a bad CRC carries the
    /// controller's data-error bits (ST1 DE, ST2 DD); an empty track side is recorded as unformatted.</summary>
    public byte[] ToExtendedDsk()
    {
        using var output = new MemoryStream();
        var header = new byte[256];
        "EXTENDED CPC DSK File\r\nDisk-Info\r\n"u8.CopyTo(header);
        "COBRA-EMU"u8.CopyTo(header.AsSpan(34));
        header[48] = checked((byte)Cylinders);
        header[49] = checked((byte)Heads);
        var tracks = new List<byte[]>();
        for (var c = 0; c < Cylinders; c++)
            for (var h = 0; h < Heads; h++)
            {
                var sectors = _tracks[c, h];
                if (sectors.Count == 0)
                {
                    tracks.Add([]);
                    continue;
                }

                if (sectors.Count > 29) throw new InvalidDataException($"cylinder {c} head {h} has {sectors.Count} sectors; an EDSK track holds 29");
                var length = (256 + sectors.Sum(s => s.Data.Length) + 255) & ~255;
                var track = new byte[length];
                "Track-Info\r\n"u8.CopyTo(track);
                track[16] = (byte)c;
                track[17] = (byte)h;
                track[20] = (byte)sectors[0].SizeCode;
                track[21] = (byte)sectors.Count;
                track[22] = 0x4E;
                track[23] = 0xE5;
                var at = 256;
                for (var i = 0; i < sectors.Count; i++)
                {
                    var s = sectors[i];
                    var id = track.AsSpan(24 + i * 8, 8);
                    id[0] = (byte)s.Cylinder;
                    id[1] = (byte)s.Head;
                    id[2] = (byte)s.Id;
                    id[3] = (byte)s.SizeCode;
                    id[4] = s.CrcOk ? (byte)0 : (byte)0x20;
                    id[5] = s.CrcOk ? (byte)0 : (byte)0x20;
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(id[6..], checked((ushort)s.Data.Length));
                    s.Data.CopyTo(track, at);
                    at += s.Data.Length;
                }

                tracks.Add(track);
            }

        for (var i = 0; i < tracks.Count; i++) header[52 + i] = checked((byte)(tracks[i].Length / 256));
        output.Write(header);
        foreach (var track in tracks) output.Write(track);
        return output.ToArray();
    }
}
