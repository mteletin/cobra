using System.Collections.Concurrent;
using System.Diagnostics;
using Cobra.App.Audio;
using Cobra.Machine;
using Cobra.Machine.Media;
using Cobra.Video;

namespace Cobra.App;

/// <summary>One running COBRA. A single background thread owns the machine: it applies queued commands
/// (keys, media, reset) at frame boundaries, runs one 69,888-T-state frame, composes the frame with
/// <see cref="CobraVideo"/> and leaves it in a one-slot mailbox, then paces to the machine's own 50.08 Hz.
/// The UI never touches the machine. <see cref="AdvanceFrame"/> is the same step without the thread, for
/// tests and for a paused machine's single steps.</summary>
public sealed class MachineSession : IDisposable
{
    public const double FramesPerSecond = 3_500_000.0 / CobraMachine.TStatesPerFrame;

    private readonly RomSet _roms;
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly FloppyImage?[] _disks = new FloppyImage?[4];
    private readonly string?[] _diskNames = new string?[4];
    private readonly object _advance = new();
    private CobraMachine _machine;
    private List<byte[]>? _tape;
    private string? _tapeName;
    private VideoFrame? _latest;
    private long _frame;
    private readonly Dictionary<CobraKey, long> _hold = new();
    private readonly List<CobraKey> _deferred = new();
    private bool _deferReleaseAll;
    private Thread? _thread;
    private volatile bool _stopping;
    private volatile bool _paused;
    private volatile string _status = "";

    private readonly Func<IAudioSink?>? _audioFactory;
    private IAudioSink? _audio;
    private bool _audioOpened;
    private long _audioTState;
    private double _audioSamplesOwed;

    /// <param name="audio">Opens the sound output on the machine's own thread (the SDL device belongs to
    /// that thread); null or a factory that returns null runs silent.</param>
    public MachineSession(RomSet roms, BootChoice boot = BootChoice.Standard80K, Func<IAudioSink?>? audio = null)
    {
        _roms = roms ?? throw new ArgumentNullException(nameof(roms));
        Boot = boot;
        _audioFactory = audio;
        _machine = Build();
        UpdateStatus();
    }

    /// <summary>Why there is no sound, when there is none (the sound device could not be opened).</summary>
    public string? AudioProblem { get; private set; }

    public BootChoice Boot { get; private set; }

    public bool Paused => _paused;

    /// <summary>The last error the machine thread stopped on, if it stopped on one.</summary>
    public Exception? Failure { get; private set; }

    /// <summary>A one-line description for the status bar: configuration, drives, tape.</summary>
    public string Status => _status;

    public long FrameNumber => Interlocked.Read(ref _frame);

    /// <summary>The newest composed frame, or null if none was composed since the last call.</summary>
    public VideoFrame? TakeLatestFrame() => Interlocked.Exchange(ref _latest, null);

    /// <summary>A key is held for at least one whole frame: the guest samples the keyboard only at the
    /// T-states of the frames it runs, so a press and a release that land in the same frame window are
    /// drained together and the latch is never seen while the key is down. A release that follows its
    /// press from the same frame is therefore deferred until after that frame completes: an
    /// instant tap must never be invisible to the machine.</summary>
    public void Press(CobraKey key) => _commands.Enqueue(() =>
    {
        _machine.Keyboard.Press(key, _machine.Cpu.TStates);
        _hold[key] = _frame;
    });

    public void Release(CobraKey key) => _commands.Enqueue(() =>
    {
        var pressedThisFrame = _hold.TryGetValue(key, out var pressedAt) && pressedAt == _frame;
        _hold.Remove(key);
        if (pressedThisFrame) _deferred.Add(key);
        else _machine.Keyboard.Release(key);
    });

    public void ReleaseAllKeys() => _commands.Enqueue(() =>
    {
        if (_pressedThisFrameAny())
        {
            // A key pressed in the current frame window is still owed its one frame: hold everything
            // for the rest of the frame, then release all.
            _deferReleaseAll = true;
            _hold.Clear();
        }
        else
        {
            _machine.Keyboard.ReleaseAll();
        }
        _machine.Joystick = Joystick.None;
    });

    /// <summary>True while a key pressed in the current frame window is still owed its one frame.</summary>
    private bool _pressedThisFrameAny()
    {
        foreach (var pressedAt in _hold.Values)
            if (pressedAt == _frame) return true;
        return false;
    }

