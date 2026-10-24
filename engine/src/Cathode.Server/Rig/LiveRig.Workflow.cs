using Cathode.Contracts;
using Cathode.Engine.Rig;
using Cathode.Ipc;
using Cathode.Server.Api;

namespace Cathode.Server.Rig;

/// <summary>
///     The editor's workflow operations (docs/02-architecture.md §7.5): revert, clear, snapshots and templates, block
///     replace, swap and drop, row and column moves, pasting settings, all-scene actions, whole-block Library recall,
///     performance pages and footswitch overrides. Each is one undo step.
/// </summary>
public sealed partial class LiveRig {
    /// <summary>The working copy's override for a footswitch, if any (unlocked read, for the MIDI router).</summary>
    public FootswitchOverride? FootswitchOverrideFor(string layout, int index) =>
        Volatile.Read(ref preset).FootswitchOverrides.ToArray()
            .FirstOrDefault(o => o.Layout == layout && o.Index == index);

    /// <summary>Discards the edits: the working copy goes back to the version in the library (undoable).</summary>
    public Task<LiveState> RevertAsync(long? expected) =>
        Locked(async () => {
                Check(expected);
                var saved = presets.Get(preset.Id)
                    ?? throw RigException.Conflict("This preset has not been saved yet: there is nothing to revert to.");
                ApplyGlobals(saved);
                await ReplaceUndoableAsync(saved);
                dirty = false;
                return Changed("presetReverted", markDirty: false);
            }
        );

    /// <summary>Empties the grid (Input to Output through shunts) and the name, keeping the preset's identity.</summary>
    public Task<LiveState> ClearAsync(long? expected) =>
        Locked(async () => {
                Check(expected);
                var empty = Preset.CreateDefault();
                empty.Name = string.Empty;
                empty.Tempo = preset.Tempo;
                await ReplaceUndoableAsync(empty);
                return Changed("presetCleared");
            }
        );

    /// <summary>Puts a snapshot's content into the working copy (undoable; the preset keeps its id).</summary>
    public Task<LiveState> LoadSnapshotAsync(PresetSnapshot snapshot, long? expected) =>
        Locked(async () => {
                Check(expected);
                CheckShape(snapshot.Preset);
                ApplyGlobals(snapshot.Preset);
                await ReplaceUndoableAsync(snapshot.Preset);
                return Changed("snapshotLoaded");
            }
        );

    /// <summary>Starts a new preset from a template, as New does from the default one.</summary>
    public Task<LiveState> NewFromTemplateAsync(Preset template) =>
        Locked(async () => {
                CheckShape(template);
                template.Id = Preset.NewId();
                ApplyGlobals(template);
                await ReplaceAsync(template);
                return Changed("presetLoaded", markDirty: false);
            }
        );

    /// <summary>Replaces a block with a new block of <paramref name="type" /> in its cell, on its cables.</summary>
    public Task<LiveState> ReplaceBlockAsync(string id, string type, long? expected) =>
        Locked(async () => {
                Check(expected);
                var block = Find(id);
                var info = catalog.Info(type) ?? throw new RigException(400, $"Unknown block type '{type}'.");
                if (block.Type is BuiltInTypes.Input or BuiltInTypes.Output
                    || type is BuiltInTypes.Input or BuiltInTypes.Output) {
                    throw new RigException(400, "The grid's Input and Output cannot be replaced.");
                }

                if (type == block.Type) {
                    throw new RigException(400, $"'{id}' is already a {info.Name}.");
                }

                if (preset.Blocks.Count(b => b.Type == type) >= info.MaxInstances) {
                    throw RigException.Conflict($"A preset can hold at most {info.MaxInstances} {info.Name} block(s).");
                }

                var replacement = type == BuiltInTypes.Shunt && preset.Blocks.All(b => b.Id != ShuntId(block))
                    ? ShuntId(block)
                    : NextId(type);
                await StructuralAsync(p => {
                        var old = p.Blocks.Single(b => b.Id == id);
                        p.Blocks.Remove(old);
                        p.Blocks.Add(new() { Id = replacement, Type = type, Row = old.Row, Column = old.Column });
                        Forget(p, id);
                        Repoint(p, id, replacement);
                    }
                );
                return Changed("blockReplaced", replacement);
            }
        );

