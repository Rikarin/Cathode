using System.Numerics;
using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Tests.Circuits;

/// <summary>
///     A circuit for <see cref="ReferenceSimulator" />: a plain SPICE-like element list, independent of the wave digital
///     filter code (only the device models are shared, through <see cref="INonlinearDevice" />).
/// </summary>
sealed class ReferenceCircuit {
    public const int Ground = 0;

    int nodes;

    public int Nodes => nodes;

    public List<(int A, int B, double Ohms)> Resistors { get; } = [];

    public List<(int A, int B, double Farads)> Capacitors { get; } = [];

    public List<(int A, int B, double Henries)> Inductors { get; } = [];

    /// <summary>Ideal transformers: each a list of windings (plus, minus, turns), all on one core.</summary>
    public List<(int Plus, int Minus, double Turns)[]> Transformers { get; } = [];

    /// <summary>MNA unknowns added by the transformers: one winding current each.</summary>
    public int Windings => Transformers.Sum(static t => t.Length);

    public List<(int Plus, int Minus, Func<double, double> Volts)> Sources { get; } = [];

    public List<(int Plus, int Minus, int ControlPlus, int ControlMinus, double Gain)> Controlled { get; } = [];

    public List<(INonlinearDevice Device, (int Plus, int Minus)[] Ports)> Devices { get; } = [];

    public int Node() => ++nodes;

    public void Resistor(int a, int b, double ohms) => Resistors.Add((a, b, ohms));

    public void Capacitor(int a, int b, double farads) => Capacitors.Add((a, b, farads));

    /// <summary>An inductor, current flowing from <paramref name="a" /> to <paramref name="b" />: an MNA branch of its own.</summary>
    public void Inductor(int a, int b, double henries) => Inductors.Add((a, b, henries));

    /// <summary>
    ///     An ideal transformer: every winding has the same volts per turn, and the ampere-turns sum to zero. Each
    ///     winding's current flows in at its plus node.
    /// </summary>
    public void Transformer(params (int Plus, int Minus, double Turns)[] windings) => Transformers.Add(windings);

    public void Source(int plus, int minus, Func<double, double> volts) => Sources.Add((plus, minus, volts));

    public void Vcvs(int plus, int minus, int controlPlus, int controlMinus, double gain) =>
        Controlled.Add((plus, minus, controlPlus, controlMinus, gain));

    /// <summary>A device, boxed: the reference keeps and commits its own copy (op-amps have state).</summary>
    public void Device(INonlinearDevice device, params (int Plus, int Minus)[] ports) {
        if (ports.Length != device.Ports) {
            throw new ArgumentException("port count");
        }

        Devices.Add((device, ports));
    }
}

/// <summary>
///     A small dense modified-nodal-analysis circuit simulator, the reference for the wave digital filter tests: an AC
///     solve for linear circuits, a DC operating point (capacitors open, source stepping) and a transient analysis with
///     the trapezoidal rule and a full Newton solve at every step, meant to run at many times the WDF's rate.
/// </summary>
sealed class ReferenceSimulator {
    const double Gmin = 1e-15;

    readonly ReferenceCircuit circuit;
    readonly int size;
    readonly double[] x;
    readonly double[] capacitorCurrents;
    readonly double[] inductorCurrents;
    readonly double[] inductorVoltages;
    readonly double[][] evaluation; // per device: the (limited) port voltages its next evaluation uses
    int newtonFailures;
    int mostIterations;

    public int NewtonFailures => newtonFailures;

    public int MostIterations => mostIterations;

    public ReferenceSimulator(ReferenceCircuit circuit) {
        this.circuit = circuit;
        size = circuit.Nodes + circuit.Sources.Count + circuit.Controlled.Count + circuit.Inductors.Count + circuit.Windings;
        x = new double[size];
        capacitorCurrents = new double[circuit.Capacitors.Count];
        inductorCurrents = new double[circuit.Inductors.Count];
        inductorVoltages = new double[circuit.Inductors.Count];
        evaluation = [..circuit.Devices.Select(static d => new double[d.Ports.Length])];
    }

