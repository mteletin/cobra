using System.Buffers.Binary;
using System.Text;

namespace Cobra.Machine.Media;

/// Reads HxC Floppy Emulator v1 track recordings (.hfe), the format the site publishes beside
/// each raw image (the CoBra disk formats): a 512-byte header ("HXCPICFE",
/// tracks, sides, encoding, bit rate, the track list's block), a track list of (block offset,
/// length) pairs, and each track's cells with the two sides interleaved in 256-byte halves of
/// 512-byte blocks, least significant bit first. Each data bit is two cells (clock, data).
///
/// The decoder finds IBM MFM address marks (three A1 sync bytes with a missing clock, cells
/// 0x4489), then an ID field (FE: C, H, R, N, CRC) and the data field that follows it (FB, or
/// F8 deleted: 128 << N bytes, CRC). The CRC is CCITT (0x1021, initial FFFF) over the sync
/// bytes, mark and field. FM-recorded tracks are not decoded: every published COBRA HFE is MFM.
public static class HfeReader
{
    private const ushort MfmSync = 0x4489;

    public static FloppyImage Read(byte[] hfe)
    {
        ArgumentNullException.ThrowIfNull(hfe);
        if (hfe.Length < 512 || Encoding.ASCII.GetString(hfe, 0, 8) != "HXCPICFE")
            throw new InvalidDataException("not an HFE v1 file (no HXCPICFE signature)");
        int tracks = hfe[9], sides = hfe[10], encoding = hfe[11];
        if (encoding != 0)
            throw new NotSupportedException($"HFE track encoding {encoding}: only IBM MFM (0) is decoded");
        var listAt = BinaryPrimitives.ReadUInt16LittleEndian(hfe.AsSpan(18)) * 512;
        var image = new FloppyImage(tracks, sides);
        for (var t = 0; t < tracks; t++)
        {
            var offset = BinaryPrimitives.ReadUInt16LittleEndian(hfe.AsSpan(listAt + t * 4)) * 512;
            var length = BinaryPrimitives.ReadUInt16LittleEndian(hfe.AsSpan(listAt + t * 4 + 2));
            for (var side = 0; side < sides; side++)
            {
                foreach (var sector in DecodeMfm(SideBytes(hfe, offset, length, side), t, side))
                    image.Add(sector);
            }
        }

        return image;
    }

    /// <summary>One side's bytes of a track: 256-byte halves of each 512-byte block.</summary>
    private static byte[] SideBytes(byte[] hfe, int offset, int length, int side)
    {
        var bytes = new byte[length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var block = i / 256;
            var at = offset + block * 512 + side * 256 + i % 256;
            if (at < hfe.Length) bytes[i] = hfe[at];
        }

        return bytes;
    }

    private static IEnumerable<FloppySector> DecodeMfm(byte[] side, int cylinder, int head)
    {
        var cells = side.Length * 8;
        bool Cell(int i) => (side[i >> 3] & (1 << (i & 7))) != 0; // LSB first
        (int C, int H, int R, int N)? id = null;
        var shift = 0;
        for (var i = 0; i < cells; i++)
        {
            shift = (shift << 1 | (Cell(i) ? 1 : 0)) & 0xFFFF;
            // An address mark follows three A1 syncs in a row: this one ends at i, the other two
            // end 16 and 32 cells earlier, and the next 16 cells are not a fourth one.
            var mark = i + 1;
            if (shift != MfmSync || i < 47 || Raw16(Cell, i - 31) != MfmSync || Raw16(Cell, i - 47) != MfmSync
                || mark + 16 > cells || Raw16(Cell, mark) == MfmSync)
            {
                continue;
            }

            var code = ReadByte(Cell, mark);
            if (code == 0xFE && mark + 16 * 7 <= cells)
            {
                var field = ReadBytes(Cell, mark, 7);
                id = Crc(field) == 0 ? (field[1], field[2], field[3], field[4]) : null;
                i = mark + 16 * 7 - 1;
                shift = 0;
            }
            else if ((code == 0xFB || code == 0xF8) && id is { } found)
            {
                var size = 128 << found.N;
                if (mark + 16 * (size + 3) > cells) yield break;
                var field = ReadBytes(Cell, mark, size + 3);
                yield return new FloppySector(found.C, found.H, found.R, found.N, field[1..(1 + size)], Mfm: true, CrcOk: Crc(field) == 0);
                id = null;
                i = mark + 16 * (size + 3) - 1;
                shift = 0;
            }
        }
    }

    private static int Raw16(Func<int, bool> cell, int at)
    {
        var v = 0;
        for (var k = 0; k < 16; k++) v = v << 1 | (cell(at + k) ? 1 : 0);
        return v;
    }

    private static byte ReadByte(Func<int, bool> cell, int at)
    {
        var v = 0;
        for (var k = 0; k < 8; k++) v = v << 1 | (cell(at + 2 * k + 1) ? 1 : 0); // data cells
        return (byte)v;
    }

    private static byte[] ReadBytes(Func<int, bool> cell, int at, int count)
    {
        var bytes = new byte[count];
        for (var b = 0; b < count; b++) bytes[b] = ReadByte(cell, at + 16 * b);
        return bytes;
    }

    /// <summary>CRC-CCITT over A1 A1 A1 + <paramref name="field"/> (mark, data, CRC): 0 when intact.</summary>
    private static int Crc(byte[] field)
    {
        var crc = 0xFFFF;
        void Feed(byte b)
        {
            crc ^= b << 8;
            for (var k = 0; k < 8; k++) crc = (crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1;
            crc &= 0xFFFF;
        }

        Feed(0xA1); Feed(0xA1); Feed(0xA1);
        foreach (var b in field) Feed(b);
        return crc;
    }
}
