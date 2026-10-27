using Cathode.Dsp.Wdf;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Cathode.Dsp.Blocks;

/// <summary>Four doubles held inline: the device-port vectors of a <see cref="CircuitRoot{TSource,TDevice}" />.</summary>
[InlineArray(4)]
struct DeviceVector {
    double element;
}

/// <summary>Four indices held inline: the pivot order of a factored device-port matrix.</summary>
[InlineArray(4)]
struct DevicePivots {
    int element;
}

/// <summary>
///     A 4 × 4 row-major matrix held inline: the device-port matrices of a <see cref="CircuitRoot{TSource,TDevice}" />.
/// </summary>
[InlineArray(16)]
struct DeviceMatrix {
    double element;
}

/// <summary>Eight doubles held inline: the child waves of a <see cref="CircuitRoot{TSource,TDevice}" />.</summary>
[InlineArray(8)]
struct PortVector {
    double element;
}

/// <summary>
///     The Newton solver state of a <see cref="CircuitRoot{TSource,TDevice}" /> with device ports: its vectors and
///     matrices, about 1 KB. A linear root (a tone network) has no device ports and shares <see cref="Inert" />, which
///     nothing writes, so it carries none of the solver's buffers.
/// </summary>
sealed class NewtonState {
    public static readonly NewtonState Inert = new();

    public DeviceVector conductances;
    public DeviceVector voltages;
    public DeviceVector history; // the solution before last, for extrapolating before there is a Jacobian
    public DeviceVector currents; // the solution's residual currents r = i - G_c v
    public DeviceVector prediction;
    public DeviceVector lastPrediction;
    public DeviceVector start;
    public DeviceVector proposed;
    public DeviceVector step;
    public DeviceVector direction;
    public DeviceVector trial;
    public DeviceVector deviceCurrents;
    public DeviceVector best;
    public DeviceVector bestCurrents;
    public DeviceVector rhs;
    public DeviceVector inverses;
    public DeviceVector lastInverses;
    public DevicePivots pivots;
    public DevicePivots lastPivots;
    public DeviceMatrix identity; // I + F G_c
    public DeviceMatrix jacobian;
    public DeviceMatrix system;
    public DeviceMatrix lastFactors; // LU of the last converged sample's Jacobian of g
}