    /// <summary>
    ///     Drops block <paramref name="id" /> on another: <c>swap</c> exchanges their cells and leaves the cables where
    ///     they are (each block takes the other's), <c>replace</c> removes the target and takes its cell and cables.
    /// </summary>
    public Task<LiveState> DropBlockAsync(string id, DropBlockRequest request, long? expected) =>
        Locked(async () => {
                Check(expected);
                var block = Find(id);
                var target = Find(request.Target);
                if (block.Id == target.Id) {
                    throw new RigException(400, "A block cannot be dropped on itself.");
                }

                switch (request.Mode) {
                    case "swap":
                        await StructuralAsync(p => {
                                var a = p.Blocks.Single(b => b.Id == id);
                                var b = p.Blocks.Single(x => x.Id == request.Target);
                                (a.Row, a.Column, b.Row, b.Column) = (b.Row, b.Column, a.Row, a.Column);
                                foreach (var cable in p.Cables) {
                                    cable.From = Swapped(cable.From, a.Id, b.Id);
                                    cable.To = Swapped(cable.To, a.Id, b.Id);
                                }
                            }
                        );
                        return Changed("blocksSwapped", id);
                    case "replace":
                        if (BuiltInTypes.IsBuiltIn(block.Type) && block.Type != BuiltInTypes.Shunt
                            || target.Type is BuiltInTypes.Input or BuiltInTypes.Output) {
                            throw new RigException(400, "The grid's Input and Output cannot be replaced or moved so.");
                        }

                        await StructuralAsync(p => {
                                var a = p.Blocks.Single(b => b.Id == id);
                                var b = p.Blocks.Single(x => x.Id == request.Target);
                                p.Blocks.Remove(b);
                                Forget(p, b.Id);
                                if (!LeaveShunt(p, a, a.Row, a.Column)) {
                                    p.Cables.RemoveAll(c => c.From == a.Id || c.To == a.Id);
                                }

                                (a.Row, a.Column) = (b.Row, b.Column);
                                Repoint(p, b.Id, a.Id);
                                p.Cables.RemoveAll(c => c.From == c.To || !Adjacent(p, c));
                            }
                        );
                        return Changed("blockReplaced", id);
                    default:
                        throw new RigException(400, $"Drop mode '{request.Mode}' is not swap or replace.");
                }
            }
        );

    /// <summary>
    ///     Moves a whole row or column to another index, shifting those between. Rows keep every cable; a column move
    ///     drops the cables it leaves joining non-adjacent columns.
    /// </summary>
    public Task<LiveState> MoveLineAsync(MoveLineRequest request, long? expected) =>
        Locked(async () => {
                Check(expected);
                var rows = request.Axis switch {
                    "row" => true,
                    "column" => false,
                    _ => throw new RigException(400, $"Axis '{request.Axis}' is not row or column.")
                };
                var count = rows ? preset.Rows : preset.Columns;
                if (request.From < 0 || request.From >= count || request.To < 0 || request.To >= count) {
                    throw new RigException(400, $"{request.Axis} {request.From} or {request.To} is outside the grid.");
                }

                await StructuralAsync(p => {
                        foreach (var block in p.Blocks) {
                            var at = rows ? block.Row : block.Column;
                            var moved = Shifted(at, request.From, request.To);
                            if (rows) {
                                block.Row = moved;
                            } else {
                                block.Column = moved;
                            }
                        }

                        p.Cables.RemoveAll(c => !Adjacent(p, c));
                    }
                );
                return Changed("lineMoved");
            }
        );

