namespace Cobra.Machine;

/// The memory of the reference machine (the hobbyist mainboard v.0
/// in the 80K configuration) and how each hardware configuration maps it into
/// the Z80's 64K, in four 16 KB windows.
///
/// Parts: the BOOT EPROM (2 KB standard, up to 16 KB), the SYSTEM ROM (up to
/// 64 KB: OPUS, BASIC, DEVIL, CP/M quarters), one 64 KB DRAM whose quarters the
/// sources call DRAM#0..#3, and 16 KB of video RAM ("80KB total addressable
/// RAM: 64KB DRAM ... 16KB video RAM", rev1.3.html).
///
/// | window      | startup                    | BASIC               | CP/M                 |
/// |-------------|----------------------------|---------------------|----------------------|
/// | $0000-$3FFF | BOOT ROM                   | DRAM#0 (read-only)  | DRAM#0               |
/// | $4000-$7FFF | SYSTEM ROM quarter TO,SO   | video RAM           | O6 ? video : DRAM#1  |
/// | $8000-$BFFF | DRAM#0                     | DRAM#2              | DRAM#2               |
/// | $C000-$FFFF | O6 ? video : DRAM#1        | DRAM#3              | DRAM#3               |
///
/// Evidence, from the CoBra Project's published sources:
/// - startup: BOOT code at $0000; "4000 is the start addr of BASIC EPROM in the
///   startup hw config", "8000 is the start addr of DRAM#0 in the startup hw
///   config" (80K STD BOOT 0038-003B); "C000 is the start addr for video mem in
///   startup hw configuration" with O6 = 1 (0007, 0032); Boot Manager config
///   detection: a string written at $E000 with O6 = 1 is in VRAM, "set O6 to 0,
///   if we are in 80K config then at E000 there s/b now DRAM#1 instead of VRAM".
/// - BASIC: "jumps to $0000 of DRAM#0, where BASIC begins" (BOOT 0055, the
///   copy at 0038-0041 lands at $8000 = DRAM#0 in startup); the Spectrum screen
///   at $4000 (Boot Manager header: "at 4000 for standard Spectrum"); BASIC area
///   read-only unless the R/W switch is set (rev1.3.html, JP9 on v.0.2); the
///   Spectrum area "4000-FFFF, that is DRAM#1-#3" (boot.html, CP/M Game Loader).
/// - CP/M: "DRAM#0 is located at 0000 in CP/M config" (BOOT 0561 comment);
///   port C $01 = "O6 to 0 (CPU access to DRAM#1 instead of VRAM)" (0563); the
///   loader's block at $3FF9-$47FF "is physically located in DRAM#1" and the
///   erasure "stops 8 bytes before DRAM#1/VRAM" (cpm80.html); BOOT relocated to
///   "F800-FFFF (top of RAM, end of DRAM#3)" (0565).
/// - SYSTEM ROM quarter: A14 = SO (port C bit 7), A15 = TO (port C bit 3), see
///   <see cref="Port8255.SoBit"/>.
///
/// A 2 KB BOOT EPROM sees only A0-A10, so it repeats through its window; ROM
/// writes go nowhere. The 64K configuration (four 16K banks, bank #1 = video)
/// and the 80-column modification are other boards, not modelled here.
public sealed class CobraMemory
{
    public const int WindowSize = 0x4000;
    public const int MaxBootRomSize = 0x4000;
    public const int SystemRomSize = 0x10000;

    private readonly byte[] _bootRom;

    public CobraMemory(byte[] bootRom, byte[] systemRom)
    {
        ArgumentNullException.ThrowIfNull(bootRom);
        ArgumentNullException.ThrowIfNull(systemRom);
        if (bootRom.Length == 0 || bootRom.Length > MaxBootRomSize || (bootRom.Length & (bootRom.Length - 1)) != 0)
        {
            throw new ArgumentException($"A BOOT EPROM is 2-16 KB, a power of two; got {bootRom.Length} bytes.", nameof(bootRom));
        }

        if (systemRom.Length > SystemRomSize)
        {
            throw new ArgumentException($"The SYSTEM ROM is at most 64 KB; got {systemRom.Length} bytes.", nameof(systemRom));
        }

        _bootRom = (byte[])bootRom.Clone();
        SystemRom = new byte[SystemRomSize];
        Array.Fill(SystemRom, (byte)0xFF); // unprogrammed EPROM cells (SYSTEM_ROM.bin is 62283 bytes, boot.html)
        systemRom.CopyTo(SystemRom, 0);
    }

    /// <summary>The BOOT EPROM image as fitted.</summary>
    public ReadOnlySpan<byte> BootRom => _bootRom;

    /// <summary>The SYSTEM ROM, padded to 64 KB with $FF.</summary>
    public byte[] SystemRom { get; }

