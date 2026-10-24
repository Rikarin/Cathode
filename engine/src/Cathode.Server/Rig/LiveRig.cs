using System.Text.Json;
using Cathode.Contracts;
using Cathode.Dsp.Blocks;
using Cathode.Engine.Rig;
using Cathode.Ipc;
using Cathode.Server.Engine;
using Cathode.Server.Realtime;
using Cathode.Server.Stores;

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
    GlobalStore globals) : IDisposable {
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

    /// <summary>Controller settings a preset may store; the rest of the rig bank is scene, global or live state.</summary>
    static readonly HashSet<string> ControllerParams = [
        ..RigControls.Descriptor.Params.Select(static p => p.Id)
            .Where(static id => id.StartsWith("lfo", StringComparison.Ordinal)
                || id.StartsWith("adsr", StringComparison.Ordinal)
                || id.StartsWith("env", StringComparison.Ordinal)
                || id.StartsWith("seq", StringComparison.Ordinal)
                || id is "pitchLow" or "pitchHigh")
    ];

    /// <summary>Raised (inside the rig's lock) for every change, like the WebSocket events: for in-process listeners.</summary>
    public event Action<RigEvent>? Changes;

    public void Dispose() => gate.Dispose();

    /// <summary>The active scene, for listeners that must not wait for the lock (footswitch LEDs).</summary>
    public int ActiveScene => Volatile.Read(ref activeScene);

    /// <summary>Whether a block is bypassed in the active scene (unlocked read for LED feedback).</summary>
    public bool IsBypassed(string block) => preset.Scenes[ActiveScene].StateOf(block).Bypassed;

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
                // A fresh preset has nothing to lose: it is not an unsaved change.
                return Changed("presetLoaded", markDirty: false);
            }
        );

    /// <summary>Loads a preset from the library and starts on <paramref name="scene" /> (a setlist entry's scene).</summary>
    public Task<LiveState> LoadAsync(string id, int scene = 0) =>
        Locked(async () => {
                var loaded = presets.Get(id) ?? throw RigException.NotFound($"No preset '{id}'.");
                if (loaded.SchemaVersion > Preset.CurrentSchemaVersion) {
                    throw RigException.Conflict(
                        $"Preset '{loaded.Name}' was saved by a newer version of Cathode (schema {loaded.SchemaVersion})."
                    );
                }

                CheckScene(scene);
                ApplyGlobals(loaded);
                await ReplaceAsync(loaded, scene);
                // Just loaded, so it matches what is saved.
                return Changed("presetLoaded", scene: scene, markDirty: false);
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

                presets.Save(preset);
                dirty = false;
                return Task.FromResult(Changed("presetSaved", markDirty: false));
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

                        p.Blocks.Add(
                            new() { Id = id, Type = request.Type, Row = request.Row, Column = request.Column }
                        );
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
                var occupant = preset.Blocks.FirstOrDefault(
                    b => b.Id != id && b.Row == request.Row && b.Column == request.Column
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
                        var shunt = p.Blocks.FirstOrDefault(
                            b => b.Id != id && b.Row == request.Row && b.Column == request.Column
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
                if (to.Column != from.Column + 1) {
                    throw new RigException(400, "Cables connect a block to one in the next column.");
                }

                if (from.Type == BuiltInTypes.Output || to.Type == BuiltInTypes.Input) {
                    throw new RigException(
                        400,
                        "Cables run from an output side to an input side: nothing leaves an Output block or enters an Input block."
                    );
                }

                if (preset.Cables.Any(c => c.From == request.From && c.To == request.To)) {
                    throw RigException.Conflict("That cable already exists.");
                }

                await StructuralAsync(p => p.Cables.Add(new() { From = request.From, To = request.To }));
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
        Locked(() => {
                Check(expected);
                var block = Find(id);
                var descriptor = catalog.Descriptor(block.Type)
                    ?? throw new RigException(400, $"'{id}' has no parameters.");
                var state = preset.Scenes[activeScene].StateOf(id);
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

                PushUndo($"params:{id}:{target}:{string.Join(',', values.Keys.Order())}");
                var stored = block.Channel(target);
                var sent = new List<ParamValue>();
                foreach (var (param, raw) in values) {
                    var value = descriptor.Params[descriptor.IndexOf(param)].Sanitize(raw);
                    stored[param] = value;
                    sent.Add(new(id, param, value));
                }

                if (target == state.Channel) {
                    engine.SetParams(generation, sent);
                }

                if (block.Global is { } link) {
                    globals.Update(g => {
                            if (g.Blocks.FirstOrDefault(b => b.Id == link) is { } shared) {
                                shared.Channels[target] = new(stored);
                            }
                        }
                    );
                }

                revision++;
                dirty = true;
                foreach (var value in sent) {
                    Publish(new("paramChanged", revision, id, target, value.Param, value.Value));
                }

                return Task.FromResult(State());
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
                var scene = preset.Scenes[activeScene];
                scene.Blocks[id] = scene.StateOf(id) with { Bypassed = bypassed };
                engine.SetBypass(generation, id, bypassed);
                return Task.FromResult(Changed("bypassChanged", id));
            }
        );

    public Task<LiveState> SetChannelAsync(string id, string channel, long? expected) =>
        Locked(() => {
                Check(expected);
                var block = Find(id);
                var descriptor = catalog.Descriptor(block.Type)
                    ?? throw new RigException(400, $"'{id}' has no channels.");
                CheckChannel(channel, descriptor.Channels);

                PushUndo(null);
                var scene = preset.Scenes[activeScene];
                scene.Blocks[id] = scene.StateOf(id) with { Channel = channel };
                engine.SetParams(generation, ChannelValues(block, channel));
                return Task.FromResult(Changed("channelChanged", id, channel));
            }
        );

    /// <summary>Switches scene: only the blocks whose bypass or channel differ are touched; nothing recompiles.</summary>
    public Task<LiveState> SelectSceneAsync(int scene) =>
        Locked(() => {
                CheckScene(scene);
                var from = preset.Scenes[activeScene];
                var to = preset.Scenes[scene];
                foreach (var block in preset.Blocks.Where(static b => !BuiltInTypes.IsBuiltIn(b.Type))) {
                    var before = from.StateOf(block.Id);
                    var after = to.StateOf(block.Id);
                    if (before.Channel != after.Channel) {
                        engine.SetParams(generation, ChannelValues(block, after.Channel));
                    }

                    if (before.Bypassed != after.Bypassed) {
                        engine.SetBypass(generation, block.Id, after.Bypassed);
                    }
                }

                activeScene = scene;
                engine.SetParams(generation, SceneValues());
                revision++;
                Publish(new("sceneChanged", revision, Scene: scene));
                return Task.FromResult(State());
            }
        );

    public Task<LiveState> UpdateSceneAsync(int scene, SceneUpdateRequest request, long? expected) =>
        Locked(() => {
                Check(expected);
                CheckScene(scene);
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

                if (scene == activeScene) {
                    engine.SetParams(generation, SceneValues());
                }

                return Task.FromResult(Changed("sceneUpdated", scene: scene));
            }
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
        await engine.LoadGraphAsync(PresetGraph.Build(preset, activeScene, assets), generation);
        engine.SetParams(generation, [..RigValues(), ..GlobalValues(globals.Get())]);
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

    /// <summary>Everything the global settings put into the engine: tempo (global mode), metronome, Manual A-D, EQ.</summary>
    public static List<ParamValue> GlobalValues(GlobalSettings global) {
        var values = new List<ParamValue> {
            new(IpcBlocks.Rig, "metronome", global.Metronome ? 1 : 0),
            new(IpcBlocks.Rig, "metroLevel", global.MetronomeLevel)
        };
        if (global.TempoMode == "global") {
            values.Add(new(IpcBlocks.Rig, "tempo", (float)global.Tempo));
        }

        values.AddRange(global.Manual.Take(4).Select(static (v, i) => new ParamValue(IpcBlocks.Rig, $"manual{(char)('A' + i)}", v)));
        foreach (var param in GraphicEqBlock.Info.Params) {
            values.Add(new(IpcBlocks.Global, param.Id, global.Eq.GetValueOrDefault(param.Id, param.Default)));
        }

        return values;
    }

    /// <summary>Linked blocks take their settings from the global library (when it still has them).</summary>
    void ApplyGlobals(Preset target) {
        var library = globals.Get().Blocks;
        foreach (var block in target.Blocks.Where(static b => b.Global is not null)) {
            if (library.FirstOrDefault(g => g.Id == block.Global && g.Type == block.Type) is { } shared) {
                block.Channels = shared.Channels.ToDictionary(static c => c.Key, static c => new Dictionary<string, float>(c.Value));
                block.Assets = new(shared.Assets);
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
        return new(copy, activeScene, revision, dirty, undo.Count > 0, redo.Count > 0);
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
        var index = Array.IndexOf(Preset.ChannelNames, channel);
        if (index < 0 || index >= channels) {
            throw new RigException(
                400,
                $"Channel '{channel}' does not exist; this block has {string.Join(", ", Preset.ChannelNames[..channels])}."
            );
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

    sealed record Snapshot(string Json, int ActiveScene);
}
