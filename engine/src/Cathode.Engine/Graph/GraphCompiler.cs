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
        var oversampling = ParseOversampling(definition.Oversampling, errors);
        var (inputs, consumers) = ResolveCables(definition, indexById, errors);
        if (errors.Count > 0) {
            throw new GraphValidationException(errors);
        }

        var set = CreateBlocks(nodes, spec, oversampling, errors);
        var feedbackPairs = PairFeedback(nodes, set.SlotOfNode, set.Blocks, spec, errors);
        var modifiers = BindModifiers(definition.Modifiers, set, errors);
        if (errors.Count > 0) {
            set.Blocks.ForEach(static b => b.Dispose());
            throw new GraphValidationException(errors);
        }

        // Plain loops from here on: this runs on every preset load in a process that must not collect.
        var live = FindLiveNodes(definition, inputs, consumers, feedbackPairs);
        var order = LiveOrder(nodes, live);
        var timing = MeasureLatency(nodes, order, inputs, live, set);
        FollowCabs(order, consumers, live, set);
        var plan = new PlanBuilder(definition, order, inputs, live, set, timing);
        plan.Build(consumers);
        var emitter = plan.Emitter;
        return new(
            generation,
            spec,
            [..emitter.Ops],
            emitter.BufferCount,
            [..set.Blocks],
            [..set.SlotIds],
            [..set.Bypassed],
            [..set.BypassKinds],
            [..emitter.Delays],
            timing.GraphLatency,
            [..modifiers],
            set.Analyzer,
            set.Port
        ) { LatencyPath = LatencyPath(nodes, timing, set), Tone = TestTone };
    }

    static OversamplingPhase ParseOversampling(string name, List<string> errors) {
        var oversampling = name switch {
            "minimum" or "" => OversamplingPhase.Minimum,
            "linear" => OversamplingPhase.Linear,
            _ => (OversamplingPhase)(-1)
        };
        if ((int)oversampling < 0) {
            errors.Add($"Unknown oversampling '{name}' (minimum or linear).");
        }

        return oversampling;
    }

    /// <summary>
    ///     Creates every block instance (even unconnected ones keep their slot so the API can address them), sets its
    ///     parameters and assets, hands it what it reads from the engine and prepares it.
    /// </summary>
    BlockSet CreateBlocks(
        List<NodeDefinition> nodes,
        ProcessSpec spec,
        OversamplingPhase oversampling,
        List<string> errors
    ) {
        var set = new BlockSet(nodes.Count);
        for (var n = 0; n < nodes.Count; n++) {
            set.SlotOfNode[n] = -1;
            var node = nodes[n];
            // Input and Output run their own block (gate, row mixer) when the registry has one; shunts never do.
            var builtIn = NodeTypes.IsBuiltIn(node.Type);
            if (node.Type == NodeTypes.Shunt || (builtIn && !registry.Contains(node.Type))) {
                continue;
            }

            var block = registry.Create(node.Type);
            ApplyParameters(node, block, errors);
            if (!BypassModes.IsValid(node.BypassMode)) {
                errors.Add($"Node '{node.Id}': unknown bypass mode '{node.BypassMode}'.");
            }

            LoadAssets(node, block, errors);
            ConnectToEngine(block, n, set, spec, oversampling);
            block.Prepare(spec);
            set.Add(
                n,
                node.Id,
                block,
                node.Bypassed && !builtIn,
                ToBypassKind(builtIn ? BypassModes.Thru : node.BypassMode)
            );
        }

        return set;
    }

    static void ApplyParameters(NodeDefinition node, IDspBlock block, List<string> errors) {
        foreach (var (paramId, value) in node.Parameters) {
            var index = block.Descriptor.IndexOf(paramId);
            if (index < 0) {
                errors.Add($"Node '{node.Id}': block type '{node.Type}' has no parameter '{paramId}'.");
                continue;
            }

            block.Params.SetTarget(index, value);
        }
    }

    /// <summary>Hands a block what it reads from the engine or the graph: FX loop, side inputs, analyzer tap.</summary>
    void ConnectToEngine(IDspBlock block, int n, BlockSet set, ProcessSpec spec, OversamplingPhase oversampling) {
        if (block is FxLoopBlock loop) {
            loop.Port = HardwareLoop;
        }

        if (block is ISideInputBlock side) {
            set.SideInputsOf[n] = side.SideInputs;
            if ((side.SideInputs & SideInputs.Hardware) != 0 && HardwareInputs is { } hardware) {
                side.Inputs = set.Port ??= new(hardware, spec.SampleRate);
            }
        }

        if (block is RtaBlock rta) {
            rta.Tap = set.Analyzer ??= new(spec.MaxBlockSize);
        }

        if (block is IOversampledBlock oversampled) {
            oversampled.OversamplingPhase = oversampling;
        }
    }

    static BypassKind ToBypassKind(string mode) =>
        mode switch {
            BypassModes.Mute => BypassKind.Mute,
            BypassModes.MuteIn => BypassKind.MuteIn,
            BypassModes.MuteFxIn => BypassKind.MuteFxIn,
            BypassModes.MuteOut => BypassKind.MuteOut,
            BypassModes.MuteFxOut => BypassKind.MuteFxOut,
            _ => BypassKind.Thru
        };

    static List<ModifierBinding> BindModifiers(
        List<ModifierDefinition> definitions,
        BlockSet set,
        List<string> errors
    ) {
        var modifiers = new List<ModifierBinding>();
        foreach (var modifier in definitions) {
            var (slot, index, engage) = TargetOf(modifier, set);
            if (index == -1) {
                errors.Add($"Modifier: no parameter '{modifier.Param}' on block '{modifier.Block}'.");
            } else if (!ModNames.TryParseSource(modifier.Source, out var source)) {
                errors.Add($"Modifier on '{modifier.Block}.{modifier.Param}': unknown source '{modifier.Source}'.");
            } else if (!ModNames.TryParseCurve(modifier.Curve, out var curve)) {
                errors.Add($"Modifier on '{modifier.Block}.{modifier.Param}': unknown curve '{modifier.Curve}'.");
            } else if (HasModifier(modifiers, slot, index)) {
                errors.Add($"Parameter '{modifier.Block}.{modifier.Param}' has more than one modifier.");
            } else {
                modifiers.Add(Bind(modifier, slot, index, source, curve, engage ? null : set.Blocks[slot]));
            }
        }

        return modifiers;
    }

    /// <summary>
    ///     The block slot and parameter index a modifier drives (index −1 for none), and whether that is the block's
    ///     engage switch.
    /// </summary>
    static (int Slot, int Index, bool Engage) TargetOf(ModifierDefinition modifier, BlockSet set) {
        var slot = set.SlotIds.IndexOf(modifier.Block);
        var engage = modifier.Param == ModNames.EngageParam
            && slot >= 0
            && !NodeTypes.IsBuiltIn(set.Blocks[slot].Descriptor.TypeId);
        var index = slot < 0 ? -1 :
            engage ? ModifierBinding.Engage : set.Blocks[slot].Descriptor.IndexOf(modifier.Param);
        return (slot, index, engage);
    }

    static bool HasModifier(List<ModifierBinding> modifiers, int slot, int index) =>
        modifiers.Exists(m => m.Slot == slot && m.ParamIndex == index);

    /// <summary>A valid modifier's binding; <paramref name="block" /> is null for the block's engage switch.</summary>
    static ModifierBinding Bind(
        ModifierDefinition modifier,
        int slot,
        int index,
        ModSource source,
        ModCurve curve,
        IDspBlock? block
    ) {
        if (block is null) {
            return new(
                slot,
                index,
                source,
                curve,
                Math.Clamp(modifier.Min, 0f, 1f),
                Math.Clamp(modifier.Max, 0f, 1f),
                false,
                modifier.AttackMs,
                modifier.ReleaseMs
            ) { Enabled = modifier.Enabled };
        }

        var param = block.Descriptor.Params[index];
        return new(
            slot,
            index,
            source,
            curve,
            Math.Clamp(modifier.Min, param.Min, param.Max),
            Math.Clamp(modifier.Max, param.Min, param.Max),
            param.Taper == ParamTaper.Log,
            modifier.AttackMs,
            modifier.ReleaseMs
        ) { Enabled = modifier.Enabled };
    }

    /// <summary>
    ///     <see cref="FindLiveNodes(GraphDefinition, List{int}[], List{int}[])" /> with each send feeding its return
    ///     without a cable: linked for liveness only, not for ordering or latency.
    /// </summary>
    static bool[] FindLiveNodes(
        GraphDefinition definition,
        List<int>[] inputs,
        List<int>[] consumers,
        List<(int Send, int Return)> feedbackPairs
    ) {
        foreach (var (send, ret) in feedbackPairs) {
            consumers[send].Add(ret);
            inputs[ret].Add(send);
        }

        var live = FindLiveNodes(definition, inputs, consumers);
        foreach (var (send, ret) in feedbackPairs) {
            consumers[send].Remove(ret);
            inputs[ret].Remove(send);
        }

        return live;
    }

    /// <summary>The live nodes in processing order: by column, then by row.</summary>
    static int[] LiveOrder(List<NodeDefinition> nodes, bool[] live) {
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
        return order;
    }

    /// <summary>Path latency: the latest-arriving input defines each node's start time.</summary>
    static PathTiming MeasureLatency(
        List<NodeDefinition> nodes,
        int[] order,
        List<int>[] inputs,
        bool[] live,
        BlockSet set
    ) {
        var latency = new int[nodes.Count];
        var latest = new int[nodes.Count]; // the input that arrives last, for the latency report
        var graphLatency = 0;
        var slowestOutput = -1;
        foreach (var n in order) {
            latest[n] = LatestInput(inputs[n], live, latency);
            latency[n] = Arrival(inputs[n], live, latency) + set.LatencyOf(n);
            if (NodeTypes.IsOutput(nodes[n].Type) && (slowestOutput < 0 || latency[n] > graphLatency)) {
                graphLatency = latency[n];
                slowestOutput = n;
            }
        }

        return new(latency, latest, graphLatency, slowestOutput);
    }

    /// <summary>The live input that arrives last (the first of equals), or −1.</summary>
    static int LatestInput(List<int> inputs, bool[] live, int[] latency) {
        var latest = -1;
        foreach (var i in inputs) {
            if (live[i] && (latest < 0 || latency[i] > latency[latest])) {
                latest = i;
            }
        }

        return latest;
    }

    /// <summary>
    ///     Blocks that follow a Cab block (an amp's Auto speaker load): the first Cab reached through their outputs.
    /// </summary>
    static void FollowCabs(int[] order, List<int>[] consumers, bool[] live, BlockSet set) {
        foreach (var n in order) {
            if (set.SlotOfNode[n] >= 0 && set.Blocks[set.SlotOfNode[n]] is ICabFollower follower) {
                follower.FollowCab(DownstreamCab(n, consumers, live, set.SlotOfNode, set.Blocks));
            }
        }
    }

    /// <summary>The blocks on the slowest output's path, from the input on.</summary>
    static List<LatencyStep> LatencyPath(List<NodeDefinition> nodes, PathTiming timing, BlockSet set) {
        var path = new List<LatencyStep>();
        for (var n = timing.SlowestOutput; n >= 0; n = timing.Latest[n]) {
            if (set.SlotOfNode[n] >= 0) {
                path.Add(new(nodes[n].Id, nodes[n].Type, set.LatencyOf(n)));
            }
        }

        path.Reverse();
        return path;
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
                    $"Node '{nodes[n].Id}': a preset has at most one {kind} per pair ({kind} "
                    + $"{FeedbackPair(nodes[n].Id)} is '{nodes[found[FeedbackPair(nodes[n].Id)]].Id}')."
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
                    $"Node '{node.Id}' at ({node.Row},{node.Column}) is outside the "
                    + $"{definition.Rows}x{definition.Columns} grid."
                );
            } else if (!occupied.TryAdd((node.Row, node.Column), node.Id)) {
                errors.Add(
                    $"Node '{node.Id}' shares cell ({node.Row},{node.Column}) with "
                    + $"'{occupied[(node.Row, node.Column)]}'."
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
        var inputs = Enumerable.Range(0, count).Select(static _ => new List<int>()).ToArray();
        var consumers = Enumerable.Range(0, count).Select(static _ => new List<int>()).ToArray();
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

    /// <summary>
    ///     The first live Cab block downstream of node <paramref name="start" />, breadth first; null for none.
    /// </summary>
    static CabBlock? DownstreamCab(
        int start,
        List<int>[] consumers,
        bool[] live,
        int[] slotOfNode,
        List<IDspBlock> blocks
    ) {
        var queue = new Queue<int>(consumers[start]);
        var seen = new HashSet<int> { start };
        while (queue.Count > 0) {
            var n = queue.Dequeue();
            if (!seen.Add(n) || !live[n]) {
                continue;
            }

            if (slotOfNode[n] >= 0 && blocks[slotOfNode[n]] is CabBlock cab) {
                return cab;
            }

            foreach (var next in consumers[n]) {
                queue.Enqueue(next);
            }
        }

        return null;
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
        return [..Enumerable.Range(0, count).Select(n => fromInput[n] && toOutput[n])];
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

    /// <summary>Each node's block slot and what the compiled graph keeps per slot.</summary>
    sealed class BlockSet(int nodeCount) {
        /// <summary>Each node's block slot, −1 for a node without a block.</summary>
        public int[] SlotOfNode { get; } = new int[nodeCount];

        /// <summary>What each node's block takes from elsewhere (<see cref="ISideInputBlock" />).</summary>
        public SideInputs[] SideInputsOf { get; } = new SideInputs[nodeCount];

        public List<IDspBlock> Blocks { get; } = [];

        public List<string> SlotIds { get; } = [];

        public List<bool> Bypassed { get; } = [];

        public List<BypassKind> BypassKinds { get; } = [];

        public AnalyzerTap? Analyzer { get; set; }

        public InputPort? Port { get; set; }

        public void Add(int node, string id, IDspBlock block, bool bypassed, BypassKind bypassKind) {
            SlotOfNode[node] = Blocks.Count;
            Blocks.Add(block);
            SlotIds.Add(id);
            Bypassed.Add(bypassed);
            BypassKinds.Add(bypassKind);
        }

        /// <summary>The latency of node <paramref name="node" />'s block, 0 without one.</summary>
        public int LatencyOf(int node) => SlotOfNode[node] >= 0 ? Blocks[SlotOfNode[node]].LatencySamples : 0;
    }

    /// <summary>Each node's path latency and latest input, the graph's latency and its slowest output.</summary>
    readonly record struct PathTiming(int[] Latency, int[] Latest, int GraphLatency, int SlowestOutput);

    /// <summary>
    ///     Emits the graph's ops in processing order and assigns work buffers by lifetime, so they are reused: a buffer
    ///     is freed when its last reader has taken it.
    /// </summary>
    sealed class PlanBuilder(
        GraphDefinition definition,
        int[] order,
        List<int>[] inputs,
        bool[] live,
        BlockSet set,
        PathTiming timing
    ) {
        readonly List<NodeDefinition> nodes = definition.Nodes;
        readonly int[] bufferOf = new int[definition.Nodes.Count];
        readonly int[] readersLeft = new int[definition.Nodes.Count];
        readonly List<int> liveInputs = [];

        public PlanEmitter Emitter { get; } = new();

        public void Build(List<int>[] consumers) {
            foreach (var n in order) {
                foreach (var c in consumers[n]) {
                    readersLeft[n] += live[c] ? 1 : 0;
                }
            }

            var taps = CollectTaps();
            foreach (var n in order) {
                EmitNode(n, taps[n]);
            }
        }

        /// <summary>
        ///     Side inputs (ISideInputBlock). Taps: per row, the last live node to the left of the block. Each counts
        ///     as one more reader, so its buffer survives (and is not processed over in place) until the block has
        ///     been offered it. Cabled inputs by row are handed over where the block's input is assembled; hardware
        ///     inputs went to the blocks with the graph's InputPort.
        /// </summary>
        List<(int Row, int Source)>?[] CollectTaps() {
            var taps = new List<(int Row, int Source)>?[nodes.Count];
            foreach (var n in order) {
                if ((set.SideInputsOf[n] & SideInputs.Taps) != 0) {
                    taps[n] = TapsOf(n);
                }
            }

            return taps;
        }

        List<(int Row, int Source)> TapsOf(int n) {
            var sources = new List<(int Row, int Source)>();
            for (var row = 0; row < definition.Rows; row++) {
                var best = LastOnRowLeftOf(n, row);
                if (best >= 0) {
                    sources.Add((row, best));
                    readersLeft[best]++;
                }
            }

            return sources;
        }

        /// <summary>
        ///     The live node on <paramref name="row" /> nearest to the left of node <paramref name="n" />, or −1.
        /// </summary>
        int LastOnRowLeftOf(int n, int row) {
            var best = -1;
            foreach (var m in order) {
                if (nodes[m].Row == row
                    && nodes[m].Column < nodes[n].Column
                    && !NodeTypes.IsOutput(nodes[m].Type)
                    && (best < 0 || nodes[m].Column > nodes[best].Column)) {
                    best = m;
                }
            }

            return best;
        }

        void EmitNode(int n, List<(int Row, int Source)>? tapSources) {
            var node = nodes[n];
            liveInputs.Clear();
            foreach (var i in inputs[n]) {
                if (live[i]) {
                    liveInputs.Add(i);
                }
            }

            var arrival = Arrival(inputs[n], live, timing.Latency);
            if (tapSources is not null) {
                EmitTaps(set.SlotOfNode[n], tapSources);
            }

            var buffer = AssembleInput(n, arrival);
            bufferOf[n] = buffer;
            if (set.SlotOfNode[n] >= 0) {
                Emitter.Emit(new(GraphOpKind.Process, buffer, Index: set.SlotOfNode[n]));
            }

            if (NodeTypes.IsOutput(node.Type)) {
                // Every output is aligned to the slowest, so outputs played side by side stay in time.
                Emitter.EmitDelay(buffer, timing.GraphLatency - timing.Latency[n]);
                Emitter.Emit(new(GraphOpKind.WriteOutput, buffer, Index: NodeTypes.OutputIndex(node.Type)));
            }

            if (readersLeft[n] == 0) {
                Emitter.Free(buffer);
            }
        }

        void EmitTaps(int slot, List<(int Row, int Source)> tapSources) {
            Emitter.EmitSideInput(slot, -1, -1, -1); // the grid input
            foreach (var (row, source) in tapSources) {
                Emitter.EmitSideInput(slot, row, bufferOf[source], -1);
                ReleaseReader(source);
            }
        }

        /// <summary>The buffer node <paramref name="n" />'s input is assembled in, from its live inputs.</summary>
        int AssembleInput(int n, int arrival) {
            var slot = set.SlotOfNode[n];
            var soleReader = liveInputs.Count == 1 && readersLeft[liveInputs[0]] == 1;
            if (nodes[n].Type == NodeTypes.Input) {
                return Fresh(GraphOpKind.ReadInput);
            }

            if (liveInputs.Count == 0) {
                return Fresh(GraphOpKind.Clear); // a Feedback Return fed only by its send
            }

            if (soleReader && slot >= 0 && set.Blocks[slot] is OutputBlock) {
                // An Output fed by one row scales it in place rather than mixing it into a second buffer.
                var row = nodes[liveInputs[0]].Row;
                var scaled = TakeOver(arrival);
                Emitter.Emit(new(GraphOpKind.ScaleRow, scaled, Index: slot, Row: row));
                return scaled;
            }

            if ((set.SideInputsOf[n] & SideInputs.Cables) != 0) {
                return SideInputsByRow(slot, arrival);
            }

            // Sole remaining reader: take over the upstream buffer and process in place.
            return soleReader ? TakeOver(arrival) : Mix(arrival);
        }

        /// <summary>A newly allocated buffer, filled by an op of kind <paramref name="kind" />.</summary>
        int Fresh(GraphOpKind kind) {
            var buffer = Emitter.Allocate();
            Emitter.Emit(new(kind, buffer));
            return buffer;
        }

        /// <summary>The sole live input's buffer, taken over and delayed to the arrival time.</summary>
        int TakeOver(int arrival) {
            var source = liveInputs[0];
            var buffer = bufferOf[source];
            readersLeft[source] = 0;
            Emitter.EmitDelay(buffer, arrival - timing.Latency[source]);
            return buffer;
        }

        /// <summary>
        ///     Each cabled input goes to the block by row (a multiplexer's selection fade, a mixer's row gains, a
        ///     detector's side buffer) instead of a plain sum.
        /// </summary>
        int SideInputsByRow(int slot, int arrival) {
            var buffer = Fresh(GraphOpKind.Clear);
            foreach (var source in liveInputs) {
                var delay = arrival - timing.Latency[source];
                var from = bufferOf[source];
                var temp = -1;
                if (delay > 0) {
                    temp = Emitter.Allocate();
                    Emitter.Emit(new(GraphOpKind.Copy, temp, from));
                    Emitter.EmitDelay(temp, delay);
                    from = temp;
                }

                Emitter.EmitSideInput(slot, nodes[source].Row, from, buffer);
                if (temp >= 0) {
                    Emitter.Free(temp);
                }

                ReleaseReader(source);
            }

            return buffer;
        }

        /// <summary>The live inputs summed into a new buffer, each delayed to the arrival time.</summary>
        int Mix(int arrival) {
            var buffer = Emitter.Allocate();
            for (var k = 0; k < liveInputs.Count; k++) {
                var source = liveInputs[k];
                var delay = arrival - timing.Latency[source];
                if (k == 0) {
                    Emitter.Emit(new(GraphOpKind.Copy, buffer, bufferOf[source]));
                    Emitter.EmitDelay(buffer, delay);
                } else if (delay == 0) {
                    Emitter.Emit(new(GraphOpKind.Add, buffer, bufferOf[source]));
                } else {
                    var temp = Emitter.Allocate();
                    Emitter.Emit(new(GraphOpKind.Copy, temp, bufferOf[source]));
                    Emitter.EmitDelay(temp, delay);
                    Emitter.Emit(new(GraphOpKind.Add, buffer, temp));
                    Emitter.Free(temp);
                }

                ReleaseReader(source);
            }

            return buffer;
        }

        /// <summary>One reader of <paramref name="source" />'s buffer is done; the last frees it.</summary>
        void ReleaseReader(int source) {
            if (--readersLeft[source] == 0) {
                Emitter.Free(bufferOf[source]);
            }
        }
    }

    sealed class PlanEmitter {
        readonly Stack<int> free = new();

        public List<GraphOp> Ops { get; } = [];

        public List<DelayLine> Delays { get; } = [];

        public int BufferCount { get; private set; }

        public int Allocate() => free.TryPop(out var buffer) ? buffer : BufferCount++;

        public void Free(int buffer) => free.Push(buffer);

        public void Emit(GraphOp op) => Ops.Add(op);

        /// <summary>
        ///     A <see cref="GraphOpKind.SideInput" />: buffer <paramref name="source" /> (−1: the grid input), the
        ///     signal on grid row <paramref name="row" />, to block slot <paramref name="slot" />; a cabled input is
        ///     added into
        ///     <paramref name="destination" />, a tap (<paramref name="destination" /> −1) only offered.
        /// </summary>
        public void EmitSideInput(int slot, int row, int source, int destination) =>
            Emit(new(GraphOpKind.SideInput, destination, source, slot, row));

        public void EmitDelay(int buffer, int samples) {
            if (samples <= 0) {
                return;
            }

            Delays.Add(new(samples));
            Emit(new(GraphOpKind.Delay, buffer, Index: Delays.Count - 1));
        }
    }
}