    /// <summary>Holds or lets go the Kempston joystick's <paramref name="direction"/> (port B, 1FH).</summary>
    public void Stick(Joystick direction, bool held) => _commands.Enqueue(() =>
        _machine.Joystick = held ? _machine.Joystick | direction : _machine.Joystick & ~direction);

    public void InsertDisk(int unit, string name, FloppyImage image)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(unit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(unit, 3);
        _commands.Enqueue(() =>
        {
            _disks[unit] = image;
            _diskNames[unit] = name;
            _machine.Floppy.Insert(unit, image);
        });
    }

    /// <summary>The disk in <paramref name="unit"/> as it is now, with what the machine wrote to it: a raw image
    /// for a raw disk, else an Extended DSK; null for an empty drive.</summary>
    public Task<(byte[] Bytes, bool Raw)?> TakeDiskAsync(int unit)
    {
        var done = new TaskCompletionSource<(byte[], bool)?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _commands.Enqueue(() =>
        {
            var image = _machine.Floppy.Drive(unit).Image;
            done.SetResult(image is null ? null : (image.Save(), image.Format == Hc2000.Machine.Storage.DiskFormat.Raw));
        });
        if (_thread is null || _paused) AdvanceCommandsOnly();
        return done.Task;
    }

    public void EjectDisk(int unit) => _commands.Enqueue(() =>
    {
        _disks[unit] = null;
        _diskNames[unit] = null;
        _machine.Floppy.Insert(unit, (Hc2000.Machine.Storage.Dsk?)null);
    });

    public void InsertTape(string name, List<byte[]> blocks) => _commands.Enqueue(() =>
    {
        _tape = blocks;
        _tapeName = name;
        _machine.Tape.Stop();
    });

    /// <summary>Starts the inserted tape from its beginning (the deck's PLAY).</summary>
    public void PlayTape() => _commands.Enqueue(() =>
    {
        if (_tape is not null) _machine.Tape.Play(_tape);
    });

    public void StopTape() => _commands.Enqueue(() => _machine.Tape.Stop());

    /// <summary>What the machine has written to tape since the last call (SAVE), as .tap bytes.</summary>
    public Task<byte[]> TakeTapeRecordingAsync()
    {
        var done = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _commands.Enqueue(() =>
        {
            var blocks = TapeDeck.Decode(_machine.Tape.Recording);
            _machine.Tape.ClearRecording();
            done.SetResult(TapeDeck.ToTap(blocks));
        });
        if (_thread is null || _paused) AdvanceCommandsOnly();
        return done.Task;
    }

    /// <summary>Power-cycles the machine (optionally from another BOOT); disks stay in their drives.</summary>
    public void Reset(BootChoice? boot = null) => _commands.Enqueue(() =>
    {
        if (boot is { } b) Boot = b;
        _machine = Build();
        _audioTState = 0;
        _hold.Clear();
        _deferred.Clear();
        _deferReleaseAll = false;
        for (var unit = 0; unit < 4; unit++)
            if (_disks[unit] is { } disk) _machine.Floppy.Insert(unit, disk);
    });

    /// <summary>Presses the NMI button.</summary>
    public void PressNmi() => _commands.Enqueue(() => _machine.PressNmiButton());

    /// <summary>The board's BASIC R/W switch (the NMI routine keeps its data in the BASIC area); kept across resets.</summary>
    public bool BasicAreaWritable { get; private set; }

    public void SetBasicAreaWritable(bool writable) => _commands.Enqueue(() =>
    {
        BasicAreaWritable = writable;
        _machine.Memory.BasicAreaWritable = writable;
    });

    public void Pause() => _paused = true;

    public void Resume() => _paused = false;

    /// <summary>Applies the queued commands and runs one frame.</summary>
    public void AdvanceFrame()
    {
        lock (_advance)
        {
            Drain();
            _machine.RunFrames(1);
            if (_deferred.Count > 0)
            {
                foreach (var key in _deferred) _machine.Keyboard.Release(key);
                _deferred.Clear();
            }
            if (_deferReleaseAll)
            {
                _deferReleaseAll = false;
                _machine.Keyboard.ReleaseAll();
            }
            Sound();
            var frame = Interlocked.Increment(ref _frame);
            var mode = _machine.EightyColumnLatch ? VideoMode.EightyColumns : _machine.VideoSecondHalf ? VideoMode.SecondHalf : VideoMode.Standard;
            var video = CobraVideo.Compose(_machine.Memory.Vram, _machine.Io.Eight255.PortCPins & 7, frame, mode);
            Interlocked.Exchange(ref _latest, video);
            UpdateStatus();
        }
    }

