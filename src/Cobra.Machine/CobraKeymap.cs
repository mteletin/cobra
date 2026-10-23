namespace Cobra.Machine;

/// <summary>
/// The documented keymaps of the published OSes, against the matrix as the board has it
/// (the keyboard v.0/v.1.0 schematics, pages 2 and 15, switch references
/// K200..K513; the CoBra software documentation; CoBra BASIC manual, 1988 ITCI).
///
/// The machine carries no keymap of its own: port A returns the raw matrix and the guest
/// ROMs (BOOT, BASIC, CP/M) do the mapping. These tables are what the tests type with,
/// so a wrong keymap surfaces as a wrong character in the guest while the raw scan
/// stays right - a keymap fault cannot look like a matrix fault.
/// </summary>
public static class CobraKeymap
{
    /// <summary>The key menu of the standard 80K BOOT (BOOT_2K_COBRA_80K 0115-012B): B loads
    /// BASIC, C loads tape, D loads CP/M from disk, W loads OPUS, F4 or CTRL starts the
    /// checksum routine (test pattern). The menu's rows KA15/KA10/KA9/KA8 (LD A,$78) put
    /// these keys in columns 1-4, and F4/CTRL - the K5 column - in column 5.</summary>
    public static IReadOnlyDictionary<string, CobraKey[]> BootKeyMenu { get; } = new Dictionary<string, CobraKey[]>
    {
        ["B"] = [CobraKey.B],    // 0127 JP NC,$07F0
        ["C"] = [CobraKey.C],    // 0123 JP NC,$0537
        ["D"] = [CobraKey.D],    // 011F JP NC,$0552
        ["W"] = [CobraKey.W],    // 011B JP NC,$07F4
        ["F4"] = [CobraKey.F4],  // 012B JP NC,$0497
        ["CTRL"] = [CobraKey.Ctrl], // 012B: "if F4 or CTRL pressed"
    };

    /// <summary>The published BASIC's keymap (Sinclair Spectrum BASIC:
    /// the SYSTEM ROM's BASIC is Spectrum BASIC plus COPY and MON, keymap unchanged; the 1988
    /// CoBra BASIC manual keeps K/L/E modes, and in K mode an unshifted letter is the lower
    /// (black) command - P is PRINT). Letters and digits sit at the sheet's documented
    /// positions; the SYMBOL SHIFT symbols are the Spectrum 48K map; BREAK is CAPS SHIFT +
    /// SPACE (manual 2.1). The K5 column adds CTRL, NO SCR, LF, ESC and F1-F4 as the sheet
    /// draws them.</summary>
    public static IReadOnlyDictionary<string, CobraKey[]> Basic { get; } = new Dictionary<string, CobraKey[]>
    {
        ["A"] = [CobraKey.A], ["B"] = [CobraKey.B], ["C"] = [CobraKey.C], ["D"] = [CobraKey.D],
        ["E"] = [CobraKey.E], ["F"] = [CobraKey.F], ["G"] = [CobraKey.G], ["H"] = [CobraKey.H],
        ["I"] = [CobraKey.I], ["J"] = [CobraKey.J], ["K"] = [CobraKey.K], ["L"] = [CobraKey.L],
        ["M"] = [CobraKey.M], ["N"] = [CobraKey.N], ["O"] = [CobraKey.O], ["P"] = [CobraKey.P],
        ["Q"] = [CobraKey.Q], ["R"] = [CobraKey.R], ["S"] = [CobraKey.S], ["T"] = [CobraKey.T],
        ["U"] = [CobraKey.U], ["V"] = [CobraKey.V], ["W"] = [CobraKey.W], ["X"] = [CobraKey.X],
        ["Y"] = [CobraKey.Y], ["Z"] = [CobraKey.Z],
        ["0"] = [CobraKey.D0], ["1"] = [CobraKey.D1], ["2"] = [CobraKey.D2], ["3"] = [CobraKey.D3],
        ["4"] = [CobraKey.D4], ["5"] = [CobraKey.D5], ["6"] = [CobraKey.D6], ["7"] = [CobraKey.D7],
        ["8"] = [CobraKey.D8], ["9"] = [CobraKey.D9],
        ["*"] = [CobraKey.SymbolShift, CobraKey.B],
        ["+"] = [CobraKey.SymbolShift, CobraKey.K],
        ["\""] = [CobraKey.SymbolShift, CobraKey.P],
        ["."] = [CobraKey.SymbolShift, CobraKey.M],
        [","] = [CobraKey.SymbolShift, CobraKey.N],
        ["ENTER"] = [CobraKey.Enter],
        ["BREAK"] = [CobraKey.CapsShift, CobraKey.Space],
        ["CTRL"] = [CobraKey.Ctrl],
        ["ESC"] = [CobraKey.Escape],
        ["NO SCR"] = [CobraKey.NoScroll],
        ["F1"] = [CobraKey.F1], ["F2"] = [CobraKey.F2], ["F3"] = [CobraKey.F3], ["F4"] = [CobraKey.F4],
    };

    /// <summary>The CP/M prompt's keymap: letters, digits and ENTER at the sheet's positions.
    /// The K5 column's CTRL (K200) is the CP/M escape - CTRL+0 at the Unified Boot starts the
    /// SYSTEM ROM's CP/M.</summary>
    public static IReadOnlyDictionary<string, CobraKey[]> Cpm { get; } = new Dictionary<string, CobraKey[]>
    {
        ["A"] = [CobraKey.A], ["B"] = [CobraKey.B], ["C"] = [CobraKey.C], ["D"] = [CobraKey.D],
        ["E"] = [CobraKey.E], ["F"] = [CobraKey.F], ["G"] = [CobraKey.G], ["H"] = [CobraKey.H],
        ["I"] = [CobraKey.I], ["J"] = [CobraKey.J], ["K"] = [CobraKey.K], ["L"] = [CobraKey.L],
        ["M"] = [CobraKey.M], ["N"] = [CobraKey.N], ["O"] = [CobraKey.O], ["P"] = [CobraKey.P],
        ["Q"] = [CobraKey.Q], ["R"] = [CobraKey.R], ["S"] = [CobraKey.S], ["T"] = [CobraKey.T],
        ["U"] = [CobraKey.U], ["V"] = [CobraKey.V], ["W"] = [CobraKey.W], ["X"] = [CobraKey.X],
        ["Y"] = [CobraKey.Y], ["Z"] = [CobraKey.Z],
        ["0"] = [CobraKey.D0], ["1"] = [CobraKey.D1], ["2"] = [CobraKey.D2], ["3"] = [CobraKey.D3],
        ["4"] = [CobraKey.D4], ["5"] = [CobraKey.D5], ["6"] = [CobraKey.D6], ["7"] = [CobraKey.D7],
        ["8"] = [CobraKey.D8], ["9"] = [CobraKey.D9],
        ["ENTER"] = [CobraKey.Enter],
        ["CTRL"] = [CobraKey.Ctrl],
    };
}
