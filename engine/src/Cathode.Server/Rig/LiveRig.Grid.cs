using Cathode.Contracts;

namespace Cathode.Server.Rig;

/// <summary>
///     The grid's edits: adding, removing and moving blocks, laying and removing cables, and the shunts and ids that
///     go with them. Each is a structural change: the engine graph is recompiled.
/// </summary>
public sealed partial class LiveRig {
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
}
