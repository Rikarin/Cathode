using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Wdf;

/// <summary>
///     The scattering matrix of an R-type adaptor, computed from the modified nodal analysis (MNA) of its
///     <see cref="WdfNetlist" />, after Werner, Smith and Abel, "Wave digital filter adaptors for arbitrary topologies and
///     multiport linear elements" (DAFx-15). Every buffer is allocated once (a small network solves in stack scratch),
///     so <see cref="Recompute" /> can run on the audio thread whenever a component changes. The topology arrays are the
///     netlist's own, shared by every adaptor built from it.
/// </summary>
/// <remarks>
///     <para>
///         Each port k is replaced by its Thévenin equivalent: the wave a_k it sends into the adaptor (the child's
///         reflected wave) behind the port resistance R_k, stamped as a conductance 1 / R_k and a current a_k / R_k. With
///         v_k = a_k + R_k i_k for the current i_k into the child, the wave the adaptor sends back is
///         b_k = a_k + 2 R_k i_k = 2 v_k - a_k, so the port resistances cancel out of everything but the MNA matrix. The
///         inputs u are the port waves, then the internal sources; a root's nonlinear device ports add one column each
///         for the current they draw. Solving the MNA system X x = B [u; r] once for every column gives the matrix
///         <c>M</c> with one row per output:
///     </para>
///     <list type="bullet">
///         <item>port rows: b = M_port [u; r] (that is, 2 v - a);</item>
///         <item>
///             device rows: the device port voltages, v = M_dev [u; r] = E u + F r, where F is the K × K network
///             impedance the devices see;
///         </item>
///         <item>probe rows: the probed voltages.</item>
///     </list>
///     <para>
///         An internal adaptor must be reflection-free at its parent port: its resistance is the Thévenin resistance the
///         parent sees, R_up = dᵀ X₀⁻¹ d, where X₀ is the MNA matrix without the parent port and d selects its two nodes.
///         Adding the parent port's conductance G = 1 / R_up is a rank-one update, so the Sherman-Morrison formula
///         X⁻¹ = X₀⁻¹ - (X₀⁻¹ d)(dᵀ X₀⁻¹) G / (1 + G R_up) reuses the factors of X₀: one LU factorisation per recompute.
///         The parent port's own reflection coefficient is then exactly zero, and is stored as such.
///     </para>
///     <para>
///         Each device port is stamped with a companion conductance G_c and draws only the residual current
///         r = i(v) - G_c v, which keeps X regular when a node hangs on a device alone (a transistor's base, a tube's
///         grid). Newton's method is affine invariant, so G_c does not change the iterates, only the conditioning. A tiny
///         conductance (1e-15 S) from every node to ground is the last safety net against a floating node.
///     </para>
/// </remarks>
public sealed class RTypeScattering {
    const double MinimumConductance = 1e-15;
    const double MinimumResistance = 1e-6; // a pot turned fully down: never a short, which would make X singular
    const int MaxStackScratch = 1_024; // doubles (8 KB): larger networks keep their scratch on the heap

    readonly NetlistElementKind[] kinds;
    readonly int[] plus;
    readonly int[] minus;
    readonly int[] controlPlus;
    readonly int[] controlMinus;
    readonly int[] ordinal; // index among the elements of the same kind
    readonly double[] resistances;
    readonly double[] gains;
    readonly double[] deviceConductances;
    readonly double[] portResistances;
    readonly int voltageSources;
    readonly int currentSources;
    readonly int vcvsCount;
    readonly int size; // MNA unknowns: node voltages, then voltage-source and VCVS currents
    readonly double[]? scratch; // the MNA, its solution and the parent response, when too large for the stack
    readonly int[]? scratchPivots;
    readonly double[] matrix;
    readonly int upElement;

    /// <summary>Nodes besides ground.</summary>
    public int Nodes { get; }

    /// <summary>Adapted child ports.</summary>
    public int PortCount { get; }

    /// <summary>True for an internal adaptor, which has a parent port.</summary>
    public bool HasUp { get; }

    /// <summary>Nonlinear device ports (roots only).</summary>
    public int DeviceCount { get; }

