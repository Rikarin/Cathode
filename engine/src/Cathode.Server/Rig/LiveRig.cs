using Cathode.Contracts;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Parameters;
using Cathode.Engine.Rig;
using Cathode.Ipc;
using Cathode.Server.Engine;
using Cathode.Server.Realtime;
using Cathode.Server.Stores;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Cathode.Server.Rig;

/// <summary>
///     The rig as it runs: the working copy of a preset, the active scene, undo/redo and revisions
///     (docs/adr/0004-state-ownership.md). Structural edits recompile the engine graph and roll back if the host
///     rejects them; knobs, scenes and channels only send parameter and bypass commands. Every change raises an
///     event for WebSocket clients. All operations are serialised.
/// </summary>
public sealed partial class LiveRig(
    BlockCatalog catalog,
    AssetStore assets,
    PresetStore presets,
    IEngineSync engine,
    EventHub hub,
    GlobalStore globals
) : IDisposable {
    const int UndoLimit = 100;

    readonly SemaphoreSlim gate = new(1, 1);
    readonly List<Snapshot> undo = [];
    readonly List<Snapshot> redo = [];
    Preset preset = Preset.CreateDefault();
    int activeScene;
    long revision;
    bool dirty;
    int generation;
    string? coalesceKey;
    string compiledOversampling = "minimum";

    /// <summary>
    ///     Scene Ignore: blocks whose live bypass and channel differ from the active scene's, because their channel
    ///     ignored the last scene change. Concurrent: footswitch LEDs read it without the lock.
    /// </summary>
    readonly ConcurrentDictionary<string, SceneBlockState> held = new(StringComparer.Ordinal);

    /// <summary>The working copy as last loaded or saved, for Scene Revert.</summary>
    string savedJson = JsonSerializer.Serialize(Preset.CreateDefault(), ContractsJsonContext.Default.Preset);

    /// <summary>Controller settings a preset may store; the rest of the rig bank is scene, global or live state.</summary>
    static readonly HashSet<string> ControllerParams = [
        ..RigControls.Descriptor.Params.Select(static p => p.Id)
            .Where(static id => id.StartsWith("lfo", StringComparison.Ordinal)
                || id.StartsWith("adsr", StringComparison.Ordinal)
                || id.StartsWith("env", StringComparison.Ordinal)
                || id.StartsWith("seq", StringComparison.Ordinal)
                || id is "pitchLow" or "pitchHigh"
            )
    ];

    /// <summary>The active scene, for listeners that must not wait for the lock (footswitch LEDs).</summary>
    public int ActiveScene => Volatile.Read(ref activeScene);

    public void Dispose() => gate.Dispose();

    /// <summary>Whether a block is bypassed in the active scene (unlocked read for LED feedback).</summary>
    public bool IsBypassed(string block) => Live(block).Bypassed;

    /// <summary>What a block is running now: its held state (Scene Ignore) or the active scene's.</summary>
    public SceneBlockState Live(string block) =>
        held.TryGetValue(block, out var state) ? state : Volatile.Read(ref preset).Scenes[ActiveScene].StateOf(block);

    public Task<LiveState> StateAsync() => Locked(() => Task.FromResult(State()));

    /// <summary>Sends the whole rig to the engine again (after the host (re)connects).</summary>
    public Task ResyncAsync() =>
        Locked(async () => {
                await SyncGraphAsync();
                return State();
            }
        );

    public Task<LiveState> NewAsync() =>
        Locked(async () => {
                await ReplaceAsync(Preset.CreateDefault());
                Remember();
                // A fresh preset has nothing to lose: it is not an unsaved change.
                return Changed("presetLoaded", markDirty: false);
            }
        );

    /// <summary>
    ///     Loads a preset from the library and starts on <paramref name="scene" /> (a setlist entry's scene), or by
    ///     default on the global Default Scene: the scene it was saved on, or a fixed one.
    /// </summary>
    public Task<LiveState> LoadAsync(string id, int? scene = null) =>
        Locked(async () => {
                var loaded = presets.Get(id) ?? throw RigException.NotFound($"No preset '{id}'.");
                if (loaded.SchemaVersion > Preset.CurrentSchemaVersion) {
                    throw RigException.Conflict(
                        $"Preset '{loaded.Name}' was saved by a newer version of Cathode (schema {loaded.SchemaVersion})."
                    );
                }

                var start = scene
                    ?? (globals.Get().DefaultScene is var fixedScene and >= 0 and < Preset.SceneCount
                        ? fixedScene
                        : Math.Clamp(loaded.SavedScene, 0, Preset.SceneCount - 1));
                CheckScene(start);
                ApplyGlobals(loaded);
                await ReplaceAsync(loaded, start);
                Remember();
                // Just loaded, so it matches what is saved.
                return Changed("presetLoaded", scene: start, markDirty: false);
            }
        );

    public Task<LiveState> SaveAsync(SaveRequest request, long? expected) =>
        Locked(() => {
                Check(expected);
                if (request.AsNew) {
                    preset.Id = Preset.NewId();
                }

                if (!string.IsNullOrWhiteSpace(request.Name)) {
                    preset.Name = request.Name.Trim();
                }

                return Task.FromResult(SaveWorkingCopy());
            }
        );

    /// <summary>
    ///     Replaces the working copy's content with <paramref name="next" />, keeping its identity, as one undoable
    ///     edit. Editors use it to switch between A/B compare snapshots.
    /// </summary>
    public Task<LiveState> PutAsync(Preset next, long? expected) =>
        Locked(async () => {
                Check(expected);
                if (next.SchemaVersion > Preset.CurrentSchemaVersion) {
                    throw RigException.Conflict(
                        $"Preset '{next.Name}' was saved by a newer version of Cathode (schema {next.SchemaVersion})."
                    );
                }

                if (next.Scenes.Count != Preset.SceneCount) {
                    throw new RigException(400, $"A preset has exactly {Preset.SceneCount} scenes.");
                }

                await StructuralAsync(p => {
                        p.Name = next.Name;
                        p.Author = next.Author;
                        p.Description = next.Description;
                        p.Tags = next.Tags;
                        p.Tempo = next.Tempo;
                        p.Rows = next.Rows;
                        p.Columns = next.Columns;
                        p.Blocks = next.Blocks;
                        p.Cables = next.Cables;
                        p.Scenes = next.Scenes;
                    }
                );
                return Changed("presetReplaced");
            }
        );

    public Task<LiveState> UndoAsync() => Locked(() => StepAsync(undo, redo, "undo"));

    public Task<LiveState> RedoAsync() => Locked(() => StepAsync(redo, undo, "redo"));

    public Task<LiveState> AddBlockAsync(AddBlockRequest request, long? expected) =>
        Locked(async () => {
                Check(expected);
                var info = catalog.Info(request.Type)
                    ?? throw new RigException(400, $"Unknown block type '{request.Type}'.");
                CheckCell(request.Row, request.Column);
                if (preset.Blocks.Count(b => b.Type == request.Type) >= info.MaxInstances) {
                    throw RigException.Conflict($"A preset can hold at most {info.MaxInstances} {info.Name} block(s).");
                }

                var id = request.Id ?? NextId(request.Type);
                if (preset.Blocks.Any(b => b.Id == id)) {
                    throw RigException.Conflict($"Block id '{id}' is taken.");
                }

                // A new Input takes the first hardware input no other Input reads.
                var source = request.Type == BuiltInTypes.Input ? FreeSource(preset) : 0;

                await StructuralAsync(p => {
                        // Dropping a block onto a shunt replaces it and keeps its cables, as on the Axe-Fx.
                        var occupant = p.Blocks.FirstOrDefault(b => b.Row == request.Row && b.Column == request.Column);
                        if (occupant is not null && occupant.Type != BuiltInTypes.Shunt) {
                            throw RigException.Conflict(
                                $"Cell ({request.Row},{request.Column}) holds '{occupant.Id}'."
                            );
                        }

                        if (occupant is not null) {
                            p.Blocks.Remove(occupant);
                            Repoint(p, occupant.Id, id);
                        }

                        var added = new PresetBlock {
                            Id = id, Type = request.Type, Row = request.Row, Column = request.Column
                        };
                        if (source > 0) {
                            SetSource(added, source, info.Channels);
                        }

                        p.Blocks.Add(added);
                    }
                );
                return Changed("blockAdded", id);
            }
        );

    public Task<LiveState> RemoveBlockAsync(string id, long? expected) =>
        Locked(async () => {
                Check(expected);
                Find(id);
                await StructuralAsync(p => {
                        var block = p.Blocks.Single(b => b.Id == id);
                        p.Blocks.Remove(block);
                        foreach (var scene in p.Scenes) {
                            scene.Blocks.Remove(id);
                        }

                        held.TryRemove(id, out _);

                        // A block in a signal path leaves a shunt that keeps its cables, so the path stays whole: the
                        // reverse of dropping a block onto a shunt. A shunt, or a block at the end of a branch, just goes.
                        if (!LeaveShunt(p, block, block.Row, block.Column)) {
                            p.Cables.RemoveAll(c => c.From == id || c.To == id);
                        }
                    }
                );
                return Changed("blockRemoved", id);
            }
        );

    public Task<LiveState> MoveBlockAsync(string id, MoveBlockRequest request, long? expected) =>
        Locked(async () => {
                Check(expected);
                Find(id);
                CheckCell(request.Row, request.Column);
                var occupant = preset.Blocks.FirstOrDefault(b => b.Id != id
                    && b.Row == request.Row
                    && b.Column == request.Column
                );
                if (occupant is not null && occupant.Type != BuiltInTypes.Shunt) {
                    throw RigException.Conflict($"Cell ({request.Row},{request.Column}) is occupied.");
                }

                await StructuralAsync(p => {
                        var block = p.Blocks.Single(b => b.Id == id);
                        var (fromRow, fromColumn) = (block.Row, block.Column);
                        // Moving out of a signal path leaves a shunt behind, as removing does.
                        LeaveShunt(p, block, fromRow, fromColumn);
                        block.Row = request.Row;
                        block.Column = request.Column;
                        // Dropped onto a shunt, the block takes its place and its cables, as adding one does.
                        var shunt = p.Blocks.FirstOrDefault(b => b.Id != id
                            && b.Row == request.Row
                            && b.Column == request.Column
                        );
                        if (shunt is not null) {
                            p.Blocks.Remove(shunt);
                            Repoint(p, shunt.Id, id);
                        }

                        // Cables only join adjacent columns; any the move broke are removed.
                        p.Cables.RemoveAll(c => (c.From == id || c.To == id) && !Adjacent(p, c));
                    }
                );
                return Changed("blockMoved", id);
            }
        );

    public Task<LiveState> AddCableAsync(CableRequest request, long? expected) =>
        Locked(async () => {
                Check(expected);
                var from = Find(request.From);
                var to = Find(request.To);
                if (to.Column <= from.Column) {
                    throw new RigException(400, "Cables run left to right, to a block in a later column.");
                }

                // Across empty cells, shunts carry the cable on the source's row (or else the target's).
                var row = new[] { from.Row, to.Row }.Cast<int?>()
                    .FirstOrDefault(r => Enumerable.Range(from.Column + 1, to.Column - from.Column - 1)
                        .All(c => BlockAt(preset, r!.Value, c) is null or { Type: BuiltInTypes.Shunt })
                    );
                if (row is null) {
                    throw new RigException(400, "A block is in the way: cables cross only empty cells and shunts.");
                }

                if (BuiltInTypes.IsOutput(from.Type) || to.Type == BuiltInTypes.Input) {
                    throw new RigException(
                        400,
                        "Cables run from an output side to an input side: nothing leaves an Output block or enters an Input block."
                    );
                }

                if (preset.Cables.Any(c => c.From == request.From && c.To == request.To)) {
                    throw RigException.Conflict("That cable already exists.");
                }

                await StructuralAsync(p => {
                        var previous = request.From;
                        for (var column = from.Column + 1; column < to.Column; column++) {
                            var shunt = BlockAt(p, row.Value, column);
                            if (shunt is null) {
                                var id = $"shunt-r{row}c{column}";
                                shunt = new() {
                                    Id = p.Blocks.Any(b => b.Id == id) ? NextId(BuiltInTypes.Shunt) : id,
                                    Type = BuiltInTypes.Shunt,
                                    Row = row.Value,
                                    Column = column
                                };
                                p.Blocks.Add(shunt);
                            }

                            Connect(p, previous, shunt.Id);
                            previous = shunt.Id;
                        }

                        Connect(p, previous, request.To);
                    }
                );
                return Changed("cableAdded", request.From);
            }
        );

    public Task<LiveState> RemoveCableAsync(CableRequest request, long? expected) =>
        Locked(async () => {
                Check(expected);
                if (!preset.Cables.Any(c => c.From == request.From && c.To == request.To)) {
                    throw RigException.NotFound("No such cable.");
                }

                await StructuralAsync(p => p.Cables.RemoveAll(c => c.From == request.From && c.To == request.To));
                return Changed("cableRemoved", request.From);
            }
        );

    public Task<LiveState> SetAssetAsync(string id, string slot, AssetRequest request, long? expected) =>
        Locked(async () => {
                Check(expected);
                var block = Find(id);
                var info = catalog.Info(block.Type)!;
                if (!info.AssetSlots.Contains(slot)) {
                    throw new RigException(400, $"{info.Name} has no asset slot '{slot}'.");
                }

                var asset = assets.Get(request.Asset) ?? throw RigException.NotFound($"No asset {request.Asset}.");
                if (!AssetStore.Fits(slot, asset.Kind)) {
                    throw new RigException(400, $"Slot '{slot}' cannot take a {asset.Kind} asset.");
                }

                await StructuralAsync(p => p.Blocks.Single(b => b.Id == id).Assets[slot] = request.Asset);
                return Changed("assetChanged", id);
            }
        );

    /// <summary>Sets parameters on <paramref name="channel" /> (default: the channel the active scene uses).</summary>
    public Task<LiveState> SetParamsAsync(
        string id,
        string? channel,
        IReadOnlyDictionary<string, float> values,
        long? expected
    ) =>
        Locked(async () => {
                Check(expected);
                var block = Find(id);
                var descriptor = catalog.Descriptor(block.Type)
                    ?? throw new RigException(400, $"'{id}' has no parameters.");
                var state = Live(id);
                var target = channel ?? state.Channel;
                CheckChannel(target, descriptor.Channels);

                var unknown = values.Keys.Where(k => descriptor.IndexOf(k) < 0).ToList();
                if (unknown.Count > 0) {
                    throw new RigException(
                        400,
                        $"{descriptor.Name} has no parameter(s) {string.Join(", ", unknown)}.",
                        unknown
                    );
                }

                int? source = block.Type == BuiltInTypes.Input && values.TryGetValue(SourceParam, out var rawSource)
                    ? (int)descriptor.Params[descriptor.IndexOf(SourceParam)].Sanitize(rawSource)
                    : null;
                if (source is { } wanted) {
                    CheckSource(block, wanted);
                }

                PushUndo($"params:{id}:{target}:{string.Join(',', values.Keys.Order())}");
                var stored = block.Channel(target);
                var sent = new List<ParamValue>();
                var recompile = false;
                var typeParam = descriptor.IndexOf("type") is var typeIndex and >= 0
                    ? descriptor.Params[typeIndex]
                    : null;
                var typeChanged = typeParam is not null
                    && values.TryGetValue("type", out var type)
                    && typeParam.Sanitize(type) != stored.GetValueOrDefault("type", typeParam.Default);
                foreach (var (param, raw) in values) {
                    var descriptorParam = descriptor.Params[descriptor.IndexOf(param)];
                    var value = descriptorParam.Sanitize(raw);
                    recompile |= descriptorParam.Recompile
                        && stored.GetValueOrDefault(param, descriptorParam.Default) != value;
                    stored[param] = value;
                    sent.Add(new(id, param, value));
                }

                if (source is { } chosen) {
                    SetSource(block, chosen, descriptor.Channels);
                }

                // A new model starts from its own defaults, not the old one's settings (values sent with it still win).
                if (typeChanged) {
                    foreach (var param in descriptor.TypeResets.Where(r => !values.ContainsKey(r))) {
                        var value = descriptor.Params[descriptor.IndexOf(param)].Default;
                        recompile |= descriptor.Params[descriptor.IndexOf(param)].Recompile
                            && stored.GetValueOrDefault(param, value) != value;
                        stored[param] = value;
                        sent.Add(new(id, param, value));
                    }
                }

                if (block.Global is { } link) {
                    globals.Update(g => {
                            if (g.Blocks.FirstOrDefault(b => b.Id == link) is { } shared) {
                                shared.Channels[target] = new(stored);
                            }
                        }
                    );
                }

                // The preset's other blocks linked to the same global block follow.
                var linked = MirrorLinked(block);
                var running = linked.Where(b => Live(b.Id).Channel == target).ToList();
                if (recompile && (target == state.Channel || running.Count > 0)) {
                    await SyncGraphAsync(); // a latency change (look-ahead): the compiler has to see it
                } else {
                    if (target == state.Channel) {
                        engine.SetParams(generation, sent);
                    }

                    foreach (var other in running) {
                        engine.SetParams(generation, [..sent.Select(v => v with { Block = other.Id })]);
                    }
                }

                revision++;
                dirty = true;
                foreach (var value in sent) {
                    Publish(new("paramChanged", revision, id, target, value.Param, value.Value));
                    foreach (var other in linked) {
                        Publish(new("paramChanged", revision, other.Id, target, value.Param, value.Value));
                    }
                }

                return State();
            }
        );

    public Task<LiveState> SetBypassAsync(string id, bool bypassed, long? expected) =>
        Locked(() => {
                Check(expected);
                var block = Find(id);
                if (BuiltInTypes.IsBuiltIn(block.Type)) {
                    throw new RigException(400, $"'{id}' cannot be bypassed.");
                }

                PushUndo(null);
                // A change made while playing stays with the scene (unless Scene Revert); a held block takes it too.
                var live = Live(id) with { Bypassed = bypassed };
                preset.Scenes[activeScene].Blocks[id] = live;
                held.TryRemove(id, out _);
                engine.SetBypass(generation, id, bypassed);
                return Task.FromResult(Changed("bypassChanged", id));
            }
        );

    public Task<LiveState> SetChannelAsync(string id, string channel, long? expected) =>
        Locked(async () => {
                Check(expected);
                var block = Find(id);
                var descriptor = catalog.Descriptor(block.Type)
                    ?? throw new RigException(400, $"'{id}' has no channels.");
                CheckChannel(channel, descriptor.Channels);

                PushUndo(null);
                var before = LiveStates();
                preset.Scenes[activeScene].Blocks[id] = Live(id) with { Channel = channel };
                held.TryRemove(id, out _);
                await ApplyLiveAsync(before);
                return Changed("channelChanged", id, channel);
            }
        );

    /// <summary>
    ///     Switches scene: only the blocks whose bypass or channel differ are touched; nothing recompiles. With Scene
    ///     Revert on, the scene is first put back as it was last saved. A block whose live channel has Scene Ignore on
    ///     keeps its bypass and channel.
    /// </summary>
    public Task<LiveState> SelectSceneAsync(int scene) =>
        Locked(async () => {
                CheckScene(scene);
                var before = LiveStates();
                if (globals.Get().SceneRevert) {
                    var saved = JsonSerializer.Deserialize(savedJson, ContractsJsonContext.Default.Preset)!;
                    if (saved.Id == preset.Id && saved.Scenes.Count == Preset.SceneCount) {
                        preset.Scenes[scene] = saved.Scenes[scene];
                    }
                }

                var to = preset.Scenes[scene];
                foreach (var block in preset.Blocks) {
                    var live = before.GetValueOrDefault(block.Id) ?? SceneBlockState.Default;
                    if (block.IgnoresScenes(live.Channel) && live != to.StateOf(block.Id)) {
                        held[block.Id] = live;
                    } else {
                        held.TryRemove(block.Id, out _);
                    }
                }

                activeScene = scene;
                await ApplyLiveAsync(before);
                engine.SetParams(generation, SceneValues());
                revision++;
                Publish(new("sceneChanged", revision, Scene: scene));
                return State();
            }
        );

    /// <summary>Every block's live state (<see cref="Live" />), by id.</summary>
    Dictionary<string, SceneBlockState> LiveStates() =>
        preset.Blocks.ToDictionary(static b => b.Id, b => Live(b.Id), StringComparer.Ordinal);

    /// <summary>
    ///     Moves the engine from the live states <paramref name="before" /> to the current ones: the channel values and
    ///     scoped modifiers of every block whose channel changed and the bypass of every one whose bypass did, or a
    ///     gapless rebuild when a channel differs in a parameter only a recompile applies (look-ahead).
    /// </summary>
    async Task ApplyLiveAsync(IReadOnlyDictionary<string, SceneBlockState> before) {
        var changes = preset.Blocks.Where(b => catalog.Descriptor(b.Type) is not null)
            .Select(b => (Block: b, From: before.GetValueOrDefault(b.Id) ?? SceneBlockState.Default, To: Live(b.Id)))
            .Where(static c => c.From != c.To)
            .ToList();
        if (changes.Any(c => NeedsRecompile(c.Block, c.From.Channel, c.To.Channel))) {
            await SyncGraphAsync();
            return;
        }

        foreach (var (block, from, to) in changes) {
            if (from.Channel != to.Channel) {
                SyncModifiers(block, to.Channel);
                engine.SetParams(generation, ChannelValues(block, to.Channel));
            }

            if (from.Bypassed != to.Bypassed && !BuiltInTypes.IsBuiltIn(block.Type)) {
                engine.SetBypass(generation, block.Id, to.Bypassed);
            }
        }
    }

    /// <summary>Turns the block's channel-scoped modifiers on for <paramref name="channel" /> and off for the rest.</summary>
    void SyncModifiers(PresetBlock block, string channel) {
        var descriptor = catalog.Descriptor(block.Type);
        foreach (var modifier in preset.Modifiers.Where(m => m.Block == block.Id && m.Channel is not null)) {
            var index = descriptor?.IndexOf(modifier.Param) ?? -1;
            var value = index < 0
                ? 0f
                : block.Channels.GetValueOrDefault(channel)
                    ?.GetValueOrDefault(modifier.Param, descriptor!.Params[index].Default)
                ?? descriptor!.Params[index].Default;
            engine.SetModifier(generation, block.Id, modifier.Param, modifier.Channel == channel, value);
        }
    }

    /// <summary>
    ///     Stores the working copy in the library, remembering the scene it is on and the copy for Scene Revert. A
    ///     factory preset cannot be overwritten: saving one stores the user's own copy, under a new id.
    /// </summary>
    LiveState SaveWorkingCopy() {
        if (preset.Factory == true || FactoryPresets.IsFactoryId(preset.Id)) {
            preset.Id = Preset.NewId();
            preset.Factory = null;
        }

        preset.SavedScene = activeScene;
        presets.Save(preset);
        Remember();
        dirty = false;
        return Changed("presetSaved", markDirty: false);
    }

    /// <summary>Takes the working copy as the saved state Scene Revert recalls.</summary>
    void Remember() => savedJson = JsonSerializer.Serialize(preset, ContractsJsonContext.Default.Preset);

    /// <summary>
    ///     Renames the working copy and sets its author, tags and description, as one undoable edit. Nothing reaches
    ///     the engine. Blank tags are dropped; an empty author or description clears it.
    /// </summary>
    public Task<LiveState> UpdateInfoAsync(PresetInfoRequest request, long? expected) =>
        Locked(() => {
                Check(expected);
                PushUndo(null);
                if (request.Name is { } name) {
                    preset.Name = name.Trim();
                }

                if (request.Author is { } author) {
                    preset.Author = string.IsNullOrWhiteSpace(author) ? null : author.Trim();
                }

                if (request.Tags is { } tags) {
                    preset.Tags = [..tags.Select(static t => t.Trim()).Where(static t => t.Length > 0).Distinct()];
                }

                if (request.Description is { } description) {
                    preset.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
                }

                return Task.FromResult(Changed("presetInfoUpdated"));
            }
        );

    public Task<LiveState> UpdateSceneAsync(int scene, SceneUpdateRequest request, long? expected) =>
        Locked(() => {
                Check(expected);
                CheckScene(scene);
                if (request.Midi is { } messages) {
                    CheckSceneMidi(messages);
                }

                PushUndo(null);
                var target = preset.Scenes[scene];
                if (request.Name is { } name) {
                    target.Name = name.Trim();
                }

                if (request.Level is { } level) {
                    target.Level = Math.Clamp(level, -60f, 12f);
                }

                if (request.Controllers is { Count: 4 } controllers) {
                    target.Controllers = [..controllers.Select(static c => Math.Clamp(c, 0f, 1f))];
                }

                if (request.Midi is { } midi) {
                    target.Midi = [..midi];
                }

                if (scene == activeScene) {
                    engine.SetParams(generation, SceneValues());
                }

                return Task.FromResult(Changed("sceneUpdated", scene: scene));
            }
        );

    /// <summary>
    ///     Everything the global settings put into the engine: tempo (global mode), metronome, Manual A-D, the outputs'
    ///     modes, Boost/Pad and EQs, and Input 1's gain and calibration.
    /// </summary>
    public static List<ParamValue> GlobalValues(GlobalSettings global) {
        var values = new List<ParamValue> {
            new(IpcBlocks.Rig, "metronome", global.Metronome ? 1 : 0),
            new(IpcBlocks.Rig, "metroLevel", global.MetronomeLevel),
            new(IpcBlocks.Rig, "effectsMix", global.EffectsMix),
            new(IpcBlocks.Rig, "gateOffset", global.NoiseGateOffset),
            new(IpcBlocks.Rig, "cabsOff", global.CabModelingOff ? 1 : 0),
            new(IpcBlocks.Rig, "spillover", global.PresetSpillover ? 1 : 0)
        };
        if (global.TempoMode == "global") {
            values.Add(new(IpcBlocks.Rig, "tempo", (float)global.Tempo));
        }

        values.AddRange(
            global.Manual.Take(4).Select(static (v, i) => new ParamValue(IpcBlocks.Rig, $"manual{(char)('A' + i)}", v))
        );
        values.Add(new(IpcBlocks.Rig, "copyOut2", global.CopyOutput1To2 ? 1 : 0));
        for (var k = 0; k < 4; k++) {
            var output = k < global.Outputs.Count ? global.Outputs[k] : new();
            var mode = Api.GlobalValidation.OutputModes.IndexOf(output.Mode);
            values.Add(new(IpcBlocks.Rig, $"out{k + 1}Mode", Math.Max(mode, 0)));
            values.Add(new(IpcBlocks.Rig, $"out{k + 1}Level", output.BoostPad));
            foreach (var param in GraphicEqBlock.Info.Params) {
                values.Add(new(IpcBlocks.GlobalEq(k), param.Id, output.Eq.GetValueOrDefault(param.Id, param.Default)));
            }
        }

        var input = global.Inputs.Count > 0 ? global.Inputs[0] : new();
        values.Add(new(IpcBlocks.Rig, "in1Gain", input.Gain));
        values.Add(new(IpcBlocks.Rig, "in1Cal", (float)(20 * Math.Log10(Math.Max(input.FullScaleVolts, 1e-3f)))));
        return values;
    }

    /// <summary>The MIDI messages scene <paramref name="scene" /> sends (unlocked read, for the MIDI router).</summary>
    public IReadOnlyList<SceneMidiMessage> SceneMidi(int scene) =>
        (uint)scene < Preset.SceneCount ? [..Volatile.Read(ref preset).Scenes[scene].Midi] : [];

    /// <summary>
    ///     Whether channels <paramref name="from" /> and <paramref name="to" /> differ in a parameter only a recompile
    ///     applies.
    /// </summary>
    bool NeedsRecompile(PresetBlock block, string from, string to) {
        if (from == to || catalog.Descriptor(block.Type) is not { } descriptor) {
            return false;
        }

        return RecompileDiffers(
            descriptor,
            block.Channels.GetValueOrDefault(from),
            block.Channels.GetValueOrDefault(to)
        );
    }

    /// <summary>
    ///     Whether the channel values <paramref name="a" /> and <paramref name="b" /> (null: all defaults) differ in a
    ///     parameter only a recompile applies. A recompile rebuilds every block of the graph, so the rig asks for one
    ///     only when this holds.
    /// </summary>
    static bool RecompileDiffers(
        BlockDescriptor descriptor,
        IReadOnlyDictionary<string, float>? a,
        IReadOnlyDictionary<string, float>? b
    ) =>
        descriptor.Params.Any(p => p.Recompile
            && (a?.GetValueOrDefault(p.Id, p.Default) ?? p.Default)
            != (b?.GetValueOrDefault(p.Id, p.Default) ?? p.Default)
        );

    /// <summary>Every parameter of the block type with <paramref name="channel" />'s value or the default.</summary>
    List<ParamValue> ChannelValues(PresetBlock block, string channel) {
        var descriptor = catalog.Descriptor(block.Type)!;
        var stored = block.Channels.GetValueOrDefault(channel);
        return [
            ..descriptor.Params.Select(p => new ParamValue(
                    block.Id,
                    p.Id,
                    stored?.GetValueOrDefault(p.Id, p.Default) ?? p.Default
                )
            )
        ];
    }

    async Task StructuralAsync(Action<Preset> edit) {
        var before = Capture();
        edit(preset);
        try {
            await SyncGraphAsync();
        } catch (IpcException e) {
            Restore(before);
            await SyncGraphAsync(); // put the engine back on the previous graph
            throw new RigException(422, "The engine could not load the result: " + e.Message, e.Errors);
        } catch (RigException) {
            Restore(before);
            throw;
        }

        coalesceKey = null;
        Remember(undo, before);
        redo.Clear();
    }

    async Task ReplaceAsync(Preset next, int scene = 0) {
        var before = Capture();
        preset = next;
        activeScene = scene;
        held.Clear();
        try {
            await SyncGraphAsync();
        } catch (Exception e) when (e is IpcException or RigException) {
            Restore(before);
            await SyncGraphAsync();
            throw e is IpcException ipc
                ? new RigException(422, "The engine could not load this preset: " + ipc.Message, ipc.Errors)
                : e;
        }

        undo.Clear();
        redo.Clear();
        coalesceKey = null;
        dirty = false;
    }

    async Task<LiveState> StepAsync(List<Snapshot> from, List<Snapshot> to, string eventType) {
        if (from.Count == 0) {
            throw RigException.Conflict($"Nothing to {eventType}.");
        }

        Remember(to, Capture());
        Restore(from[^1]);
        from.RemoveAt(from.Count - 1);
        coalesceKey = null;
        await SyncGraphAsync();
        return Changed(eventType);
    }

    async Task SyncGraphAsync() {
        generation++;
        var oversampling = globals.Get().Oversampling;
        await engine.LoadGraphAsync(PresetGraph.Build(preset, activeScene, assets, held, oversampling), generation);
        compiledOversampling = oversampling;
        engine.SetParams(
            generation,
            [..RigValues(), ..GlobalValues(globals.Get()), new(IpcBlocks.Rig, "mute", Muted ? 1 : 0)]
        );
    }

    /// <summary>The rig bank as this preset and scene set it: controllers, tempo, scene level and controllers.</summary>
    List<ParamValue> RigValues() {
        var values = new List<ParamValue>(SceneValues());
        foreach (var id in ControllerParams) {
            var param = RigControls.Descriptor.Params[RigControls.Descriptor.IndexOf(id)];
            values.Add(new(IpcBlocks.Rig, id, preset.Controllers.GetValueOrDefault(id, param.Default)));
        }

        var global = globals.Get();
        if (global.TempoMode == "preset") {
            values.Add(new(IpcBlocks.Rig, "tempo", (float)preset.Tempo));
        }

        return values;
    }

    List<ParamValue> SceneValues() {
        var scene = preset.Scenes[activeScene];
        return [
            new(IpcBlocks.Rig, "level", scene.Level),
            ..scene.Controllers.Take(4).Select(static (v, i) => new ParamValue(IpcBlocks.Rig, $"scene{i + 1}", v))
        ];
    }

    /// <summary>
    ///     Linked blocks take their settings from the global library; one whose global block was deleted keeps its own
    ///     and is unlinked.
    /// </summary>
    void ApplyGlobals(Preset target) {
        var library = globals.Get().Blocks;
        foreach (var block in target.Blocks.Where(static b => b.Global is not null)) {
            if (library.FirstOrDefault(g => g.Id == block.Global && g.Type == block.Type) is { } shared) {
                block.Channels = shared.Channels.ToDictionary(
                    static c => c.Key,
                    static c => new Dictionary<string, float>(c.Value)
                );
                block.Assets = new(shared.Assets);
            } else {
                block.Global = null;
            }
        }
    }

    void Publish(RigEvent change) {
        hub.Publish(change, ContractsJsonContext.Default.RigEvent);
        Changes?.Invoke(change);
    }

    /// <summary>Records an undo point unless this edit continues the previous one (e.g. a knob being dragged).</summary>
    void PushUndo(string? key) {
        if (key is null || key != coalesceKey) {
            Remember(undo, Capture());
            redo.Clear();
        }

        coalesceKey = key;
    }

    static void Remember(List<Snapshot> stack, Snapshot snapshot) {
        stack.Add(snapshot);
        if (stack.Count > UndoLimit) {
            stack.RemoveAt(0);
        }
    }

    Snapshot Capture() => new(JsonSerializer.Serialize(preset, ContractsJsonContext.Default.Preset), activeScene);

    void Restore(Snapshot snapshot) {
        preset = JsonSerializer.Deserialize(snapshot.Json, ContractsJsonContext.Default.Preset)!;
        activeScene = snapshot.ActiveScene;
        foreach (var id in held.Keys.Where(id => preset.Blocks.All(b => b.Id != id))) {
            held.TryRemove(id, out _);
        }
    }

    LiveState Changed(
        string eventType,
        string? block = null,
        string? channel = null,
        int? scene = null,
        bool markDirty = true
    ) {
        revision++;
        dirty |= markDirty;
        Publish(new(eventType, revision, block, channel, Scene: scene));
        return State();
    }

    LiveState State() {
        var copy = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(preset, ContractsJsonContext.Default.Preset),
            ContractsJsonContext.Default.Preset
        )!;
        return new(
            copy,
            activeScene,
            revision,
            dirty,
            undo.Count > 0,
            redo.Count > 0,
            held.IsEmpty ? null : new(held, StringComparer.Ordinal),
            Muted
        );
    }

    PresetBlock Find(string id) =>
        preset.Blocks.FirstOrDefault(b => b.Id == id) ?? throw RigException.NotFound($"No block '{id}'.");

    /// <summary>
    ///     Puts a shunt in a block's cell and hands it the block's cables, when the block sits in a signal path (cables
    ///     both in and out); a shunt or a branch end leaves nothing. Returns whether a shunt was left.
    /// </summary>
    bool LeaveShunt(Preset p, PresetBlock block, int row, int column) {
        var inPath = block.Type != BuiltInTypes.Shunt
            && p.Cables.Any(c => c.To == block.Id)
            && p.Cables.Any(c => c.From == block.Id);
        if (!inPath) {
            return false;
        }

        var shunt = $"shunt-r{row}c{column}";
        if (p.Blocks.Any(b => b.Id == shunt)) {
            shunt = NextId(BuiltInTypes.Shunt);
        }

        p.Blocks.Add(new() { Id = shunt, Type = BuiltInTypes.Shunt, Row = row, Column = column });
        Repoint(p, block.Id, shunt);
        return true;
    }

    /// <summary>Moves every cable end at <paramref name="from" /> to <paramref name="to" />.</summary>
    static void Repoint(Preset p, string from, string to) {
        foreach (var cable in p.Cables) {
            cable.From = cable.From == from ? to : cable.From;
            cable.To = cable.To == from ? to : cable.To;
        }
    }

    string NextId(string type) {
        // Outputs 2-4 are named like Output 1 (out1): out2, out3, out4.
        if (type is BuiltInTypes.Output2 or BuiltInTypes.Output3 or BuiltInTypes.Output4
            && preset.Blocks.All(b => b.Id != $"out{type[^1]}")) {
            return $"out{type[^1]}";
        }

        for (var n = 1;; n++) {
            var id = $"{type}{n}";
            if (preset.Blocks.All(b => b.Id != id)) {
                return id;
            }
        }
    }

    void Check(long? expected) {
        if (expected is { } value && value != revision) {
            throw new RigException(
                412,
                $"The rig changed (revision {revision}, expected {value}). Reload and try again."
            );
        }
    }

    void CheckCell(int row, int column) {
        if (row < 0 || row >= preset.Rows || column < 0 || column >= preset.Columns) {
            throw new RigException(400, $"Cell ({row},{column}) is outside the {preset.Rows}x{preset.Columns} grid.");
        }
    }

    static void CheckChannel(string channel, int channels) {
        var index = Preset.ChannelNames.IndexOf(channel);
        if (index < 0 || index >= channels) {
            throw new RigException(
                400,
                $"Channel '{channel}' does not exist; this block has {string.Join(", ", Preset.ChannelNames[..channels])}."
            );
        }
    }

    static void CheckSceneMidi(List<SceneMidiMessage> messages) {
        if (messages.Count > SceneMidiMessage.MaxPerScene) {
            throw new RigException(400, $"A scene sends at most {SceneMidiMessage.MaxPerScene} MIDI messages.");
        }

        if (messages.Select(static m => m.Problem()).FirstOrDefault(static p => p is not null) is { } problem) {
            throw new RigException(400, problem);
        }
    }

    static void CheckScene(int scene) {
        if (scene is < 0 or >= Preset.SceneCount) {
            throw new RigException(400, $"Scenes are numbered 0 to {Preset.SceneCount - 1}.");
        }
    }

    static bool Adjacent(Preset p, PresetCable cable) {
        var from = p.Blocks.First(b => b.Id == cable.From);
        var to = p.Blocks.First(b => b.Id == cable.To);
        return to.Column == from.Column + 1;
    }

    async Task<LiveState> Locked(Func<Task<LiveState>> operation) {
        await gate.WaitAsync();
        try {
            return await operation();
        } finally {
            gate.Release();
        }
    }

    /// <summary>Raised (inside the rig's lock) for every change, like the WebSocket events: for in-process listeners.</summary>
    public event Action<RigEvent>? Changes;

    sealed record Snapshot(string Json, int ActiveScene);
}
