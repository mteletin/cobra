using System.Text;

namespace Cobra.Machine.Media;

/// Reads Teledisk images (.td0), the format of the published DEVIL disk captures (the
/// TeleDisk format). A 12-byte header ("TD" = normal; "td" = whole-file
/// LZHUF compression, not supported here: every published COBRA image is "TD"), an optional
/// comment block (stepping bit 7), then tracks: a 4-byte header (sector count, cylinder,
/// head, CRC; count FF ends the image) and per sector a 6-byte header (C, H, R, N, flags, CRC)
/// and, unless flags 0x30 say there is none, a data block: 2-byte length, an encoding byte and
/// the encoded data - 0 raw, 1 repeated 2-byte patterns, 2 run-length blocks. Sectors keep the
/// order and IDs the capture found, defects included.
public static class TeleDiskReader
{
    public static FloppyImage Read(byte[] td0)
    {
        ArgumentNullException.ThrowIfNull(td0);
        if (td0.Length < 12) throw new InvalidDataException("too short for a Teledisk image");
        var signature = Encoding.ASCII.GetString(td0, 0, 2);
        if (signature == "td") throw new NotSupportedException("advanced-compression Teledisk (\"td\") images are not read");
        if (signature != "TD") throw new InvalidDataException("not a Teledisk image (no TD signature)");
        var sides = td0[9];
        if (sides is not (1 or 2)) throw new InvalidDataException("invalid Teledisk head count");
        var at = 12;
        if ((td0[7] & 0x80) != 0)
        {
            Require(td0.Length, at, 10);
            var commentLength = td0[at + 2] | td0[at + 3] << 8;
            Require(td0.Length, at + 10, commentLength);
            at += 10 + commentLength;
        }

        var sectors = new List<(int Cylinder, int Head, FloppySector Sector)>();
        var cylinders = 0;
        while (at < td0.Length && td0[at] != 0xFF)
        {
            Require(td0.Length, at, 4);
            int count = td0[at], cylinder = td0[at + 1], head = td0[at + 2] & 0x7F;
            if (head >= sides) throw new InvalidDataException("track head exceeds Teledisk head count");
            var fm = (td0[at + 2] & 0x80) != 0 || (td0[5] & 0x80) != 0;
            at += 4;
            cylinders = Math.Max(cylinders, cylinder + 1);
            for (var s = 0; s < count; s++)
            {
                Require(td0.Length, at, 6);
                int c = td0[at], h = td0[at + 1], r = td0[at + 2], n = td0[at + 3], flags = td0[at + 4];
                if (n > 6) throw new InvalidDataException("invalid Teledisk sector size");
                at += 6;
                if ((flags & 0x30) != 0) continue; // no data recorded for this ID
                Require(td0.Length, at, 2);
                var length = td0[at] | td0[at + 1] << 8;
                Require(td0.Length, at + 2, length);
                var data = Decode(td0.AsSpan(at + 2, length), 128 << n);
                at += 2 + length;
                sectors.Add((cylinder, head, new FloppySector(c, h, r, n, data, Mfm: !fm)));
            }
        }

        if (at >= td0.Length) throw new InvalidDataException("missing Teledisk end marker");
        var image = new FloppyImage(Math.Max(cylinders, 1), Math.Clamp((int)sides, 1, 2));
        foreach (var (cylinder, head, sector) in sectors) image.Add(cylinder, head, sector);
        return image;
    }

    private static byte[] Decode(ReadOnlySpan<byte> block, int size)
    {
        Require(block.Length, 0, 1);
        var method = block[0];
        var source = block[1..];
        var data = new byte[size];
        switch (method)
        {
            case 0:
                if (source.Length != size) throw new InvalidDataException("incorrect raw sector length");
                source[..size].CopyTo(data);
                break;
            case 1:
            {
                var at = 0;
                for (var i = 0; i + 4 <= source.Length && at < size; i += 4)
                {
                    var repeats = source[i] | source[i + 1] << 8;
                    Require(size, at, repeats * 2);
                    for (var k = 0; k < repeats && at + 1 < size + 1; k++)
                    {
                        if (at < size) data[at++] = source[i + 2];
                        if (at < size) data[at++] = source[i + 3];
                    }
                }

                if (at != size || source.Length % 4 != 0) throw new InvalidDataException("incomplete repeated sector");
                break;
            }

            case 2:
            {
                var at = 0;
                for (var i = 0; i < source.Length && at < size;)
                {
                    Require(source.Length, i, 2);
                    int type = source[i], count = source[i + 1];
                    i += 2;
                    if (type == 0)
                    {
                        Require(source.Length, i, count);
                        Require(size, at, count);
                        for (var k = 0; k < count && at < size; k++) data[at++] = source[i + k];
                        i += count;
                    }
                    else
                    {
                        Require(source.Length, i, 2 * type);
                        Require(size, at, 2 * type * count);
                        var pattern = source.Slice(i, 2 * type);
                        for (var k = 0; k < count; k++)
                            foreach (var b in pattern)
                                if (at < size) data[at++] = b;
                        i += 2 * type;
                    }
                }

                if (at != size) throw new InvalidDataException("incomplete run-length sector");
                break;
            }

            default:
                throw new InvalidDataException($"unknown Teledisk sector encoding {method}");
        }

        return data;
    }

    private static void Require(int length, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > length || count > length - offset)
            throw new InvalidDataException("truncated or oversized Teledisk data");
    }
}