    /// <summary>Node voltage (ground is 0).</summary>
    public double Voltage(int node) => node == 0 ? 0 : x[node - 1];

    /// <summary>Current through inductor <paramref name="index" />, from its first node to its second.</summary>
    public double InductorCurrent(int index) => x[InductorRow(index)];

    /// <summary>Solves the DC operating point at time 0 (capacitors open) and makes it the transient's initial state.</summary>
    public void OperatingPoint(int steps = 20) {
        Array.Clear(x);
        Array.Clear(capacitorCurrents);
        for (var s = 1; s <= steps; s++) {
            Newton(0, 0, (double)s / steps, transient: false);
        }

        SyncDevices();
        SyncInductors();
    }

    /// <summary>Starts the transient from all-zero state (every capacitor discharged).</summary>
    public void ZeroState() {
        Array.Clear(x);
        Array.Clear(capacitorCurrents);
        SyncDevices();
        SyncInductors();
    }

    /// <summary>Advances from <paramref name="time" /> - <paramref name="step" /> to <paramref name="time" />.</summary>
    public void Step(double time, double step) {
        var previous = new double[circuit.Capacitors.Count];
        for (var i = 0; i < previous.Length; i++) {
            var (a, b, _) = circuit.Capacitors[i];
            previous[i] = Voltage(a) - Voltage(b);
        }

        Newton(time, step, 1, transient: true, previous);
        for (var i = 0; i < previous.Length; i++) {
            var (a, b, c) = circuit.Capacitors[i];
            var g = 2 * c / step;
            capacitorCurrents[i] = g * (Voltage(a) - Voltage(b) - previous[i]) - capacitorCurrents[i];
        }

        for (var d = 0; d < circuit.Devices.Count; d++) {
            var (device, ports) = circuit.Devices[d];
            device.Commit(PortVoltages(ports));
        }

        SyncInductors();
    }

    /// <summary>
    ///     The response V(outPlus) - V(outMinus) to a unit amplitude on source <paramref name="input" />. With
    ///     <paramref name="linearised" />, the devices take part as their small-signal conductances at the last solution
    ///     (an operating point, say); otherwise they are left out.
    /// </summary>
    public Complex Ac(Complex s, int input, int outPlus, int outMinus = 0, bool linearised = false) {
        var a = new Complex[size, size];
        var rhs = new Complex[size];
        for (var n = 0; n < circuit.Nodes; n++) {
            a[n, n] += Gmin;
        }

        foreach (var (p, m, r) in circuit.Resistors) {
            Stamp(a, p, m, 1 / r);
        }

        foreach (var (p, m, c) in circuit.Capacitors) {
            Stamp(a, p, m, s * c);
        }

        for (var j = 0; j < circuit.Sources.Count; j++) {
            var (p, m, _) = circuit.Sources[j];
            var row = circuit.Nodes + j;
            Branch(a, p, m, row);
            rhs[row] = j == input ? 1 : 0;
        }

        for (var j = 0; j < circuit.Controlled.Count; j++) {
            var (p, m, cp, cm, gain) = circuit.Controlled[j];
            var row = circuit.Nodes + circuit.Sources.Count + j;
            Branch(a, p, m, row);
            if (cp > 0) {
                a[row, cp - 1] -= gain;
            }

            if (cm > 0) {
                a[row, cm - 1] += gain;
            }
        }

        for (var j = 0; j < circuit.Inductors.Count; j++) {
            var (p, m, l) = circuit.Inductors[j];
            var row = InductorRow(j);
            Branch(a, p, m, row);
            a[row, row] -= s * l;
        }

        StampTransformers(a);

        if (linearised) {
            for (var d = 0; d < circuit.Devices.Count; d++) {
                var (device, ports) = circuit.Devices[d];
                var k = ports.Length;
                var currents = new double[k];
                var jacobian = new double[k * k];
                device.Evaluate(PortVoltages(ports), currents, jacobian, k);
                for (var r = 0; r < k; r++) {
                    for (var c = 0; c < k; c++) {
                        StampTransconductance(a, ports[r], ports[c], jacobian[r * k + c]);
                    }
                }
            }
        }

        var solution = SolveComplex(a, rhs);
        return (outPlus > 0 ? solution[outPlus - 1] : 0) - (outMinus > 0 ? solution[outMinus - 1] : 0);
    }

