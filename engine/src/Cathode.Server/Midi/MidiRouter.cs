using Cathode.Contracts;
using Cathode.Ipc;
using Cathode.Server.Engine;
using Cathode.Server.Realtime;
using Cathode.Server.Rig;
using Cathode.Server.Stores;

namespace Cathode.Server.Midi;

/// <summary>
///     Turns MIDI into rig actions (docs/02-architecture.md §5.10). The host forwards every message with the engine sample
///     time it arrived at; the router applies MIDI learn, the global mappings and the active footswitch layout, and
///     drives footswitch LEDs. All state changes still go through <see cref="LiveRig" />, so the editor sees them too.
/// </summary>
/// <remarks>
///     Footswitches: a switch without a hold action acts on press. One with a hold action acts on release (a tap), or
///     after <see cref="HoldTime" /> held (the hold action, then nothing on release). Toggles (bypass, tuner,
///     metronome) flip on a press; a CC mapping sets them from its value instead (64 and up means on/engaged), and a
///     CC mapped to a scene selects the scene its value names.
/// </remarks>
public sealed partial class MidiRouter : IDisposable {
    public static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(500);

    readonly EngineConnection engine;
    readonly LiveRig rig;
    readonly GlobalStore globals;
    readonly PresetStore presets;
    readonly Library library;
    readonly EventHub hub;
    readonly ILogger<MidiRouter> logger;
    readonly Lock gate = new();
    readonly Dictionary<int, CancellationTokenSource> held = [];
    readonly Dictionary<int, int> leds = [];
    readonly Timer ledRefresh;

    /// <summary>Actions run one at a time, in the order the MIDI arrived: a scene change then a bypass must not swap.</summary>
    readonly System.Threading.Channels.Channel<Func<Task>> actions =
        System.Threading.Channels.Channel.CreateUnbounded<Func<Task>>(new() { SingleReader = true });

    readonly Task worker;
    MidiLearnRequest? learning;
    bool tunerOn;

