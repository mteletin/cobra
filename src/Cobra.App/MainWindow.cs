using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Cobra.Machine;

namespace Cobra.App;

/// <summary>The App's window: a menu (machine, four drives, tape, view), the screen and a status line.
/// Keys go to the machine while the window has focus; the menu is used with the mouse.</summary>
public sealed class MainWindow : Window
{
    private readonly DispatcherTimer _presentation = new() { Interval = TimeSpan.FromMilliseconds(20) };
    private readonly Dictionary<Key, IReadOnlyList<CobraKey>> _held = [];
    private readonly TextBlock _status = new() { Margin = new Thickness(8, 4), FontSize = 12 };

    public MainWindow(RomSet roms, bool start = true, Func<Audio.IAudioSink?>? audio = null)
    {
        Title = "COBRA";
        Session = new MachineSession(roms, audio: audio);
        Screen = new ScreenView { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        var menu = BuildMenu();
        DockPanel.SetDock(menu, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        Content = new DockPanel
        {
            Background = Brushes.Black,
            Children = { menu, _status, Screen },
        };
        SizeToContent = SizeToContent.WidthAndHeight;

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => ReleaseHeld();
        _presentation.Tick += (_, _) => PresentLatest();
        Opened += (_, _) => Screen.Focus();
        Closed += (_, _) =>
        {
            _presentation.Stop();
            Session.Dispose();
        };
        if (start)
        {
            Session.Start();
            _presentation.Start();
        }
    }

    public MachineSession Session { get; }

    /// <summary>The arrow keys and right Ctrl play the Kempston joystick instead of the cursor keys and CTRL.</summary>
    public bool ArrowsAreJoystick { get; set; }

    private static Joystick? StickFor(Key key) => key switch
    {
        Key.Left => Joystick.Left,
        Key.Right => Joystick.Right,
        Key.Up => Joystick.Up,
        Key.Down => Joystick.Down,
        Key.RightCtrl => Joystick.Fire,
        _ => null,
    };

    public ScreenView Screen { get; }

    /// <summary>Shows the newest frame and the status line (the presentation timer's tick).</summary>
    public void PresentLatest()
    {
        if (Session.TakeLatestFrame() is { } frame) Screen.Present(frame);
        _status.Text = Session.Status;
    }

    /// <summary>A PC key went down: the COBRA keys it holds go down (key repeat is the machine's own).</summary>
    public void KeyPressed(Key key)
    {
        if (ArrowsAreJoystick && StickFor(key) is { } stick)
        {
            Session.Stick(stick, true);
            return;
        }

        if (_held.ContainsKey(key)) return;
        var shift = _held.ContainsKey(Key.LeftShift) || _held.ContainsKey(Key.RightShift);
        var keys = KeyMap.For(key, shift);
        if (keys.Count == 0) return;
        _held[key] = keys;
        // A PC Shift that picked a punctuation key's upper symbol is not the COBRA's CAPS SHIFT: lift it
        // while the symbol is held (CAPS + SYMBOL SHIFT together would be the extended mode).
        if (KeyMap.IsSymbol(key) && _held.ContainsKey(Key.LeftShift)) Session.Release(CobraKey.CapsShift);
        foreach (var k in keys) Session.Press(k);
    }

    public void KeyReleased(Key key)
    {
        if (ArrowsAreJoystick && StickFor(key) is { } stick)
        {
            Session.Stick(stick, false);
            return;
        }

        if (!_held.Remove(key, out var keys)) return;
        foreach (var k in keys)
            if (!_held.Values.Any(other => other.Contains(k))) Session.Release(k);
        if (KeyMap.IsSymbol(key) && _held.ContainsKey(Key.LeftShift)) Session.Press(CobraKey.CapsShift);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F12)
        {
            Session.PressNmi();
            e.Handled = true;
            return;
        }

        if (KeyMap.For(e.Key, true).Count == 0) return;
        KeyPressed(e.Key);
        e.Handled = true;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (KeyMap.For(e.Key, true).Count == 0) return;
        KeyReleased(e.Key);
        e.Handled = true;
    }

    private void ReleaseHeld()
    {
        _held.Clear();
        Session.ReleaseAllKeys();
    }

    private Menu BuildMenu()
    {
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }

        var machine = new MenuItem { Header = "Machine" };
        machine.Items.Add(Item("Reset", () => Session.Reset()));
        machine.Items.Add(Item("Reset from the 80K standard BOOT", () => Session.Reset(BootChoice.Standard80K)));
        machine.Items.Add(Item("Reset from the Unified Boot", () => Session.Reset(BootChoice.Unified)));
        machine.Items.Add(Item("Reset as the original 64K board", () => Session.Reset(BootChoice.Original64K)));
        machine.Items.Add(new Separator());
        machine.Items.Add(Item("NMI button (F12)", () => Session.PressNmi()));
        var writable = new MenuItem { Header = "BASIC area writable (the R/W switch)", ToggleType = MenuItemToggleType.CheckBox };
        writable.Click += (_, _) =>
        {
            Session.SetBasicAreaWritable(!writable.IsChecked);
            writable.IsChecked = !writable.IsChecked;
        };
        machine.Items.Add(writable);
        machine.Items.Add(new Separator());
        machine.Items.Add(Item("Pause / resume", () => { if (Session.Paused) Session.Resume(); else Session.Pause(); }));

        var disks = new MenuItem { Header = "Disks" };
        for (var unit = 0; unit < 4; unit++)
        {
            var u = unit;
            var drive = new MenuItem { Header = $"Drive {u}" };
            drive.Items.Add(Item("Insert disk...", () => _ = InsertDiskAsync(u)));
            drive.Items.Add(Item("Insert a blank DEVIL disk", () => Session.InsertDisk(u, "blank DEVIL", MediaFiles.Blank(Cobra.Machine.Media.FloppyFormat.Devil))));
            drive.Items.Add(Item("Insert a blank CP/M 720K disk", () => Session.InsertDisk(u, "blank CP/M", MediaFiles.Blank(Cobra.Machine.Media.FloppyFormat.Cpm720))));
            drive.Items.Add(Item("Save disk as...", () => _ = SaveDiskAsync(u)));
            drive.Items.Add(Item("Eject", () => Session.EjectDisk(u)));
            disks.Items.Add(drive);
        }

        var tape = new MenuItem { Header = "Tape" };
        tape.Items.Add(Item("Insert tape (.tap)...", () => _ = InsertTapeAsync()));
        tape.Items.Add(Item("Play", () => Session.PlayTape()));
        tape.Items.Add(Item("Stop", () => Session.StopTape()));
        tape.Items.Add(Item("Save what was recorded (.tap)...", () => _ = SaveRecordingAsync()));

        var input = new MenuItem { Header = "Input" };
        var joystick = new MenuItem { Header = "Arrow keys and right Ctrl are the Kempston joystick", ToggleType = MenuItemToggleType.CheckBox };
        joystick.Click += (_, _) =>
        {
            ArrowsAreJoystick = !ArrowsAreJoystick;
            joystick.IsChecked = ArrowsAreJoystick;
            ReleaseHeld();
        };
        input.Items.Add(joystick);

        var view = new MenuItem { Header = "View" };
        foreach (var zoom in new[] { 1, 2, 3, 4 })
            view.Items.Add(Item($"Zoom {zoom}x", () => Screen.SetZoom(zoom)));

        var menu = new Menu();
        menu.Items.Add(machine);
        menu.Items.Add(disks);
        menu.Items.Add(tape);
        menu.Items.Add(input);
        menu.Items.Add(view);
        return menu;
    }

