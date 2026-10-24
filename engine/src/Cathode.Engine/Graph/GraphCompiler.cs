using Cathode.Dsp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Routing;
using Cathode.Engine.Rig;

namespace Cathode.Engine.Graph;

/// <summary>
///     Turns a <see cref="GraphDefinition" /> into a <see cref="CompiledGraph" />: validates the grid, prunes nodes
///     that cannot reach both an input and an output, orders the rest, compensates latency between parallel
///     paths and assigns work buffers by lifetime so they are reused.
/// </summary>
/// <param name="registry">Block types available to graphs.</param>
/// <param name="assetRoot">Folder that relative asset paths resolve against (default: the current directory).</param>
public sealed class GraphCompiler(BlockRegistry registry, string? assetRoot = null) {
    /// <summary>The audio interface's effects-loop channels, handed to FX Loop blocks (the engine's own).</summary>
    public HardwareLoop? HardwareLoop { get; init; }

    public CompiledGraph Compile(GraphDefinition definition, ProcessSpec spec, int generation = 0) {
        var errors = new List<string>();
        var nodes = definition.Nodes;
        var indexById = IndexNodes(definition, errors);
        var (inputs, consumers) = ResolveCables(definition, indexById, errors);
        if (errors.Count > 0) {
            throw new GraphValidationException(errors);
        }

        // Create every block instance (even unconnected ones keep their slot so the API can address them).
        var slotOfNode = new int[nodes.Count];
        var blocks = new List<IDspBlock>();
        var slotIds = new List<string>();
        var bypassed = new List<bool>();
        var bypassModes = new List<BypassKind>();
        for (var n = 0; n < nodes.Count; n++) {
            slotOfNode[n] = -1;
            var node = nodes[n];
            if (NodeTypes.IsBuiltIn(node.Type)) {
                continue;
            }

            var block = registry.Create(node.Type);
            foreach (var (paramId, value) in node.Parameters) {
                var index = block.Descriptor.IndexOf(paramId);
                if (index < 0) {
                    errors.Add($"Node '{node.Id}': block type '{node.Type}' has no parameter '{paramId}'.");
                    continue;
                }

                block.Params.SetTarget(index, value);
            }

            if (!BypassModes.IsValid(node.BypassMode)) {
                errors.Add($"Node '{node.Id}': unknown bypass mode '{node.BypassMode}'.");
            }

            LoadAssets(node, block, errors);
            if (block is FxLoopBlock loop) {
                loop.Port = HardwareLoop;
            }

            block.Prepare(spec);
            slotOfNode[n] = blocks.Count;
            blocks.Add(block);
            slotIds.Add(node.Id);
            bypassed.Add(node.Bypassed);
            bypassModes.Add(node.BypassMode switch {
                    BypassModes.Mute => BypassKind.Mute,
                    BypassModes.MuteIn => BypassKind.MuteIn,
                    BypassModes.MuteFxIn => BypassKind.MuteFxIn,
                    BypassModes.MuteOut => BypassKind.MuteOut,
                    BypassModes.MuteFxOut => BypassKind.MuteFxOut,
                    _ => BypassKind.Thru
                }
            );
        }

        var (send, ret) = PairFeedback(nodes, slotOfNode, blocks, spec, errors);

        var modifiers = new List<ModifierBinding>();
        foreach (var modifier in definition.Modifiers) {
            var slot = slotIds.IndexOf(modifier.Block);
            var index = slot < 0 ? -1 : blocks[slot].Descriptor.IndexOf(modifier.Param);
            if (index < 0) {
                errors.Add($"Modifier: no parameter '{modifier.Param}' on block '{modifier.Block}'.");
            } else if (!ModNames.TryParseSource(modifier.Source, out var source)) {
                errors.Add($"Modifier on '{modifier.Block}.{modifier.Param}': unknown source '{modifier.Source}'.");
            } else if (!ModNames.TryParseCurve(modifier.Curve, out var curve)) {
                errors.Add($"Modifier on '{modifier.Block}.{modifier.Param}': unknown curve '{modifier.Curve}'.");
            } else if (modifiers.Any(m => m.Slot == slot && m.ParamIndex == index)) {
                errors.Add($"Parameter '{modifier.Block}.{modifier.Param}' has more than one modifier.");
            } else {
                var param = blocks[slot].Descriptor.Params[index];
                modifiers.Add(
                    new(
                        slot,
                        index,
                        source,
                        curve,
                        Math.Clamp(modifier.Min, param.Min, param.Max),
                        Math.Clamp(modifier.Max, param.Min, param.Max),
                        param.Taper == ParamTaper.Log,
                        modifier.AttackMs,
                        modifier.ReleaseMs
                    )
                );
            }
        }

        if (errors.Count > 0) {
            blocks.ForEach(static b => b.Dispose());
            throw new GraphValidationException(errors);
        }

        // Plain loops from here on: this runs on every preset load in a process that must not collect.
        // A send feeds its return without a cable: link them for liveness only, not for ordering or latency.
        if (send >= 0 && ret >= 0) {
            consumers[send].Add(ret);
            inputs[ret].Add(send);
        }

        var live = FindLiveNodes(definition, inputs, consumers);
        if (send >= 0 && ret >= 0) {
            consumers[send].Remove(ret);
            inputs[ret].Remove(send);
        }

        var liveCount = 0;
        var order = new int[nodes.Count];
        for (var n = 0; n < nodes.Count; n++) {
            if (live[n]) {
                order[liveCount++] = n;
            }
        }

        Array.Resize(ref order, liveCount);
        Array.Sort(
            order,
            (a, b) => nodes[a].Column != nodes[b].Column
                ? nodes[a].Column.CompareTo(nodes[b].Column)
                : nodes[a].Row.CompareTo(nodes[b].Row)
        );

        // Path latency: the latest-arriving input defines each node's start time.
        var latency = new int[nodes.Count];
        var graphLatency = 0;
        foreach (var n in order) {
            latency[n] = Arrival(inputs[n], live, latency)
                + (slotOfNode[n] >= 0 ? blocks[slotOfNode[n]].LatencySamples : 0);
            if (nodes[n].Type == NodeTypes.Output) {
                graphLatency = Math.Max(graphLatency, latency[n]);
            }
        }

        var emitter = new PlanEmitter();
        var bufferOf = new int[nodes.Count];
        var readersLeft = new int[nodes.Count];
        foreach (var n in order) {
            foreach (var c in consumers[n]) {
                readersLeft[n] += live[c] ? 1 : 0;
            }
        }

        var liveInputs = new List<int>();

        foreach (var n in order) {
            var node = nodes[n];
            liveInputs.Clear();
            foreach (var i in inputs[n]) {
                if (live[i]) {
                    liveInputs.Add(i);
                }
            }

            var arrival = Arrival(inputs[n], live, latency);
            int buffer;

            var mux = slotOfNode[n] >= 0 && blocks[slotOfNode[n]] is IRowInputBlock;
            if (node.Type == NodeTypes.Input) {
                buffer = emitter.Allocate();
                emitter.Emit(new(GraphOpKind.ReadInput, buffer));
            } else if (liveInputs.Count == 0) {
                buffer = emitter.Allocate(); // a Feedback Return fed only by its send
                emitter.Emit(new(GraphOpKind.Clear, buffer));
            } else if (mux) {
                // Each input goes through the multiplexer's selection fade instead of a plain sum.
                buffer = emitter.Allocate();
                emitter.Emit(new(GraphOpKind.Clear, buffer));
                foreach (var source in liveInputs) {
                    var delay = arrival - latency[source];
                    var from = bufferOf[source];
                    var temp = -1;
                    if (delay > 0) {
                        temp = emitter.Allocate();
                        emitter.Emit(new(GraphOpKind.Copy, temp, from));
                        emitter.EmitDelay(temp, delay);
                        from = temp;
                    }

                    emitter.Emit(new(GraphOpKind.MuxInput, buffer, from, slotOfNode[n], nodes[source].Row));
                    if (temp >= 0) {
                        emitter.Free(temp);
                    }

                    if (--readersLeft[source] == 0) {
                        emitter.Free(bufferOf[source]);
                    }
                }
            } else if (liveInputs.Count == 1 && readersLeft[liveInputs[0]] == 1) {
                // Sole remaining reader: take over the upstream buffer and process in place.
                var source = liveInputs[0];
                buffer = bufferOf[source];
                readersLeft[source] = 0;
                emitter.EmitDelay(buffer, arrival - latency[source]);
            } else {
                buffer = emitter.Allocate();
                for (var k = 0; k < liveInputs.Count; k++) {
                    var source = liveInputs[k];
                    var delay = arrival - latency[source];
                    if (k == 0) {
                        emitter.Emit(new(GraphOpKind.Copy, buffer, bufferOf[source]));
                        emitter.EmitDelay(buffer, delay);
                    } else if (delay == 0) {
                        emitter.Emit(new(GraphOpKind.Add, buffer, bufferOf[source]));
                    } else {
                        var temp = emitter.Allocate();
                        emitter.Emit(new(GraphOpKind.Copy, temp, bufferOf[source]));
                        emitter.EmitDelay(temp, delay);
                        emitter.Emit(new(GraphOpKind.Add, buffer, temp));
                        emitter.Free(temp);
                    }

                    if (--readersLeft[source] == 0) {
                        emitter.Free(bufferOf[source]);
                    }
                }
            }

            bufferOf[n] = buffer;
            if (slotOfNode[n] >= 0) {
                emitter.Emit(new(GraphOpKind.Process, buffer, Index: slotOfNode[n]));
            } else if (node.Type == NodeTypes.Output) {
                emitter.EmitDelay(buffer, graphLatency - latency[n]);
                emitter.Emit(new(GraphOpKind.WriteOutput, buffer));
            }

            if (readersLeft[n] == 0) {
                emitter.Free(buffer);
            }
        }

        return new(
            generation,
            spec,
            [..emitter.Ops],
            emitter.BufferCount,
            [..blocks],
            [..slotIds],
            [..bypassed],
            [..bypassModes],
            [..emitter.Delays],
            graphLatency,
            [..modifiers]
        );
    }