    public MidiRouter(
        EngineConnection engine,
        LiveRig rig,
        GlobalStore globals,
        PresetStore presets,
        Library library,
        EventHub hub,
        ILogger<MidiRouter> logger
    ) {
        this.engine = engine;
        this.rig = rig;
        this.globals = globals;
        this.presets = presets;
        this.library = library;
        this.hub = hub;
        this.logger = logger;
        engine.MidiReceived += OnMidi;
        rig.Changes += change => {
            UpdateLeds();
            if (change.Type is "sceneChanged" or "presetLoaded") {
                SendSceneMidi(change.Scene ?? rig.ActiveScene);
            }
        };
        globals.Changed += _ => UpdateLeds();
        engine.Connected += () => {
            lock (gate) {
                leds.Clear(); // a restarted host (or controller) needs every LED again
            }

            UpdateLeds();
            return Task.CompletedTask;
        };
        // The looper changes on its own (a recording fills up): poll its LED state now and then.
        ledRefresh = new(_ => UpdateLeds(), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
        worker = Task.Run(WorkAsync);
    }

    /// <summary>The armed MIDI learn request, if any.</summary>
    public MidiLearnRequest? Learning {
        get {
            lock (gate) {
                return learning;
            }
        }
    }

    public void Learn(MidiLearnRequest? request) {
        lock (gate) {
            learning = request;
        }
    }

    /// <summary>Handles one batch; public so tests (and the in-process engine) can feed it directly.</summary>
    public void OnMidi(IReadOnlyList<MidiEventMessage> messages) {
        var settings = globals.Get();
        foreach (var message in messages) {
            hub.Publish(
                new("midi", message.Status, message.Data1, message.Data2),
                ContractsJsonContext.Default.MidiActivity
            );
            var channel = message.Status < 0xF0 ? (message.Status & 0x0F) + 1 : 0;
            if (channel == 0 || settings.MidiChannel != 0 && channel != settings.MidiChannel) {
                continue;
            }

            var kind = Kind(message);
            if (kind is null) {
                continue;
            }

            if (TryLearn(kind, channel, message)) {
                continue;
            }

            var down = kind == "pc" || message.Data2 >= 64 && (message.Status & 0xF0) != 0x80;
            var layout = settings.Footswitches.FirstOrDefault(l => l.Id == settings.ActiveLayout);
            foreach (var footswitch in layout?.Switches ?? []) {
                if (footswitch.Message == kind
                    && footswitch.Number == message.Data1
                    && (footswitch.Channel == 0 || footswitch.Channel == channel)) {
                    Footswitch(footswitch, down, message.SampleTime);
                }
            }

            foreach (var mapping in settings.MidiMappings) {
                // A presetSlot mapping takes any program change (its number picks the slot); everything else matches
                // its own CC, note or program number.
                var matches = mapping.Message == kind
                    && (mapping.Channel == 0 || mapping.Channel == channel)
                    && (kind == "pc" && mapping.Action == "presetSlot" || mapping.Number == message.Data1);
                if (matches) {
                    var data = kind == "pc" ? message.Data1 : message.Data2;
                    Run(
                        new(mapping.Action, mapping.Target, mapping.Value),
                        data,
                        kind == "cc",
                        down,
                        message.SampleTime
                    );
                }
            }
        }
    }

    public void Dispose() {
        engine.MidiReceived -= OnMidi;
        ledRefresh.Dispose();
        actions.Writer.TryComplete();
        lock (gate) {
            foreach (var hold in held.Values) {
                hold.Cancel();
            }
        }
    }

    bool TryLearn(string kind, int channel, MidiEventMessage message) {
        MidiLearnRequest request;
        lock (gate) {
            if (learning is null || kind == "note" && message.Data2 == 0) {
                return false;
            }

            request = learning;
            learning = null;
        }

        var mapping = new MidiMapping(
            Preset.NewId(),
            kind,
            channel,
            message.Data1,
            request.Action,
            request.Target,
            request.Value
        );
        globals.Update(g => {
                g.MidiMappings.RemoveAll(m => m.Message == kind && m.Number == mapping.Number && m.Channel == channel);
                g.MidiMappings.Add(mapping);
            }
        );
        hub.Publish(
            new("midiLearned", message.Status, message.Data1, message.Data2),
            ContractsJsonContext.Default.MidiActivity
        );
        return true;
    }

    void Footswitch(Footswitch footswitch, bool down, long sampleTime) {
        lock (gate) {
            if (down) {
                if (footswitch.Hold is null) {
                    Run(footswitch.Tap, 127, false, true, sampleTime);
                    return;
                }

                var timer = new CancellationTokenSource();
                held[footswitch.Index] = timer;
                _ = Task.Delay(HoldTime, timer.Token)
                    .ContinueWith(
                        t => {
                            if (!t.IsCanceled) {
                                lock (gate) {
                                    held.Remove(footswitch.Index);
                                }

                                Run(footswitch.Hold, 127, false, true, sampleTime);
                            }
                        },
                        TaskScheduler.Default
                    );
            } else if (held.Remove(footswitch.Index, out var timer)) {
                timer.Cancel(); // released before the hold time: a tap
                Run(footswitch.Tap, 127, false, true, sampleTime);
            }
        }
    }

    /// <summary>Queues an action behind the ones before it; failures are logged, as there is nobody to answer.</summary>
    void Run(SwitchAction action, int data, bool continuous, bool down, long sampleTime) =>
        actions.Writer.TryWrite(() => ExecuteAsync(action, data, continuous, down, sampleTime));

    async Task WorkAsync() {
        await foreach (var action in actions.Reader.ReadAllAsync()) {
            try {
                await action();
            } catch (Exception e) when (e is RigException or IpcException or IOException or KeyNotFoundException) {
                ActionFailed(logger, "MIDI", e.Message);
            }
        }
    }

    async Task ExecuteAsync(SwitchAction action, int data, bool continuous, bool down, long sampleTime) {
        var scenes = Preset.SceneCount;
        switch (action.Action) {
            case "scene" when down || continuous: // a CC's value is the scene number, whatever it is
                await rig.SelectSceneAsync(Math.Clamp(action.Value ?? data, 0, scenes - 1));
                break;
            case "sceneNext" when down:
                await rig.SelectSceneAsync((rig.ActiveScene + 1) % scenes);
                break;
            case "scenePrevious" when down:
                await rig.SelectSceneAsync((rig.ActiveScene + scenes - 1) % scenes);
                break;
            case "presetSlot" when down && action.Target is { } bank:
                await rig.LoadAsync(library.Slot(bank, action.Value ?? data));
                break;
            case "presetNext" or "presetPrevious" when down:
                var all = presets.List().OrderBy(static p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
                if (all.Count > 0) {
                    var current = all.FindIndex(p => p.Id == rig.PresetId);
                    var step = action.Action == "presetNext" ? 1 : -1;
                    await rig.LoadAsync(all[(Math.Max(current, 0) + step + all.Count) % all.Count].Id);
                }

                break;
            case "bypass" when action.Target is { } block:
                if (continuous) {
                    await rig.SetBypassAsync(block, data < 64, null);
                } else if (down) {
                    await rig.SetBypassAsync(block, !rig.IsBypassed(block), null);
                }

                break;
            case "channel" when down && action.Target is { } block:
                await rig.SetChannelAsync(block, Preset.ChannelNames[(action.Value ?? data) & 3], null);
                break;
            case "external":
                rig.SetExternal(Math.Clamp((action.Value ?? 1) - 1, 0, 7), data / 127f);
                break;
            case "manual":
                rig.SetManual(Math.Clamp(action.Value ?? 0, 0, 3), data / 127f);
                break;
            case "tap" when down:
                rig.Tap(sampleTime);
                break;
            case "tuner" when down || continuous:
                lock (gate) {
                    tunerOn = continuous ? data >= 64 : !tunerOn;
                }

                engine.SetTuner(tunerOn);
                UpdateLeds();
                break;
            case "metronome" when down || continuous:
                globals.Update(g => g.Metronome = continuous ? data >= 64 : !g.Metronome);
                break;
            case "looper" when down && action.Target is { } looper:
                engine.Looper(looper);
                break;
        }
    }

    /// <summary>Scene MIDI: the messages a scene sends to other gear when it is selected or its preset loads on it.</summary>
    void SendSceneMidi(int scene) {
        if (!engine.IsConnected) {
            return;
        }

        foreach (var message in rig.SceneMidi(scene)) {
            if (message.Problem() is null) {
                engine.MidiOut(null, message.Bytes());
            }
        }
    }

    /// <summary>Sends each LED whose state changed: on (127) when its switch's tap action is in effect.</summary>
    void UpdateLeds() {
        if (!engine.IsConnected) {
            return;
        }

        var settings = globals.Get();
        var layout = settings.Footswitches.FirstOrDefault(l => l.Id == settings.ActiveLayout);
        if (layout is null) {
            return;
        }

        var looper = engine.LastTelemetry?.Looper?.State;
        foreach (var footswitch in layout.Switches.Where(static s => s.Led)) {
            var on = footswitch.Tap.Action switch {
                "scene" => rig.ActiveScene == footswitch.Tap.Value,
                "bypass" when footswitch.Tap.Target is { } block => !rig.IsBypassed(block),
                "tuner" => tunerOn,
                "metronome" => settings.Metronome,
                "looper" => footswitch.Tap.Target switch {
                    "record" => looper == "recording",
                    "play" => looper is "playing" or "overdubbing",
                    "overdub" => looper == "overdubbing",
                    _ => false
                },
                _ => false
            };
            var value = on ? 127 : 0;
            lock (gate) {
                if (leds.TryGetValue(footswitch.Index, out var sent) && sent == value) {
                    continue;
                }

                leds[footswitch.Index] = value;
            }

            var channel = Math.Clamp(footswitch.Channel == 0 ? 1 : footswitch.Channel, 1, 16) - 1;
            var status = footswitch.Message == "note" ? 0x90 : 0xB0;
            engine.MidiOut(null, [(byte)(status | channel), (byte)footswitch.Number, (byte)value]);
        }
    }

    static string? Kind(MidiEventMessage message) =>
        (message.Status & 0xF0) switch {
            0xB0 => "cc",
            0x90 or 0x80 => "note",
            0xC0 => "pc",
            _ => null
        };

    [LoggerMessage(Level = LogLevel.Warning, Message = "MIDI action {Action} failed: {Error}")]
    static partial void ActionFailed(ILogger logger, string action, string error);
}