    public int ProbeCount { get; }

    /// <summary>Length of the input vector u: port waves, the parent wave, voltage sources, current sources.</summary>
    public int Inputs { get; }

    /// <summary><see cref="Inputs" /> plus one column per device port.</summary>
    public int Columns { get; }

    public int Rows { get; }

    /// <summary>Column of the parent wave in u (internal adaptors).</summary>
    public int UpColumn => PortCount;

    /// <summary>Column of the first voltage source in u.</summary>
    public int VoltageSourceColumn => PortCount + (HasUp ? 1 : 0);

    /// <summary>Column of the first current source in u.</summary>
    public int CurrentSourceColumn => VoltageSourceColumn + voltageSources;

    /// <summary>Row of the parent port in <see cref="Matrix" />.</summary>
    public int UpRow => PortCount;

    /// <summary>Row of the first device port in <see cref="Matrix" />.</summary>
    public int DeviceRow => PortCount + (HasUp ? 1 : 0);

    /// <summary>Row of the first probe in <see cref="Matrix" />.</summary>
    public int ProbeRow => DeviceRow + DeviceCount;

    /// <summary>Resistance of the parent port, adapted at the last <see cref="Recompute" />.</summary>
    public double UpResistance { get; private set; }

    /// <summary>False when the last <see cref="Recompute" /> met a singular network (and kept the previous matrix).</summary>
    public bool IsValid { get; private set; }

    /// <summary>Row-major <see cref="Rows" /> × <see cref="Columns" /> output matrix; see the remarks.</summary>
    public ReadOnlySpan<double> Matrix => matrix;

    /// <summary>The port resistances, written by the port list before <see cref="Recompute" />.</summary>
    public Span<double> PortResistances => portResistances;

    public RTypeScattering(WdfNetlist netlist) {
        ArgumentNullException.ThrowIfNull(netlist);
        var elements = netlist.Elements;
        // The topology is immutable and shared by every adaptor built from the same netlist.
        var topology = netlist.Topology;
        kinds = topology.Kinds;
        plus = topology.Plus;
        minus = topology.Minus;
        controlPlus = topology.ControlPlus;
        controlMinus = topology.ControlMinus;
        ordinal = topology.Ordinal;
        var counts = topology.Counts;
        upElement = Array.IndexOf(kinds, NetlistElementKind.Up);
        Nodes = netlist.Nodes;
        PortCount = counts[(int)NetlistElementKind.Port];
        HasUp = counts[(int)NetlistElementKind.Up] > 0;
        voltageSources = counts[(int)NetlistElementKind.VoltageSource];
        currentSources = counts[(int)NetlistElementKind.CurrentSource];
        vcvsCount = counts[(int)NetlistElementKind.Vcvs];
        DeviceCount = counts[(int)NetlistElementKind.Device];
        ProbeCount = counts[(int)NetlistElementKind.Probe];
        Inputs = PortCount + (HasUp ? 1 : 0) + voltageSources + currentSources;
        Columns = Inputs + DeviceCount;
        Rows = PortCount + (HasUp ? 1 : 0) + DeviceCount + ProbeCount;
        size = Nodes + voltageSources + vcvsCount;

        resistances = new double[counts[(int)NetlistElementKind.Resistor]];
        gains = new double[vcvsCount];
        for (var i = 0; i < elements.Count; i++) {
            if (kinds[i] == NetlistElementKind.Resistor) {
                resistances[ordinal[i]] = elements[i].Value;
            } else if (kinds[i] == NetlistElementKind.Vcvs) {
                gains[ordinal[i]] = elements[i].Value;
            }
        }

        deviceConductances = new double[DeviceCount];
        deviceConductances.AsSpan().Fill(1e-3);
        portResistances = new double[PortCount];
        if (ScratchLength > MaxStackScratch) {
            scratch = new double[ScratchLength];
            scratchPivots = new int[size];
        }

        matrix = new double[Rows * Columns];
    }

    /// <summary>Doubles of scratch a <see cref="Recompute" /> needs: the MNA matrix, its solution, the parent response.</summary>
    int ScratchLength => size * size + size * Columns + size;

