# COBRA emulator

**English** · [Română](README.ro.md)

An emulator of the **COBRA**, a Romanian home computer designed in Brașov in the 1980s, written from scratch in C# on
.NET 10. It comes with a desktop app for Windows and Linux.

## What the COBRA is

The COBRA was designed at ITCI Brașov, the Brașov research institute for computer hardware and software. The prototype
was shown in 1986 and the machine went into production in 1988. It is built around a **Zilog Z80** and is compatible with
the **ZX Spectrum**, but it was also meant as a small professional computer:

- **80 KB of RAM**: 64 KB of DRAM plus 16 KB of video RAM, switched between three hardware configurations (startup,
  BASIC and CP/M).
- A **floppy disk interface** with an Intel 8272A controller and a Z80-CTC, for up to four drives.
- A **BOOT ROM** that starts one of the systems in its **SYSTEM ROM**: Sinclair **BASIC**, **OPUS**, the **DEVIL** disk
  operating system, or **CP/M 2.2** with an **80-column** text screen.
- A cassette interface, a beeper and two Kempston joystick ports.

Only a few hundred machines were made. Today the machine is documented, and rebuilt by hobbyists, through the
[CoBra Project](https://cobrasov.com/CoBra%20Project/index.html), which publishes its manuals, schematics, ROMs and disk
images. This emulator is built from that documentation, and the ROMs and system disks it ships come from there.

## What runs

- The **80K standard BOOT**, the **Unified Boot** (with CP/M straight from ROM) and the **original 64K board** with its
  own BOOT.
- **BASIC** and **OPUS** from the SYSTEM ROM.
- **DEVIL**: the disk catalogue (`CAT`), loading and saving programs on disk.
- **CP/M 2.2** from disk and from ROM, on the 80-column screen: `DIR`, saving and running a program.
- The **NMI button**, which saves the screen to tape and returns to the program.
- **Tape**: saving and loading programs, `.tap` files in and out.
- **Sound** from the beeper, the **keyboard** on your PC keys, and the **Kempston joystick** on the arrow keys.
- **Four floppy drives** with TeleDisk (`.td0`), HFE and raw (`.img`) images, blank DEVIL and CP/M disks, and saving a
  disk back to a file.

## Install

### Windows 10 / 11 (x64)

Open **PowerShell** and run:

```powershell
irm https://raw.githubusercontent.com/mteletin/cobra/main/install.ps1 | iex
```

The script downloads the latest `setup.exe` from [Releases](https://github.com/mteletin/cobra/releases), checks its
SHA-256 against the digest GitHub publishes for it, and installs it for your user. It needs no administrator rights.

- To install by hand, download `cobra-emu-<version>-setup.exe` from Releases and run it. The installer is not signed, so
  Windows SmartScreen may say "Windows protected your PC": choose **More info**, then **Run anyway**.
- To install a given version: `$env:COBRA_VERSION = '0.1.4'` before the command.
- To see the setup wizard instead of a silent install: `$env:COBRA_WIZARD = '1'` before the command.

| what | where |
|---|---|
| program | `%LOCALAPPDATA%\Programs\COBRA\app` (you can choose another folder in the wizard) |
| disk images | `%LOCALAPPDATA%\Programs\COBRA\disks`, also in the Start menu: **COBRA > Disk images** |
| shortcuts | Start menu and desktop: **COBRA** |
| uninstall | Settings > Apps > **COBRA** |

### Ubuntu (x64)

```bash
curl -fsSL https://raw.githubusercontent.com/mteletin/cobra/main/install.sh | bash
```

The script downloads the latest `linux-x64.run` from Releases, checks its SHA-256 against the digest GitHub publishes for
it, and runs it. It needs no `sudo`: everything goes under your home folder. .NET and SDL2 are bundled. The app also needs
`libfontconfig1` from the system: Ubuntu Desktop has it; on a minimal or server install, run
`sudo apt install libfontconfig1` first.

- To remove it: `curl -fsSL https://raw.githubusercontent.com/mteletin/cobra/main/install.sh | bash -s -- --uninstall`.
- To install a given version: `... | COBRA_VERSION=0.1.4 bash`.
- To install by hand, download `cobra-emu-<version>-linux-x64.run` from Releases and run
  `bash cobra-emu-<version>-linux-x64.run`.

| what | where |
|---|---|
| program | `~/.local/share/cobra-emu/<version>` (with a `current` link to the newest) |
| disk images | `~/.local/share/cobra-emu/current/disks` |
| launcher | `cobra` (in `~/.local/bin`), and **COBRA** in the applications menu |

If the `cobra` command is not found right after the first install, log out and back in (Ubuntu adds `~/.local/bin` to
your `PATH` at login), or run `~/.local/bin/cobra`.

### Disks that come with it

| file | what it is |
|---|---|
| `CPM-80-columns-system.img` | CP/M 2.2 system disk with the 80-column screen |
| `DEVIL-UTILS1.TD0`, `DEVIL-UTILS3.TD0` | DEVIL utility disks |

On Windows, running the installer again upgrades the program and keeps the disks folder as you left it, also after an
uninstall.

## Using the emulator

The machine starts at its BOOT screen. On the **80K standard BOOT**:

| key | starts |
|---|---|
| **B** | BASIC |
| **W** | OPUS |
| **D** | the system on the disk in drive 0 (DEVIL or CP/M) |

The **Unified Boot** (Machine > Reset from the Unified Boot) has its own menu, and **CTRL+0** there starts CP/M from the
SYSTEM ROM, without a disk.

- **Machine:** reset from any of the three BOOTs, the **NMI button** (also **F12**), the BASIC area's R/W switch, and
  pause.
- **Disks:** for each of the four drives, insert a disk image, insert a blank DEVIL or CP/M 720K disk, save the disk to a
  file, or eject it.
- **Tape:** insert a `.tap` file, play and stop, and save what the machine recorded.
- **Input:** the arrow keys and right Ctrl as the Kempston joystick.
- **View:** zoom.

To try it: put `CPM-80-columns-system.img` in drive 0, reset from the 80K standard BOOT, press **D** and type `DIR` at the
`A>` prompt.

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), and on Linux `libfontconfig1`.

```bash
git clone https://github.com/mteletin/cobra.git
cd cobra
dotnet build Cobra.slnx
dotnet run --project src/Cobra.App
```

| folder | what it holds |
|---|---|
| `src/Hc2000.Z80` | the Z80 processor, shared with the HC2000 emulator |
| `src/Cobra.Machine` | the machine: memory and its configurations, the 8255, the keyboard, tape, the floppy board (8272A and Z80-CTC) and disk image readers |
| `src/Cobra.Video` | the video controller's picture |
| `src/Cobra.App` | the desktop app (Avalonia) |
| `roms/` | the BOOT and SYSTEM ROM images |
| `disks/` | the system disks the installers ship |
| `deploy/sdl2/` | SDL2 2.32.10 for sound |

The build copies the ROMs and SDL2 next to the app. Sound uses SDL2 2.32.10 and the app checks for exactly that version;
`--sound-dump file.wav` also writes the machine's sound to a file.

## Credits

The COBRA was designed at ITCI Brașov by Vasile Prodan, Sorin Finichiu, Bernd Hansgeorg Wagner, Marcel Arefia, Mircea Pop,
Mircea Ungur and Alexandru Antal, coordinated by Gheorghe Toacşe (CoBra Hardware Manual, 1988). Its documentation, ROMs
and disk images are kept by the [CoBra Project](https://cobrasov.com/CoBra%20Project/index.html).