    /// <summary>Removes every cable into and out of a block.</summary>
    public Task<LiveState> DisconnectAsync(string id, long? expected) =>
        Locked(async () => {
                Check(expected);
                Find(id);
                await StructuralAsync(p => p.Cables.RemoveAll(c => c.From == id || c.To == id));
                return Changed("blockDisconnected", id);
            }
        );

    /// <summary>Gives a block another block's settings: every channel (replaced, not merged), and its assets.</summary>
    public Task<LiveState> PasteSettingsAsync(string id, BlockSettingsRequest request, long? expected) =>
        Locked(async () => {
                Check(expected);
                var block = Find(id);
                if (request.Type != block.Type) {
                    throw new RigException(400, $"Settings of a {request.Type} cannot go onto '{id}' ({block.Type}).");
                }

                var descriptor = catalog.Descriptor(block.Type)
                    ?? throw new RigException(400, $"'{id}' has no parameters.");
                var channels = new Dictionary<string, Dictionary<string, float>>();
                foreach (var (channel, values) in request.Channels) {
                    CheckChannel(channel, descriptor.Channels);
                    var unknown = values.Keys.Where(k => descriptor.IndexOf(k) < 0).ToList();
                    if (unknown.Count > 0) {
                        throw new RigException(
                            400,
                            $"{descriptor.Name} has no parameter(s) {string.Join(", ", unknown)}.",
                            unknown
                        );
                    }

                    channels[channel] = values.ToDictionary(
                        static v => v.Key,
                        v => descriptor.Params[descriptor.IndexOf(v.Key)].Sanitize(v.Value)
                    );
                }

                var slots = catalog.Info(block.Type)!.AssetSlots;
                var assigned = (request.Assets ?? [])
                    .Where(a => slots.Contains(a.Key)
                        && assets.Get(a.Value) is { } asset
                        && Stores.AssetStore.Fits(a.Key, asset.Kind)
                    )
                    .ToDictionary(static a => a.Key, static a => a.Value);
                await StructuralAsync(p => {
                        var target = p.Blocks.Single(b => b.Id == id);
                        target.Channels = channels;
                        if (request.Assets is not null) {
                            target.Assets = assigned;
                        }
                    }
                );
                ShareWithGlobal(block);
                return Changed("blockSettingsPasted", id);
            }
        );

    /// <summary>Sets a block's bypass and/or channel in every scene.</summary>
    public Task<LiveState> SetAllScenesAsync(string id, AllScenesRequest request, long? expected) =>
        Locked(async () => {
                Check(expected);
                var block = Find(id);
                if (request.Bypassed is not null && BuiltInTypes.IsBuiltIn(block.Type)) {
                    throw new RigException(400, $"'{id}' cannot be bypassed.");
                }

                if (request.Channel is { } channel) {
                    var descriptor = catalog.Descriptor(block.Type)
                        ?? throw new RigException(400, $"'{id}' has no channels.");
                    CheckChannel(channel, descriptor.Channels);
                }

                PushUndo(null);
                var before = preset.Scenes[activeScene].StateOf(id);
                foreach (var scene in preset.Scenes) {
                    var state = scene.StateOf(id);
                    scene.Blocks[id] = state with {
                        Bypassed = request.Bypassed ?? state.Bypassed,
                        Channel = request.Channel ?? state.Channel
                    };
                }

                var after = preset.Scenes[activeScene].StateOf(id);
                if (NeedsRecompile(block, before.Channel, after.Channel)) {
                    await SyncGraphAsync();
                } else {
                    if (before.Channel != after.Channel) {
                        engine.SetParams(generation, ChannelValues(block, after.Channel));
                    }

                    if (before.Bypassed != after.Bypassed) {
                        engine.SetBypass(generation, id, after.Bypassed);
                    }
                }

                return Changed("allScenesChanged", id, after.Channel);
            }
        );