    /// <summary>One row of <see cref="Matrix" />.</summary>
    public ReadOnlySpan<double> Row(int row) => matrix.AsSpan(row * Columns, Columns);

    /// <summary>Changes internal resistor <paramref name="index" />; takes effect at the next <see cref="Recompute" />.</summary>
    public void SetResistance(int index, double ohms) => resistances[index] = Math.Max(ohms, MinimumResistance);

    public double GetResistance(int index) => resistances[index];

    /// <summary>Changes the gain of VCVS <paramref name="index" />; takes effect at the next <see cref="Recompute" />.</summary>
    public void SetGain(int index, double gain) => gains[index] = gain;

    /// <summary>The companion conductance of device port <paramref name="port" /> (see the remarks).</summary>
    public void SetDeviceConductance(int port, double siemens) => deviceConductances[port] = Math.Max(siemens, 1e-15);

    /// <summary>
    ///     Rebuilds the MNA from the current component values and <see cref="PortResistances" />, and recomputes the
    ///     scattering matrix. Allocation-free. Returns false (keeping the previous matrix) if the network is singular.
    /// </summary>
    public bool Recompute() {
        var n = size;
        var columns = Columns;
        // Scratch for this call only: on the stack when it fits, so a small adaptor holds no buffers it uses only here.
        var work = scratch is null ? stackalloc double[ScratchLength] : scratch.AsSpan();
        var pivots = scratchPivots is null ? stackalloc int[n] : scratchPivots.AsSpan();
        var a = work[..(n * n)];
        var rhs = work.Slice(n * n, n * columns);
        var upResponse = work.Slice(n * n + n * columns, n);
        a.Clear();
        rhs.Clear();
        for (var node = 1; node <= Nodes; node++) {
            a[(node - 1) * n + node - 1] += MinimumConductance;
        }

        for (var i = 0; i < kinds.Length; i++) {
            var p = plus[i];
            var m = minus[i];
            var k = ordinal[i];
            switch (kinds[i]) {
                case NetlistElementKind.Port: {
                    var g = 1 / Math.Max(portResistances[k], MinimumResistance);
                    Conductance(a, n, p, m, g);
                    Inject(rhs, columns, p, m, k, g);
                    break;
                }

                case NetlistElementKind.Up:
                    // Not stamped: X₀ excludes the parent port. Its column holds d, so solving gives X₀⁻¹ d.
                    Inject(rhs, columns, p, m, UpColumn, 1);
                    break;

                case NetlistElementKind.Resistor:
                    Conductance(a, n, p, m, 1 / resistances[k]);
                    break;

                case NetlistElementKind.VoltageSource: {
                    var row = Nodes + k;
                    Branch(a, n, p, m, row);
                    rhs[row * columns + VoltageSourceColumn + k] = 1;
                    break;
                }

                case NetlistElementKind.CurrentSource:
                    Inject(rhs, columns, p, m, CurrentSourceColumn + k, 1);
                    break;

                case NetlistElementKind.Vcvs: {
                    var row = Nodes + voltageSources + k;
                    Branch(a, n, p, m, row);
                    var cp = controlPlus[i];
                    var cm = controlMinus[i];
                    if (cp > 0) {
                        a[row * n + cp - 1] -= gains[k];
                    }

                    if (cm > 0) {
                        a[row * n + cm - 1] += gains[k];
                    }

                    break;
                }

                case NetlistElementKind.Device:
                    Conductance(a, n, p, m, deviceConductances[k]);
                    Inject(rhs, columns, p, m, Inputs + k, -1); // the residual current leaves the plus node
                    break;

                case NetlistElementKind.Probe:
                    // Not stamped: a probe reads the solution and adds nothing to the system.
                    break;
            }
        }

        if (!DenseLu.Factor(a, n, pivots)) {
            IsValid = false;
            return false;
        }

        DenseLu.Solve(a, n, pivots, rhs, columns);

        if (HasUp && !AdaptUp(rhs, columns, upResponse)) {
            IsValid = false;
            return false;
        }

        Extract(rhs, columns);
        IsValid = true;
        return true;
    }

