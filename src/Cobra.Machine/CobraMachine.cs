using Hc2000.Z80;

namespace Cobra.Machine;

/// The CPU, the memory and the mainboard I/O wired together: enough machine to
/// run the BOOT through its configuration switches. Video, the keyboard
/// matrix, the 8272A and the CTC plug into the I/O decode.
///
/// The Z80 core is the HC2000 emulator's, unchanged;
/// this class uses only its public bus contract. Every opcode fetch comes
/// through <see cref="FetchM1"/>, which reads the opcode in the configuration
/// in force and then lets the refresh half of that M1 cycle clock the
/// configurator with R bit 7. Refreshes the core does not report (an interrupt
/// acknowledge, a HALT) are not sampled; R bit 7 only changes with LD R,A, so
/// this matters only for an interrupt taken right after one, which no
/// published sequence does.
public sealed class CobraMachine : IM1FetchBus
{
    /// <param name="original64K">The original 64K board instead of the reference 80K one (see
    /// <see cref="CobraMemory.Original64K"/>); it has no AV13 select and no 80 characters/line circuit.</param>
    public CobraMachine(byte[] bootRom, byte[] systemRom, long powerOnHoldTStates = ConfigurationLatch.DefaultPowerOnHoldTStates, bool original64K = false)
    {
        Memory = new CobraMemory(bootRom, systemRom) { Original64K = original64K };
        if (original64K) EightyColumnCircuit = false;
        PowerOnHoldTStates = powerOnHoldTStates;
        Cpu = new Z80(this);
        Beeper = new Beeper(Io.Eight255, () => Cpu.TStates);
        Tape = new TapeDeck(Io.Eight255, () => Cpu.TStates);
        Floppy = new FloppyBoard();
        PowerOn();
    }

    public Z80 Cpu { get; }

    public CobraMemory Memory { get; }

    public MainboardIo Io { get; } = new();

    public ConfigurationLatch Configurator { get; } = new();

    public long PowerOnHoldTStates { get; }

    public HardwareConfiguration Configuration => Configurator.Configuration;

    /// <summary>The floppy interface board on JEXA/JEXB: i8272A, Z80-CTC and four drives.</summary>
    public FloppyBoard Floppy { get; }

    private bool _boardOutOfReset;
    private bool _lastFetchWasEd;

    /// <summary>The cassette: TO on port C bit 3, TI on port A bit 6.</summary>
    public TapeDeck Tape { get; }

    /// <summary>The speaker on port C bit 4.</summary>
    public Beeper Beeper { get; }

    /// <summary>The Kempston joystick on port B bits 0..4 (J5's PB5..PB7 are not modelled).</summary>
    public Joystick Joystick
    {
        get => (Joystick)(Io.Eight255.PortBInput & 0x1F);
        set => Io.Eight255.PortBInput = (byte)((Io.Eight255.PortBInput & 0xE0) | (byte)value);
    }

    /// <summary>The keyboard matrix on port A bits 0..5 (<see cref="CobraKeyboard"/>).</summary>
    public CobraKeyboard Keyboard { get; } = new();

    /// <summary>Port A bits 6 (tape in, TI) and 7 (serial in, SI); idle high.</summary>
    public bool TapeIn { get; set; } = true;

    public bool SerialIn { get; set; } = true;

    /// <summary>Replaces the keyboard with a function of the IN's high address byte (test use).</summary>
    public Func<byte, byte>? KeyboardScan { get; set; }

    /// <summary>Power-on: CPU reset, 8255 reset, configurator preset. The CPU runs at
    /// once; NPOR (the 8255's POR and U36's preset) stays on for
    /// <see cref="PowerOnHoldTStates"/> more.</summary>
    public void PowerOn()
    {
        Io.Reset();
        Cpu.Reset();
        Configurator.PowerOn(PowerOnHoldTStates);
        Floppy?.SetReset(true);
        _boardOutOfReset = false;
        _lastFetchWasEd = false;
    }

    /// <summary>CPU T-states per TV line: 56 character slots of 4 (64 us at 3.5 MHz).</summary>
    public const int TStatesPerLine = 224;

    /// <summary>TV lines per frame: the vertical counter resets at 312 (v.0 schematic page 6).</summary>
    public const int LinesPerFrame = 312;

    /// <summary>69 888 T-states, a 19.968 ms frame.</summary>
    public const long TStatesPerFrame = TStatesPerLine * LinesPerFrame;

