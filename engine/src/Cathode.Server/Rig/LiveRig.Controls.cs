using Cathode.Contracts;
using Cathode.Engine.Graph;
using Cathode.Engine.Rig;
using Cathode.Ipc;

namespace Cathode.Server.Rig;

/// <summary>Phase 5 operations: bypass modes, modifiers, controllers, tempo, channels, Manual/External, global blocks.</summary>
public sealed partial class LiveRig {
    /// <summary>How long after a tap the engine's tempo is taken back into the preset.</summary>
    static readonly TimeSpan TapFollowWindow = TimeSpan.FromSeconds(3);

    long tapFollowUntil;

    /// <summary>The working copy's id (unlocked read, for "next preset" from a footswitch).</summary>
    public string PresetId => Volatile.Read(ref preset).Id;

    /// <summary>
    ///     Whether every output is muted. Live only: not part of the preset or the global settings, so Cathode never
    ///     starts silent.
    /// </summary>
    public bool Muted { get; private set; }

    /// <summary>Mutes or unmutes every output (from the editor, a footswitch or a MIDI message).</summary>
    public Task<LiveState> SetMutedAsync(bool muted) =>
        Locked(() => {
                Muted = muted;
                engine.SetParams(generation, [new(IpcBlocks.Rig, "mute", muted ? 1 : 0)]);
                return Task.FromResult(Changed("muteChanged", markDirty: false));
            }
        );

    /// <summary>A tap (from the editor or a MIDI switch); the engine averages taps, sample-accurately.</summary>
    public void Tap(long? sampleTime = null) {
        Volatile.Write(ref tapFollowUntil, DateTime.UtcNow.Add(TapFollowWindow).Ticks);
        engine.Tap(sampleTime);
    }

    public Task<LiveState> SetBypassModeAsync(string id, string mode, long? expected) =>
        Locked(async () => {
                Check(expected);
                var block = Find(id);
                if (BuiltInTypes.IsBuiltIn(block.Type) || !BypassModes.IsValid(mode)) {
                    throw new RigException(
                        400,
                        $"Bypass mode '{mode}' is not one of thru, mute, muteIn, muteFxIn, muteOut, muteFxOut."
                    );
                }

                await StructuralAsync(p => p.Blocks.Single(b => b.Id == id).BypassMode = mode);
                return Changed("bypassModeChanged", id);
            }
        );

    /// <summary>Replaces the preset's modifiers (validated against the catalogue), recompiling gaplessly.</summary>
    public Task<LiveState> SetModifiersAsync(IReadOnlyList<PresetModifier> modifiers, long? expected) =>
        Locked(async () => {
                Check(expected);
                var errors = new List<string>();
                foreach (var m in modifiers) {
                    var block = preset.Blocks.FirstOrDefault(b => b.Id == m.Block);
                    var descriptor = block is null ? null : catalog.Descriptor(block.Type);
                    var engage = m.Param == ModNames.EngageParam
                        && block is not null
                        && !BuiltInTypes.IsBuiltIn(block.Type);
                    if (descriptor is null || (!engage && descriptor.IndexOf(m.Param) < 0)) {
                        errors.Add($"No parameter '{m.Param}' on block '{m.Block}'.");
                    } else if (!ModNames.TryParseSource(m.Source, out _)) {
                        errors.Add($"Unknown modifier source '{m.Source}'.");
                    } else if (!ModNames.TryParseCurve(m.Curve, out _)) {
                        errors.Add($"Unknown curve '{m.Curve}'.");
                    } else if (m.Channel is { } scope
                               && Array.IndexOf(Preset.ChannelNames, scope) is var index
                               && (index < 0 || index >= descriptor.Channels)) {
                        errors.Add($"Block '{m.Block}' has no channel '{scope}'.");
                    }
                }

                if (modifiers.GroupBy(static m => (m.Block, m.Param)).Any(static g => g.Count() > 1)) {
                    errors.Add("A parameter can have only one modifier.");
                }

                if (errors.Count > 0) {
                    throw new RigException(400, "Some modifiers are invalid.", errors);
                }

                await StructuralAsync(p => p.Modifiers = [..modifiers]);
                return Changed("modifiersChanged");
            }
        );

    /// <summary>Controller settings (LFO, ADSR, envelope, sequencer, pitch range) stored with the preset.</summary>
    public Task<LiveState> SetControllersAsync(IReadOnlyDictionary<string, float> values, long? expected) =>
        Locked(() => {
                Check(expected);
                var unknown = values.Keys.Where(static k => !ControllerParams.Contains(k)).ToList();
                if (unknown.Count > 0) {
                    throw new RigException(400, $"Not controller settings: {string.Join(", ", unknown)}.", unknown);
                }

                PushUndo($"controllers:{string.Join(',', values.Keys.Order())}");
                var sent = new List<ParamValue>();
                foreach (var (id, raw) in values) {
                    var value = RigControls.Descriptor.Params[RigControls.Descriptor.IndexOf(id)].Sanitize(raw);
                    preset.Controllers[id] = value;
                    sent.Add(new(IpcBlocks.Rig, id, value));
                }

                engine.SetParams(generation, sent);
                return Task.FromResult(Changed("controllersChanged"));
            }
        );

