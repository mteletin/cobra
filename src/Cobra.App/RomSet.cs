namespace Cobra.App;

/// <summary>Which BOOT EPROM the machine starts from.</summary>
public enum BootChoice
{
    /// <summary>The published 2 KB 80K STD BOOT (the reference board's): B BASIC, W OPUS, D disk.</summary>
    Standard80K,

    /// <summary>The site author's 16 KB Unified Boot rev.01: the code selector, CTRL+0..7 CP/M from ROM.</summary>
    Unified,

    /// <summary>The original 64K board with its 2 KB 64K STD BOOT and the 16 KB BASIC EPROMs: SPACE BASIC.</summary>
    Original64K,
}

/// <summary>The ROM images the machine needs, read from a directory (the App ships them in roms/).</summary>
public sealed record RomSet(byte[] System, byte[] Standard80K, byte[] Unified, byte[] Standard64K)
{
    public const string SystemFile = "SYSTEM_ROM.bin";
    public const string Standard80KFile = "BOOT_2K_COBRA_80K.bin";
    public const string UnifiedFile = "16K_UNIFIED_BOOT_rev.01.bin";
    public const string Standard64KFile = "BOOT_2K_COBRA_64K.bin";

    public static RomSet FromDirectory(string directory)
    {
        byte[] Read(string name)
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) throw new FileNotFoundException($"The ROM {name} is missing from {directory}.", path);
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 1024) throw new InvalidDataException($"{name} is {bytes.Length} bytes: a Git LFS pointer, not the ROM (git lfs pull).");
            return bytes;
        }

        return new RomSet(Read(SystemFile), Read(Standard80KFile), Read(UnifiedFile), Read(Standard64KFile));
    }

    /// <summary>The BOOT EPROM image for <paramref name="choice"/>; the Unified Boot's 12 KB are padded to its
    /// 16 KB part with erased bytes.</summary>
    public byte[] Boot(BootChoice choice)
    {
        if (choice == BootChoice.Standard80K) return Standard80K;
        if (choice == BootChoice.Original64K) return Standard64K;
        var boot = new byte[0x4000];
        Array.Fill(boot, (byte)0xFF);
        Unified.CopyTo(boot, 0);
        return boot;
    }

    /// <summary>The SYSTEM ROM the board for <paramref name="choice"/> carries: the 64K board has only the
    /// 16 KB BASIC EPROMs (the SYSTEM ROM's BASIC quarter).</summary>
    public byte[] SystemFor(BootChoice choice) => choice == BootChoice.Original64K ? System[0x4000..0x8000] : System;
}