    void Newton(double time, double step, double scale, bool transient, double[]? previous = null) {
        var a = new double[size, size];
        var rhs = new double[size];
        var last = new double[size];
        for (var iteration = 1; iteration <= 200; iteration++) {
            Array.Clear(a);
            Array.Clear(rhs);
            for (var n = 0; n < circuit.Nodes; n++) {
                a[n, n] += Gmin;
            }

            foreach (var (p, m, r) in circuit.Resistors) {
                Stamp(a, p, m, 1 / r);
            }

            if (transient) {
                // Trapezoidal companion: i = G (v - v_prev) - i_prev, G = 2C / h.
                for (var i = 0; i < circuit.Capacitors.Count; i++) {
                    var (p, m, c) = circuit.Capacitors[i];
                    var g = 2 * c / step;
                    Stamp(a, p, m, g);
                    Inject(rhs, p, m, g * previous![i] + capacitorCurrents[i]);
                }
            }

            for (var j = 0; j < circuit.Sources.Count; j++) {
                var (p, m, volts) = circuit.Sources[j];
                var row = circuit.Nodes + j;
                Branch(a, p, m, row);
                rhs[row] = scale * volts(time);
            }

            for (var j = 0; j < circuit.Controlled.Count; j++) {
                var (p, m, cp, cm, gain) = circuit.Controlled[j];
                var row = circuit.Nodes + circuit.Sources.Count + j;
                Branch(a, p, m, row);
                if (cp > 0) {
                    a[row, cp - 1] -= gain;
                }

                if (cm > 0) {
                    a[row, cm - 1] += gain;
                }
            }

            // Inductors: a short at DC; trapezoidal in the transient, v + v_prev = (2L / h)(i - i_prev).
            for (var j = 0; j < circuit.Inductors.Count; j++) {
                var (p, m, l) = circuit.Inductors[j];
                var row = InductorRow(j);
                Branch(a, p, m, row);
                if (transient) {
                    var r = 2 * l / step;
                    a[row, row] -= r;
                    rhs[row] = -inductorVoltages[j] - r * inductorCurrents[j];
                }
            }

            // Devices linearised at their evaluation voltages: i ≈ i₀ + J (v - v₀).
            for (var d = 0; d < circuit.Devices.Count; d++) {
                var (device, ports) = circuit.Devices[d];
                var k = ports.Length;
                var v0 = evaluation[d];
                var i0 = new double[k];
                var jacobian = new double[k * k];
                device.Evaluate(v0, i0, jacobian, k);
                for (var r = 0; r < k; r++) {
                    var constant = i0[r];
                    for (var c = 0; c < k; c++) {
                        constant -= jacobian[r * k + c] * v0[c];
                        StampTransconductance(a, ports[r], ports[c], jacobian[r * k + c]);
                    }

                    Inject(rhs, ports[r].Plus, ports[r].Minus, -constant);
                }
            }

            StampTransformers(a);

            Array.Copy(x, last, size);
            var solution = Solve(a, rhs);
            Array.Copy(solution, x, size);

            var limited = false;
            for (var d = 0; d < circuit.Devices.Count; d++) {
                var (device, ports) = circuit.Devices[d];
                var proposed = PortVoltages(ports);
                var before = (double[])proposed.Clone();
                device.Limit(evaluation[d], proposed);
                limited |= !before.AsSpan().SequenceEqual(proposed);
                proposed.CopyTo(evaluation[d], 0);
            }

            var change = 0.0;
            for (var i = 0; i < size; i++) {
                change = Math.Max(change, Math.Abs(x[i] - last[i]) / (1e-9 + 1e-9 * Math.Abs(x[i])));
            }

            if (!limited && change <= 1 && iteration > 1) {
                mostIterations = Math.Max(mostIterations, iteration);
                return;
            }
        }

        newtonFailures++;
    }

