namespace Cobra.Machine;

/// The three hardware configurations (the CoBra Project's hardware
/// notes, basic+nmi.html): startup/CoBra (PO 1), BASIC/Spectrum (PO 0, LO6 0) and
/// CP/M (PO 0, LO6 1).
public enum HardwareConfiguration
{
    Startup,
    Basic,
    Cpm,
}

/// The configurator: two halves of a 74LS74 (U36 on v.0.2).
///
/// PO samples address line BA7 on every refresh cycle, and during refresh the
/// Z80 drives BA7 with bit 7 of its R register, which counting never changes:
/// "the bit is reset and appears on address line BA7 during the first refresh
/// cycle after the instruction that reset it" (the CoBra Project's hardware notes).
/// The BOOT switches with OUT ($FE),A ; LD R,A ; JP (HL): the opcode of
/// JP (HL) is still read in the old configuration, its refresh moves PO, and
/// the jump target is fetched in the new one (80K STD BOOT 0051-0055).
///
/// From BASIC there is no way back: LO6 = 0 holds PO cleared (U36/1 = LO6, v.0
/// schematic page 3), so only power-on (NPOR presets both) leaves BASIC.
///
/// LO6 ("latched O6") takes the 8255's O6 net when PO falls, so the
/// configuration a switch lands in is decided by O6 at that moment: $41 (O6 1)
/// gives CP/M, $00 or $03 (O6 0) gives BASIC (BOOT 055E and 0051). PO rising
/// leaves LO6 alone, so from CP/M the only way to BASIC is up to startup and
/// down again ("CP/M must pass through startup", basic+nmi.html; the CP/M Game
/// Loader page). The site text says the second flip-flop is clocked by PO on
/// its rising edge; with LO6 = O6 sampled on PO's rise, the BOOT's own startup
/// to BASIC switch would land in CP/M, so the clock is PO's complement (PO
/// falling), which every published switch sequence agrees with.
///
/// Power-on: "The two flip-flops (U36) are forced to
/// '1' at power-up, by a circuit made of C15, D02, R09, D03. The diode D02 makes
/// sure the microprocessor exits the reset state a few miliseconds before the
/// NPOR signal turns off. During this time the microprocessor executes the first
/// instructions where bit 7 of the R register is set to '1', which makes sure the
/// flip-flop U36/5 is maintained at '1' after NPOR turns off" (CoBra Hardware
/// Manual rev.1.0, pp.9-10). The Z80 resets with R = 0, so without that hold the
/// first refresh would already leave startup. The same NPOR, as POR, holds the
/// 8255 in reset (<see cref="Port8255.HeldInReset"/>).
public sealed class ConfigurationLatch
{
    /// <summary>How long NPOR outlasts the Z80's reset: "a few miliseconds" (manual
    /// p.10); 3 ms at 3.5 MHz. The network is R09 2K2 and C15 10 uF (manual parts
    /// list), a 22 ms time constant whose two thresholds the manual does not give.
    /// Any value from the end of the BOOT's LD R,A (16 T-states) to its first 8255
    /// write (002E, about 0.19 M T-states) boots the same; a test holds it there.</summary>
    public const long DefaultPowerOnHoldTStates = 10_500;

    /// <summary>PO (U36/5).</summary>
    public bool PO { get; private set; } = true;

    /// <summary>LO6, the latched O6.</summary>
    public bool LO6 { get; private set; } = true;

    /// <summary>CPU T-state up to which NPOR still presets PO.</summary>
    public long PowerOnHoldUntil { get; private set; }

    public HardwareConfiguration Configuration =>
        PO ? HardwareConfiguration.Startup : LO6 ? HardwareConfiguration.Cpm : HardwareConfiguration.Basic;

    /// <summary>Power-on reset: PO and LO6 preset, PO held for <paramref name="holdTStates"/>.</summary>
    public void PowerOn(long holdTStates = DefaultPowerOnHoldTStates)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(holdTStates);
        PO = true;
        LO6 = true;
        PowerOnHoldUntil = holdTStates;
    }

    /// <summary>One refresh cycle: <paramref name="ba7"/> is R bit 7, <paramref name="o6"/>
    /// the 8255's O6 net, <paramref name="tStates"/> the CPU's T-state counter.</summary>
    public void Refresh(bool ba7, bool o6, long tStates)
    {
        if (tStates <= PowerOnHoldUntil)
        {
            return;
        }

        // LO6 drives PO's clear input (U36/1 = LO6 on v.0 page 3 and v.1.3 page 3): once
        // LO6 is 0, PO is held at 0 and LO6 is never clocked again, so BASIC is one-way
        // until power-on. A Spectrum program's own LD R,A cannot leave BASIC.
        if (!LO6)
        {
            PO = false;
            return;
        }

        if (PO && !ba7)
        {
            PO = false;
            LO6 = o6;
        }
        else if (!PO && ba7)
        {
            PO = true;
        }
    }
}
