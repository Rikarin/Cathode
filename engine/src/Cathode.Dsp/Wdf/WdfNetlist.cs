namespace Cathode.Dsp.Wdf;

/// <summary>What one entry of a <see cref="WdfNetlist" /> is.</summary>
public enum NetlistElementKind {
    /// <summary>An adapted port: the next child of the R-type adaptor's port list, between two nodes.</summary>
    Port,

    /// <summary>The adaptor's parent port (internal R-type adaptors only).</summary>
    Up,

    /// <summary>A resistor inside the adaptor: no port of its own, changed with <c>SetResistance</c>.</summary>
    Resistor,

    /// <summary>An ideal voltage source inside the adaptor, set every sample (a supply rail, say).</summary>
    VoltageSource,

    /// <summary>An ideal current source inside the adaptor, set every sample, pushing current into its plus node.</summary>
    CurrentSource,

    /// <summary>
    ///     A voltage-controlled voltage source: v(plus, minus) = gain × v(control plus, control minus). An ideal (or
    ///     finite-gain) op-amp in a linear circuit.
    /// </summary>
    Vcvs,

    /// <summary>One port of a nonlinear device at the root (R-type roots only).</summary>
    Device,

    /// <summary>An output: the voltage between two nodes, read after each sample.</summary>
    Probe
}

/// <summary>One entry of a <see cref="WdfNetlist" />. Node 0 is ground.</summary>
public readonly record struct NetlistElement(
    NetlistElementKind Kind,
    int Plus,
    int Minus,
    double Value = 0,
    int ControlPlus = 0,
    int ControlMinus = 0
);

/// <summary>
///     The circuit embedded in an R-type adaptor, described like a SPICE netlist: nodes, and what joins them. Build it on
///     the control thread, then hand it to <see cref="RTypeAdaptor{TPorts}" /> or <see cref="RTypeRoot{TPorts,TDevice}" />
///     ,
///     which compute their scattering matrix from its modified nodal analysis.
/// </summary>
/// <remarks>
///     <para>
///         Anything with memory (capacitors, inductors), anything with a source resistance and anything below another
///         adaptor is a <see cref="Port" />: the n-th port declared is the n-th child of the port list
///         (<see cref="Ports{TA,TB,TC}" /> and friends). Resistors that do not need to be leaves (pots, bias networks)
///         can live inside the adaptor instead, which saves ports; changing one recomputes the matrix, which is cheap
///         enough to do every audio block. Ideal sources (supply rails, bias voltages) and controlled sources also live
///         inside. Nonlinear devices are declared with <see cref="Device" /> or the helpers (<see cref="Diode" />,
///         <see cref="Transistor" />, <see cref="Triode" />, <see cref="Pentode" />, <see cref="OpAmp" />), in the order
///         of
///         the device's ports.
///     </para>
///     <para>
///         Every method returns an index: a node, or the element's position among elements of its kind (the k-th
///         resistor, the k-th voltage source, the k-th probe), which is what the circuit's setters and readers take.
///     </para>
/// </remarks>
public sealed class WdfNetlist {
    /// <summary>The reference node.</summary>
    public const int Ground = 0;

    readonly List<NetlistElement> elements = [];
    NetlistTopology? topology;

    /// <summary>Number of nodes besides ground.</summary>
    public int Nodes { get; private set; }

    /// <summary>Every element, in declaration order.</summary>
    public IReadOnlyList<NetlistElement> Elements => elements;

    /// <summary>Adds a node and returns its index (1, 2, ...).</summary>
    public int Node() => ++Nodes;

    /// <summary>Adds <paramref name="count" /> nodes and returns the first.</summary>
    public int AddNodes(int count) {
        var first = Nodes + 1;
        Nodes += count;
        return first;
    }

    /// <summary>
    ///     An adapted port between <paramref name="plus" /> and <paramref name="minus" />: the child's voltage is
    ///     v(plus) - v(minus) and its current flows in at <paramref name="plus" />. Returns the port index.
    /// </summary>
    public int Port(int plus, int minus) => Add(new(NetlistElementKind.Port, plus, minus));

    /// <summary>The parent port of an internal adaptor, oriented like <see cref="Port" />. At most one.</summary>
    public int Up(int plus, int minus) => Add(new(NetlistElementKind.Up, plus, minus));

