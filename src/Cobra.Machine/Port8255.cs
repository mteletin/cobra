namespace Cobra.Machine;

/// The 8255A parallel I/O chip (U79, mainboard v.0), one configuration only:
/// ports A and B input, port C output.
/// Port A: keyboard matrix lines K0-K5 plus tape-in. Port B: two Kempston
/// joystick ports (J6) - kept on the chip; the sources give no Z80-visible
/// address for it. Port C: the output nets TO (tape out), LS
/// (loudspeaker), SO (serial out / SYSTEM ROM A14), O5, O6 and BORDER.
public sealed class Port8255
{
    // Port-C bit identities given by the published BOOT disassembly
    // (BOOT_2K_COBRA_80K updated, 0032-0034 and the CTRL branch at 0061-0065):
    //   $40 -> "set border to black, SO to 0, O5 to 0, O6 to 1"
    //   $03 -> "set border to white, SO to 0, O5 to 0, O6 to 0"
    //   (IN A,($FE); OR $40; XOR $20; OUT ($FE),A) -> "set O6 to 1, O5 to 1"
    // So O6 is bit 6, O5 is bit 5, and BORDER follows the same bit 6 on the
    // v.0 board (black when set, white when clear). The remaining nets (TO,
    // LS, SO) are not given bit by bit there, so only the raw byte and the
    // known bits are asserted.
    public const int O5Bit = 0x20;
    public const int O6Bit = 0x40;
    public const int BorderBit = 0x40;

    // SYSTEM ROM quarter select, given by the Boot Manager source
    // (MasterBoot-CoBra+CPM_rev.01.asm, ROM select routine): "bit 3 (TO=A15)",
    // "bit 7 (SO=A14)"; OPUS = SO 0 TO 0, BASIC = SO 1 TO 0, DEVIL = SO 0 TO 1.
    // The 80K STD BOOT sets SO with the bit set/reset command $0F before its
    // BASIC copy (07F0: "set bit 7 of Port C ... SO drives pin A14").
    public const int SoBit = 0x80;
    public const int ToBit = 0x08;

    // 8255 datasheet reset state: control word 0x80, port-C latch low.
    public const byte ResetControlWord = 0x80;

    private byte _portA;
    private byte _portB;
    private byte _portC;
    private byte _control;
    private bool _portCUpperOutput = true;
    private bool _portCLowerOutput = true;

    /// <summary>Raised for every port-C write: the value latched.</summary>
    public event Action<byte>? PortCWritten;

    /// <summary>Port A input stub cells (keyboard + tape-in).</summary>
    public byte PortAInput
    {
        get => _portA;
        set => _portA = value;
    }

    /// <summary>Port B input stub cells (two Kempston joystick ports).</summary>
    public byte PortBInput
    {
        get => _portB;
        set => _portB = value;
    }

    /// <summary>The port-C output latch as last written.</summary>
    public byte PortC => _portC;

    /// <summary>The last mode-set control word written to $0DF (a bit
    /// set/reset command changes port C, not the mode).</summary>
    public byte ControlWord => _control;

    /// <summary>True while the board's POR line holds the chip in reset: the
    /// Z80 leaves reset a few milliseconds earlier (Hardware Manual rev.1.0, p.10:
    /// "the diode D02 makes sure the microprocessor exits the reset state a few
    /// miliseconds before the NPOR signal turns off"), and OUTs to the chip in that
    /// time "will have no effect" (boot.html). The machine sets it before each OUT.</summary>
    public bool HeldInReset { get; set; }

    /// <summary>Port C as the board sees it: a half programmed as output drives its
    /// latch bits; a half still in input mode (after reset, before the BOOT's mode
    /// set) drives nothing, and the undriven TTL inputs it feeds read high. That is
    /// why the BOOT can clear video RAM at $C000 (0007) before it sets the chip up:
    /// O6 reads 1.</summary>
    public byte PortCPins => (byte)((_portCUpperOutput ? _portC & 0xF0 : 0xF0) | (_portCLowerOutput ? _portC & 0x0F : 0x0F));

    /// <summary>O6 as the configuration logic sees it (<see cref="PortCPins"/>).</summary>
    public bool PinO6 => (PortCPins & O6Bit) != 0;

    /// <summary>SO as the SYSTEM ROM's A14 sees it (<see cref="PortCPins"/>).</summary>
    public bool PinSO => (PortCPins & SoBit) != 0;

    /// <summary>TO as the SYSTEM ROM's A15 sees it (<see cref="PortCPins"/>).</summary>
    public bool PinTO => (PortCPins & ToBit) != 0;

    /// <summary>SO (pin 10): SYSTEM ROM address line A14.</summary>
    public bool SO => (_portC & SoBit) != 0;

    /// <summary>TO: SYSTEM ROM address line A15.</summary>
    public bool TO => (_portC & ToBit) != 0;

    /// <summary>O5: one of the two hardware-configuration bits.</summary>
    public bool O5 => (_portC & O5Bit) != 0;

    /// <summary>O6: the hardware-configuration bit that, with O5, selects the
    /// startup / BASIC / CP/M configurations through the PO/LO6 flip-flops
    /// (the configurator owns the flip-flops; this chip only emits the nets).</summary>
    public bool O6 => (_portC & O6Bit) != 0;

    /// <summary>BORDER net: drives the border colour to the video circuit.
    /// Black when set, white when clear (BOOT init $40 / BASIC switch $03).</summary>
    public bool BorderBlack => (_portC & BorderBit) != 0;

    /// <summary>8255A RESET: output latches cleared and every port in input mode
    /// until the next mode set.</summary>
    public void Reset()
    {
        _portA = 0;
        _portB = 0;
        _portC = 0;
        _control = ResetControlWord;
        _portCUpperOutput = false;
        _portCLowerOutput = false;
    }

    /// <summary>Writes the control register (Z80 $0DF). 8255A datasheet: with
    /// D7 = 1 it is a mode set, which also clears every output latch; with
    /// D7 = 0 it is a port-C bit set/reset: D3-D1 name the bit, D0 sets (1)
    /// or resets (0) it. The BOOT uses both ($92 mode set, $0F = set PC7).</summary>
    public void WriteControlWord(byte value)
    {
        if (HeldInReset)
        {
            return;
        }

        if ((value & 0x80) == 0)
        {
            var mask = (byte)(1 << ((value >> 1) & 7));
            _portC = (value & 1) != 0 ? (byte)(_portC | mask) : (byte)(_portC & ~mask);
            PortCWritten?.Invoke(_portC);
            return;
        }

        _control = value;
        _portC = 0;
        _portCUpperOutput = (value & 0x08) == 0; // D3: port C upper, 0 = output
        _portCLowerOutput = (value & 0x01) == 0; // D0: port C lower, 0 = output
    }

    /// <summary>Latches a port-C output write (Z80 $0FE). Every write fires
    /// <see cref="PortCWritten"/> so a test hook can observe them all.</summary>
    public void WritePortC(byte value)
    {
        if (HeldInReset)
        {
            return;
        }

        _portC = value;
        PortCWritten?.Invoke(value);
    }

    public byte ReadPortA() => _portA;

    public byte ReadPortB() => _portB;
}
