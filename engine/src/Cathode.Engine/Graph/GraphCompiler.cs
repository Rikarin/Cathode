using Cathode.Dsp;

namespace Cathode.Engine.Graph;

/// <summary>
/// Turns a <see cref="GraphDefinition"/> into a <see cref="CompiledGraph"/>: validates the grid, prunes nodes
/// that cannot reach both an input and an output, orders the rest, compensates latency between parallel
/// paths and assigns work buffers by lifetime so they are reused.
/// </summary>
/// <param name="registry">Block types available to graphs.</param>
/// <param name="assetRoot">Folder that relative asset paths resolve against (default: the current directory).</param>
public sealed class GraphCompiler(BlockRegistry registry, string? assetRoot = null)
{
    public CompiledGraph Compile(GraphDefinition definition, ProcessSpec spec, int generation = 0)
    {
        var errors = new List<string>();
        var nodes = definition.Nodes;
        var indexById = IndexNodes(definition, errors);
        var (inputs, consumers) = ResolveCables(definition, indexById, errors);
        if (errors.Count > 0)
        {
            throw new GraphValidationException(errors);
        }

        // Create every block instance (even unconnected ones keep their slot so the API can address them).
        var slotOfNode = new int[nodes.Count];
        var blocks = new List<IDspBlock>();
        var slotIds = new List<string>();
        var bypassed = new List<bool>();
        for (var n = 0; n < nodes.Count; n++)
        {
            slotOfNode[n] = -1;
            var node = nodes[n];
            if (NodeTypes.IsBuiltIn(node.Type))
            {
                continue;
            }

            var block = registry.Create(node.Type);
            foreach (var (paramId, value) in node.Parameters)
            {
                var index = block.Descriptor.IndexOf(paramId);
                if (index < 0)
                {
                    errors.Add($"Node '{node.Id}': block type '{node.Type}' has no parameter '{paramId}'.");
                    continue;
                }

                block.Params.SetTarget(index, value);
            }

            LoadAssets(node, block, errors);
            block.Prepare(spec);
            slotOfNode[n] = blocks.Count;
            blocks.Add(block);
            slotIds.Add(node.Id);
            bypassed.Add(node.Bypassed);
        }

        if (errors.Count > 0)
        {
            blocks.ForEach(static b => b.Dispose());
            throw new GraphValidationException(errors);
        }

        var live = FindLiveNodes(definition, inputs, consumers);
        var order = Enumerable.Range(0, nodes.Count)
            .Where(n => live[n])
            .OrderBy(n => nodes[n].Column)
            .ThenBy(n => nodes[n].Row)
            .ToArray();

        // Path latency: the latest-arriving input defines each node's start time.
        var latency = new int[nodes.Count];
        foreach (var n in order)
        {
            var arrival = inputs[n].Where(i => live[i]).Select(i => latency[i]).DefaultIfEmpty(0).Max();
            latency[n] = arrival + (slotOfNode[n] >= 0 ? blocks[slotOfNode[n]].LatencySamples : 0);
        }

        var graphLatency = order.Where(n => nodes[n].Type == NodeTypes.Output).Select(n => latency[n]).DefaultIfEmpty(0).Max();

        var emitter = new PlanEmitter();
        var bufferOf = new int[nodes.Count];
        var readersLeft = new int[nodes.Count];
        foreach (var n in order)
        {
            readersLeft[n] = consumers[n].Count(c => live[c]);
        }

        foreach (var n in order)
        {
            var node = nodes[n];
            var liveInputs = inputs[n].Where(i => live[i]).ToArray();
            var arrival = liveInputs.Select(i => latency[i]).DefaultIfEmpty(0).Max();
            int buffer;

            if (node.Type == NodeTypes.Input)
            {
                buffer = emitter.Allocate();
                emitter.Emit(new GraphOp(GraphOpKind.ReadInput, buffer));
            }
            else if (liveInputs.Length == 1 && readersLeft[liveInputs[0]] == 1)
            {
                // Sole remaining reader: take over the upstream buffer and process in place.
                var source = liveInputs[0];
                buffer = bufferOf[source];
                readersLeft[source] = 0;
                emitter.EmitDelay(buffer, arrival - latency[source]);
            }
            else
            {
                buffer = emitter.Allocate();
                for (var k = 0; k < liveInputs.Length; k++)
                {
                    var source = liveInputs[k];
                    var delay = arrival - latency[source];
                    if (k == 0)
                    {
                        emitter.Emit(new GraphOp(GraphOpKind.Copy, buffer, bufferOf[source]));
                        emitter.EmitDelay(buffer, delay);
                    }
                    else if (delay == 0)
                    {
                        emitter.Emit(new GraphOp(GraphOpKind.Add, buffer, bufferOf[source]));
                    }
                    else
                    {
                        var temp = emitter.Allocate();
                        emitter.Emit(new GraphOp(GraphOpKind.Copy, temp, bufferOf[source]));
                        emitter.EmitDelay(temp, delay);
                        emitter.Emit(new GraphOp(GraphOpKind.Add, buffer, temp));
                        emitter.Free(temp);
                    }

                    if (--readersLeft[source] == 0)
                    {
                        emitter.Free(bufferOf[source]);
                    }
                }
            }

            bufferOf[n] = buffer;
            if (slotOfNode[n] >= 0)
            {
                emitter.Emit(new GraphOp(GraphOpKind.Process, buffer, Index: slotOfNode[n]));
            }
            else if (node.Type == NodeTypes.Output)
            {
                emitter.EmitDelay(buffer, graphLatency - latency[n]);
                emitter.Emit(new GraphOp(GraphOpKind.WriteOutput, buffer));
            }

            if (readersLeft[n] == 0)
            {
                emitter.Free(buffer);
            }
        }

        return new CompiledGraph(
            generation,
            spec,
            [.. emitter.Ops],
            emitter.BufferCount,
            [.. blocks],
            [.. slotIds],
            [.. bypassed],
            [.. emitter.Delays],
            graphLatency);
    }