    double[] PortVoltages((int Plus, int Minus)[] ports) {
        var v = new double[ports.Length];
        for (var i = 0; i < ports.Length; i++) {
            v[i] = Voltage(ports[i].Plus) - Voltage(ports[i].Minus);
        }

        return v;
    }

    /// <summary>
    ///     Ideal transformers: a current unknown per winding; one row makes the ampere-turns sum to zero, the others make
    ///     each winding's volts per turn equal the first's.
    /// </summary>
    void StampTransformers(double[,] a) {
        var row = circuit.Nodes + circuit.Sources.Count + circuit.Controlled.Count + circuit.Inductors.Count;
        foreach (var windings in circuit.Transformers) {
            var (p0, m0, n0) = windings[0];
            for (var k = 0; k < windings.Length; k++) {
                var (p, m, n) = windings[k];
                var current = row + k;
                if (p > 0) {
                    a[p - 1, current] += 1;
                }

                if (m > 0) {
                    a[m - 1, current] -= 1;
                }

                a[row, current] += n; // Σ N_k i_k = 0
                if (k == 0) {
                    continue;
                }

                // N_0 v_k - N_k v_0 = 0
                if (p > 0) {
                    a[current, p - 1] += n0;
                }

                if (m > 0) {
                    a[current, m - 1] -= n0;
                }

                if (p0 > 0) {
                    a[current, p0 - 1] -= n;
                }

                if (m0 > 0) {
                    a[current, m0 - 1] += n;
                }
            }

            row += windings.Length;
        }
    }

    void StampTransformers(Complex[,] a) {
        var real = new double[size, size];
        StampTransformers(real);
        for (var i = 0; i < size; i++) {
            for (var j = 0; j < size; j++) {
                a[i, j] += real[i, j];
            }
        }
    }

    int InductorRow(int index) => circuit.Nodes + circuit.Sources.Count + circuit.Controlled.Count + index;

    void SyncInductors() {
        for (var j = 0; j < circuit.Inductors.Count; j++) {
            var (p, m, _) = circuit.Inductors[j];
            inductorCurrents[j] = x[InductorRow(j)];
            inductorVoltages[j] = Voltage(p) - Voltage(m);
        }
    }

    void SyncDevices() {
        for (var d = 0; d < circuit.Devices.Count; d++) {
            PortVoltages(circuit.Devices[d].Ports).CopyTo(evaluation[d], 0);
        }
    }

    static void Stamp(double[,] a, int p, int m, double g) {
        if (p > 0) {
            a[p - 1, p - 1] += g;
        }

        if (m > 0) {
            a[m - 1, m - 1] += g;
        }

        if (p > 0 && m > 0) {
            a[p - 1, m - 1] -= g;
            a[m - 1, p - 1] -= g;
        }
    }

    static void StampTransconductance(Complex[,] a, (int Plus, int Minus) row, (int Plus, int Minus) column, double g) {
        void Add(int r, int c, double value) {
            if (r > 0 && c > 0) {
                a[r - 1, c - 1] += value;
            }
        }

        Add(row.Plus, column.Plus, g);
        Add(row.Plus, column.Minus, -g);
        Add(row.Minus, column.Plus, -g);
        Add(row.Minus, column.Minus, g);
    }