    /// <summary>
    ///     Recalls a Block Library entry into every channel: a whole-block entry's own channels, modifiers and assets,
    ///     or a single-channel entry's values on each channel.
    /// </summary>
    public Task<LiveState> ApplyBlockPresetAllAsync(string id, BlockPreset source, long? expected) =>
        Locked(async () => {
                Check(expected);
                var block = Find(id);
                var descriptor = catalog.Descriptor(block.Type)
                    ?? throw new RigException(400, $"'{id}' has no parameters.");
                if (source.Type != block.Type) {
                    throw new RigException(400, $"'{source.Name}' is a {source.Type} preset; '{id}' is a {block.Type}.");
                }

                Dictionary<string, float> Clean(Dictionary<string, float> values) =>
                    values.Where(v => descriptor.IndexOf(v.Key) >= 0)
                        .ToDictionary(
                            static v => v.Key,
                            v => descriptor.Params[descriptor.IndexOf(v.Key)].Sanitize(v.Value)
                        );

                var channels = Preset.ChannelNames[..descriptor.Channels]
                    .ToDictionary(
                        static c => c,
                        c => Clean(source.Channels is { } stored ? stored.GetValueOrDefault(c) ?? [] : source.Values)
                    );
                var slots = catalog.Info(block.Type)!.AssetSlots;
                await StructuralAsync(p => {
                        var target = p.Blocks.Single(b => b.Id == id);
                        target.Channels = channels;
                        foreach (var (slot, reference) in source.Assets ?? []) {
                            if (slots.Contains(slot)
                                && assets.Get(reference) is { } asset
                                && Stores.AssetStore.Fits(slot, asset.Kind)) {
                                target.Assets[slot] = reference;
                            }
                        }

                        if (source.Modifiers is { } modifiers) {
                            p.Modifiers.RemoveAll(m => m.Block == id);
                            p.Modifiers.AddRange(
                                modifiers.Where(m => (descriptor.IndexOf(m.Param) >= 0 || m.Param == ModNames.EngageParam)
                                        && ModNames.TryParseSource(m.Source, out _)
                                        && ModNames.TryParseCurve(m.Curve, out _)
                                    )
                                    .Select(m => new PresetModifier {
                                            Block = id,
                                            Param = m.Param,
                                            Source = m.Source,
                                            Min = m.Min,
                                            Max = m.Max,
                                            Curve = m.Curve,
                                            AttackMs = m.AttackMs,
                                            ReleaseMs = m.ReleaseMs
                                        }
                                    )
                            );
                        }
                    }
                );
                ShareWithGlobal(block);
                return Changed("blockPresetApplied", id);
            }
        );

    /// <summary>The preset's performance page; every control must name a block of the preset and its parameter.</summary>
    public Task<LiveState> SetPerformAsync(IReadOnlyList<PerformControl> controls, long? expected) =>
        Locked(() => {
                Check(expected);
                var errors = controls
                    .Where(c => preset.Blocks.FirstOrDefault(b => b.Id == c.Block) is not { } block
                        || catalog.Descriptor(block.Type)?.IndexOf(c.Param) is null or < 0
                    )
                    .Select(static c => $"No parameter '{c.Param}' on block '{c.Block}'.")
                    .ToList();
                if (errors.Count > 0) {
                    throw new RigException(400, "Some performance controls are invalid.", errors);
                }

                PushUndo(null);
                preset.Perform = [..controls];
                return Task.FromResult(Changed("performChanged"));
            }
        );

    /// <summary>The preset's own footswitch actions, which replace the global layouts' while it is loaded.</summary>
    public Task<LiveState> SetFootswitchOverridesAsync(IReadOnlyList<FootswitchOverride> overrides, long? expected) =>
        Locked(() => {
                Check(expected);
                foreach (var o in overrides) {
                    GlobalValidation.CheckAction(o.Tap.Action);
                    if (o.Hold is { } hold) {
                        GlobalValidation.CheckAction(hold.Action);
                    }
                }

                if (overrides.GroupBy(static o => (o.Layout, o.Index)).Any(static g => g.Count() > 1)) {
                    throw new RigException(400, "A switch can have only one override per preset.");
                }

                PushUndo(null);
                preset.FootswitchOverrides = [..overrides];
                return Task.FromResult(Changed("footswitchOverridesChanged"));
            }
        );

