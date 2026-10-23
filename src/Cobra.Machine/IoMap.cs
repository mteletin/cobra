namespace Cobra.Machine;

/// The I/O address table the COBRA starts with, given by the published
/// BOOT disassembly (BOOT_2K_COBRA_80K updated, 0021-0034) and the v.0
/// schematic. The 8255's two Z80-visible
/// addresses are the data line (RD selects port A, WR selects port C) and
/// the control line; the 8272A and the CTC channels sit on their own
/// decode lines, on the same table.
public static class IoMap
{
    /// <summary>J5A+B: 8-BIT INPUT PORT @ 0DFH, the expansion input port.</summary>
    public const ushort ExternalInputPort = 0x0DF;

    /// <summary>8255 A/C data: IN reads port A (keyboard/tape-in), OUT
    /// writes port C (TO, LS, SO, O5, O6, BORDER).</summary>
    public const ushort Port8255AcData = 0x0FE;

    /// <summary>8255 control word register (BOOT: OUT ($DF),$92).</summary>
    public const ushort Port8255Control = 0x0DF;

    /// <summary>8272A data register (BOOT: OUT ($FD),$00).</summary>
    public const ushort Port8272Data = 0x0FD;

    // No i8212 entry, and none is owed: the CoBra Hardware Manual rev.1.0 (p.14)
    // names the i8212 (U76) as the "separation and storage register" that holds a
    // byte the CPU reads from video RAM while the memory access prioritizer keeps it
    // in WAIT, driven by its DS selection inputs - a latch on the memory path, not an
    // I/O device, so there is no "8212 base address" to find.

    /// <summary>CTC channels 0-3 (BOOT: OUT ($E3|$EB|$F3|$FB),$03).</summary>
    public const ushort CtcChannel0 = 0x0E3;
    public const ushort CtcChannel1 = 0x0EB;
    public const ushort CtcChannel2 = 0x0F3;
    public const ushort CtcChannel3 = 0x0FB;
}

/// The mainboard I/O decode as the Z80 sees it: the 8255A, the external
/// 8-bit input port, and slots for the devices that plug in
/// (8272A at $0FD, CTC at $E3/$EB/$F3/$FB).
public sealed class MainboardIo
{
    private readonly Dictionary<ushort, Func<ushort, byte>> _reads = new();
    private readonly Dictionary<ushort, Action<ushort, byte>> _writes = new();

    /// <summary>The 8255A (U79).</summary>
    public Port8255 Eight255 { get; } = new();

    /// <summary>J5A+B, "8-BIT INPUT PORT @ 0DFH": the connector of the 8255's port B
    /// (Hardware Manual p.35: j5 pins 1b-8b = PB0-PB7), so it is port B's cells.</summary>
    public byte ExternalInput
    {
        get => Eight255.PortBInput;
        set => Eight255.PortBInput = value;
    }

    public void Reset() => Eight255.Reset();

    /// <summary>Plugs a device (8272A, CTC) into the decode.</summary>
    public void RegisterRead(ushort port, Func<ushort, byte> read) => _reads[port] = read;

    /// <summary>Plugs a device (8272A, CTC) into the decode.</summary>
    public void RegisterWrite(ushort port, Action<ushort, byte> write) => _writes[port] = write;

    /// <summary>The main board decodes I/O on two address lines only (mainboard v.0
    /// schematic, page 15): the 8255 is selected when A5 differs from A0
    /// (U35 XOR -> NICS), with its A0 = BA0 and its A1 = /RD, so the direction picks the
    /// register: IN with A0 = 0 reads port A, OUT there writes port C; IN with A0 = 1
    /// reads port B, OUT there writes the control register.</summary>
    public static bool Selects8255(ushort port) => ((port & 0x01) != 0) != ((port & 0x20) != 0);

    /// <summary>Z80 IN (n),A, decoded on the low address byte. An address the 8255 does not
    /// answer goes to a registered floppy-board device, and otherwise reads $FF: the
    /// unbuffered data bus has 4.7K pull-ups (v.0 schematic page 16).</summary>
    public byte Read(ushort port)
    {
        var low = (byte)port;
        if (Selects8255(low))
        {
            return (low & 0x01) == 0 ? Eight255.ReadPortA() : Eight255.ReadPortB();
        }

        return _reads.TryGetValue(low, out var read) ? read(port) : (byte)0xFF;
    }

    /// <summary>Z80 OUT (n),A, decoded like <see cref="Read"/>; nothing answers the rest.</summary>
    public void Write(ushort port, byte value)
    {
        var low = (byte)port;
        if (Selects8255(low))
        {
            if ((low & 0x01) == 0)
            {
                Eight255.WritePortC(value);
            }
            else
            {
                Eight255.WriteControlWord(value);
            }

            return;
        }

        if (_writes.TryGetValue(low, out var write))
        {
            write(port, value);
        }
    }
}
