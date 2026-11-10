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
    readonly SemaphoreSlim gate = new(1, 1);
    readonly UndoHistory history = new(TimeProvider.System);
    Preset preset = Preset.CreateDefault();
    int activeScene;
    long revision;

    /// <summary>Counts the presets loaded into the working copy (<see cref="LiveState.Load" />).</summary>
    long load;

    bool dirty;

    /// <summary>
    ///     Counts the names the library has given the working copy: a save under a name, a rename in the Library.
    ///     Undo and redo do not take such a name back (<see cref="StepAsync" />).
    /// </summary>
    long naming;

    int generation;
    string compiledOversampling = "minimum";

    /// <summary>
    ///     Scene Ignore: blocks whose live bypass and channel differ from the active scene's, because their channel
    ///     ignored the last scene change.
    /// </summary>
    readonly ConcurrentDictionary<string, SceneBlockState> held = new(StringComparer.Ordinal);

    /// <summary>The working copy as last loaded or saved, for Scene Revert.</summary>
    string savedJson = JsonSerializer.Serialize(Preset.CreateDefault(), ContractsJsonContext.Default.Preset);

    /// <summary>
    ///     Controller settings a preset may store; the rest of the rig bank is scene, global or live state.
    /// </summary>
    static readonly HashSet<string> ControllerParams = [
        ..RigControls.Descriptor.Params.Select(static p => p.Id)
            .Where(static id => id.StartsWith("lfo", StringComparison.Ordinal)
                || id.StartsWith("adsr", StringComparison.Ordinal)
                || id.StartsWith("env", StringComparison.Ordinal)
                || id.StartsWith("seq", StringComparison.Ordinal)
                || id is "pitchLow" or "pitchHigh"
            )
    ];

    /// <summary>What a save or a rename that would leave a preset without a name is refused with.</summary>
    const string NeedsName = "A preset needs a name.";

    /// <summary>The longest gesture id a parameter change may carry (<see cref="SetParamsAsync" />).</summary>
    public const int MaxGestureLength = 64;

    /// <summary>The active scene, for listeners that must not wait for the lock (footswitch LEDs).</summary>
    public int ActiveScene => Volatile.Read(ref activeScene);

    public void Dispose() => gate.Dispose();

    /// <summary>
    ///     Every block's live state as of the last change, for readers that must not wait for the lock (footswitch
    ///     LEDs, the MIDI bypass toggle). Never changed once published: each change swaps in a new one
    ///     (<see cref="PublishLive" />). The scenes' own dictionaries are written under the lock and are not safe to
    ///     read outside it.
    /// </summary>
    Dictionary<string, SceneBlockState> published = new(StringComparer.Ordinal);

    /// <summary>
    ///     Whether a block is bypassed now (unlocked read for LED feedback): as of the last change, which a listener
    ///     to <see cref="Changes" /> already sees.
    /// </summary>
    public bool IsBypassed(string block) =>
        Volatile.Read(ref published).TryGetValue(block, out var state) && state.Bypassed;

    /// <summary>
    ///     What a block is running now: its held state (Scene Ignore) or the active scene's. Inside the lock only.
    /// </summary>
    SceneBlockState Live(string block) =>
        held.TryGetValue(block, out var state) ? state : preset.Scenes[activeScene].StateOf(block);

    /// <summary>Publishes the live states for the unlocked readers (<see cref="IsBypassed" />).</summary>
    void PublishLive() => Volatile.Write(ref published, LiveStates());

    public Task<LiveState> StateAsync() => Locked(() => Task.FromResult(State()));

    /// <summary>
    ///     Sends the whole rig to the engine again (after the host (re)connects): the graph and its parameters, mute,
    ///     the tuner and whether to follow or send MIDI clock.
    /// </summary>
    public Task ResyncAsync() =>
        Locked(async () => {
                await SyncGraphAsync();
                SyncMidi(globals.Get());
                engine.SetTuner(TunerOn);
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
    ///     default on the global Default Scene: the scene it was saved on, or a fixed one. <paramref name="from" /> is
    ///     the bank slot or setlist entry it came from, if any, for preset next/previous
    ///     (<see cref="PresetNavigator" />).
    /// </summary>
    public Task<LiveState> LoadAsync(string id, int? scene = null, PresetPosition? from = null) =>
        Locked(async () => {
                var loaded = presets.Get(id) ?? throw RigException.NotFound($"No preset '{id}'.");
                CheckShape(loaded); // a file stored before the store checked, or edited by hand
                var start = scene
                    ?? (globals.Get().DefaultScene is var fixedScene and >= 0 and < Preset.SceneCount
                        ? fixedScene
                        : Math.Clamp(loaded.SavedScene, 0, Preset.SceneCount - 1));
                CheckScene(start);
                ApplyGlobals(loaded);
                await ReplaceAsync(loaded, start);
                Remember();
                position = from is null ? null : (from, loaded.Id);
                // Just loaded, so it matches what is saved.
                return Changed("presetLoaded", scene: start, markDirty: false);
            }
        );

    /// <summary>
    ///     Stores the working copy, under the request's name if it has one, as a new preset if it asks. A working copy
    ///     without a name (after Clear) is refused unless the request names it: the library holds no nameless preset.
    /// </summary>
    public Task<LiveState> SaveAsync(SaveRequest request, long? expected) =>
        Locked(() => {
                Check(expected);
                if (string.IsNullOrWhiteSpace(request.Name) && string.IsNullOrWhiteSpace(preset.Name)) {
                    throw new RigException(400, NeedsName);
                }

                if (request.AsNew) {
                    preset.Id = Preset.NewId();
                }

                if (!string.IsNullOrWhiteSpace(request.Name)) {
                    preset.Name = request.Name.Trim();
                    naming++; // the library's name now: no undo brings the old one back
                }

                return Task.FromResult(SaveWorkingCopy());
            }
        );

    public Task<LiveState> UndoAsync() => Locked(() => StepAsync(history.Undo, "undo"));

    public Task<LiveState> RedoAsync() => Locked(() => StepAsync(history.Redo, "redo"));

    public Task<LiveState> AddBlockAsync(AddBlockRequest request, long? expected) =>
        Locked(async () => {
                Check(expected);
                var info = catalog.Info(request.Type)
                    ?? throw new RigException(400, $"Unknown block type '{request.Type}'.");
                CheckCell(request.Row, request.Column);
                if (preset.Blocks.Count(b => b.Type == request.Type) >= info.MaxInstances) {
                    throw RigException.Conflict($"A preset can hold at most {info.MaxInstances} {info.Name} block(s).");
                }

                if (request.Id is { } chosen) {
                    CheckBlockId(chosen);
                }

                var id = request.Id ?? NextId(request.Type);
                if (preset.Blocks.Exists(b => b.Id == id)) {
                    throw RigException.Conflict($"Block id '{id}' is taken.");
                }

                var added = new PresetBlock {
                    Id = id,
                    Type = request.Type,
                    Row = request.Row,
                    Column = request.Column,
                    Channels = request.Channels is { } channels ? NewBlockChannels(request.Type, channels) : [],
                    Assets = request.Assets is { } wanted ? FittingAssets(request.Type, wanted) : []
                };

                // A new Input takes the first hardware input no other Input reads.
                var source = request.Type == BuiltInTypes.Input ? FreeSource(preset) : 0;

                await StructuralAsync(p => PlaceNewBlock(p, added, source, info.Channels));
                return Changed("blockAdded", id);
            }
        );

    /// <summary>
    ///     Settings for a new block, sanitized; an Input's source is left out, since it reads the free input it is
    ///     given rather than the one its original reads.
    /// </summary>
    Dictionary<string, Dictionary<string, float>> NewBlockChannels(
        string type,
        Dictionary<string, Dictionary<string, float>> requested
    ) {
        if (requested.Count == 0) {
            return [];
        }

        var descriptor = catalog.Descriptor(type) ?? throw new RigException(400, $"A {type} has no parameters.");
        var channels = SanitizeChannels(descriptor, requested);
        if (type == BuiltInTypes.Input) {
            foreach (var values in channels.Values) {
                values.Remove(SourceParam);
            }
        }

        return channels;
    }

    /// <summary>
    ///     Adds a new block in its cell, reading hardware input <paramref name="source" /> when above 0. Dropping a
    ///     block onto a shunt replaces it and keeps its cables, as on the Axe-Fx.
    /// </summary>
    static void PlaceNewBlock(Preset p, PresetBlock added, int source, int channels) {
        var occupant = p.Blocks.Find(b => b.Row == added.Row && b.Column == added.Column);
        if (occupant is not null && occupant.Type != BuiltInTypes.Shunt) {
            throw RigException.Conflict($"Cell ({added.Row},{added.Column}) holds '{occupant.Id}'.");
        }

        if (occupant is not null) {
            p.Blocks.Remove(occupant);
            Repoint(p, occupant.Id, added.Id);
        }

        if (source > 0) {
            SetSource(added, source, channels);
        }

        p.Blocks.Add(added);
    }

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
                        // reverse of dropping a block onto a shunt. A shunt, or a block at the end of a branch, just
                        // goes.
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
                var occupant = preset.Blocks.Find(b => b.Id != id
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
                        var shunt = p.Blocks.Find(b => b.Id != id
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

                var row = CableRow(from, to);
                if (row is null) {
                    throw new RigException(400, "A block is in the way: cables cross only empty cells and shunts.");
                }

                if (BuiltInTypes.IsOutput(from.Type) || to.Type == BuiltInTypes.Input) {
                    throw new RigException(
                        400,
                        "Cables run from an output side to an input side: nothing leaves an Output block or "
                        + "enters an Input block."
                    );
                }

                if (preset.Cables.Exists(c => c.From == request.From && c.To == request.To)) {
                    throw RigException.Conflict("That cable already exists.");
                }

                await StructuralAsync(p => LayCable(p, request, from.Column, to.Column, row.Value));
                return Changed("cableAdded", request.From);
            }
        );

    /// <summary>
    ///     The row a cable from <paramref name="from" /> to <paramref name="to" /> can take: across empty cells,
    ///     shunts carry it on the source's row (or else the target's); null when blocks are in the way on both.
    /// </summary>
    int? CableRow(PresetBlock from, PresetBlock to) =>
        new[] { from.Row, to.Row }.Cast<int?>()
            .FirstOrDefault(r => Enumerable.Range(from.Column + 1, to.Column - from.Column - 1)
                .All(c => BlockAt(preset, r!.Value, c) is null or { Type: BuiltInTypes.Shunt })
            );

    /// <summary>Cables the request's blocks on <paramref name="row" />, through shunts (added where missing).</summary>
    void LayCable(Preset p, CableRequest request, int fromColumn, int toColumn, int row) {
        var previous = request.From;
        for (var column = fromColumn + 1; column < toColumn; column++) {
            var shunt = BlockAt(p, row, column);
            if (shunt is null) {
                var id = $"shunt-r{row}c{column}";
                shunt = new() {
                    Id = HasBlock(p, id) ? NextId(BuiltInTypes.Shunt) : id,
                    Type = BuiltInTypes.Shunt,
                    Row = row,
                    Column = column
                };
                p.Blocks.Add(shunt);
            }

            Connect(p, previous, shunt.Id);
            previous = shunt.Id;
        }

        Connect(p, previous, request.To);
    }

    public Task<LiveState> RemoveCableAsync(CableRequest request, long? expected) =>
        Locked(async () => {
                Check(expected);
                if (!preset.Cables.Exists(c => c.From == request.From && c.To == request.To)) {
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

                // A linked block's assets belong to its global block: the preset's other blocks linked to it take the
                // asset in the same rebuild, and the global block once the engine has accepted it.
                await StructuralAsync(p => {
                        var edited = p.Blocks.Single(b => b.Id == id);
                        edited.Assets[slot] = request.Asset;
                        MirrorLinked(edited);
                    }
                );
                if (block.Global is { } link) {
                    globals.Update(g => {
                            if (g.Blocks.Find(b => b.Id == link) is { } shared) {
                                shared.Assets[slot] = request.Asset;
                            }
                        }
                    );
                }

                return Changed("assetChanged", id);
            }
        );

    /// <summary>
    ///     Sets parameters on <paramref name="channel" /> (default: the channel the active scene uses). With
    ///     <paramref name="loaded" />, the values were set on that load of a preset (<see cref="LiveState.Load" />) and
    ///     are refused (412) once another preset has replaced it: a knob's last PATCH still on the wire when a program
    ///     change loads another preset must not land on the new preset's block with the same id. With
    ///     <paramref name="gesture" /> (an id the client makes for one drag of a knob, from the first move to the
    ///     release), the values of one gesture are one undo step however long it lasts, and the next gesture is a new
    ///     step even a moment later; without, a run of changes to the same parameters is one step until a pause.
    /// </summary>
    public Task<LiveState> SetParamsAsync(
        string id,
        string? channel,
        IReadOnlyDictionary<string, float> values,
        long? expected,
        long? loaded = null,
        string? gesture = null
    ) =>
        Locked(async () => {
                Check(expected);
                CheckLoad(loaded);
                var block = Find(id);
                var descriptor = catalog.Descriptor(block.Type)
                    ?? throw new RigException(400, $"'{id}' has no parameters.");
                var state = Live(id);
                var target = channel ?? state.Channel;
                CheckChannel(target, descriptor.Channels);

                CheckParamNames(descriptor, values);
                var source = RequestedSource(block, descriptor, values);
                if (source is { } wanted) {
                    CheckSource(block, wanted);
                }

                // In a gesture the parameters set may vary (an XY pad); without one, each set of them is its own run.
                PushUndo(
                    gesture is null
                        ? $"params:{id}:{target}:{string.Join(',', values.Keys.Order())}"
                        : $"params:{id}:{target}",
                    gesture
                );

                var stored = block.Channel(target);
                var sent = new List<ParamValue>();
                var typeChanged = ChangesType(descriptor, values, stored);
                var recompile = StoreParams(id, descriptor, values, stored, sent);
                if (source is { } chosen) {
                    SetSource(block, chosen, descriptor.Channels);
                }

                // A new model starts from its own defaults, not the old one's settings (values sent with it still win).
                if (typeChanged) {
                    recompile |= ResetForNewType(id, descriptor, values, stored, sent);
                }

                if (block.Global is { } link) {
                    ShareWithGlobal(link, target, stored);
                }

                // The preset's other blocks linked to the same global block follow.
                var linked = MirrorLinked(block);
                var running = linked.Where(b => Live(b.Id).Channel == target).ToList();
                await SendParamsAsync(recompile, target == state.Channel, running, sent);
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

    /// <summary>
    ///     Turns the block's channel-scoped modifiers on for <paramref name="channel" /> and off for the rest.
    /// </summary>
    void SyncModifiers(PresetBlock block, string channel) {
        var descriptor = catalog.Descriptor(block.Type);
        foreach (var modifier in preset.Modifiers.Where(m => m.Block == block.Id && m.Channel is not null)) {
            var index = descriptor?.IndexOf(modifier.Param) ?? -1;
            var value = index < 0 ? 0f : descriptor!.ValueOf(index, block.Channels.GetValueOrDefault(channel));
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

    /// <summary>
    ///     Updates a scene. Level and controller changes coalesce as knob changes do, by <paramref name="gesture" />
    ///     when the client names one (see <see cref="SetParamsAsync" />).
    /// </summary>
    public Task<LiveState> UpdateSceneAsync(
        int scene,
        SceneUpdateRequest request,
        long? expected,
        string? gesture = null
    ) =>
        Locked(() => {
                Check(expected);
                CheckScene(scene);
                if (request.Midi is { } messages) {
                    CheckSceneMidi(messages);
                }

                PushUndo(SceneUndoKey(scene, request), gesture);
                var target = preset.Scenes[scene];
                if (request.Name is { } name) {
                    target.Name = name.Trim();
                }

                if (request.Level is { } level) {
                    target.Level = ClampSceneLevel(level);
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
    ///     The undo key of a scene edit: a dragged Scene Level or scene-controller knob continues one undo step, like a
    ///     block knob; a rename or a MIDI change is a step of its own.
    /// </summary>
    static string? SceneUndoKey(int scene, SceneUpdateRequest request) =>
        request is { Name: null, Midi: null } && (request.Level is not null || request.Controllers is not null)
            ? $"scene:{scene}:{(request.Level is null ? "" : "level")},"
            + $"{(request.Controllers is null ? "" : "controllers")}"
            : null;

    /// <summary>
    ///     Everything the global settings put into the engine: tempo (global mode), metronome, Manual A-E, the outputs'
    ///     modes, Boost/Pad and EQs, Input 1's gain and calibration, and the tuner's output and reference.
    /// </summary>
    public static List<ParamValue> GlobalValues(GlobalSettings global) {
        var values = new List<ParamValue> {
            new(IpcBlocks.Rig, "metronome", global.Metronome ? 1 : 0),
            new(IpcBlocks.Rig, "metroLevel", global.MetronomeLevel),
            new(IpcBlocks.Rig, "effectsMix", global.EffectsMix),
            new(IpcBlocks.Rig, "gateOffset", global.NoiseGateOffset),
            new(IpcBlocks.Rig, "cabsOff", global.CabModelingOff ? 1 : 0),
            new(IpcBlocks.Rig, "spillover", global.PresetSpillover ? 1 : 0),
            new(IpcBlocks.Rig, "tunerOut", Math.Max(Api.GlobalValidation.TunerOutputs.IndexOf(global.TunerOutput), 0)),
            new(IpcBlocks.Rig, "tunerRef", (float)global.TunerReference)
        };
        if (global.TempoMode == "global") {
            values.Add(new(IpcBlocks.Rig, "tempo", (float)global.Tempo));
        }

        values.AddRange(
            global.Manual.Take(RigControls.ManualCount).Select(static (v, i) => new ParamValue(IpcBlocks.Rig, $"manual{(char)('A' + i)}", v))
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

        // Inputs 1-4; one the settings do not list is at its defaults (1 V full scale, no trim).
        for (var k = 0; k < RigControls.CalibratedInputs; k++) {
            var input = k < global.Inputs.Count ? global.Inputs[k] : new();
            var calibration = (float)(20 * Math.Log10(Math.Max(input.FullScaleVolts, 1e-3f)));
            values.Add(new(IpcBlocks.Rig, $"in{k + 1}Gain", input.Gain));
            values.Add(new(IpcBlocks.Rig, $"in{k + 1}Cal", calibration));
        }

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
        Enumerable.Range(0, descriptor.Params.Count)
            .Any(i => descriptor.Params[i].Recompile && descriptor.ValueOf(i, a) != descriptor.ValueOf(i, b));

    /// <summary>
    ///     Every parameter of the block type with <paramref name="channel" />'s value or the default on its type.
    /// </summary>
    List<ParamValue> ChannelValues(PresetBlock block, string channel) {
        var descriptor = catalog.Descriptor(block.Type)!;
        var stored = block.Channels.GetValueOrDefault(channel);
        return [..descriptor.Params.Select((p, i) => new ParamValue(block.Id, p.Id, descriptor.ValueOf(i, stored)))];
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
        } catch (Exception) {
            // A RigException, or anything else the edit's graph threw: the working copy goes back either way.
            Restore(before);
            throw;
        }

        history.Push(before);
    }

    async Task ReplaceAsync(Preset next, int scene = 0) {
        var before = Capture();
        preset = next;
        activeScene = scene;
        held.Clear();
        try {
            await SyncGraphAsync();
        } catch (Exception e) {
            // Whatever failed, the rig stays on the preset it had rather than a working copy it cannot play.
            Restore(before);
            await SyncGraphAsync();
            throw e is IpcException ipc
                ? new RigException(422, "The engine could not load this preset: " + ipc.Message, ipc.Errors)
                : e;
        }

        history.Clear();
        dirty = false;
        load++;
    }

    /// <summary>
    ///     Undo or redo: the working copy's content goes back to the step's. Which preset of the library it is does
    ///     not: the id (Save As New, or the copy a save of a factory preset made), the factory flag, the colour tag
    ///     and the favourite flag stay as they are now, and so does a name the library gave it after the step was
    ///     recorded. Otherwise an undo after Save As New would turn the working copy back into the original preset,
    ///     and the next save would overwrite that one.
    /// </summary>
    async Task<LiveState> StepAsync(Func<Func<UndoSnapshot>, UndoSnapshot?> step, string eventType) {
        if (step(Capture) is not { } target) {
            throw RigException.Conflict($"Nothing to {eventType}.");
        }

        var current = preset;
        Restore(target);
        preset.Id = current.Id;
        preset.Factory = current.Factory;
        preset.Color = current.Color;
        preset.Favorite = current.Favorite;
        if (target.Naming != naming) {
            preset.Name = current.Name;
        }

        await SyncGraphAsync();
        return Changed(eventType);
    }

    /// <summary>
    ///     What an offline render of the working copy takes (<see cref="Reamp" />): the graph the engine is given and
    ///     the rig and global values sent after it, as <see cref="SyncGraphAsync" /> sends them, but with the rig
    ///     unmuted and the metronome off: a render is the preset's sound, whatever the rig is doing at the moment.
    /// </summary>
    public Task<(GraphDefinition Graph, List<ParamValue> Values)> RenderStateAsync() =>
        Locked(() => {
                var global = globals.Get();
                return Task.FromResult<(GraphDefinition, List<ParamValue>)>(
                    (
                        PresetGraph.Build(preset, activeScene, assets, held, global.Oversampling),
                        [
                            ..RigValues(),
                            ..GlobalValues(global),
                            new(IpcBlocks.Rig, "mute", 0),
                            new(IpcBlocks.Rig, "metronome", 0)
                        ]
                    )
                );
            }
        );

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

    /// <summary>
    ///     The rig bank as this preset and scene set it: controllers, tempo, scene level and controllers.
    /// </summary>
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
            if (library.Find(g => g.Id == block.Global && g.Type == block.Type) is { } shared) {
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
        PublishLive(); // listeners read the live states from their handlers (footswitch LEDs)
        hub.Publish(change, ContractsJsonContext.Default.RigEvent);
        Changes?.Invoke(change);
    }

    /// <summary>
    ///     Records an undo point unless this edit continues the previous one (e.g. a knob being dragged); the preset
    ///     is serialised only when a point is recorded. A <paramref name="key" /> edit (one that may coalesce) made
    ///     with a <paramref name="gesture" /> continues exactly the edits of that gesture, whatever the time between
    ///     them (<see cref="UndoHistory" />); the gesture is checked before anything changes.
    /// </summary>
    void PushUndo(string? key, string? gesture = null) {
        CheckGesture(gesture);
        if (key is not null && gesture is not null) {
            history.Record($"{key}|gesture:{gesture}", Capture, gesture: true);
        } else {
            history.Record(key, Capture);
        }
    }

    UndoSnapshot Capture() =>
        new(JsonSerializer.SerializeToUtf8Bytes(preset, ContractsJsonContext.Default.Preset), activeScene, naming);

    void Restore(UndoSnapshot snapshot) {
        preset = JsonSerializer.Deserialize(snapshot.Json, ContractsJsonContext.Default.Preset)!;
        activeScene = snapshot.ActiveScene;
        foreach (var id in held.Keys.Where(id => preset.Blocks.TrueForAll(b => b.Id != id))) {
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
            JsonSerializer.SerializeToUtf8Bytes(preset, ContractsJsonContext.Default.Preset),
            ContractsJsonContext.Default.Preset
        )!;
        return new(
            copy,
            activeScene,
            revision,
            dirty,
            history.CanUndo,
            history.CanRedo,
            held.IsEmpty ? null : new(held, StringComparer.Ordinal),
            Muted,
            load,
            position is (var at, var id) && id == preset.Id ? at : null
        );
    }

    PresetBlock Find(string id) =>
        preset.Blocks.Find(b => b.Id == id) ?? throw RigException.NotFound($"No block '{id}'.");

    /// <summary>
    ///     Puts a shunt in a block's cell and hands it the block's cables, when the block sits in a signal path (cables
    ///     both in and out); a shunt or a branch end leaves nothing. Returns whether a shunt was left.
    /// </summary>
    bool LeaveShunt(Preset p, PresetBlock block, int row, int column) {
        var inPath = block.Type != BuiltInTypes.Shunt
            && p.Cables.Exists(c => c.To == block.Id)
            && p.Cables.Exists(c => c.From == block.Id);
        if (!inPath) {
            return false;
        }

        var shunt = $"shunt-r{row}c{column}";
        if (p.Blocks.Exists(b => b.Id == shunt)) {
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
            if (preset.Blocks.TrueForAll(b => b.Id != id)) {
                return id;
            }
        }
    }

    static bool HasBlock(Preset p, string id) => p.Blocks.Exists(b => b.Id == id);

    static bool Adjacent(Preset p, PresetCable cable) {
        var from = p.Blocks.First(b => b.Id == cable.From);
        var to = p.Blocks.First(b => b.Id == cable.To);
        return to.Column == from.Column + 1;
    }

    async Task<T> Locked<T>(Func<Task<T>> operation) {
        // Not cancellable: every rig operation is queued for the lock and runs to completion once it has it.
        await gate.WaitAsync(CancellationToken.None);
        try {
            return await operation();
        } finally {
            try {
                PublishLive(); // also after an operation that raised no event, or failed and rolled back
            } finally {
                gate.Release();
            }
        }
    }

    /// <summary>
    ///     Raised (inside the rig's lock) for every change, like the WebSocket events: for in-process listeners.
    /// </summary>
    public event Action<RigEvent>? Changes;
}