    private async Task InsertDiskAsync(int unit)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Disk for drive {unit}",
            AllowMultiple = false,
            SuggestedStartLocation = await ShippedDisksAsync(),
            FileTypeFilter = [new FilePickerFileType("COBRA disks (Teledisk, HFE, raw)") { Patterns = MediaFiles.DiskPatterns }],
        });
        if (files.Count == 0) return;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            Session.InsertDisk(unit, files[0].Name, MediaFiles.LoadDisk(files[0].Name, buffer.ToArray()));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
        {
            _status.Text = $"{files[0].Name}: {ex.Message}";
        }
    }

    /// <summary>The published disks the installers put beside the App (Linux: next to it; Windows: ..\disks).</summary>
    private async Task<IStorageFolder?> ShippedDisksAsync()
    {
        foreach (var path in new[] { Path.Combine(AppContext.BaseDirectory, "disks"), Path.Combine(AppContext.BaseDirectory, "..", "disks") })
            if (Directory.Exists(path)) return await StorageProvider.TryGetFolderFromPathAsync(Path.GetFullPath(path));
        return null;
    }

    private async Task InsertTapeAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Tape",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Tape images (.tap)") { Patterns = MediaFiles.TapePatterns }],
        });
        if (files.Count == 0) return;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            Session.InsertTape(files[0].Name, MediaFiles.LoadTape(buffer.ToArray()));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
        {
            _status.Text = $"{files[0].Name}: {ex.Message}";
        }
    }

    /// <summary>Writes the drive's disk, with what the machine saved on it, to a file the user picks.</summary>
    private async Task SaveDiskAsync(int unit)
    {
        if (await Session.TakeDiskAsync(unit) is not { } disk)
        {
            _status.Text = $"Drive {unit} is empty.";
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Save the disk in drive {unit}",
            SuggestedFileName = disk.Raw ? "disk.img" : "disk.dsk",
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(disk.Bytes);
    }

    private async Task SaveRecordingAsync()
    {
        var tap = await Session.TakeTapeRecordingAsync();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save tape", SuggestedFileName = "recording.tap" });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(tap);
    }
}
