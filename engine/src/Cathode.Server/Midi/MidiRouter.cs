using Cathode.Contracts;
using Cathode.Ipc;
using Cathode.Server.Engine;
using Cathode.Server.Realtime;
using Cathode.Server.Rig;
using Cathode.Server.Stores;
using System.Threading.Channels;

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
///     CC mapped to a scene selects the scene its value names (0-7), or steps to the next one, wrapping, for a value
///     above 63. With Ignore Redundant PC on, a program change for the preset already loaded does nothing.
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

    /// <summary>The switches with a hold action that are down, by index, from press to release.</summary>
    readonly Dictionary<int, Hold> held = [];

    readonly Dictionary<int, int> leds = [];
    readonly Timer ledRefresh;

    /// <summary>Actions run one at a time, in the order the MIDI arrived: a scene change then a bypass must not swap.</summary>
    readonly Channel<Func<Task>> actions =
        Channel.CreateUnbounded<Func<Task>>(new() { SingleReader = true });

    readonly Task worker;
    MidiLearnRequest? learning;
    string? ledPage;

    /// <summary>The armed MIDI learn request, if any.</summary>
    public MidiLearnRequest? Learning {
        get {
            lock (gate) {
                return learning;
            }
        }
    }

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

    public void Learn(MidiLearnRequest? request) {
        lock (gate) {
            learning = request;
        }
    }

    /// <summary>Handles one batch; public so tests (and the in-process engine) can feed it directly.</summary>
    public void OnMidi(IReadOnlyList<MidiEventMessage> messages) {
        var settings = globals.Get();
        foreach (var message in messages) {
            hub.PublishActivity(
                new("midi", message.Status, message.Data1, message.Data2),
                ContractsJsonContext.Default.MidiActivity
            );
            var channel = message.Status < 0xF0 ? (message.Status & 0x0F) + 1 : 0;
            if (channel == 0 || (settings.MidiChannel != 0 && channel != settings.MidiChannel)) {
                continue;
            }

            var kind = Kind(message);
            if (kind is null) {
                continue;
            }

            if (TryLearn(kind, channel, message)) {
                continue;
            }

            var down = kind == "pc" || (message.Data2 >= 64 && (message.Status & 0xF0) != 0x80);
            PressFootswitches(settings, kind, channel, down, message);
            RunMappings(settings, kind, channel, down, message);
        }
    }

    /// <summary>The active layout's switches that listen to the message.</summary>
    void PressFootswitches(GlobalSettings settings, string kind, int channel, bool down, MidiEventMessage message) {
        var layout = settings.Footswitches.Find(l => l.Id == settings.ActiveLayout);
        var pressed = ActiveSwitches(settings, layout)
            .Where(s => s.Message == kind && s.Number == message.Data1 && (s.Channel == 0 || s.Channel == channel));
        foreach (var footswitch in pressed) {
            Footswitch(footswitch, down, message.SampleTime);
        }
    }

    /// <summary>The global MIDI mappings that match the message.</summary>
    void RunMappings(GlobalSettings settings, string kind, int channel, bool down, MidiEventMessage message) {
        foreach (var mapping in settings.MidiMappings) {
            // A presetSlot mapping takes any program change (its number picks the slot); everything else matches
            // its own CC, note or program number.
            var matches = mapping.Message == kind
                && (mapping.Channel == 0 || mapping.Channel == channel)
                && ((kind == "pc" && mapping.Action == "presetSlot") || mapping.Number == message.Data1);
            if (matches) {
                var data = kind == "pc" ? message.Data1 : message.Data2;
                Run(new(mapping.Action, mapping.Target, mapping.Value), data, kind == "cc", down, message.SampleTime);
            }
        }
    }

    public void Dispose() {
        engine.MidiReceived -= OnMidi;
        ledRefresh.Dispose();
        actions.Writer.TryComplete();
        lock (gate) {
            foreach (var hold in held.Values) {
                hold.Timer.Cancel();
                hold.Timer.Dispose();
            }

            held.Clear();
        }
    }

    bool TryLearn(string kind, int channel, MidiEventMessage message) {
        MidiLearnRequest request;
        lock (gate) {
            if (learning is null || (kind == "note" && message.Data2 == 0)) {
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
        try {
            globals.Update(g => {
                    g.MidiMappings.RemoveAll(m => m.Message == kind
                        && m.Number == mapping.Number
                        && m.Channel == channel
                    );
                    g.MidiMappings.Add(mapping);
                }
            );
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
            // The global settings could not be written (a full disk, a read-only data folder): the message is
            // consumed, nothing is learnt, and learn stays armed for another try.
            lock (gate) {
                learning ??= request;
            }

            ActionFailed(logger, "learn", e.Message);
            return true;
        }

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
                    Tap(footswitch, sampleTime);
                    return;
                }

                // Already down: a controller that repeats its "down" (a CC pedal held past 64, key repeat) is still one
                // press, with one hold timer and at most one hold action.
                if (held.ContainsKey(footswitch.Index)) {
                    return;
                }

                var hold = new Hold(new());
                held[footswitch.Index] = hold;
                _ = Task.Delay(HoldTime, hold.Timer.Token)
                    .ContinueWith(
                        t => {
                            lock (gate) {
                                // Released (or pressed again) meanwhile: that press is over.
                                if (t.IsCanceled
                                    || !held.TryGetValue(footswitch.Index, out var current)
                                    || current != hold) {
                                    return;
                                }

                                hold.Fired = true; // stays held until released, which then does nothing
                            }

                            Run(footswitch.Hold, 127, false, true, sampleTime);
                        },
                        TaskScheduler.Default
                    );
            } else if (held.Remove(footswitch.Index, out var hold)) {
                hold.Timer.Cancel();
                hold.Timer.Dispose();
                if (!hold.Fired) {
                    Tap(footswitch, sampleTime); // released before the hold time: a tap
                }
            }
        }
    }

    /// <summary>A switch's tap action, then the jump to its linked layout, if any.</summary>
    void Tap(Footswitch footswitch, long sampleTime) {
        Run(footswitch.Tap, 127, false, true, sampleTime);
        if (footswitch.Link is { } link) {
            Run(new("layout", link), 127, false, true, sampleTime);
        }
    }

    /// <summary>
    ///     The switches of the layout's active view, each with the loaded preset's override (if it has one) in place of
    ///     its own actions.
    /// </summary>
    IEnumerable<Footswitch> ActiveSwitches(GlobalSettings settings, FootswitchLayout? layout) {
        if (layout is null) {
            yield break;
        }

        var view = Math.Clamp(settings.ActiveView, 0, Math.Max(1, layout.Views?.Count ?? 0) - 1);
        foreach (var footswitch in layout.Switches.Where(s => s.View == view)) {
            yield return rig.FootswitchOverrideFor(layout.Id, footswitch.Index) is { } own
                ? footswitch with { Tap = own.Tap, Hold = own.Hold, Label = own.Label ?? footswitch.Label }
                : footswitch;
        }
    }

    /// <summary>Queues an action behind the ones before it; failures are logged, as there is nobody to answer.</summary>
    void Run(SwitchAction action, int data, bool continuous, bool down, long sampleTime) =>
        actions.Writer.TryWrite(() => ExecuteAsync(action, data, continuous, down, sampleTime));

    /// <remarks>
    ///     Any failure is logged and the next action runs: one failed action (a save that could not write its file,
    ///     say) must not stop MIDI control for the rest of the session.
    /// </remarks>
    async Task WorkAsync() {
        await foreach (var action in actions.Reader.ReadAllAsync()) {
            try {
                await action();
            } catch (Exception e) when (e is not OutOfMemoryException) {
                ActionFailed(logger, "MIDI", e.Message);
            }
        }
    }

    async Task ExecuteAsync(SwitchAction action, int data, bool continuous, bool down, long sampleTime) {
        switch (action.Action) {
            case "scene" or "sceneLevelUp" or "sceneLevelDown" or "sceneNext" or "scenePrevious":
                await SceneAsync(action, data, continuous, down);
                break;
            case "save" or "presetSlot" or "presetNext" or "presetPrevious":
                await PresetAsync(action, data, down);
                break;
            case "bypass" or "channel" or "external" or "manual" or "tap":
                await BlockAsync(action, data, continuous, down, sampleTime);
                break;
            case "tuner" or "metronome" or "mute" or "looper":
                await ToggleAsync(action, data, continuous, down);
                break;
            case "layout" or "view" or "viewNext" or "viewPrevious" when down:
                Layout(action);
                break;
        }
    }

    async Task SceneAsync(SwitchAction action, int data, bool continuous, bool down) {
        var scenes = Preset.SceneCount;
        switch (action.Action) {
            case "scene" when continuous && action.Value is null && data > 63: // the top half of a CC steps on
                await rig.SelectSceneAsync((rig.ActiveScene + 1) % scenes);
                break;
            case "scene" when down || continuous: // a CC's value is the scene number, whatever it is
                await rig.SelectSceneAsync(Math.Clamp(action.Value ?? data, 0, scenes - 1));
                break;
            case "sceneLevelUp" or "sceneLevelDown" when down:
                await rig.StepSceneLevelAsync(action.Action == "sceneLevelUp" ? 1f : -1f);
                break;
            case "sceneNext" when down:
                await rig.SelectSceneAsync((rig.ActiveScene + 1) % scenes);
                break;
            case "scenePrevious" when down:
                await rig.SelectSceneAsync((rig.ActiveScene + scenes - 1) % scenes);
                break;
        }
    }

    async Task PresetAsync(SwitchAction action, int data, bool down) {
        if (!down) {
            return;
        }

        switch (action.Action) {
            case "save":
                await rig.SaveAsync(new(), null);
                break;
            case "presetSlot" when action.Target is { } bank:
                var slot = library.Slot(bank, action.Value ?? data);
                if (!(globals.Get().IgnoreRedundantPc && slot == rig.PresetId)) {
                    await rig.LoadAsync(slot);
                }

                break;
            case "presetNext" or "presetPrevious":
                await StepPresetAsync(action.Action == "presetNext" ? 1 : -1);
                break;
        }
    }

    /// <summary>Loads the next or previous preset by name, wrapping around.</summary>
    async Task StepPresetAsync(int step) {
        var all = presets.List().OrderBy(static p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (all.Count > 0) {
            var current = all.FindIndex(p => p.Id == rig.PresetId);
            await rig.LoadAsync(all[(Math.Max(current, 0) + step + all.Count) % all.Count].Id);
        }
    }

    async Task BlockAsync(SwitchAction action, int data, bool continuous, bool down, long sampleTime) {
        switch (action.Action) {
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
        }
    }

    async Task ToggleAsync(SwitchAction action, int data, bool continuous, bool down) {
        switch (action.Action) {
            case "tuner" when down || continuous:
                await rig.SetTunerAsync(continuous ? data >= 64 : !rig.TunerOn);
                break;
            case "metronome" when down || continuous:
                globals.Update(g => g.Metronome = continuous ? data >= 64 : !g.Metronome);
                break;
            case "mute" when down || continuous:
                await rig.SetMutedAsync(continuous ? data >= 64 : !rig.Muted);
                break;
            case "looper" when down && action.Target is { } looper:
                engine.Looper(looper);
                break;
        }
    }

    /// <summary>Jumps to a footswitch layout, or to one of the active layout's views.</summary>
    void Layout(SwitchAction action) {
        if (action.Action == "layout") {
            if (action.Target is { } target) {
                globals.Update(g => {
                        if (g.Footswitches.Exists(l => l.Id == target)) {
                            g.ActiveLayout = target;
                            g.ActiveView = 0;
                        }
                    }
                );
            }

            return;
        }

        globals.Update(g => {
                var views = Math.Max(1, g.Footswitches.Find(l => l.Id == g.ActiveLayout)?.Views?.Count ?? 0);
                g.ActiveView = action.Action switch {
                    "view" => Math.Clamp(action.Value ?? 0, 0, views - 1),
                    "viewNext" => (g.ActiveView + 1) % views,
                    _ => (g.ActiveView + views - 1) % views
                };
            }
        );
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
        var layout = settings.Footswitches.Find(l => l.Id == settings.ActiveLayout);
        if (layout is null) {
            return;
        }

        var looper = engine.LastTelemetry?.Looper?.State;
        lock (gate) {
            // Another layout or view puts other switches under the same indexes: send every LED again.
            var page = $"{layout.Id}/{settings.ActiveView}";
            if (page != ledPage) {
                ledPage = page;
                leds.Clear();
            }
        }

        foreach (var footswitch in ActiveSwitches(settings, layout).Where(static s => s.Led)) {
            var value = IsLit(footswitch.Tap, settings, looper) ? 127 : 0;
            if (!MarkLed(footswitch.Index, value)) {
                continue;
            }

            var channel = Math.Clamp(footswitch.Channel == 0 ? 1 : footswitch.Channel, 1, 16) - 1;
            var status = footswitch.Message == "note" ? 0x90 : 0xB0;
            engine.MidiOut(null, [(byte)(status | channel), (byte)footswitch.Number, (byte)value]);
        }
    }

    /// <summary>Whether a switch's tap action is in effect, so its LED is on.</summary>
    bool IsLit(SwitchAction tap, GlobalSettings settings, string? looper) =>
        tap.Action switch {
            "scene" => rig.ActiveScene == tap.Value,
            "bypass" when tap.Target is { } block => !rig.IsBypassed(block),
            "tuner" => rig.TunerOn,
            "metronome" => settings.Metronome,
            "mute" => rig.Muted,
            "looper" => tap.Target switch {
                "record" => looper == "recording",
                "play" => looper is "playing" or "overdubbing",
                "overdub" => looper == "overdubbing",
                _ => false
            },
            _ => false
        };

    /// <summary>Records an LED's new value; false when that is what was last sent, so nothing needs sending.</summary>
    bool MarkLed(int index, int value) {
        lock (gate) {
            if (leds.TryGetValue(index, out var sent) && sent == value) {
                return false;
            }

            leds[index] = value;
            return true;
        }
    }

    static string? Kind(MidiEventMessage message) =>
        (message.Status & 0xF0) switch {
            0xB0 => "cc",
            0x90 or 0x80 => "note",
            0xC0 => "pc",
            _ => null
        };

    /// <summary>A switch with a hold action, down: its hold timer, and whether the hold action has run.</summary>
    sealed class Hold(CancellationTokenSource timer) {
        public CancellationTokenSource Timer { get; } = timer;

        public bool Fired { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "MIDI action {Action} failed: {Error}")]
    static partial void ActionFailed(ILogger logger, string action, string error);
}