    /// <summary>
    ///     Connects the preset's Feedback Send to its Feedback Return through a shared <see cref="FeedbackBus" />.
    ///     Returns their node indices (-1 when absent).
    /// </summary>
    static (int Send, int Return) PairFeedback(
        List<NodeDefinition> nodes,
        int[] slotOfNode,
        List<IDspBlock> blocks,
        ProcessSpec spec,
        List<string> errors
    ) {
        int send = -1, ret = -1;
        for (var n = 0; n < nodes.Count; n++) {
            var block = slotOfNode[n] >= 0 ? blocks[slotOfNode[n]] : null;
            if (block is FeedbackSendBlock) {
                if (send >= 0) {
                    errors.Add($"Node '{nodes[n].Id}': a preset has at most one Feedback Send.");
                }

                send = n;
            } else if (block is FeedbackReturnBlock) {
                if (ret >= 0) {
                    errors.Add($"Node '{nodes[n].Id}': a preset has at most one Feedback Return.");
                }

                ret = n;
            }
        }

        if (send >= 0 && ret >= 0) {
            var bus = new FeedbackBus(spec.MaxBlockSize);
            ((FeedbackSendBlock)blocks[slotOfNode[send]]).Bus = bus;
            ((FeedbackReturnBlock)blocks[slotOfNode[ret]]).Bus = bus;
        }

        return (send, ret);
    }