    /// <summary>Runs the machine on its own thread at 50.08 frames a second.</summary>
    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "COBRA machine" };
        _thread.Start();
    }

    public void Dispose()
    {
        _stopping = true;
        if (_thread is not null) _thread.Join(TimeSpan.FromSeconds(2));
        else CloseAudio();
    }

    /// <summary>The beeper's level since the last frame as samples for the sound output, paced by the
    /// machine's T-states (3.5 MHz); a queue already 200 ms deep skips a frame rather than grow the delay.</summary>
    private void Sound()
    {
        if (!_audioOpened)
        {
            _audioOpened = true;
            try { _audio = _audioFactory?.Invoke(); }
            catch (Exception ex) when (ex is IOException or DllNotFoundException or InvalidOperationException or EntryPointNotFoundException)
            {
                AudioProblem = ex.Message;
            }
        }

        var end = _machine.Cpu.TStates / CobraMachine.TStatesPerFrame * CobraMachine.TStatesPerFrame;
        if (_audio is null || end <= _audioTState)
        {
            _audioTState = Math.Max(_audioTState, end);
            _machine.Beeper.Discard(_audioTState);
            return;
        }

        _audioSamplesOwed += (end - _audioTState) * (double)_audio.SampleRate / 3_500_000;
        var count = (int)_audioSamplesOwed;
        _audioSamplesOwed -= count;
        var pcm = _machine.Beeper.Render(_audioTState, count, _audio.SampleRate);
        _audioTState = end;
        _machine.Beeper.Discard(end);
        if (_audio.QueuedSamples > _audio.SampleRate / 5) return;
        var samples = new float[pcm.Length];
        for (var i = 0; i < pcm.Length; i++) samples[i] = pcm[i] / 32768f;
        _audio.Queue(samples);
        if (_audio is SdlQueuedAudioSink sdl) sdl.Pause(false);
        else if (_audio is TeeSink { } tee) tee.Start();
    }

    private void CloseAudio()
    {
        _audio?.Dispose();
        _audio = null;
    }

    private void Run()
    {
        var clock = Stopwatch.StartNew();
        var ticksPerFrame = Stopwatch.Frequency / FramesPerSecond;
        double next = 0;
        try
        {
            while (!_stopping)
            {
                if (_paused)
                {
                    AdvanceCommandsOnly();
                    Thread.Sleep(10);
                    next = clock.ElapsedTicks;
                    continue;
                }

                AdvanceFrame();
                next += ticksPerFrame;
                var wait = next - clock.ElapsedTicks;
                if (wait > 0) Thread.Sleep(TimeSpan.FromTicks((long)(wait * TimeSpan.TicksPerSecond / Stopwatch.Frequency)));
                else if (wait < -5 * ticksPerFrame) next = clock.ElapsedTicks; // fell behind: do not race to catch up
            }
        }
        catch (Exception ex)
        {
            Failure = ex;
            _status = "The machine stopped: " + ex.Message;
        }
        finally
        {
            CloseAudio();
        }
    }

    private void AdvanceCommandsOnly()
    {
        lock (_advance) Drain();
    }

    private void Drain()
    {
        while (_commands.TryDequeue(out var command)) command();
    }

    private CobraMachine Build()
    {
        var machine = new CobraMachine(_roms.Boot(Boot), _roms.SystemFor(Boot), original64K: Boot == BootChoice.Original64K);
        machine.Memory.BasicAreaWritable = BasicAreaWritable;
        return machine;
    }

    private void UpdateStatus()
    {
        var drives = string.Join("  ", Enumerable.Range(0, 4).Select(u => $"{u}: {_diskNames[u] ?? "-"}"));
        var tape = _tapeName is null ? "tape: -" : $"tape: {_tapeName}{(_machine.Tape.Playing ? " (playing)" : "")}";
        var sound = AudioProblem is null ? "" : "  |  no sound";
        _status = $"{_machine.Configuration}  |  {drives}  |  {tape}{sound}";
    }
}