    /// <summary>The frame-start pulse VLD lasts until HB falls, about 80 pixel clocks, 40
    /// T-states, at the start of line 0 (v.0 schematic page 6).</summary>
    public const int InterruptTStates = 40;

    /// <summary>The Z80's /INT: NKBI = NAND(NLO6, VLD) (v.0 schematic page 3), so the frame
    /// interrupt reaches the CPU only in the BASIC configuration, during the first 40
    /// T-states of each frame. Frames count from power-on; line 0 is where the counter
    /// first resets, so the phase is arbitrary and fixed.</summary>
    public bool InterruptActive =>
        Configuration == HardwareConfiguration.Basic && Cpu.TStates % TStatesPerFrame < InterruptTStates;

    /// <summary>One instruction (an interrupt is taken at its boundary when /INT is low and
    /// the CPU has interrupts enabled); returns its T-states.</summary>
    public int Step()
    {
        Cpu.IntLine = InterruptActive || Floppy.Ctc.InterruptRequest;
        var before = Cpu.TStates;
        var tStates = Cpu.Step();
        Floppy.Ctc.Tick(Cpu.TStates - before);
        Floppy.AdvanceTo(Cpu.TStates);
        UpdateEightyColumnLatch();
        var line = Cpu.TStates % TStatesPerFrame / TStatesPerLine;
        Floppy.Ctc.Trigger(3, line is < 240 or >= 256); // NVS: low during vertical sync, lines 240-255
        if (!_boardOutOfReset && !PowerOnResetActive)
        {
            Floppy.SetReset(false);
            _boardOutOfReset = true;
        }

        return tStates;
    }

    /// <summary>The 80 characters/line circuit is fitted (the module of main board v.0.2, built into v.1.3;
    /// the published CP/M 80-column systems need it). Default on.</summary>
    public bool EightyColumnCircuit { get; private init; } = true;

    /// <summary>The circuit's select latch (U04/U27 NAND pair, mainboard v.0.2 schematic, module page,
    /// v.1.3 add-on #2): with X = NLO6 XOR NPO it is set while X = 1 and O5 = 1, holds while X = 1, and clears
    /// when X = 0. X = 1 only in the CP/M configuration (LO6 1, PO 0). Set, AV13 follows the character
    /// counter's AC5 and AC5new = 0 (no horizontal border): the 80-column screen.</summary>
    public bool EightyColumnLatch { get; private set; }

    /// <summary>The video controller reads the second 8 KB half for the whole picture: AV13 = O5 * LO6 while the
    /// 80-column latch is clear (the 80K modification's AV13).</summary>
    public bool VideoSecondHalf => !Memory.Original64K && !EightyColumnLatch && Configurator.LO6 && (Io.Eight255.PortCPins & Port8255.O5Bit) != 0;

    private void UpdateEightyColumnLatch()
    {
        var x = Configurator.LO6 ^ Configurator.PO;
        if (!EightyColumnCircuit || !x) EightyColumnLatch = false;
        else if ((Io.Eight255.PortCPins & Port8255.O5Bit) != 0) EightyColumnLatch = true;
    }

    /// <summary>The NMI push button (K3 on v.1.3 add-on #2, the 80 chars/line module's NMIBTN on v.0.2): a
    /// debounced single falling edge on the Z80's NMI per press (mainboard v.0.2 schematic, module page).</summary>
    public void PressNmiButton() => Cpu.Nmi();

    /// <summary>Runs until the CPU's T-state counter reaches <paramref name="tStates"/>.</summary>
    public void RunUntil(long tStates)
    {
        while (Cpu.TStates < tStates)
        {
            Step();
        }
    }

