using Cobra.Machine;
using Cobra.Machine.Media;

namespace Cobra.App;

/// <summary>A disk file as the drive's medium: Teledisk (.td0), HFE (.hfe), CPC/Extended DSK (.dsk, what
/// the App saves), or a raw image by size and
/// content (CP/M 720K or DEVIL, both 737,280 bytes; CP/M 860K, 880,640 bytes).</summary>
public static class MediaFiles
{
    public static readonly string[] DiskPatterns = ["*.td0", "*.TD0", "*.hfe", "*.HFE", "*.dsk", "*.DSK", "*.img", "*.IMG", "*.raw", "*.RAW"];
    public static readonly string[] TapePatterns = ["*.tap", "*.TAP"];

    public static FloppyImage LoadDisk(string name, byte[] bytes)
    {
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (extension == ".td0") return TeleDiskReader.Read(bytes);
        if (extension == ".hfe") return HfeReader.Read(bytes);
        if (extension == ".dsk") return FloppyImage.FromDsk(Hc2000.Machine.Storage.Dsk.Load(bytes));
        return FloppyImage.FromRaw(bytes, RawFormat(bytes));
    }

    /// <summary>The format of a raw image. 860K is told by size. A 720K-sized image is DEVIL when its first
    /// sector is a DEVIL catalog entry (type 0..3, a printable name, attributes in byte 17) and CP/M
    /// otherwise (a CP/M system disk starts with boot code).</summary>
    public static FloppyFormat RawFormat(byte[] raw)
    {
        if (raw.Length == FloppyFormat.Cpm860.RawBytes) return FloppyFormat.Cpm860;
        if (raw.Length != FloppyFormat.Cpm720.RawBytes)
            throw new InvalidDataException($"A raw COBRA disk is {FloppyFormat.Cpm720.RawBytes} or {FloppyFormat.Cpm860.RawBytes} bytes; this one is {raw.Length}.");
        var devil = raw[0] <= 3 && raw.AsSpan(1, 10).ToArray().All(b => b is >= 0x20 and < 0x7F) && raw[17] <= 3;
        return devil ? FloppyFormat.Devil : FloppyFormat.Cpm720;
    }

    public static List<byte[]> LoadTape(byte[] tap) => TapeDeck.FromTap(tap);

    /// <summary>A freshly formatted disk in <paramref name="format"/>: every sector E5, as a formatter leaves
    /// it (an empty CP/M directory, every DEVIL catalog entry free).</summary>
    public static FloppyImage Blank(FloppyFormat format)
    {
        var raw = new byte[format.RawBytes];
        Array.Fill(raw, (byte)0xE5);
        return FloppyImage.FromRaw(raw, format);
    }
}