    /// <summary>
    ///     R_up = dᵀ X₀⁻¹ d from the parent column, then the Sherman-Morrison update of every column for the parent
    ///     port's conductance G: z = z₀ - w (dᵀ z₀) G / 2, since 1 + G R_up = 2.
    /// </summary>
    bool AdaptUp(Span<double> z, int columns, Span<double> w) {
        var p = plus[upElement];
        var m = minus[upElement];
        for (var i = 0; i < size; i++) {
            w[i] = z[i * columns + UpColumn];
        }

        var r = Difference(w, 1, 0, p, m);
        if (!(r > 0) || double.IsInfinity(r)) {
            return false;
        }

        var g = 1 / r;
        UpResistance = r;
        for (var i = 0; i < size; i++) {
            z[i * columns + UpColumn] *= g; // the parent's Norton current is G a_up
        }

        for (var c = 0; c < columns; c++) {
            var projection = Difference(z, columns, c, p, m) * 0.5 * g;
            if (projection == 0) {
                continue;
            }

            for (var i = 0; i < size; i++) {
                z[i * columns + c] -= w[i] * projection;
            }
        }

        return true;
    }

    /// <summary>Output rows: node-voltage differences of the solution, turned into waves for the ports.</summary>
    void Extract(ReadOnlySpan<double> z, int columns) {
        for (var i = 0; i < kinds.Length; i++) {
            int row;
            var wave = false;
            switch (kinds[i]) {
                case NetlistElementKind.Port:
                    row = ordinal[i];
                    wave = true;
                    break;
                case NetlistElementKind.Up:
                    row = UpRow;
                    wave = true;
                    break;
                case NetlistElementKind.Device:
                    row = DeviceRow + ordinal[i];
                    break;
                case NetlistElementKind.Probe:
                    row = ProbeRow + ordinal[i];
                    break;
                default:
                    continue;
            }

            var target = matrix.AsSpan(row * columns, columns);
            for (var c = 0; c < columns; c++) {
                var v = Difference(z, columns, c, plus[i], minus[i]);
                target[c] = wave ? 2 * v : v;
            }

            if (wave) {
                target[row] -= 1; // b = 2 v - a: the port's own wave column is its row index
            }

            if (kinds[i] == NetlistElementKind.Up) {
                target[UpColumn] = 0; // adapted: exact zero instead of rounding noise
            }
        }
    }

    static double Difference(ReadOnlySpan<double> z, int columns, int column, int p, int m) =>
        (p > 0 ? z[(p - 1) * columns + column] : 0) - (m > 0 ? z[(m - 1) * columns + column] : 0);

    static void Conductance(Span<double> a, int n, int p, int m, double g) {
        if (p > 0) {
            a[(p - 1) * n + p - 1] += g;
        }

        if (m > 0) {
            a[(m - 1) * n + m - 1] += g;
        }

        if (p > 0 && m > 0) {
            a[(p - 1) * n + m - 1] -= g;
            a[(m - 1) * n + p - 1] -= g;
        }
    }

    static void Inject(Span<double> rhs, int columns, int p, int m, int column, double amount) {
        if (p > 0) {
            rhs[(p - 1) * columns + column] += amount;
        }

        if (m > 0) {
            rhs[(m - 1) * columns + column] -= amount;
        }
    }

    /// <summary>The MNA stamp of a branch whose current is unknown <paramref name="row" /> and whose voltage it fixes.</summary>
    static void Branch(Span<double> a, int n, int p, int m, int row) {
        if (p > 0) {
            a[(p - 1) * n + row] += 1;
            a[row * n + p - 1] += 1;
        }

        if (m > 0) {
            a[(m - 1) * n + row] -= 1;
            a[row * n + m - 1] -= 1;
        }
    }

    /// <summary>Σ row_j u_j over the length of <paramref name="u" />.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Dot(ReadOnlySpan<double> row, ReadOnlySpan<double> u) {
        var sum = 0.0;
        for (var j = 0; j < u.Length; j++) {
            sum += row[j] * u[j];
        }

        return sum;
    }
}