    /// <summary>A resistor of <paramref name="ohms" /> inside the adaptor. Returns its resistor index.</summary>
    public int Resistor(int a, int b, double ohms) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ohms);
        return Add(new(NetlistElementKind.Resistor, a, b, ohms));
    }

    /// <summary>An ideal voltage source, v(plus) - v(minus) = V set per sample. Returns its source index.</summary>
    public int VoltageSource(int plus, int minus) => Add(new(NetlistElementKind.VoltageSource, plus, minus));

    /// <summary>An ideal current source pushing I (set per sample) into <paramref name="plus" />. Returns its index.</summary>
    public int CurrentSource(int plus, int minus) => Add(new(NetlistElementKind.CurrentSource, plus, minus));

    /// <summary>
    ///     A voltage-controlled voltage source, v(plus, minus) = <paramref name="gain" /> × v(controlPlus, controlMinus).
    ///     Returns its index, for changing the gain later.
    /// </summary>
    public int Vcvs(int plus, int minus, int controlPlus, int controlMinus, double gain) =>
        Add(new(NetlistElementKind.Vcvs, plus, minus, gain, controlPlus, controlMinus));

    /// <summary>
    ///     One port of a nonlinear device: its voltage is v(plus) - v(minus) and its current flows in at
    ///     <paramref name="plus" />. Returns the device port index.
    /// </summary>
    public int Device(int plus, int minus) => Add(new(NetlistElementKind.Device, plus, minus));

    /// <summary>A two-terminal device (a diode, or an antiparallel pair): anode, cathode.</summary>
    public int Diode(int anode, int cathode) => Device(anode, cathode);

    /// <summary>The two ports of a <see cref="Bjt" />: (base, emitter) then (base, collector). Returns the first.</summary>
    public int Transistor(int collector, int @base, int emitter) {
        var first = Device(@base, emitter);
        Device(@base, collector);
        return first;
    }

    /// <summary>The two ports of a <see cref="Triode" />: (grid, cathode) then (plate, cathode). Returns the first.</summary>
    public int Triode(int plate, int grid, int cathode) {
        var first = Device(grid, cathode);
        Device(plate, cathode);
        return first;
    }

    /// <summary>
    ///     The three ports of a <see cref="Pentode" />: (control grid, cathode), (screen, cathode), (plate, cathode).
    ///     Returns the first.
    /// </summary>
    public int Pentode(int plate, int screen, int grid, int cathode) {
        var first = Device(grid, cathode);
        Device(screen, cathode);
        Device(plate, cathode);
        return first;
    }

    /// <summary>
    ///     The two ports of an <see cref="Wdf.OpAmp" />: the input (non-inverting, inverting) then the output (output,
    ///     <paramref name="reference" />, usually ground, which its rails are relative to). Returns the first.
    /// </summary>
    public int OpAmp(int nonInverting, int inverting, int output, int reference = Ground) {
        var first = Device(nonInverting, inverting);
        Device(output, reference);
        return first;
    }

    /// <summary>An output, v(plus) - v(minus), read with <c>Probe(index)</c> after each sample. Returns its index.</summary>
    public int Probe(int plus, int minus = Ground) => Add(new(NetlistElementKind.Probe, plus, minus));

    /// <summary>
    ///     The elements' kinds, nodes and per-kind indices as arrays, built once and shared by every adaptor made from
    ///     this netlist (the catalogue's netlists are static, so their adaptors need no copy of their own). Rebuilt when
    ///     elements were added since; never mutated, so publishing it without a lock is safe.
    /// </summary>
    internal NetlistTopology Topology {
        get {
            var current = topology;
            if (current is null || current.Kinds.Length != elements.Count) {
                current = new(elements);
                topology = current;
            }

            return current;
        }
    }

    /// <summary>How many elements of <paramref name="kind" /> the netlist has.</summary>
    public int Count(NetlistElementKind kind) {
        var count = 0;
        foreach (var element in elements) {
            if (element.Kind == kind) {
                count++;
            }
        }

        return count;
    }

    int Add(NetlistElement element) {
        Check(element.Plus);
        Check(element.Minus);
        Check(element.ControlPlus);
        Check(element.ControlMinus);
        if (element.Kind == NetlistElementKind.Up && Count(NetlistElementKind.Up) > 0) {
            throw new InvalidOperationException("An adaptor has a single parent port.");
        }

        var index = Count(element.Kind);
        elements.Add(element);
        return index;
    }

    void Check(int node) {
        if (node < 0 || node > Nodes) {
            throw new ArgumentOutOfRangeException(
                nameof(node),
                node,
                $"Node {node} does not exist; call Node() first."
            );
        }
    }
}

/// <summary>The immutable arrays an adaptor reads a <see cref="WdfNetlist" /> through (see <c>Topology</c>).</summary>
sealed class NetlistTopology {
    public NetlistTopology(List<NetlistElement> elements) {
        Kinds = new NetlistElementKind[elements.Count];
        Plus = new int[elements.Count];
        Minus = new int[elements.Count];
        ControlPlus = new int[elements.Count];
        ControlMinus = new int[elements.Count];
        Ordinal = new int[elements.Count];
        Counts = new int[Enum.GetValues<NetlistElementKind>().Length];
        for (var i = 0; i < elements.Count; i++) {
            var e = elements[i];
            Kinds[i] = e.Kind;
            Plus[i] = e.Plus;
            Minus[i] = e.Minus;
            ControlPlus[i] = e.ControlPlus;
            ControlMinus[i] = e.ControlMinus;
            Ordinal[i] = Counts[(int)e.Kind]++;
        }
    }

    public NetlistElementKind[] Kinds { get; }
    public int[] Plus { get; }
    public int[] Minus { get; }
    public int[] ControlPlus { get; }
    public int[] ControlMinus { get; }

    /// <summary>Each element's index among the elements of its kind.</summary>
    public int[] Ordinal { get; }

    /// <summary>Elements of each kind.</summary>
    public int[] Counts { get; }
}