    static void Stamp(Complex[,] a, int p, int m, Complex g) {
        if (p > 0) {
            a[p - 1, p - 1] += g;
        }

        if (m > 0) {
            a[m - 1, m - 1] += g;
        }

        if (p > 0 && m > 0) {
            a[p - 1, m - 1] -= g;
            a[m - 1, p - 1] -= g;
        }
    }

    /// <summary>A current from row port's plus to minus node, controlled by the column port's voltage.</summary>
    static void StampTransconductance(double[,] a, (int Plus, int Minus) row, (int Plus, int Minus) column, double g) {
        void Add(int r, int c, double value) {
            if (r > 0 && c > 0) {
                a[r - 1, c - 1] += value;
            }
        }

        Add(row.Plus, column.Plus, g);
        Add(row.Plus, column.Minus, -g);
        Add(row.Minus, column.Plus, -g);
        Add(row.Minus, column.Minus, g);
    }

    static void Inject(double[] rhs, int p, int m, double current) {
        if (p > 0) {
            rhs[p - 1] += current;
        }

        if (m > 0) {
            rhs[m - 1] -= current;
        }
    }

    static void Branch(double[,] a, int p, int m, int row) {
        if (p > 0) {
            a[p - 1, row] += 1;
            a[row, p - 1] += 1;
        }

        if (m > 0) {
            a[m - 1, row] -= 1;
            a[row, m - 1] -= 1;
        }
    }

    static void Branch(Complex[,] a, int p, int m, int row) {
        if (p > 0) {
            a[p - 1, row] += 1;
            a[row, p - 1] += 1;
        }

        if (m > 0) {
            a[m - 1, row] -= 1;
            a[row, m - 1] -= 1;
        }
    }

    /// <summary>Gaussian elimination with partial pivoting.</summary>
    static double[] Solve(double[,] a, double[] b) {
        var n = b.Length;
        var m = (double[,])a.Clone();
        var y = (double[])b.Clone();
        for (var k = 0; k < n; k++) {
            var pivot = k;
            for (var i = k + 1; i < n; i++) {
                if (Math.Abs(m[i, k]) > Math.Abs(m[pivot, k])) {
                    pivot = i;
                }
            }

            if (pivot != k) {
                for (var j = 0; j < n; j++) {
                    (m[k, j], m[pivot, j]) = (m[pivot, j], m[k, j]);
                }

                (y[k], y[pivot]) = (y[pivot], y[k]);
            }

            for (var i = k + 1; i < n; i++) {
                var f = m[i, k] / m[k, k];
                if (f == 0) {
                    continue;
                }

                for (var j = k; j < n; j++) {
                    m[i, j] -= f * m[k, j];
                }

                y[i] -= f * y[k];
            }
        }

        for (var i = n - 1; i >= 0; i--) {
            var sum = y[i];
            for (var j = i + 1; j < n; j++) {
                sum -= m[i, j] * y[j];
            }

            y[i] = sum / m[i, i];
        }

        return y;
    }

    static Complex[] SolveComplex(Complex[,] a, Complex[] b) {
        var n = b.Length;
        for (var k = 0; k < n; k++) {
            var pivot = k;
            for (var i = k + 1; i < n; i++) {
                if (a[i, k].Magnitude > a[pivot, k].Magnitude) {
                    pivot = i;
                }
            }

            if (pivot != k) {
                for (var j = 0; j < n; j++) {
                    (a[k, j], a[pivot, j]) = (a[pivot, j], a[k, j]);
                }

                (b[k], b[pivot]) = (b[pivot], b[k]);
            }

            for (var i = k + 1; i < n; i++) {
                var f = a[i, k] / a[k, k];
                for (var j = k; j < n; j++) {
                    a[i, j] -= f * a[k, j];
                }

                b[i] -= f * b[k];
            }
        }

        var y = new Complex[n];
        for (var i = n - 1; i >= 0; i--) {
            var sum = b[i];
            for (var j = i + 1; j < n; j++) {
                sum -= a[i, j] * y[j];
            }

            y[i] = sum / a[i, i];
        }

        return y;
    }
}