    private void LoadAssets(NodeDefinition node, IDspBlock block, List<string> errors)
    {
        foreach (var (slot, path) in node.Assets)
        {
            if (block is not IAssetConsumer consumer || !consumer.AssetSlots.Contains(slot))
            {
                errors.Add($"Node '{node.Id}': block type '{node.Type}' has no asset slot '{slot}'.");
                continue;
            }

            try
            {
                consumer.LoadAsset(slot, Path.GetFullPath(path, assetRoot ?? Environment.CurrentDirectory));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or DllNotFoundException or ArgumentException)
            {
                errors.Add($"Node '{node.Id}': could not load asset '{slot}' from '{path}': {e.Message}");
            }
        }
    }

    private Dictionary<string, int> IndexNodes(GraphDefinition definition, List<string> errors)
    {
        var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        var occupied = new Dictionary<(int, int), string>();
        for (var n = 0; n < definition.Nodes.Count; n++)
        {
            var node = definition.Nodes[n];
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                errors.Add($"Node #{n} has no id.");
                continue;
            }

            if (!indexById.TryAdd(node.Id, n))
            {
                errors.Add($"Duplicate node id '{node.Id}'.");
            }

            if (node.Row < 0 || node.Row >= definition.Rows || node.Column < 0 || node.Column >= definition.Columns)
            {
                errors.Add($"Node '{node.Id}' at ({node.Row},{node.Column}) is outside the {definition.Rows}x{definition.Columns} grid.");
            }
            else if (!occupied.TryAdd((node.Row, node.Column), node.Id))
            {
                errors.Add($"Node '{node.Id}' shares cell ({node.Row},{node.Column}) with '{occupied[(node.Row, node.Column)]}'.");
            }

            if (!NodeTypes.IsBuiltIn(node.Type) && !registry.Contains(node.Type))
            {
                errors.Add($"Node '{node.Id}' has unknown type '{node.Type}'.");
            }
        }

        return indexById;
    }

    private static (List<int>[] Inputs, List<int>[] Consumers) ResolveCables(
        GraphDefinition definition,
        Dictionary<string, int> indexById,
        List<string> errors)
    {
        var count = definition.Nodes.Count;
        var inputs = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
        var consumers = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
        var seen = new HashSet<(int, int)>();
        foreach (var cable in definition.Cables)
        {
            if (!indexById.TryGetValue(cable.From, out var from) || !indexById.TryGetValue(cable.To, out var to))
            {
                errors.Add($"Cable '{cable.From}' -> '{cable.To}' references an unknown node.");
                continue;
            }

            var source = definition.Nodes[from];
            var target = definition.Nodes[to];
            if (target.Column != source.Column + 1)
            {
                errors.Add($"Cable '{cable.From}' -> '{cable.To}' must connect adjacent columns (use shunts to bridge gaps).");
            }
            else if (source.Type == NodeTypes.Output)
            {
                errors.Add($"Cable '{cable.From}' -> '{cable.To}' starts at an output.");
            }
            else if (target.Type == NodeTypes.Input)
            {
                errors.Add($"Cable '{cable.From}' -> '{cable.To}' ends at an input.");
            }
            else if (!seen.Add((from, to)))
            {
                errors.Add($"Duplicate cable '{cable.From}' -> '{cable.To}'.");
            }
            else
            {
                inputs[to].Add(from);
                consumers[from].Add(to);
            }
        }

        return (inputs, consumers);
    }

    /// <summary>A node is live when it is reachable from an input and can reach an output.</summary>
    private static bool[] FindLiveNodes(GraphDefinition definition, List<int>[] inputs, List<int>[] consumers)
    {
        var count = definition.Nodes.Count;
        var fromInput = Flood(Enumerable.Range(0, count).Where(n => definition.Nodes[n].Type == NodeTypes.Input), consumers, count);
        var toOutput = Flood(Enumerable.Range(0, count).Where(n => definition.Nodes[n].Type == NodeTypes.Output), inputs, count);
        return Enumerable.Range(0, count).Select(n => fromInput[n] && toOutput[n]).ToArray();
    }

    private static bool[] Flood(IEnumerable<int> seeds, List<int>[] edges, int count)
    {
        var visited = new bool[count];
        var stack = new Stack<int>(seeds);
        while (stack.TryPop(out var n))
        {
            if (visited[n])
            {
                continue;
            }

            visited[n] = true;
            foreach (var next in edges[n])
            {
                stack.Push(next);
            }
        }

        return visited;
    }

    private sealed class PlanEmitter
    {
        private readonly Stack<int> _free = new();

        public List<GraphOp> Ops { get; } = [];

        public List<DelayLine> Delays { get; } = [];

        public int BufferCount { get; private set; }

        public int Allocate() => _free.TryPop(out var buffer) ? buffer : BufferCount++;

        public void Free(int buffer) => _free.Push(buffer);

        public void Emit(GraphOp op) => Ops.Add(op);

        public void EmitDelay(int buffer, int samples)
        {
            if (samples <= 0)
            {
                return;
            }

            Delays.Add(new DelayLine(samples));
            Emit(new GraphOp(GraphOpKind.Delay, buffer, Index: Delays.Count - 1));
        }
    }
}