    /// <summary>Runs <paramref name="frames"/> whole frames from the next frame start.</summary>
    public void RunFrames(int frames)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frames);
        RunUntil((Cpu.TStates / TStatesPerFrame + frames) * TStatesPerFrame);
    }

    /// <summary>Video RAM contention: the video circuit reads its RAM once per character cell,
    /// every 4 CPU clocks, and the CPU gets the cell's other slot (t1-t2 of the 8-state
    /// sequencer; v.0 schematic page 1, arbiter U02). A CPU access to video RAM pulls WAIT until
    /// its slot comes round: the manual's Fig. 6 marks 4, 3, 2 and 1 wait states for a request
    /// whose MREQ falls in the first, second, third and fourth CPU clock of the cell. The
    /// sequencer runs all the time (it also refreshes the video RAM), so there is no
    /// uncontended border. The cell's phase against the CPU's T-state counter is fixed at
    /// power-on and not given by the sources; it is taken as T-state 0 here.</summary>
    public bool VideoContention { get; set; } = true;

    /// <summary>Wait states for a memory access to video RAM whose T1 starts at <paramref name="start"/>.</summary>
    public static int VideoWaitStates(long start) => 4 - (int)(start & 3);

    /// <summary>Reads memory as the CPU would see it now, without taking any CPU time (for tests and debuggers).</summary>
    public byte Peek(ushort addr) => Memory.Read(addr, Configuration, Io.Eight255);

    public byte Read(ushort addr)
    {
        // The core has already counted this read's 3 T-states (T1 started 3 earlier).
        Wait(addr, Cpu.TStates - 3);
        return Memory.Read(addr, Configuration, Io.Eight255);
    }

    public void Write(ushort addr, byte value)
    {
        Wait(addr, Cpu.TStates - 3);
        Memory.Write(addr, value, Configuration, Io.Eight255);
    }

    private void Wait(ushort addr, long start)
    {
        if (VideoContention && Memory.WindowAt(addr, Configuration, Io.Eight255) == MemoryWindow.Video)
        {
            Cpu.TStates += VideoWaitStates(start);
        }
    }

    public byte FetchM1(ushort address, bool instructionStart, long cpuTStates)
    {
        Wait(address, cpuTStates - 4);
        var opcode = Memory.Read(address, Configuration, Io.Eight255);
        // RETI (ED 4D) on the bus ends the CTC's service, as the chip watches the opcode fetches.
        if (_lastFetchWasEd && opcode == 0x4D) Floppy.Ctc.ReturnFromInterrupt();
        _lastFetchWasEd = instructionStart && opcode == 0xED;
        Configurator.Refresh((Cpu.R.R & 0x80) != 0, Io.Eight255.PinO6, cpuTStates);
        return opcode;
    }

    /// <summary>The main board decodes I/O on the low address byte; the high byte of a
    /// port A read carries the keyboard row select (A8..A15 drive KA8..KA15).</summary>
    public byte In(ushort port)
    {
        var value = Input(port);
        IoObserver?.Invoke(new IoAccess(port, value, false, Cpu.TStates, Cpu.R.PC));
        return value;
    }

    /// <summary>Sees every IN and OUT after it happens (debuggers and tests); null costs nothing.</summary>
    public Action<IoAccess>? IoObserver { get; set; }

    private byte Input(ushort port)
    {
        var low = (byte)port;
        if (!MainboardIo.Selects8255(low) && Floppy.Selects(low))
        {
            return Floppy.Read(low, Cpu.TStates);
        }

        if (MainboardIo.Selects8255(low) && (low & 0x01) == 0)
        {
            var rows = (byte)(port >> 8);
            Io.Eight255.PortAInput = KeyboardScan is { } scan
                ? scan(rows)
                : (byte)((Keyboard.Scan(rows, Cpu.TStates) & 0x3F) | (TapeIn && Tape.InputLevel(Cpu.TStates) ? 0x40 : 0) | (SerialIn ? 0x80 : 0));
        }

        return Io.Read(low);
    }

    /// <summary>True until NPOR turns off: U36 preset and the 8255 in reset.</summary>
    public bool PowerOnResetActive => Cpu.TStates <= PowerOnHoldTStates;

    public void Out(ushort port, byte value)
    {
        Output(port, value);
        IoObserver?.Invoke(new IoAccess(port, value, true, Cpu.TStates, Cpu.R.PC));
    }

    private void Output(ushort port, byte value)
    {
        var low = (byte)port;
        if (!MainboardIo.Selects8255(low) && Floppy.Selects(low))
        {
            if (!PowerOnResetActive) Floppy.Write(low, value, Cpu.TStates);
            return;
        }

        Io.Eight255.HeldInReset = PowerOnResetActive;
        Io.Write(low, value);
    }

    public void Contend(ushort addr, int tstates)
    {
    }

    /// <summary>Interrupt acknowledge: the CTC puts its vector on the bus when it is the requester; the
    /// frame interrupt (NKBI) drives nothing and the pull-ups read FF.</summary>
    public byte IntVector() => Floppy.Ctc.InterruptRequest ? Floppy.Ctc.Acknowledge() : (byte)0xFF;
}

/// <summary>One I/O cycle as the bus saw it: the full 16-bit port, the byte, its direction, the CPU's
/// T-state counter and the PC (the instruction after the IN/OUT, as the core has advanced it).</summary>
public readonly record struct IoAccess(ushort Port, byte Value, bool IsWrite, long TStates, ushort Pc);