    /// <summary>The tempo: the preset's, or the global one when the rig runs on a global tempo.</summary>
    public Task<LiveState> SetTempoAsync(double bpm, long? expected) =>
        Locked(() => {
                Check(expected);
                var tempo = Math.Clamp(bpm, 30, 300);
                var global = globals.Get();
                if (global.TempoMode == "global") {
                    globals.Update(g => g.Tempo = tempo);
                } else {
                    PushUndo("tempo");
                    preset.Tempo = tempo;
                }

                engine.SetParams(generation, [new(IpcBlocks.Rig, "tempo", (float)tempo)]);
                return Task.FromResult(Changed("tempoChanged", markDirty: global.TempoMode != "global"));
            }
        );

    /// <summary>
    ///     The engine reports its tempo: shortly after a tap, take it back into the preset (or global) tempo so a save
    ///     keeps it. Only then: otherwise a telemetry frame from before a tempo edit would undo the edit. MIDI clock
    ///     tempo is live only and never stored.
    /// </summary>
    public Task FollowEngineTempoAsync(double bpm) =>
        DateTime.UtcNow.Ticks > Volatile.Read(ref tapFollowUntil)
            ? Task.CompletedTask
            : Locked(() => {
                    var global = globals.Get();
                    var stored = global.TempoMode == "global" ? global.Tempo : preset.Tempo;
                    if (global.TempoMode == "midiClock" || Math.Abs(stored - bpm) < 0.05) {
                        return Task.FromResult(State());
                    }

                    var rounded = Math.Round(bpm, 1);
                    if (global.TempoMode == "global") {
                        globals.Update(g => g.Tempo = rounded);
                        return Task.FromResult(Changed("tempoChanged", markDirty: false));
                    }

                    preset.Tempo = rounded;
                    return Task.FromResult(Changed("tempoChanged"));
                }
            );

    public Task<LiveState> CopyChannelAsync(string id, CopyChannelRequest request, long? expected) =>
        Locked(async () => {
                Check(expected);
                var block = Find(id);
                var descriptor = catalog.Descriptor(block.Type)
                    ?? throw new RigException(400, $"'{id}' has no channels.");
                CheckChannel(request.From, descriptor.Channels);
                CheckChannel(request.To, descriptor.Channels);
                PushUndo(null);
                var recompile = NeedsRecompile(block, request.From, request.To);
                block.Channels[request.To] = new(block.Channels.GetValueOrDefault(request.From) ?? []);
                if (Live(id).Channel == request.To) {
                    if (recompile) {
                        await SyncGraphAsync();
                    } else {
                        engine.SetParams(generation, ChannelValues(block, request.To));
                    }
                }

                return Changed("channelCopied", id, request.To);
            }
        );

    /// <summary>
    ///     Sets <paramref name="channel" /> (default: the one the active scene uses) of a block to a block preset: its
    ///     values, and every other parameter back to its default.
    /// </summary>
    public Task<LiveState> ApplyBlockPresetAsync(string id, BlockPreset source, string? channel, long? expected) =>
        Locked(async () => {
                Check(expected);
                var block = Find(id);
                var descriptor = catalog.Descriptor(block.Type)
                    ?? throw new RigException(400, $"'{id}' has no parameters.");
                if (source.Type != block.Type) {
                    throw new RigException(
                        400,
                        $"'{source.Name}' is a {source.Type} preset; '{id}' is a {block.Type}."
                    );
                }

                var state = Live(id);
                var target = channel ?? state.Channel;
                CheckChannel(target, descriptor.Channels);
                PushUndo(null);
                var before = block.Channels.GetValueOrDefault(target) ?? [];
                var values = source.Values
                    .Where(v => descriptor.IndexOf(v.Key) >= 0)
                    .ToDictionary(
                        static v => v.Key,
                        v => descriptor.Params[descriptor.IndexOf(v.Key)].Sanitize(v.Value)
                    );
                block.Channels[target] = values;
                if (block.Global is { } link) {
                    globals.Update(g => {
                            if (g.Blocks.FirstOrDefault(b => b.Id == link) is { } shared) {
                                shared.Channels[target] = new(values);
                            }
                        }
                    );
                }

                var linked = MirrorLinked(block);
                var running = linked.Where(b => Live(b.Id).Channel == target).ToList();
                if (target == state.Channel) {
                    running.Insert(0, block);
                }

                if (running.Count > 0 && RecompileDiffers(descriptor, before, values)) {
                    await SyncGraphAsync();
                } else {
                    foreach (var shown in running) {
                        engine.SetParams(generation, ChannelValues(shown, target));
                    }
                }

                return Changed("blockPresetApplied", id, target);
            }
        );

