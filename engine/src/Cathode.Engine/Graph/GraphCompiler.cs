using Cathode.Dsp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Resampling;
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

    /// <summary>The engine's test tone, handed to every graph (the cab resonance helper).</summary>
    public TestTone? TestTone { get; init; }
    /// <summary>The audio interface's input sources, handed to blocks that can read one (the engine's own).</summary>
    public HardwareInputs? HardwareInputs { get; init; }

    public CompiledGraph Compile(GraphDefinition definition, ProcessSpec spec, int generation = 0) {
        var errors = new List<string>();
        var nodes = definition.Nodes;
        var indexById = IndexNodes(definition, errors);
        var oversampling = definition.Oversampling switch {
            "minimum" or "" => OversamplingPhase.Minimum,
            "linear" => OversamplingPhase.Linear,
            _ => (OversamplingPhase)(-1)
        };
        if ((int)oversampling < 0) {
            errors.Add($"Unknown oversampling '{definition.Oversampling}' (minimum or linear).");
        }

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
        AnalyzerTap? analyzer = null;
        InputPort? port = null;
        for (var n = 0; n < nodes.Count; n++) {
            slotOfNode[n] = -1;
            var node = nodes[n];
            // Input and Output run their own block (gate, row mixer) when the registry has one; shunts never do.
            var builtIn = NodeTypes.IsBuiltIn(node.Type);
            if (node.Type == NodeTypes.Shunt || (builtIn && !registry.Contains(node.Type))) {
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

            if (block is IHardwareInputBlock reader && HardwareInputs is { } hardware) {
                reader.Inputs = port ??= new(hardware, spec.SampleRate);
            }

            if (block is RtaBlock rta) {
                rta.Tap = analyzer ??= new(spec.MaxBlockSize);
            }

            if (block is IOversampledBlock oversampled) {
                oversampled.OversamplingPhase = oversampling;
            }

            block.Prepare(spec);
            slotOfNode[n] = blocks.Count;
            blocks.Add(block);
            slotIds.Add(node.Id);
            bypassed.Add(node.Bypassed && !builtIn);
            bypassModes.Add(
                (builtIn ? BypassModes.Thru : node.BypassMode) switch {
                    BypassModes.Mute => BypassKind.Mute,
                    BypassModes.MuteIn => BypassKind.MuteIn,
                    BypassModes.MuteFxIn => BypassKind.MuteFxIn,
                    BypassModes.MuteOut => BypassKind.MuteOut,
                    BypassModes.MuteFxOut => BypassKind.MuteFxOut,
                    _ => BypassKind.Thru
                }
            );
        }

        var feedbackPairs = PairFeedback(nodes, slotOfNode, blocks, spec, errors);

        var modifiers = new List<ModifierBinding>();
        foreach (var modifier in definition.Modifiers) {
            var slot = slotIds.IndexOf(modifier.Block);
            var engage = modifier.Param == ModNames.EngageParam
                && slot >= 0
                && !NodeTypes.IsBuiltIn(blocks[slot].Descriptor.TypeId);
            var index = slot < 0 ? -1 :
                engage ? ModifierBinding.Engage : blocks[slot].Descriptor.IndexOf(modifier.Param);
            if (index == -1) {
                errors.Add($"Modifier: no parameter '{modifier.Param}' on block '{modifier.Block}'.");
            } else if (!ModNames.TryParseSource(modifier.Source, out var source)) {
                errors.Add($"Modifier on '{modifier.Block}.{modifier.Param}': unknown source '{modifier.Source}'.");
            } else if (!ModNames.TryParseCurve(modifier.Curve, out var curve)) {
                errors.Add($"Modifier on '{modifier.Block}.{modifier.Param}': unknown curve '{modifier.Curve}'.");
            } else if (modifiers.Any(m => m.Slot == slot && m.ParamIndex == index)) {
                errors.Add($"Parameter '{modifier.Block}.{modifier.Param}' has more than one modifier.");
            } else if (engage) {
                modifiers.Add(
                    new(
                        slot,
                        index,
                        source,
                        curve,
                        Math.Clamp(modifier.Min, 0f, 1f),
                        Math.Clamp(modifier.Max, 0f, 1f),
                        false,
                        modifier.AttackMs,
                        modifier.ReleaseMs
                    ) { Enabled = modifier.Enabled }
                );
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
                    ) { Enabled = modifier.Enabled }
                );
            }
        }

        if (errors.Count > 0) {
            blocks.ForEach(static b => b.Dispose());
            throw new GraphValidationException(errors);
        }

        // Plain loops from here on: this runs on every preset load in a process that must not collect.
        // A send feeds its return without a cable: link them for liveness only, not for ordering or latency.
        foreach (var (send, ret) in feedbackPairs) {
            consumers[send].Add(ret);
            inputs[ret].Add(send);
        }

        var live = FindLiveNodes(definition, inputs, consumers);
        foreach (var (send, ret) in feedbackPairs) {
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
        var latest = new int[nodes.Count]; // the input that arrives last, for the latency report
        var graphLatency = 0;
        var slowestOutput = -1;
        foreach (var n in order) {
            latest[n] = -1;
            foreach (var i in inputs[n]) {
                if (live[i] && (latest[n] < 0 || latency[i] > latency[latest[n]])) {
                    latest[n] = i;
                }
            }

            latency[n] = Arrival(inputs[n], live, latency)
                + (slotOfNode[n] >= 0 ? blocks[slotOfNode[n]].LatencySamples : 0);
            if (NodeTypes.IsOutput(nodes[n].Type) && (slowestOutput < 0 || latency[n] > graphLatency)) {
                graphLatency = latency[n];
                slowestOutput = n;
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

        // Sidechain sources: per row, the last live node to the left of each sidechain block. Each counts as one more
        // reader, so its buffer survives (and is not processed over in place) until the block has been offered it.
        var sidechains = new List<(int Row, int Source)>?[nodes.Count];
        foreach (var n in order) {
            if (slotOfNode[n] < 0 || blocks[slotOfNode[n]] is not ISidechainBlock) {
                continue;
            }

            var sources = new List<(int Row, int Source)>();
            for (var row = 0; row < definition.Rows; row++) {
                var best = -1;
                foreach (var m in order) {
                    if (nodes[m].Row == row
                        && nodes[m].Column < nodes[n].Column
                        && !NodeTypes.IsOutput(nodes[m].Type)
                        && (best < 0 || nodes[m].Column > nodes[best].Column)) {
                        best = m;
                    }
                }

                if (best >= 0) {
                    sources.Add((row, best));
                    readersLeft[best]++;
                }
            }

            sidechains[n] = sources;
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
            if (sidechains[n] is { } sidechainSources) {
                emitter.Emit(new(GraphOpKind.SidechainInput, 0, Index: slotOfNode[n], Row: -1));
                foreach (var (row, source) in sidechainSources) {
                    emitter.Emit(new(GraphOpKind.SidechainInput, bufferOf[source], Index: slotOfNode[n], Row: row));
                    if (--readersLeft[source] == 0) {
                        emitter.Free(bufferOf[source]);
                    }
                }
            }

            var mux = slotOfNode[n] >= 0 && blocks[slotOfNode[n]] is IRowInputBlock;
            var soleReader = liveInputs.Count == 1 && readersLeft[liveInputs[0]] == 1;
            if (node.Type == NodeTypes.Input) {
                buffer = emitter.Allocate();
                emitter.Emit(new(GraphOpKind.ReadInput, buffer));
            } else if (liveInputs.Count == 0) {
                buffer = emitter.Allocate(); // a Feedback Return fed only by its send
                emitter.Emit(new(GraphOpKind.Clear, buffer));
            } else if (soleReader && slotOfNode[n] >= 0 && blocks[slotOfNode[n]] is OutputBlock) {
                // An Output fed by one row scales it in place rather than mixing it into a second buffer.
                var source = liveInputs[0];
                buffer = bufferOf[source];
                readersLeft[source] = 0;
                emitter.EmitDelay(buffer, arrival - latency[source]);
                emitter.Emit(new(GraphOpKind.ScaleRow, buffer, Index: slotOfNode[n], Row: nodes[source].Row));
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
            } else if (soleReader) {
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
            }

            if (NodeTypes.IsOutput(node.Type)) {
                // Every output is aligned to the slowest, so outputs played side by side stay in time.
                emitter.EmitDelay(buffer, graphLatency - latency[n]);
                emitter.Emit(new(GraphOpKind.WriteOutput, buffer, Index: NodeTypes.OutputIndex(node.Type)));
            }

            if (readersLeft[n] == 0) {
                emitter.Free(buffer);
            }
        }

        var path = new List<LatencyStep>();
        for (var n = slowestOutput; n >= 0; n = latest[n]) {
            if (slotOfNode[n] >= 0) {
                path.Add(new(nodes[n].Id, nodes[n].Type, blocks[slotOfNode[n]].LatencySamples));
            }
        }

        path.Reverse();
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
            [..modifiers],
            analyzer,
            port
        ) {
            LatencyPath = path,
            Tone = TestTone
        };
    }

    /// <summary>The feedback pair an instance id names: its trailing number, or 1.</summary>
    public static int FeedbackPair(string id) {
        var start = id.Length;
        while (start > 0 && char.IsAsciiDigit(id[start - 1])) {
            start--;
        }

        return start < id.Length && int.TryParse(id.AsSpan(start), out var pair) ? pair : 1;
    }

    /// <summary>
    ///     Connects each Feedback Send to its Feedback Return through a shared <see cref="FeedbackBus" />. A preset has
    ///     up to two pairs; a send and a return pair when their ids end in the same number (<c>fbsend2</c> feeds
    ///     <c>fbreturn2</c>; an id without a number counts as 1). Returns the node indices of each complete pair.
    /// </summary>
    static List<(int Send, int Return)> PairFeedback(
        List<NodeDefinition> nodes,
        int[] slotOfNode,
        List<IDspBlock> blocks,
        ProcessSpec spec,
        List<string> errors
    ) {
        var sends = new Dictionary<int, int>();
        var returns = new Dictionary<int, int>();
        for (var n = 0; n < nodes.Count; n++) {
            var block = slotOfNode[n] >= 0 ? blocks[slotOfNode[n]] : null;
            var found = block switch {
                FeedbackSendBlock => sends,
                FeedbackReturnBlock => returns,
                _ => null
            };
            if (found is not null && !found.TryAdd(FeedbackPair(nodes[n].Id), n)) {
                var kind = block is FeedbackSendBlock ? "Feedback Send" : "Feedback Return";
                errors.Add(
                    $"Node '{nodes[n].Id}': a preset has at most one {kind} per pair ({kind} {FeedbackPair(nodes[n].Id)} is '{nodes[found[FeedbackPair(nodes[n].Id)]].Id}')."
                );
            }
        }

        var pairs = new List<(int Send, int Return)>();
        foreach (var (pair, send) in sends) {
            if (returns.TryGetValue(pair, out var ret)) {
                var bus = new FeedbackBus(spec.MaxBlockSize);
                ((FeedbackSendBlock)blocks[slotOfNode[send]]).Bus = bus;
                ((FeedbackReturnBlock)blocks[slotOfNode[ret]]).Bus = bus;
                pairs.Add((send, ret));
            }
        }

        return pairs;
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
            } else if (NodeTypes.IsOutput(source.Type)) {
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
            Enumerable.Range(0, count).Where(n => NodeTypes.IsOutput(definition.Nodes[n].Type)),
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