    /// <summary>The 64 KB DRAM; DRAM#n is <c>Dram[n * 0x4000 ..]</c>.</summary>
    public byte[] Dram { get; } = new byte[0x10000];

    /// <summary>The 16 KB video RAM.</summary>
    public byte[] Vram { get; } = new byte[WindowSize];

    /// <summary>The BASIC-area R/W switch (rev1.3.html; JP9 on v.0.2). Off: the
    /// BASIC copy in DRAM#0 is read-only in the BASIC configuration.</summary>
    public bool BasicAreaWritable { get; set; }

    /// <summary>The original 64K board (main board v.0 without the 80K modification): three CPU banks and the
    /// video bank #1, no DRAM#1. Its decode (v.0 page 3: AB14 = BA14 XOR BA13*LO6*BA15, AB15 = BA15 XOR LO6,
    /// U56 on AB15 AB14 NPO) works on 8 KB halves above 8000 with LO6 = 1: 8000 DRAM#0 low, A000 video high,
    /// C000 video low, E000 DRAM#0 high; below 8000 the EPROMs at power-on and DRAM#2, #3 in CP/M. BASIC is the
    /// 80K map's. Bank offset is always the address AND 3FFF (mainboard v.0 schematic, page 3).</summary>
    public bool Original64K { get; init; }

    /// <summary>What the 16 KB window holding <paramref name="address"/> shows.</summary>
    public MemoryWindow WindowAt(ushort address, HardwareConfiguration configuration, Port8255 ppi)
    {
        ArgumentNullException.ThrowIfNull(ppi);
        var window = address >> 14;
        if (Original64K && configuration != HardwareConfiguration.Basic)
        {
            return (address >> 13) switch
            {
                0 or 1 => configuration == HardwareConfiguration.Startup ? MemoryWindow.BootRom : MemoryWindow.Dram2,
                2 or 3 => configuration == HardwareConfiguration.Startup ? MemoryWindow.SystemRom : MemoryWindow.Dram3,
                4 or 7 => MemoryWindow.Dram0,
                _ => MemoryWindow.Video,
            };
        }

        return configuration switch
        {
            HardwareConfiguration.Startup => window switch
            {
                0 => MemoryWindow.BootRom,
                1 => MemoryWindow.SystemRom,
                2 => MemoryWindow.Dram0,
                _ => ppi.PinO6 ? MemoryWindow.Video : MemoryWindow.Dram1,
            },
            HardwareConfiguration.Basic => window switch
            {
                0 => MemoryWindow.Dram0,
                1 => MemoryWindow.Video,
                2 => MemoryWindow.Dram2,
                _ => MemoryWindow.Dram3,
            },
            _ => window switch
            {
                0 => MemoryWindow.Dram0,
                1 => ppi.PinO6 ? MemoryWindow.Video : MemoryWindow.Dram1,
                2 => MemoryWindow.Dram2,
                _ => MemoryWindow.Dram3,
            },
        };
    }

    /// <summary>The SYSTEM ROM quarter SO and TO select: OPUS 0, BASIC 1, DEVIL 2, CP/M 3.</summary>
    public static int SystemRomQuarter(Port8255 ppi) => (ppi.PinTO ? 2 : 0) | (ppi.PinSO ? 1 : 0);

    public byte Read(ushort address, HardwareConfiguration configuration, Port8255 ppi)
    {
        var offset = address & (WindowSize - 1);
        return WindowAt(address, configuration, ppi) switch
        {
            MemoryWindow.BootRom => _bootRom[offset & (_bootRom.Length - 1)],
            MemoryWindow.SystemRom => SystemRom[(SystemRomQuarter(ppi) << 14) | offset],
            MemoryWindow.Video => Vram[offset],
            var dram => Dram[(DramBank(dram) << 14) | offset],
        };
    }

    public void Write(ushort address, byte value, HardwareConfiguration configuration, Port8255 ppi)
    {
        var offset = address & (WindowSize - 1);
        switch (WindowAt(address, configuration, ppi))
        {
            case MemoryWindow.BootRom:
            case MemoryWindow.SystemRom:
                return;
            case MemoryWindow.Video:
                Vram[offset] = value;
                return;
            case MemoryWindow.Dram0 when configuration == HardwareConfiguration.Basic && !BasicAreaWritable:
                return;
            case var dram:
                Dram[(DramBank(dram) << 14) | offset] = value;
                return;
        }
    }

    private static int DramBank(MemoryWindow window) => window switch
    {
        MemoryWindow.Dram0 => 0,
        MemoryWindow.Dram1 => 1,
        MemoryWindow.Dram2 => 2,
        MemoryWindow.Dram3 => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(window), window, "not a DRAM window"),
    };
}

/// What one 16 KB window of the Z80's address space shows.
public enum MemoryWindow
{
    BootRom,
    SystemRom,
    Dram0,
    Dram1,
    Dram2,
    Dram3,
    Video,
}