    /// <summary>Makes a block global: its settings move to the global library and follow it from then on.</summary>
    public Task<LiveState> LinkGlobalAsync(string id, string? name, long? expected) =>
        Locked(() => {
                Check(expected);
                var block = Find(id);
                if (BuiltInTypes.IsBuiltIn(block.Type)) {
                    throw new RigException(400, $"'{id}' cannot be global.");
                }

                if (block.Global is { } link && globals.Get().Blocks.Any(g => g.Id == link)) {
                    throw RigException.Conflict($"'{id}' is already global.");
                }

                PushUndo(null);
                var shared = new GlobalBlock {
                    Name = string.IsNullOrWhiteSpace(name) ? $"{preset.Name} {id}" : name.Trim(),
                    Type = block.Type,
                    Channels = block.Channels.ToDictionary(
                        static c => c.Key,
                        static c => new Dictionary<string, float>(c.Value)
                    ),
                    Assets = new(block.Assets)
                };
                globals.Update(g => g.Blocks.Add(shared));
                block.Global = shared.Id;
                return Task.FromResult(Changed("globalLinked", id));
            }
        );

    /// <summary>Links a block to an existing global block of the same type, taking its settings.</summary>
    public Task<LiveState> UseGlobalAsync(string id, string globalId, long? expected) =>
        Locked(async () => {
                Check(expected);
                var block = Find(id);
                var shared = globals.Get().Blocks.FirstOrDefault(g => g.Id == globalId && g.Type == block.Type)
                    ?? throw RigException.NotFound($"No global {block.Type} block '{globalId}'.");
                await StructuralAsync(p => {
                        var target = p.Blocks.Single(b => b.Id == id);
                        target.Global = shared.Id;
                        target.Channels = shared.Channels.ToDictionary(
                            static c => c.Key,
                            static c => new Dictionary<string, float>(c.Value)
                        );
                        target.Assets = new(shared.Assets);
                    }
                );
                return Changed("globalLinked", id);
            }
        );

    /// <summary>Unlinks a block: it keeps its current settings as its own.</summary>
    public Task<LiveState> UnlinkGlobalAsync(string id, long? expected) =>
        Locked(() => {
                Check(expected);
                PushUndo(null);
                Find(id).Global = null;
                return Task.FromResult(Changed("globalUnlinked", id));
            }
        );

    /// <summary>
    ///     Deletes a global block from the library. Blocks linked to it (here and in saved presets, as they load) keep
    ///     its settings as their own.
    /// </summary>
    public Task<LiveState> DeleteGlobalAsync(string globalId, long? expected) =>
        Locked(() => {
                Check(expected);
                if (globals.Get().Blocks.All(g => g.Id != globalId)) {
                    throw RigException.NotFound($"No global block '{globalId}'.");
                }

                var linked = preset.Blocks.Where(b => b.Global == globalId).ToList();
                if (linked.Count > 0) {
                    PushUndo(null);
                }

                foreach (var block in linked) {
                    block.Global = null;
                }

                globals.Update(g => g.Blocks.RemoveAll(b => b.Id == globalId));
                return Task.FromResult(Changed("globalDeleted", globalId));
            }
        );

    /// <summary>Manual A-D: global modifier sources (a person, a MIDI pedal).</summary>
    public void SetManual(int index, float value) {
        var clamped = Math.Clamp(value, 0f, 1f);
        globals.Update(g => g.Manual[index] = clamped);
        engine.SetParams(generation, [new(IpcBlocks.Rig, $"manual{(char)('A' + index)}", clamped)]);
    }

    /// <summary>External 1-8 (expression pedals, CCs): live values only, never stored.</summary>
    public void SetExternal(int index, float value) =>
        engine.SetParams(generation, [new(IpcBlocks.Rig, $"ext{index + 1}", Math.Clamp(value, 0f, 1f))]);

    /// <summary>Global settings changed (EQ, metronome, tempo mode): put them into the engine.</summary>
    public void ApplyGlobalSettings(GlobalSettings global) {
        var values = GlobalValues(global);
        if (global.TempoMode == "preset") {
            values.Add(new(IpcBlocks.Rig, "tempo", (float)preset.Tempo));
        }

        engine.SetParams(generation, values);
        engine.SetMidi(global.TempoMode == "midiClock");
        if (global.Oversampling != Volatile.Read(ref compiledOversampling)) {
            // The filters change the blocks' latency, so the graph is compiled again (gaplessly).
            _ = ResyncAsync()
                .ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
