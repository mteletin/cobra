using Avalonia.Input;
using Cobra.Machine;

namespace Cobra.App;

/// <summary>PC keys -> COBRA keys (the COBRA keyboard schematics). Keys are positional: a PC key holds the
/// COBRA key in the same place, and the shifts are the machine's own (left Shift = CAPS SHIFT, right
/// Shift or Alt = SYMBOL SHIFT), so BASIC's keywords work as on the real keyboard. The extra K5 column and
/// the transistor-driven keys get PC keys of their own. A few PC punctuation keys type their symbol with
/// SYMBOL SHIFT, as on the Spectrum layout.</summary>
public static class KeyMap
{
    /// <summary>The COBRA keys a PC key holds; <paramref name="shift"/> is whether a PC Shift is down, which
    /// picks the upper symbol of the punctuation keys (US layout).</summary>
    public static IReadOnlyList<CobraKey> For(Key key, bool shift = false) => key switch
    {
        >= Key.A and <= Key.Z => [Enum.Parse<CobraKey>(((char)('A' + (key - Key.A))).ToString())],
        >= Key.D0 and <= Key.D9 => [Enum.Parse<CobraKey>("D" + (char)('0' + (key - Key.D0)))],
        >= Key.NumPad0 and <= Key.NumPad9 => [Enum.Parse<CobraKey>("D" + (char)('0' + (key - Key.NumPad0)))],
        Key.LeftShift => [CobraKey.CapsShift],
        Key.RightShift or Key.LeftAlt or Key.RightAlt => [CobraKey.SymbolShift],
        Key.LeftCtrl or Key.RightCtrl => [CobraKey.Ctrl],
        Key.Enter => [CobraKey.Enter],
        Key.Space => [CobraKey.Space],
        Key.Back or Key.Delete => [CobraKey.Delete],
        Key.Left => [CobraKey.Left],
        Key.Right => [CobraKey.Right],
        Key.Up => [CobraKey.Up],
        Key.Down => [CobraKey.Down],
        Key.CapsLock => [CobraKey.CapsLock],
        Key.Tab => [CobraKey.Tab],
        Key.Escape => [CobraKey.Escape],
        Key.F1 => [CobraKey.F1],
        Key.F2 => [CobraKey.F2],
        Key.F3 => [CobraKey.F3],
        Key.F4 => [CobraKey.F4],
        Key.Home => [CobraKey.Graph],
        Key.PageUp => [CobraKey.PageEdit],
        Key.End => [CobraKey.NewMode],
        Key.Insert => [CobraKey.LineFeed],
        Key.Scroll or Key.Pause => [CobraKey.NoScroll],
        _ => Symbol(key, shift) is { } letter ? [CobraKey.SymbolShift, letter] : [],
    };

    /// <summary>True for the PC punctuation keys, which type a symbol with SYMBOL SHIFT.</summary>
    public static bool IsSymbol(Key key) => Symbol(key, false) is not null;

    // The Spectrum's SYMBOL SHIFT symbols: , N  < R  . M  > T  ; O  : Z  ' 7  " P  - J  _ 0  = L  + K  / V  ? C  * B
    private static CobraKey? Symbol(Key key, bool shift) => key switch
    {
        Key.OemComma => shift ? CobraKey.R : CobraKey.N,
        Key.OemPeriod => shift ? CobraKey.T : CobraKey.M,
        Key.OemSemicolon => shift ? CobraKey.Z : CobraKey.O,
        Key.OemQuotes => shift ? CobraKey.P : CobraKey.D7,
        Key.OemMinus => shift ? CobraKey.D0 : CobraKey.J,
        Key.OemPlus => shift ? CobraKey.K : CobraKey.L,
        Key.OemQuestion => shift ? CobraKey.C : CobraKey.V,
        Key.Add => CobraKey.K,
        Key.Subtract => CobraKey.J,
        Key.Multiply => CobraKey.B,
        Key.Divide => CobraKey.V,
        _ => null,
    };
}