/// <summary>
///     The root of one drive stage: an R-type adaptor (<see cref="RTypeScattering" />, from a <see cref="WdfNetlist" />)
///     whose children are the stage's source (port 0, any adapted one-port: a resistive voltage source, or one in series
///     with a guitar pickup's inductance) and up to seven capacitors (ports 1 onwards), with up to four nonlinear device
///     ports at the root (none for a linear network, <see cref="NoDevice" />). It solves what
///     <see cref="RTypeRoot{TPorts,TDevice}" /> solves, with the same damped Newton (device step limiting with a
///     constrained re-solve, a late line search, a graceful fallback), specialised for the oversampled hot loop of the
///     drive circuits.
/// </summary>
/// <remarks>
///     <para>
///         Every row of the scattering matrix is packed once per recompute into one short row of the child waves, the K
///         device currents and a constant, which holds the supply rails (the netlist's voltage sources, constant between
///         recomputes). I + F G_c, the constant part of the Newton Jacobian, is precomputed with it.
///     </para>
///     <para>
///         Newton starts from the last solution carried along the last Jacobian: the solution moves with the prediction
///         p = E u as dv = J⁻¹ dp, and the last converged sample's LU factors are kept, so the start costs one forward and
///         back substitution. On noisy input that start is far better than extrapolating in time, and most samples
///         converge at the first or second iteration.
///     </para>
///     <para>
///         The tolerances are looser than the general root's: a converged step of 10 µV leaves an error around
///         (10 µV)² / 2 n Vt, about 2 nV, far below anything audible.
///     </para>
/// </remarks>
public sealed class CircuitRoot<TSource, TDevice>
    where TSource : struct, IWdfPort
    where TDevice : struct, INonlinearDevice {
    /// <summary>Most capacitor ports a circuit root takes.</summary>
    public const int MaxCapacitors = 7;

    const int MaxIterations = 24;
    const int LineSearchAfter = 12;
    const double ShortestStep = 1.0 / 16;
    const double AcceptableNorm = 1e6;

    readonly int ports; // the source and the capacitors
    readonly int stride; // child waves, device currents, one constant
    readonly int probes;
    readonly double[] rows; // device rows, then the port rows, then the probe rows
    readonly double[] supplies;
    readonly Capacitor[] capacitors;
    TSource source;
    TDevice device;
    PortVector waves;
    readonly NewtonState newton; // the device ports' solver state (a shared, never written one without devices)
    bool hasFactors;
    long iterations;

    /// <summary>The stage input (a resistive source, perhaps behind a series adaptor): port 0 of the netlist.</summary>
    public ref TSource Source => ref source;

    /// <summary>The nonlinear devices, in netlist device-port order.</summary>
    public ref TDevice Device => ref device;

    /// <summary>Number of capacitor ports (netlist ports 1 onwards).</summary>
    public int Capacitors => capacitors.Length;

    /// <summary>The scattering matrix, for inspection.</summary>
    public RTypeScattering Scattering { get; }

    /// <summary>Convergence: every Newton step below this many volts plus <see cref="RelativeTolerance" />.</summary>
    public double AbsoluteTolerance { get; set; } = 1e-5;

    public double RelativeTolerance { get; set; } = 1e-5;

    /// <summary>Samples solved since the last <see cref="ResetStatistics" />.</summary>
    public long Samples { get; private set; }

    /// <summary>Samples that ran out of iterations and kept their best (or the previous) solution.</summary>
    public long Unconverged { get; private set; }

    public double MeanIterations => Samples == 0 ? 0 : (double)iterations / Samples;

    /// <param name="netlist">Port 0 is the source, every other port a capacitor.</param>
    /// <param name="source">The source, with its resistance.</param>
    /// <param name="capacitors">One capacitor per remaining port, in netlist order.</param>
    /// <param name="device">The nonlinear devices.</param>
    public CircuitRoot(WdfNetlist netlist, TSource source, Capacitor[] capacitors, TDevice device) {
        Scattering = new(netlist);
        if (Scattering.HasUp || Scattering.PortCount != capacitors.Length + 1 || capacitors.Length > MaxCapacitors) {
            throw new ArgumentException(
                "A circuit root has a source port, then one port per capacitor.",
                nameof(netlist)
            );
        }

        if (netlist.Count(NetlistElementKind.CurrentSource) > 0) {
            throw new ArgumentException("A circuit root takes voltage sources only (supply rails).", nameof(netlist));
        }

        var k = device.Ports;
        if (Scattering.DeviceCount != k || k > 4) {
            throw new ArgumentException(
                $"The netlist has {Scattering.DeviceCount} device ports, the devices {k}.",
                nameof(device)
            );
        }

        this.source = source;
        this.capacitors = [..capacitors];
        this.device = device;
        newton = k > 0 ? new() : NewtonState.Inert;
        ports = capacitors.Length + 1;
        stride = ports + k + 1;
        probes = Scattering.ProbeCount;
        rows = new double[(k + ports + probes) * stride];
        supplies = new double[netlist.Count(NetlistElementKind.VoltageSource)];
        for (var d = 0; d < k; d++) {
            var g = Math.Max(device.Conductance(d), 1e-15);
            newton.conductances[d] = g;
            Scattering.SetDeviceConductance(d, g);
        }

        if (!Propagate()) {
            throw new ArgumentException(
                "The network is singular at its initial component values (a floating node or a source loop).",
                nameof(netlist)
            );
        }
    }

    /// <summary>Capacitor <paramref name="index" /> (netlist port <paramref name="index" /> + 1).</summary>
    public ref Capacitor Capacitor(int index) => ref capacitors[index];

    /// <summary>Sets voltage source <paramref name="index" />, a supply rail; takes effect at <see cref="Propagate" />.</summary>
    public void SetSupply(int index, double volts) => supplies[index] = volts;

    /// <summary>Changes internal resistor <paramref name="index" />; takes effect at <see cref="Propagate" />.</summary>
    public void SetResistance(int index, double ohms) => Scattering.SetResistance(index, ohms);

    /// <summary>Changes the gain of VCVS <paramref name="index" />; takes effect at <see cref="Propagate" />.</summary>
    public void SetGain(int index, double gain) => Scattering.SetGain(index, gain);

    /// <summary>
    ///     Recomputes the scattering matrix from the component values and packs it. Allocation-free; returns false (keeping
    ///     the previous matrix) for a singular network.
    /// </summary>
    public bool Propagate() {
        var resistances = Scattering.PortResistances;
        resistances[0] = source.Propagate();
        for (var i = 0; i < capacitors.Length; i++) {
            resistances[i + 1] = capacitors[i].Propagate();
        }

        if (!Scattering.Recompute()) {
            return false;
        }

        var k = device.Ports;
        var matrix = Scattering.Matrix;
        var columns = Scattering.Columns;
        var total = k + ports + probes;
        for (var i = 0; i < total; i++) {
            var row = i < k ? Scattering.DeviceRow + i
                : i < k + ports ? i - k
                : Scattering.ProbeRow + i - k - ports;
            var from = matrix.Slice(row * columns, columns);
            var to = rows.AsSpan(i * stride, stride);
            from[..ports].CopyTo(to);
            from.Slice(Scattering.Inputs, k).CopyTo(to[ports..]);
            var constant = 0.0;
            for (var s = 0; s < supplies.Length; s++) {
                constant += from[Scattering.VoltageSourceColumn + s] * supplies[s];
            }

            to[ports + k] = constant;
        }

        for (var d = 0; d < k; d++) {
            for (var l = 0; l < k; l++) {
                newton.identity[d * k + l] = (d == l ? 1 : 0) + rows[d * stride + ports + l] * newton.conductances[l];
            }
        }

        hasFactors = false; // the old Jacobian belongs to the old network
        return true;
    }

    /// <summary>Evaluates one sample (set <see cref="Source" /> first). Returns probe 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick() {
        Span<double> u = waves;
        u[0] = source.Reflect();
        var capacitors = this.capacitors;
        for (var i = 0; i < capacitors.Length; i++) {
            u[i + 1] = capacitors[i].Reflect();
        }

        var k = device.Ports;
        var m = rows.AsSpan();
        Span<double> p = newton.prediction;
        for (var d = 0; d < k; d++) {
            p[d] = Linear(m.Slice(d * stride, stride), u);
        }

        if (k > 0) {
            Solve();
        }

        Span<double> r = newton.currents;
        source.Incident(Row(m.Slice(k * stride, stride), u, r, k));
        for (var i = 0; i < capacitors.Length; i++) {
            capacitors[i].Incident(Row(m.Slice((k + 1 + i) * stride, stride), u, r, k));
        }

        if (k > 0) {
            Span<double> v = newton.voltages;
            device.Commit(v[..k]);
        }

        return probes > 0 ? Probe(0) : 0;
    }

    /// <summary>Probed voltage <paramref name="index" /> at the last sample.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Probe(int index) {
        var k = device.Ports;
        return Row(rows.AsSpan((k + ports + index) * stride, stride), waves, newton.currents, k);
    }

    /// <summary>Voltage across device port <paramref name="port" /> at the last sample.</summary>
    public double DeviceVoltage(int port) => newton.voltages[port];

    /// <summary>
    ///     Starts from a stored operating point: the capacitor voltages, then the device port voltages, as
    ///     <see cref="Snapshot" /> wrote them.
    /// </summary>
    public void StartAt(ReadOnlySpan<double> state) {
        for (var i = 0; i < capacitors.Length; i++) {
            capacitors[i].Precharge(state[i]);
        }

        for (var d = 0; d < device.Ports; d++) {
            newton.voltages[d] = state[capacitors.Length + d];
            newton.history[d] = newton.voltages[d];
            newton.currents[d] = 0;
        }

        hasFactors = false;
    }

    /// <summary>Writes the operating point (capacitors, then devices) for <see cref="StartAt" />.</summary>
    public void Snapshot(Span<double> state) {
        for (var i = 0; i < capacitors.Length; i++) {
            state[i] = capacitors[i].Voltage;
        }

        for (var d = 0; d < device.Ports; d++) {
            state[capacitors.Length + d] = newton.voltages[d];
        }
    }

    public void Reset() {
        source.Reset();
        foreach (ref var capacitor in capacitors.AsSpan()) {
            capacitor.Reset();
        }

        device.Reset();
        if (device.Ports > 0) { // the shared inert state stays untouched (and zero)
            newton.voltages = default;
            newton.history = default;
            newton.currents = default;
            newton.lastPrediction = default;
        }

        waves = default;
        hasFactors = false;
    }

    /// <summary>
    ///     Clears a circuit that has decayed to rest at zero (an op-amp stage) before its state reaches the subnormal
    ///     range. Stages biased away from zero never qualify.
    /// </summary>
    public void FlushDenormals() {
        const double Tiny = 1e-30;
        foreach (ref var capacitor in capacitors.AsSpan()) {
            if (Math.Abs(capacitor.Reflected) >= Tiny) {
                return;
            }
        }

        for (var d = 0; d < device.Ports; d++) {
            if (Math.Abs(newton.voltages[d]) >= Tiny) {
                return;
            }
        }

        Reset();
    }

    public void ResetStatistics() {
        Samples = 0;
        iterations = 0;
        Unconverged = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    double Linear(ReadOnlySpan<double> row, ReadOnlySpan<double> u) {
        var sum = row[stride - 1];
        for (var j = 0; j < ports; j++) {
            sum += row[j] * u[j];
        }

        return sum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    double Row(ReadOnlySpan<double> row, ReadOnlySpan<double> u, ReadOnlySpan<double> r, int k) {
        var sum = Linear(row, u);
        for (var d = 0; d < k; d++) {
            sum += row[ports + d] * r[d];
        }

        return sum;
    }

    /// <summary>
    ///     Damped Newton on g(v) = v - p - F r(v); leaves the solution in the voltages and its residual currents. See the
    ///     remarks on the type and on <see cref="RTypeRoot{TPorts,TDevice}" />.
    /// </summary>
    void Solve() {
        var k = device.Ports;
        Span<double> v = newton.voltages;
        Span<double> s = newton.start;
        Span<double> next = newton.proposed;
        Span<double> delta = newton.step;
        Span<double> way = newton.direction;
        Span<double> j = newton.jacobian;
        Span<double> a = newton.system;
        Span<double> t = newton.trial;
        Span<double> gc = newton.conductances;
        Span<double> r = newton.currents;
        Span<double> negative = newton.rhs;
        Span<int> order = newton.pivots;
        Span<double> reciprocals = newton.inverses;

        Copy(v, s, k);
        Predict(s, next);
        device.Limit(s[..k], next[..k]); // a jump must not fling a junction far into conduction
        Copy(next, v, k);
        var bestNorm = double.PositiveInfinity;
        var count = 1;
        var norm = Residual(v[..k], ref bestNorm);
        while (double.IsFinite(norm)) {
            Linearise(j, a);
            Copy(delta, negative, k);
            if (!Factor(a, order, reciprocals, k)) {
                break;
            }

            Substitute(a, order, reciprocals, k, delta);
            for (var d = 0; d < k; d++) {
                next[d] = v[d] + delta[d];
            }

            device.Limit(v[..k], next[..k]);
            if (k > 1) {
                Constrain(v, delta, next);
            }

            var converged = true;
            for (var d = 0; d < k; d++) {
                delta[d] = next[d] - v[d];
                converged &= Math.Abs(delta[d]) <= AbsoluteTolerance + RelativeTolerance * Math.Abs(v[d]);
            }

            if (converged) {
                // The currents at the new voltages, to second order in the step: r + (∂i/∂v - G_c) Δ.
                for (var d = 0; d < k; d++) {
                    var sum = t[d] - gc[d] * delta[d];
                    for (var l = 0; l < k; l++) {
                        sum += j[d * k + l] * delta[l];
                    }

                    r[d] = sum;
                }

                Copy(next, v, k);
                Copy(a, newton.lastFactors, k * k); // the next sample predicts with this Jacobian
                Copy(reciprocals, newton.lastInverses, k);
                for (var d = 0; d < k; d++) {
                    newton.lastPivots[d] = order[d];
                }

                hasFactors = true;
                Record(count, true);
                return;
            }

            // Backtrack until |g| decreases, once plain Newton has had its chance (Residual overwrites the step).
            Copy(delta, way, k);
            var lambda = 1.0;
            var accepted = false;
            while (count < MaxIterations) {
                count++;
                for (var d = 0; d < k; d++) {
                    next[d] = v[d] + lambda * way[d];
                }

                var candidate = Residual(next[..k], ref bestNorm);
                var plain = count <= LineSearchAfter || lambda <= ShortestStep;
                if (candidate < norm || (plain && double.IsFinite(candidate))) {
                    Copy(next, v, k);
                    norm = candidate;
                    accepted = true;
                    break;
                }

                lambda *= 0.5;
                if (lambda < ShortestStep / 2) {
                    break;
                }
            }

            if (!accepted) {
                break;
            }
        }

        if (bestNorm <= AcceptableNorm) {
            Fallback();
        } else if (double.IsFinite(bestNorm)) {
            Copy(newton.best, v, k); // hold the previous currents; start next time from the best point
        } else {
            Copy(s, v, k); // repeat the previous sample's solution, whose currents still hold
        }

        hasFactors = false;
        Record(count, false);
    }

    /// <summary>
    ///     Newton's starting point: the last solution carried to this sample's prediction along the last Jacobian
    ///     (dv = J⁻¹ dp), or, before there is one, the last two solutions extrapolated linearly.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Predict(ReadOnlySpan<double> s, Span<double> next) {
        var k = device.Ports;
        Span<double> p = newton.prediction;
        Span<double> last = newton.lastPrediction;
        Span<double> h = newton.history;
        if (hasFactors) {
            for (var d = 0; d < k; d++) {
                next[d] = p[d] - last[d];
            }

            Substitute(newton.lastFactors, newton.lastPivots, newton.lastInverses, k, next);
            for (var d = 0; d < k; d++) {
                next[d] += s[d];
            }
        } else {
            for (var d = 0; d < k; d++) {
                next[d] = 2 * s[d] - h[d];
            }
        }

        Copy(s, h, k);
        Copy(p, last, k);
    }

    /// <summary>The Jacobian of g, I + F G_c - F ∂i/∂v, into <paramref name="a" />.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Linearise(ReadOnlySpan<double> j, Span<double> a) {
        // The devices' Jacobian is block diagonal (each device sees only its own ports): skip its structural zeros.
        var k = device.Ports;
        ref var f0 = ref MemoryMarshal.GetArrayDataReference(rows);
        ref var j0 = ref MemoryMarshal.GetReference(j);
        ref var a0 = ref MemoryMarshal.GetReference(a);
        ref var fixedPart = ref Unsafe.As<DeviceMatrix, double>(ref newton.identity);
        for (var d = 0; d < k; d++) {
            ref var f = ref Unsafe.Add(ref f0, d * stride + ports);
            for (var l = 0; l < k; l++) {
                var sum = Unsafe.Add(ref fixedPart, d * k + l);
                for (var e = 0; e < k; e++) {
                    var slope = Unsafe.Add(ref j0, e * k + l);
                    if (slope != 0) {
                        sum -= Unsafe.Add(ref f, e) * slope;
                    }
                }

                Unsafe.Add(ref a0, d * k + l) = sum;
            }
        }
    }

    /// <summary>
    ///     When the devices limited some ports' steps, re-solves the linearised system for the other ports with the
    ///     limited ones held, so a limited junction cannot leave its neighbours stepping as if it had moved all the way.
    /// </summary>
    void Constrain(ReadOnlySpan<double> v, ReadOnlySpan<double> unlimited, Span<double> next) {
        var k = device.Ports;
        DevicePivots freeBuffer = default;
        Span<int> free = freeBuffer;
        var count = 0;
#pragma warning disable SK2003 // Exact by design: the limiter leaves a free port's step bit for bit, so equality is "not limited".
        for (var d = 0; d < k; d++) {
            if (next[d] == v[d] + unlimited[d]) {
                free[count++] = d;
            }
        }

        if (count == k || count == 0) {
            return;
        }

        DeviceMatrix fullBuffer = default;
        DeviceMatrix subsystem = default; // the factors in the system buffer must survive for the next prediction
        Span<double> full = fullBuffer;
        Linearise(newton.jacobian, full);
        Span<double> a = subsystem;
        Span<double> b = newton.direction;
        ReadOnlySpan<double> negative = newton.rhs;
        for (var i = 0; i < count; i++) {
            var row = free[i];
            var sum = negative[row];
            for (var d = 0; d < k; d++) {
                if (next[d] != v[d] + unlimited[d]) {
#pragma warning restore SK2003
                    sum -= full[row * k + d] * (next[d] - v[d]);
                }
            }

            b[i] = sum;
            for (var c = 0; c < count; c++) {
                a[i * count + c] = full[row * k + free[c]];
            }
        }

        DevicePivots orderBuffer = default;
        DeviceVector reciprocalBuffer = default;
        Span<int> order = orderBuffer;
        Span<double> reciprocals = reciprocalBuffer;
        if (!Factor(a, order, reciprocals, count)) {
            return;
        }

        Substitute(a, order, reciprocals, count, b);
        for (var i = 0; i < count; i++) {
            next[free[i]] = v[free[i]] + b[i];
        }

        device.Limit(v[..k], next[..k]);
    }

    /// <summary>
    ///     Newton gave up: one more linearisation at the best point found, solved without limiting, which satisfies the
    ///     network exactly with the devices replaced by their tangents there.
    /// </summary>
    void Fallback() {
        var k = device.Ports;
        Span<double> v = newton.voltages;
        Span<double> r = newton.currents;
        Span<double> delta = newton.step;
        Span<double> j = newton.jacobian;
        Span<double> a = newton.system;
        Span<double> t = newton.trial;
        Span<double> gc = newton.conductances;
        Span<double> next = newton.proposed;
        Span<int> order = newton.pivots;
        Span<double> reciprocals = newton.inverses;
        Copy(newton.best, v, k);
        Copy(newton.bestCurrents, r, k);
        var ignored = double.PositiveInfinity;
        Residual(v[..k], ref ignored);
        Linearise(j, a);
        if (!Factor(a, order, reciprocals, k)) {
            return;
        }

        Substitute(a, order, reciprocals, k, delta);
        for (var d = 0; d < k; d++) {
            var sum = t[d] - gc[d] * delta[d];
            for (var l = 0; l < k; l++) {
                sum += j[d * k + l] * delta[l];
            }

            if (!double.IsFinite(sum) || !double.IsFinite(delta[d])) {
                return;
            }

            next[d] = sum;
        }

        for (var d = 0; d < k; d++) {
            r[d] = next[d];
            v[d] += delta[d];
        }
    }

    /// <summary>
    ///     Evaluates the devices at <paramref name="v" />: currents, Jacobian, residual currents r = i - G_c v, and -g into
    ///     the step. Returns |g|² scaled by the tolerances, and remembers the best point seen.
    /// </summary>
    double Residual(ReadOnlySpan<double> v, ref double bestNorm) {
        var k = device.Ports;
        Span<double> j = newton.jacobian;
        Span<double> i = newton.deviceCurrents;
        j[..(k * k)].Clear();
        device.Evaluate(v, i[..k], j, k);
        ref var v0 = ref MemoryMarshal.GetReference(v);
        ref var i0 = ref Unsafe.As<DeviceVector, double>(ref newton.deviceCurrents);
        ref var t0 = ref Unsafe.As<DeviceVector, double>(ref newton.trial);
        ref var gc0 = ref Unsafe.As<DeviceVector, double>(ref newton.conductances);
        ref var p0 = ref Unsafe.As<DeviceVector, double>(ref newton.prediction);
        ref var delta0 = ref Unsafe.As<DeviceVector, double>(ref newton.step);
        ref var f0 = ref MemoryMarshal.GetArrayDataReference(rows);
        for (var d = 0; d < k; d++) {
            Unsafe.Add(ref t0, d) = Unsafe.Add(ref i0, d) - Unsafe.Add(ref gc0, d) * Unsafe.Add(ref v0, d);
        }

        var norm = 0.0;
        var absolute = AbsoluteTolerance;
        var relative = RelativeTolerance;
        for (var d = 0; d < k; d++) {
            ref var f = ref Unsafe.Add(ref f0, d * stride + ports);
            var vd = Unsafe.Add(ref v0, d);
            var g = vd - Unsafe.Add(ref p0, d);
            for (var e = 0; e < k; e++) {
                g -= Unsafe.Add(ref f, e) * Unsafe.Add(ref t0, e);
            }

            Unsafe.Add(ref delta0, d) = -g;
            var scaled = g / (absolute + relative * Math.Abs(vd));
            norm += scaled * scaled;
        }

        Span<double> t = newton.trial;

        if (norm < bestNorm) {
            bestNorm = norm;
            Copy(v, newton.best, k);
            Copy(t, newton.bestCurrents, k);
        }

        return norm;
    }

    void Record(int count, bool converged) {
        Samples++;
        iterations += count;
        if (!converged) {
            Unconverged++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Copy(ReadOnlySpan<double> from, Span<double> to, int count) {
        for (var i = 0; i < count; i++) {
            to[i] = from[i];
        }
    }

    /// <summary>
    ///     LU factorisation in place with partial pivoting (whole rows swapped, as LAPACK's getrf): <paramref name="order" />
    ///     records the swaps and <paramref name="reciprocals" /> the inverted pivots, so <see cref="Substitute" /> needs no
    ///     division.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool Factor(Span<double> a, Span<int> order, Span<double> reciprocals, int k) {
        if (a.Length < k * k || order.Length < k || reciprocals.Length < k) {
            return false;
        }

        ref var a0 = ref MemoryMarshal.GetReference(a);
        for (var c = 0; c < k; c++) {
            var pivot = c;
            var largest = Math.Abs(Unsafe.Add(ref a0, c * k + c));
            for (var row = c + 1; row < k; row++) {
                var size = Math.Abs(Unsafe.Add(ref a0, row * k + c));
                if (size > largest) {
                    largest = size;
                    pivot = row;
                }
            }

            if (!(largest > 1e-300)) {
                return false;
            }

            order[c] = pivot;
            if (pivot != c) {
                for (var l = 0; l < k; l++) {
                    ref var upper = ref Unsafe.Add(ref a0, c * k + l);
                    ref var lower = ref Unsafe.Add(ref a0, pivot * k + l);
                    (upper, lower) = (lower, upper);
                }
            }

            var inverse = 1 / Unsafe.Add(ref a0, c * k + c);
            reciprocals[c] = inverse;
            ref var pivotRow = ref Unsafe.Add(ref a0, c * k);
            for (var row = c + 1; row < k; row++) {
                DenseLu.EliminateRow(ref Unsafe.Add(ref a0, row * k), ref pivotRow, inverse, c, k);
            }
        }

        return true;
    }

    /// <summary>Solves with the factors of <see cref="Factor" />, in place in <paramref name="b" />.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Substitute(
        ReadOnlySpan<double> lu,
        ReadOnlySpan<int> order,
        ReadOnlySpan<double> reciprocals,
        int k,
        Span<double> b
    ) {
        if (lu.Length < k * k || order.Length < k || reciprocals.Length < k || b.Length < k) {
            return;
        }

        ref var b0 = ref MemoryMarshal.GetReference(b);
        ref var lu0 = ref MemoryMarshal.GetReference(lu);
        for (var c = 0; c < k; c++) {
            var pivot = order[c];
            if (pivot != c) {
                ref var upper = ref Unsafe.Add(ref b0, c);
                ref var lower = ref Unsafe.Add(ref b0, pivot);
                (upper, lower) = (lower, upper);
            }
        }

        for (var c = 0; c < k; c++) {
            var value = Unsafe.Add(ref b0, c);
            for (var row = c + 1; row < k; row++) {
                Unsafe.Add(ref b0, row) -= Unsafe.Add(ref lu0, row * k + c) * value;
            }
        }

        for (var row = k - 1; row >= 0; row--) {
            var sum = Unsafe.Add(ref b0, row);
            for (var l = row + 1; l < k; l++) {
                sum -= Unsafe.Add(ref lu0, row * k + l) * Unsafe.Add(ref b0, l);
            }

            Unsafe.Add(ref b0, row) = sum * reciprocals[row];
        }
    }
}
