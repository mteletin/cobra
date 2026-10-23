namespace Cobra.Machine;

/// <summary>The 58 keys of the COBRA keyboard (the COBRA keyboard schematics): 48 in the matrix
/// and 10 special keys that press matrix positions through transistors.</summary>
public enum CobraKey
{
    // KA8 .. KA15 rows, K0 .. K5 columns.
    CapsShift, Z, X, C, V, Ctrl,
    A, S, D, F, G, NoScroll,
    Q, W, E, R, T, LineFeed,
    D1, D2, D3, D4, D5, Escape,
    D0, D9, D8, D7, D6, F1,
    P, O, I, U, Y, F2,
    Enter, L, K, J, H, F3,
    Space, SymbolShift, M, N, B, F4,

    // Special keys (transistor-driven combinations).
    Left, Down, Up, Right, CapsLock, Graph, PageEdit, Delete, NewMode, Tab,
}

/// The keyboard matrix as the main board reads it (keyboard v.0/v.1.0 schematic; main board v.0
/// pages 2 and 15). Row line KAn is driven from address line An through a diode, so an
/// IN from port A selects every row whose address bit A8..A15 is 0; the six column lines
/// K0..K5 have 4.7K pull-ups and are port A bits 0..5, so a pressed key reads 0 in its
/// column bit for any selected row. K0..K4 are the ZX Spectrum layout; K5 adds CTRL, NO SCR,
/// LF, ESC, F1..F4. There are no matrix diodes; ghosting is not modelled.
///
/// The special keys drive BC171 transistors in parallel with matrix keys: the four cursor
/// keys, CAPS LOCK, GRAPH, PAGE EDIT and DELETE are CAPS SHIFT + 5/6/7/8/2/9/1/0, NEW MODE is
/// CAPS SHIFT + SYMBOL SHIFT, and TAB is CAPS SHIFT + SYMBOL SHIFT at once and P a little
/// later, through RI 4.7K / CI 10 uF / R411 4.7K on P's transistor (derived from the printed
/// values: about 10 ms, <see cref="TabPDelayTStates"/>). K409 is "I" as on keyboard v.1.0
/// (v.0's sheet prints a second "T" in the Spectrum's I position).
public sealed class CobraKeyboard
{
    /// <summary>P's transistor turns on about 10 ms after TAB is pressed: the base charges
    /// through 2.67K (1.5K + 4.7K in parallel with 4.7K) into 10 uF towards 2.2 V and conducts
    /// at about 0.65 V (derived; the sheet prints the parts, not the delay).</summary>
    public const long TabPDelayTStates = 35_000;

    private readonly HashSet<CobraKey> _down = [];
    private readonly Dictionary<CobraKey, long> _pressedAt = [];

    public bool IsDown(CobraKey key) => _down.Contains(key);

    /// <summary>Presses a key at CPU time <paramref name="tStates"/> (only TAB uses the time).</summary>
    public void Press(CobraKey key, long tStates = 0)
    {
        if (_down.Add(key)) _pressedAt[key] = tStates;
    }

    public void Release(CobraKey key)
    {
        _down.Remove(key);
        _pressedAt.Remove(key);
    }

    public void ReleaseAll()
    {
        _down.Clear();
        _pressedAt.Clear();
    }

    /// <summary>Port A bits 0..5 for an IN whose high address byte is <paramref name="rows"/>
    /// (bits 6 and 7, tape in and serial in, read 1 here; they are not on the keyboard).</summary>
    public byte Scan(byte rows, long tStates)
    {
        var value = 0xFF;
        foreach (var key in _down)
        {
            foreach (var (row, column) in Positions(key, tStates - _pressedAt[key]))
            {
                if ((rows & (1 << row)) == 0) value &= ~(1 << column);
            }
        }

        return (byte)value;
    }

    /// <summary>The matrix positions (row 0 = KA8, column 0 = K0) a key closes, <paramref name="held"/>
    /// T-states after it was pressed.</summary>
    public static IEnumerable<(int Row, int Column)> Positions(CobraKey key, long held)
    {
        if (key < CobraKey.Left)
        {
            yield return ((int)key / 6, (int)key % 6);
            yield break;
        }

        yield return Matrix(CobraKey.CapsShift);
        switch (key)
        {
            case CobraKey.Left: yield return Matrix(CobraKey.D5); break;
            case CobraKey.Down: yield return Matrix(CobraKey.D6); break;
            case CobraKey.Up: yield return Matrix(CobraKey.D7); break;
            case CobraKey.Right: yield return Matrix(CobraKey.D8); break;
            case CobraKey.CapsLock: yield return Matrix(CobraKey.D2); break;
            case CobraKey.Graph: yield return Matrix(CobraKey.D9); break;
            case CobraKey.PageEdit: yield return Matrix(CobraKey.D1); break;
            case CobraKey.Delete: yield return Matrix(CobraKey.D0); break;
            case CobraKey.NewMode: yield return Matrix(CobraKey.SymbolShift); break;
            case CobraKey.Tab:
                yield return Matrix(CobraKey.SymbolShift);
                if (held >= TabPDelayTStates) yield return Matrix(CobraKey.P);
                break;
        }
    }

    private static (int Row, int Column) Matrix(CobraKey key) => ((int)key / 6, (int)key % 6);
}
