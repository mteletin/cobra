using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Cobra.App.Audio;

namespace Cobra.App;

/// The COBRA desktop App: the machine, its screen, keyboard, four floppy drives and the tape deck.
/// The only project with UI dependencies.
public static class Program
{
    /// <summary>--sound-dump FILE.wav: also write the machine's sound to that file.</summary>
    public static string? SoundDump { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        var dump = Array.IndexOf(args, "--sound-dump");
        if (dump >= 0 && dump + 1 < args.Length) SoundDump = args[dump + 1];
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>The sound output: the pinned SDL2 device, plus the dump file when asked for one.</summary>
    public static IAudioSink? OpenAudio()
    {
        IAudioSink? device = null;
        try { device = SdlQueuedAudioSink.Open(); }
        catch (Exception ex) when (ex is IOException or DllNotFoundException or EntryPointNotFoundException)
        {
            if (SoundDump is null) throw;
        }

        if (SoundDump is null) return device;
        var file = new WavFileSink(SoundDump, device?.SampleRate ?? 48000);
        return device is null ? file : new TeeSink(device, file);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<CobraApplication>().UsePlatformDetect().LogToTrace();
}

public sealed class CobraApplication : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(RomSet.FromDirectory(Path.Combine(AppContext.BaseDirectory, "roms")), audio: Program.OpenAudio);
        base.OnFrameworkInitializationCompleted();
    }
}