    /// <summary>When the latest live input arrives, in samples of path latency (0 without live inputs).</summary>
    static int Arrival(List<int> inputs, bool[] live, int[] latency) {
        var arrival = 0;
        foreach (var i in inputs) {
            if (live[i]) {
                arrival = Math.Max(arrival, latency[i]);
            }
        }

        return arrival;
    }

    void LoadAssets(NodeDefinition node, IDspBlock block, List<string> errors) {
        foreach (var (slot, path) in node.Assets) {
            if (block is not IAssetConsumer consumer || !consumer.AssetSlots.Contains(slot)) {
                errors.Add($"Node '{node.Id}': block type '{node.Type}' has no asset slot '{slot}'.");
                continue;
            }

            try {
                consumer.LoadAsset(slot, Path.GetFullPath(path, assetRoot ?? Environment.CurrentDirectory));
            } catch (Exception e) when (e is IOException
                                            or InvalidDataException
                                            or UnauthorizedAccessException
                                            or DllNotFoundException
                                            or ArgumentException) {
                errors.Add($"Node '{node.Id}': could not load asset '{slot}' from '{path}': {e.Message}");
            }
        }
    }

    Dictionary<string, int> IndexNodes(GraphDefinition definition, List<string> errors) {
        var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        var occupied = new Dictionary<(int, int), string>();
        for (var n = 0; n < definition.Nodes.Count; n++) {
            var node = definition.Nodes[n];
            if (string.IsNullOrWhiteSpace(node.Id)) {
                errors.Add($"Node #{n} has no id.");
                continue;
            }

            if (!indexById.TryAdd(node.Id, n)) {
                errors.Add($"Duplicate node id '{node.Id}'.");
            }

            if (node.Row < 0 || node.Row >= definition.Rows || node.Column < 0 || node.Column >= definition.Columns) {
                errors.Add(
                    $"Node '{node.Id}' at ({node.Row},{node.Column}) is outside the {definition.Rows}x{definition.Columns} grid."
                );
            } else if (!occupied.TryAdd((node.Row, node.Column), node.Id)) {
                errors.Add(
                    $"Node '{node.Id}' shares cell ({node.Row},{node.Column}) with '{occupied[(node.Row, node.Column)]}'."
                );
            }

            if (!NodeTypes.IsBuiltIn(node.Type) && !registry.Contains(node.Type)) {
                errors.Add($"Node '{node.Id}' has unknown type '{node.Type}'.");
            }
        }

        return indexById;
    }

