namespace Cobra.Video;

/// <summary>One 50 Hz frame for the App: <see cref="Width"/> x <see cref="Height"/> machine pixels,
/// row-major from the top left, each a decoded colour index 0..15 (0..7 normal, 8..15 bright), plus the
/// border colour index. The App owns only index -> display colour and the integer zoom.</summary>
public sealed record VideoFrame(int Width, int Height, int BorderColour, long FrameNumber, byte[] Pixels);

/// <summary>The COBRA video controller's picture (Hardware Manual rev.1.0 pp.11-15), composed once per
/// frame from the 6,912-byte display file of video bank #1:
/// <list type="bullet">
/// <item>The display file is a bitmap (p.11): each byte is 8 adjoined pixels "B7-B0", B7 leftmost; a 0 bit
/// shows the cell's paper, a 1 bit its ink. There is no character generator in the video path.</item>
/// <item>Pixel byte <c>T*0x800 + L*0x100 + R*0x20 + C</c>, attribute <c>0x1800 + T*0x100 + R*0x20 + C</c>
/// (pp.13-14; T third, L pixel line, R character row, C column).</item>
/// <item>The attribute byte (p.11 names ink, paper, flashing, increased brightness) is decoded in the
/// Spectrum order: ink bits 0-2, paper bits 3-5, bright bit 6, flash bit 7. Flash is driven by FD7 (p.15),
/// i.e. bit 7. Evidence for the order: JETPAC, loaded by DEVIL from the published GAMES1 disk, renders
/// right with it.</item>
/// <item>BRIGHT lifts ink and paper to 8..15 and is suppressed for black (p.15). FLASH swaps ink and
/// paper in cells with bit 7 while the blinking oscillator is in its second state; the oscillator flips
/// outside the visible lines (p.15). Its rate is set by an RC network whose values the manual does not
/// give: <see cref="FlashFrames"/> is the Spectrum's 16 frames, a named assumption.</item>
/// <item>The raster is 448 x 312 (divider by 56 characters of 8 dots, divider by 312 lines, p.15), the
/// 256 x 192 picture centred in it, the rest the border colour, port C bits 0-2 (p.17).</item>
/// </list></summary>
/// <summary>What the video controller reads, from the configuration and the 80 characters/line circuit
/// (mainboard v.0.2 schematic, module page; v.1.3 add-on #2: U23 74LS157 and its U27 latch).</summary>
public enum VideoMode
{
    /// <summary>The first 8 KB half of bank #1 (AV13 = 0): BASIC, and startup/CP/M with O5 = 0.</summary>
    Standard,

    /// <summary>The second 8 KB half for the whole picture (AV13 = O5 * LO6): startup/CP/M with O5 = 1 while
    /// the 80-column latch is clear.</summary>
    SecondHalf,

    /// <summary>The 80-column CP/M screen: AV13 follows the character counter's AC5 and the horizontal border is
    /// not drawn, so four more bytes each side come from the second half: its columns 28-31 to the left of
    /// the picture and 0-3 to the right (8-byte segments, 24-byte gaps that hold the CCP copy). 40 bytes, 320
    /// pixels, 80 characters of 4 x 8 per line.</summary>
    EightyColumns,
}

public static class CobraVideo
{
    public const int RasterWidth = 448;
    public const int RasterHeight = 312;
    public const int PictureWidth = 256;
    public const int PictureHeight = 192;
    public const int PictureLeft = (RasterWidth - PictureWidth) / 2;
    public const int PictureTop = (RasterHeight - PictureHeight) / 2;
    public const int DisplayFileSize = 0x1B00;
    public const int AttributeOffset = 0x1800;

    /// <summary>Frames per blinking-oscillator state (named assumption, see the class remarks).</summary>
    public const int FlashFrames = 16;

    /// <summary>Size of one half of video bank #1 (AV13 picks the half).</summary>
    public const int HalfSize = 0x2000;

    /// <summary>The second half's columns shown left of the picture in <see cref="VideoMode.EightyColumns"/>.</summary>
    public const int LeftColumnsStart = 28;

    /// <summary>Bytes shown each side of the picture in <see cref="VideoMode.EightyColumns"/>.</summary>
    public const int SideColumns = 4;

    /// <summary>The frame for a display file (at least <see cref="DisplayFileSize"/> bytes from the start
    /// of video bank #1), a border colour 0..7 and the frame's number since power-on, in the standard mode.</summary>
    public static VideoFrame Compose(ReadOnlySpan<byte> displayFile, int border, long frameNumber) =>
        Compose(displayFile, border, frameNumber, VideoMode.Standard);

    /// <summary>The frame for video bank #1 (16 KB for <see cref="VideoMode.SecondHalf"/> and
    /// <see cref="VideoMode.EightyColumns"/>), a border colour, the frame number and the mode.</summary>
    public static VideoFrame Compose(ReadOnlySpan<byte> bank, int border, long frameNumber, VideoMode mode)
    {
        var needed = mode == VideoMode.Standard ? DisplayFileSize : HalfSize + DisplayFileSize;
        if (bank.Length < needed)
            throw new ArgumentException($"Mode {mode} reads {needed} bytes of the video bank; got {bank.Length}.", nameof(bank));
        border &= 7;
        var pixels = new byte[RasterWidth * RasterHeight];
        Array.Fill(pixels, (byte)border);
        var flashing = frameNumber / FlashFrames % 2 == 1;
        var main = mode == VideoMode.SecondHalf ? bank[HalfSize..] : bank;
        var second = mode == VideoMode.EightyColumns ? bank[HalfSize..] : default;
        for (var y = 0; y < PictureHeight; y++)
        {
            var at = (PictureTop + y) * RasterWidth + PictureLeft;
            for (var column = 0; column < 32; column++) Cell(main, y, column, pixels, at + column * 8, flashing);
            if (mode != VideoMode.EightyColumns) continue;
            for (var i = 0; i < SideColumns; i++)
            {
                Cell(second, y, LeftColumnsStart + i, pixels, at - (SideColumns - i) * 8, flashing);
                Cell(second, y, i, pixels, at + PictureWidth + i * 8, flashing);
            }
        }

        return new VideoFrame(RasterWidth, RasterHeight, border, frameNumber, pixels);
    }

    /// <summary>One character cell's line: the pixel byte and its attribute from <paramref name="half"/>, 8 pixels
    /// at <paramref name="at"/>.</summary>
    private static void Cell(ReadOnlySpan<byte> half, int y, int column, byte[] pixels, int at, bool flashing)
    {
        int third = y >> 6, line = y & 7, row = (y >> 3) & 7;
        var attribute = half[AttributeOffset + (third << 8) + (row << 5) + column];
        var bright = (attribute & 0x40) != 0;
        int ink = attribute & 7, paper = (attribute >> 3) & 7;
        if (bright)
        {
            if (ink != 0) ink += 8;
            if (paper != 0) paper += 8;
        }

        if (flashing && (attribute & 0x80) != 0) (ink, paper) = (paper, ink);
        var bits = half[(third << 11) | (line << 8) | (row << 5) | column];
        for (var b = 0; b < 8; b++) pixels[at + b] = (byte)(((bits << b) & 0x80) != 0 ? ink : paper);
    }
}
