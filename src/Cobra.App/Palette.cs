namespace Cobra.App;

/// <summary>Colour index -> display colour (ARGB), the App's half of the video contract. Indices 0..7 are
/// the normal colours and 8..15 their bright forms (index 8 is unused: bright black stays black). The
/// levels are the Spectrum's (a named assumption until the PAL coder board's levels are read from
/// the PAL coder schematic): normal 0xD7, bright 0xFF, in G R B bit order of the index.</summary>
public static class Palette
{
    public static IReadOnlyList<uint> Argb { get; } = Build();

    private static uint[] Build()
    {
        var colours = new uint[16];
        for (var i = 0; i < 16; i++)
        {
            uint level = i >= 8 ? 0xFFu : 0xD7u;
            uint b = (i & 1) != 0 ? level : 0, r = (i & 2) != 0 ? level : 0, g = (i & 4) != 0 ? level : 0;
            colours[i] = 0xFF000000u | r << 16 | g << 8 | b;
        }

        return colours;
    }
}
