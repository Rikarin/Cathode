using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Amp;

/// <summary>
///     The preamp supply's decoupling nodes below the power supply's preamp node (<see cref="DecouplingNode" />), each
///     a dropping resistor into a filter cap with the stages on it drawing a constant current, stepped once per sample
///     like the rest of the supply. <see cref="Supplies" /> holds every node's voltage as a fraction of its nominal one
///     (node 0, the power supply's preamp node, included), which is what the stages on it run on. Allocation-free after
///     construction.
/// </summary>
public sealed class DecouplingChain {
    readonly int[] parent;
    readonly double[] resistance;
    readonly double[] cap;
    readonly double[] current;
    readonly double[] voltage;
    readonly double[] nominal;
    readonly double[] flow; // per node: the current through its dropping resistor this sample
    readonly double[] load; // per node: its own draw plus its children's flows

    /// <summary>No nodes: every stage on the power supply's preamp node.</summary>
    public static DecouplingChain None => new([]);

    /// <summary>The nodes below node 0.</summary>
    public int Count { get; }

    /// <summary>What the chain's stages draw in all, through node 0 (at rest).</summary>
    public double Current { get; }

    /// <summary>Per node (0 the power supply's preamp node): its voltage as a fraction of nominal.</summary>
    public double[] Supplies { get; }

    public DecouplingChain(IReadOnlyList<DecouplingNode> nodes) {
        Count = nodes.Count;
        var n = Count + 1;
        parent = new int[n];
        resistance = new double[n];
        cap = new double[n];
        current = new double[n];
        voltage = new double[n];
        nominal = new double[n];
        flow = new double[n];
        load = new double[n];
        Supplies = new double[n];
        var total = 0.0;
        for (var k = 1; k < n; k++) {
            var node = nodes[k - 1];
            if (node.Parent < 0 || node.Parent >= k) {
                throw new ArgumentException(
                    $"Decoupling node {k} hangs from node {node.Parent}: not an earlier one.",
                    nameof(nodes)
                );
            }

            if (!(node.Resistance > 0) || !(node.Cap > 0) || node.Current < 0) {
                throw new ArgumentException(
                    $"Decoupling node {k} needs a resistance, a cap and a draw.",
                    nameof(nodes)
                );
            }

            parent[k] = node.Parent;
            resistance[k] = node.Resistance;
            cap[k] = node.Cap;
            current[k] = node.Current;
            total += node.Current;
        }

        Current = total;

        Array.Fill(nominal, 1);
        Array.Fill(voltage, 1);
        Array.Fill(Supplies, 1);
    }

    /// <summary>Node <paramref name="node" />'s voltage after the last sample.</summary>
    public double Voltage(int node) => voltage[node];

    /// <summary>
    ///     Sets the nominal voltages: every node at rest below node 0 at <paramref name="preamp" /> volts (the power
    ///     supply's preamp node at idle with the knobs at noon).
    /// </summary>
    public void Calibrate(double preamp) {
        Reset(preamp, preamp);
        Array.Copy(voltage, nominal, voltage.Length);
        Array.Fill(Supplies, 1);
    }

    /// <summary>
    ///     Every node at rest below node 0 at <paramref name="preamp" /> volts (its own nominal
    ///     <paramref name="nominalPreamp" />): each node's dropping resistor carries its own and its children's draw.
    /// </summary>
    public void Reset(double preamp, double nominalPreamp) {
        Array.Copy(current, load, current.Length);
        for (var k = Count; k > 0; k--) {
            load[parent[k]] += load[k];
        }

        voltage[0] = preamp;
        Supplies[0] = preamp / nominalPreamp;
        for (var k = 1; k <= Count; k++) {
            voltage[k] = voltage[parent[k]] - resistance[k] * load[k];
            Supplies[k] = voltage[k] / nominal[k];
        }
    }

    /// <summary>
    ///     One sample with node 0 at <paramref name="preamp" /> volts (the sample before's): every node's cap charged
    ///     by its resistor's current less its stages' draw and its children's. Returns the current the chain draws from
    ///     node 0.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick(double preamp, double dt) {
        if (Count == 0) {
            return 0;
        }

        voltage[0] = preamp;
        load[0] = 0;
        for (var k = 1; k <= Count; k++) {
            flow[k] = (voltage[parent[k]] - voltage[k]) / resistance[k];
            load[k] = current[k];
        }

        for (var k = Count; k > 0; k--) {
            load[parent[k]] += flow[k];
        }

        for (var k = 1; k <= Count; k++) {
            voltage[k] += dt / cap[k] * (flow[k] - load[k]);
            Supplies[k] = voltage[k] / nominal[k];
        }

        return load[0];
    }
}
