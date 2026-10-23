namespace Cobra.Machine;

/// The Z80-CTC (U01 on the COBRA floppy board; Hardware Manual p.17): four 8-bit down counters.
/// Each channel takes a control word (D0 = 1: D7 interrupt enable, D6 counter mode, D5 prescaler
/// 256 (else 16), D4 rising edge (else falling), D3 timer waits for a trigger, D2 time constant
/// follows, D1 software reset), a time constant (0 = 256), and channel 0 also takes the interrupt
/// vector (D0 = 0: bits 7..3). In counter mode an active CLK/TRG edge counts; in timer mode the
/// system clock counts through the prescaler. At zero the channel reloads, pulses its ZC/TO output
/// (channels 0..2) and, if enabled, requests an interrupt. Interrupts are prioritised channel 0
/// first along the daisy chain; an acknowledged channel stays in service until the CPU's RETI,
/// and while it is in service no lower channel interrupts.
public sealed class Z80Ctc
{
    private readonly Channel[] _channels = [new(), new(), new(), new()];
    private byte _vector;

    /// <summary>ZC/TO of channel 0..2 pulsed (a channel counted down to zero).</summary>
    public event Action<int>? ZeroCount;

    public void Reset()
    {
        foreach (var c in _channels) c.Reset();
        _vector = 0;
    }

    /// <summary>The down counter of <paramref name="channel"/>.</summary>
    public byte Read(int channel) => (byte)_channels[channel].Down;

    public void Write(int channel, byte value)
    {
        var c = _channels[channel];
        if (c.WaitingConstant)
        {
            c.Constant = value == 0 ? 256 : value;
            c.Down = c.Constant;
            c.WaitingConstant = false;
            c.Running = c.Counter || (c.ControlWord & 0x08) == 0; // timer: auto-start unless it waits for a trigger
            c.Prescale = 0;
            return;
        }

        if ((value & 0x01) == 0)
        {
            if (channel == 0) _vector = (byte)(value & 0xF8);
            return;
        }

        c.ControlWord = value;
        if ((value & 0x02) != 0)
        {
            c.Running = false;
            c.Pending = false;
        }

        if ((value & 0x80) == 0) c.Pending = false;
        c.WaitingConstant = (value & 0x04) != 0;
    }

    /// <summary>The CLK/TRG input of <paramref name="channel"/> changed to <paramref name="level"/>.</summary>
    public void Trigger(int channel, bool level)
    {
        var c = _channels[channel];
        var edge = level != c.Input && level == ((c.ControlWord & 0x10) != 0);
        c.Input = level;
        if (!edge || c.WaitingConstant) return;
        if (c.Counter)
        {
            if (c.Running) Count(channel);
        }
        else if (!c.Running && (c.ControlWord & 0x08) != 0 && c.Constant > 0)
        {
            c.Running = true; // timer started by its trigger
            c.Prescale = 0;
        }
    }

    /// <summary>Advances the timer-mode channels by <paramref name="clocks"/> system clocks.</summary>
    public void Tick(long clocks)
    {
        for (var ch = 0; ch < 4; ch++)
        {
            var c = _channels[ch];
            if (!c.Running || c.Counter) continue;
            var divider = (c.ControlWord & 0x20) != 0 ? 256 : 16;
            c.Prescale += clocks;
            while (c.Prescale >= divider && c.Running)
            {
                c.Prescale -= divider;
                Count(ch);
            }
        }
    }

    private void Count(int ch)
    {
        var c = _channels[ch];
        if (--c.Down > 0) return;
        c.Down = c.Constant;
        if ((c.ControlWord & 0x80) != 0) c.Pending = true;
        if (ch < 3) ZeroCount?.Invoke(ch);
    }

    /// <summary>INT: a channel has an interrupt pending and no higher-priority channel is in service.</summary>
    public bool InterruptRequest => Requester() >= 0;

    private int Requester()
    {
        for (var ch = 0; ch < 4; ch++)
        {
            if (_channels[ch].InService) return -1;
            if (_channels[ch].Pending) return ch;
        }

        return -1;
    }

    /// <summary>The CPU's interrupt acknowledge: the requesting channel goes in service and its vector is returned.</summary>
    public byte Acknowledge()
    {
        var ch = Requester();
        if (ch < 0) return 0xFF;
        _channels[ch].Pending = false;
        _channels[ch].InService = true;
        return (byte)(_vector | ch << 1);
    }

    /// <summary>RETI seen on the bus: the highest-priority channel in service ends its service.</summary>
    public void ReturnFromInterrupt()
    {
        foreach (var c in _channels)
        {
            if (!c.InService) continue;
            c.InService = false;
            return;
        }
    }

    public bool InService(int channel) => _channels[channel].InService;

    private sealed class Channel
    {
        public byte ControlWord;
        public int Constant;
        public int Down;
        public bool WaitingConstant;
        public bool Running;
        public bool Input;
        public bool Pending;
        public bool InService;
        public long Prescale;

        public bool Counter => (ControlWord & 0x40) != 0;

        public void Reset()
        {
            ControlWord = 0;
            Constant = Down = 0;
            WaitingConstant = Running = Pending = InService = false;
            Prescale = 0;
        }
    }
}
