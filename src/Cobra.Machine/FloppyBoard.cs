using Hc2000.Machine.Floppy;
using Hc2000.Machine.Storage;
using Hc2000.Machine.Timing;

namespace Cobra.Machine;

/// The COBRA floppy interface board (floppy board schematics; Hardware Manual pp.17-18),
/// on the main board's JEXA/JEXB connectors. It has no address decoder: the i8272A answers any I/O
/// cycle with A1 = 0, its register select A0 being A3 (status at F5, data at FD), and the Z80-CTC
/// answers A2 = 0, channel A4:A3 (E3, EB, F3, FB). The 8272A runs without DMA: its INT drives CTC
/// channel 0's trigger, channels 0, 1 and 2 are cascaded (each ZC/TO triggers the next), and
/// channel 2's ZC/TO is the 8272A's terminal count, so the BIOS programs channel 0 to interrupt per
/// byte and channels 1-2 to count the sector's bytes and end the transfer. Channel 3's trigger is
/// NVS, the main board's vertical sync (JEXA/9). Four drives hang on the 8272A's US0/US1 select;
/// each reports READY while it holds a disk (v.0: one READY line per drive), its head-load line
/// is its motor, and every drive is clocked at 4 MHz / 250 kbit/s (3.5" and 5.25" DD; the 8 MHz
/// 8" setting goes through Fdc8272.TimingForUnit when a board profile needs it).
///
/// The 8272A core is the HC2000 emulator's, unchanged, driven
/// on its own device scheduler at 14 MHz, four ticks per CPU T-state.
public sealed class FloppyBoard
{
    public const long MasterHz = 14_000_000;
    public const int TicksPerTState = 4;

    private readonly FloppyDrive[] _drives = new FloppyDrive[4];
    private readonly Func<int, int, bool?>?[] _recording = new Func<int, int, bool?>?[4];

    public FloppyBoard()
    {
        Timing = new FdcTiming(MasterHz: MasterHz, FdcHz: 4_000_000, MfmBitsPerSecond: 250_000, Rpm: 300);
        for (var i = 0; i < 4; i++) _drives[i] = new FloppyDrive(new DriveProfile(80, 2));
        Fdc = new Fdc8272(Scheduler, 10, unit => _drives[unit], Timing, instant: false)
        {
            DriveReady = unit => _drives[unit].Image is not null,
            TrackIsMfm = (unit, cylinder, side) => _recording[unit]?.Invoke(cylinder, side),
        };
        Fdc.InterruptChanged += (level, _) => Ctc.Trigger(0, level);
        Ctc.ZeroCount += channel =>
        {
            if (channel < 2)
            {
                Ctc.Trigger(channel + 1, true);
                Ctc.Trigger(channel + 1, false);
            }
            else
            {
                Fdc.TerminalCount();
            }
        };
    }

    public DeviceScheduler Scheduler { get; } = new();

    public FdcTiming Timing { get; }

    public Fdc8272 Fdc { get; }

    public Z80Ctc Ctc { get; } = new();

    public FloppyDrive Drive(int unit) => _drives[unit];

    /// <summary>Puts a disk in a drive (or empties it); the drive spins while it holds one. A disk given as
    /// a container is taken as MFM on every track, as every COBRA format records (CP/M 720K/860K, DEVIL).</summary>
    public void Insert(int unit, Dsk? disk) => Insert(unit, disk, disk is null ? null : (_, _) => true);

    /// <summary>Puts a decoded disk in a drive: its sectors as recorded, and each track side's density
    /// (FM or MFM, from its sectors; an unformatted side has none) for the controller's MF check.</summary>
    public void Insert(int unit, Media.FloppyImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        Insert(unit, FromImage(image), (cylinder, side) =>
            cylinder < image.Cylinders && side < image.Heads && image.Track(cylinder, side) is { Count: > 0 } t ? t[0].Mfm : null);
    }

    private void Insert(int unit, Dsk? disk, Func<int, int, bool?>? recording)
    {
        _recording[unit] = recording;
        var drive = _drives[unit];
        drive.SetMotor(false, Scheduler.Now);
        drive.HeadLoaded = false;
        drive.Mount(disk);
        if (disk is not null) drive.SetMotor(true, Scheduler.Now);
        Fdc.PollReady(); // RDY changed: the chip's poll between commands interrupts with the new state
    }

    /// <summary>A raw CP/M 720K image (80 x 2 x 9 x 512) as the drive's disk.</summary>
    public static Dsk Cpm720(byte[] raw) => Dsk.Load(raw, new DiskGeometry(80, 2, 9, 512));

    /// <summary>A decoded disk (raw in any COBRA format, Teledisk, HFE) as the drive's disk.</summary>
    public static Dsk FromImage(Media.FloppyImage image) => Dsk.Load(image.ToExtendedDsk());

    public static bool SelectsFdc(byte port) => (port & 0x02) == 0;

    public static bool SelectsCtc(byte port) => (port & 0x04) == 0;

    public bool Selects(byte port) => SelectsFdc(port) || SelectsCtc(port);

    /// <summary>Brings the board's devices up to <paramref name="tStates"/> of CPU time.</summary>
    public void AdvanceTo(long tStates)
    {
        var target = tStates * TicksPerTState;
        if (target > Scheduler.Now) Scheduler.AdvanceTo(target);
    }

    public byte Read(byte port, long tStates)
    {
        AdvanceTo(tStates);
        var value = 0xFF;
        if (SelectsFdc(port)) value &= (port & 0x08) != 0 ? Fdc.ReadData() : Fdc.ReadStatus();
        if (SelectsCtc(port)) value &= Ctc.Read((port >> 3) & 3);
        return (byte)value;
    }

    public void Write(byte port, byte value, long tStates)
    {
        AdvanceTo(tStates);
        if (SelectsFdc(port) && (port & 0x08) != 0) Fdc.WriteData(value); // the status register is read-only
        if (SelectsCtc(port)) Ctc.Write((port >> 3) & 3, value);
    }

    /// <summary>The board's RESET (POR from the main board, JEXA/7).</summary>
    public void SetReset(bool asserted)
    {
        Fdc.SetReset(asserted);
        if (asserted) Ctc.Reset();
    }
}