    /// <summary>
    ///     Renames a stored preset or changes its colour tag or favourite flag, and the working copy's too when it is
    ///     that preset (not an edit: nothing to undo, and it does not make the working copy dirty).
    /// </summary>
    public Task<LiveState> SetPresetMetadataAsync(string id, PresetMetadataRequest change) =>
        Locked(() => {
                if (change.Name is { } name && string.IsNullOrWhiteSpace(name)) {
                    throw new RigException(400, "A preset needs a name.");
                }

                var stored = presets.Get(id);
                if (stored is null && preset.Id != id) {
                    throw RigException.NotFound($"No preset '{id}'.");
                }

                if (stored is not null) {
                    Apply(stored, change);
                    presets.Save(stored);
                }

                if (preset.Id == id) {
                    Apply(preset, change);
                }

                return Task.FromResult(Changed("presetMetadataChanged", markDirty: false));
            }
        );

    static void Apply(Preset target, PresetMetadataRequest change) {
        target.Name = change.Name?.Trim() ?? target.Name;
        target.Color = change.Color is { } color ? (color.Length == 0 ? null : color) : target.Color;
        target.Favorite = change.Favorite ?? target.Favorite;
    }

    /// <summary>A linked block's settings belong to its global block: keep that in step.</summary>
    void ShareWithGlobal(PresetBlock block) {
        if (block.Global is not { } link) {
            return;
        }

        var current = preset.Blocks.Single(b => b.Id == block.Id);
        globals.Update(g => {
                if (g.Blocks.FirstOrDefault(b => b.Id == link) is { } shared) {
                    shared.Channels = current.Channels.ToDictionary(
                        static c => c.Key,
                        static c => new Dictionary<string, float>(c.Value)
                    );
                    shared.Assets = new(current.Assets);
                }
            }
        );
    }

    /// <summary>Replaces the working copy's content with <paramref name="next" /> as one undoable edit, keeping its id.</summary>
    async Task ReplaceUndoableAsync(Preset next) {
        var before = Capture();
        next.Id = preset.Id;
        preset = next;
        try {
            await SyncGraphAsync();
        } catch (IpcException e) {
            Restore(before);
            await SyncGraphAsync();
            throw new RigException(422, "The engine could not load the result: " + e.Message, e.Errors);
        } catch (RigException) {
            Restore(before);
            throw;
        }

        coalesceKey = null;
        Remember(undo, before);
        redo.Clear();
    }

    static void CheckShape(Preset next) {
        if (next.SchemaVersion > Preset.CurrentSchemaVersion) {
            throw RigException.Conflict(
                $"Preset '{next.Name}' was saved by a newer version of Cathode (schema {next.SchemaVersion})."
            );
        }

        if (next.Scenes.Count != Preset.SceneCount) {
            throw new RigException(400, $"A preset has exactly {Preset.SceneCount} scenes.");
        }
    }

    /// <summary>Drops what the preset keeps about a block that is gone: its scene states and modifiers.</summary>
    static void Forget(Preset p, string id) {
        foreach (var scene in p.Scenes) {
            scene.Blocks.Remove(id);
        }

        p.Modifiers.RemoveAll(m => m.Block == id);
    }

    static string ShuntId(PresetBlock block) => $"shunt-r{block.Row}c{block.Column}";

    static string Swapped(string end, string a, string b) => end == a ? b : end == b ? a : end;

    /// <summary>Where index <paramref name="at" /> goes when <paramref name="from" /> moves to <paramref name="to" />.</summary>
    static int Shifted(int at, int from, int to) =>
        at == from ? to
        : from < to && at > from && at <= to ? at - 1
        : from > to && at >= to && at < from ? at + 1
        : at;
}