    static (List<int>[] Inputs, List<int>[] Consumers) ResolveCables(
        GraphDefinition definition,
        Dictionary<string, int> indexById,
        List<string> errors
    ) {
        var count = definition.Nodes.Count;
        var inputs = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
        var consumers = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
        var seen = new HashSet<(int, int)>();
        foreach (var cable in definition.Cables) {
            if (!indexById.TryGetValue(cable.From, out var from) || !indexById.TryGetValue(cable.To, out var to)) {
                errors.Add($"Cable '{cable.From}' -> '{cable.To}' references an unknown node.");
                continue;
            }

            var source = definition.Nodes[from];
            var target = definition.Nodes[to];
            if (target.Column != source.Column + 1) {
                errors.Add(
                    $"Cable '{cable.From}' -> '{cable.To}' must connect adjacent columns (use shunts to bridge gaps)."
                );
            } else if (source.Type == NodeTypes.Output) {
                errors.Add($"Cable '{cable.From}' -> '{cable.To}' starts at an output.");
            } else if (target.Type == NodeTypes.Input) {
                errors.Add($"Cable '{cable.From}' -> '{cable.To}' ends at an input.");
            } else if (!seen.Add((from, to))) {
                errors.Add($"Duplicate cable '{cable.From}' -> '{cable.To}'.");
            } else {
                inputs[to].Add(from);
                consumers[from].Add(to);
            }
        }

        return (inputs, consumers);
    }

    /// <summary>A node is live when it is reachable from an input and can reach an output.</summary>
    static bool[] FindLiveNodes(GraphDefinition definition, List<int>[] inputs, List<int>[] consumers) {
        var count = definition.Nodes.Count;
        var fromInput = Flood(
            Enumerable.Range(0, count).Where(n => definition.Nodes[n].Type == NodeTypes.Input),
            consumers,
            count
        );
        var toOutput = Flood(
            Enumerable.Range(0, count).Where(n => definition.Nodes[n].Type == NodeTypes.Output),
            inputs,
            count
        );
        return Enumerable.Range(0, count).Select(n => fromInput[n] && toOutput[n]).ToArray();
    }

    static bool[] Flood(IEnumerable<int> seeds, List<int>[] edges, int count) {
        var visited = new bool[count];
        var stack = new Stack<int>(seeds);
        while (stack.TryPop(out var n)) {
            if (visited[n]) {
                continue;
            }

            visited[n] = true;
            foreach (var next in edges[n]) {
                stack.Push(next);
            }
        }

        return visited;
    }

    sealed class PlanEmitter {
        readonly Stack<int> free = new();

        public List<GraphOp> Ops { get; } = [];

        public List<DelayLine> Delays { get; } = [];

        public int BufferCount { get; private set; }

        public int Allocate() => free.TryPop(out var buffer) ? buffer : BufferCount++;

        public void Free(int buffer) => free.Push(buffer);

        public void Emit(GraphOp op) => Ops.Add(op);

        public void EmitDelay(int buffer, int samples) {
            if (samples <= 0) {
                return;
            }

            Delays.Add(new(samples));
            Emit(new(GraphOpKind.Delay, buffer, Index: Delays.Count - 1));
        }
    }
}
