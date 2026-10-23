using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Wdf;

/// <summary>
///     The root of a wave digital filter whose top is an R-type adaptor holding K nonlinear device ports, after Werner,
///     Nangia, Smith and Abel, "Resolving wave digital filters with multiple/multiport nonlinearities" (DAFx-15). Use
///     <see cref="NoDevice" /> for a linear circuit: the root then only scatters. A complete circuit: set the sources, call
///     <see cref="Tick" /> once per sample, read <see cref="Probe" /> and the children's voltages and currents.
/// </summary>
/// <remarks>
///     <para>
///         The subtrees' waves a and the source values form the input u. The adaptor's MNA (see
///         <see cref="RTypeScattering" />) gives the device port voltages as v = E u + F r, where r are the currents the
///         devices draw (less their companion conductance, r = i(v) - G_c v) and F is the K × K impedance the network
///         presents to them. Each sample solves the K equations g(v) = v - E u - F r(v) = 0 by Newton's method with
///         J = I - F (∂i/∂v - G_c): the delay-free loop is confined to K unknowns instead of the whole network, so an
///         iteration costs one device evaluation and a K × K solve (closed form up to K = 2). Then b = S u + N r goes
///         back down the tree.
///     </para>
///     <para>
///         Newton starts from the last two solutions extrapolated linearly, which on oversampled audio leaves about two
///         iterations per sample (the second only confirms the first). Each step is limited by the devices
///         (<see cref="INonlinearDevice.Limit" />: SPICE-style junction limiting, tube plates that may only halve, an
///         op-amp that may not jump across its linear window), and when some ports were limited the others are re-solved
///         with those held, so the step stays consistent. Samples still struggling after
///         <see cref="NewtonSolver.LineSearchAfter" /> iterations must also reduce |g| (a backtracking line search). On
///         convergence the currents come from the last linearisation, r + J Δ, exact to second order in the final step,
///         which saves a device evaluation.
///     </para>
///     <para>
///         If Newton has not converged after <see cref="NewtonSolver.MaxIterations" />, the sample is counted in
///         <see cref="NewtonSolver.Unconverged" />: a nearly converged best iterate is linearised once more and used;
///         otherwise the previous sample's currents are held (bounded, where a wild iterate's might not be) and the next
///         sample starts from the best point. If the devices return a non-finite value, the previous solution is repeated
///         and the sample counted in <see cref="NewtonSolver.NonFinite" />. Either way the output stays finite.
///     </para>
/// </remarks>
public struct RTypeRoot<TPorts, TDevice>
    where TPorts : struct, IWdfPortList
    where TDevice : struct, INonlinearDevice {
    readonly RTypeScattering scattering;
    readonly NewtonSolver newton;
    readonly double[] inputs;
    readonly double[] outputs;

    /// <summary>The subtrees, in netlist port order.</summary>
    public TPorts Ports;

    /// <summary>The nonlinear devices, in netlist device-port order.</summary>
    public TDevice Device;

    /// <summary>The matrix and its structure, for inspection.</summary>
    public readonly RTypeScattering Scattering => scattering;

    /// <summary>Newton settings and statistics.</summary>
    public readonly NewtonSolver Newton => newton;

    public RTypeRoot(WdfNetlist netlist, TPorts ports, TDevice device) {
        scattering = new(netlist);
        if (scattering.HasUp) {
            throw new ArgumentException("A root has no Up port.", nameof(netlist));
        }

        if (scattering.PortCount != ports.Count) {
            throw new ArgumentException(
                $"The netlist has {scattering.PortCount} ports, the port list {ports.Count}.",
                nameof(ports)
            );
        }

        if (scattering.DeviceCount != device.Ports) {
            throw new ArgumentException(
                $"The netlist has {scattering.DeviceCount} device ports, the devices {device.Ports}.",
                nameof(device)
            );
        }

        Ports = ports;
        Device = device;
        for (var k = 0; k < device.Ports; k++) {
            scattering.SetDeviceConductance(k, Math.Max(device.Conductance(k), 1e-15));
        }

        newton = new(device.Ports);
        for (var k = 0; k < device.Ports; k++) {
            newton.conductances[k] = Math.Max(device.Conductance(k), 1e-15);
        }

        inputs = new double[scattering.Inputs];
        outputs = new double[scattering.PortCount];
        Propagate();
    }

    /// <summary>Recomputes the port resistances and the scattering matrix after a component change. Allocation-free.</summary>
    public void Propagate() {
        Ports.Propagate(scattering.PortResistances);
        scattering.Recompute();
    }

    /// <summary>Sets ideal voltage source <paramref name="index" /> (in volts) for the coming samples.</summary>
    public readonly void SetVoltage(int index, double volts) => inputs[scattering.VoltageSourceColumn + index] = volts;

    /// <summary>Sets ideal current source <paramref name="index" /> (in amperes) for the coming samples.</summary>
    public readonly void SetCurrent(int index, double amps) => inputs[scattering.CurrentSourceColumn + index] = amps;

    /// <summary>Changes internal resistor <paramref name="index" />; call <see cref="Propagate" /> afterwards.</summary>
    public readonly void SetResistance(int index, double ohms) => scattering.SetResistance(index, ohms);

    /// <summary>Changes the gain of VCVS <paramref name="index" />; call <see cref="Propagate" /> afterwards.</summary>
    public readonly void SetGain(int index, double gain) => scattering.SetGain(index, gain);

    /// <summary>Evaluates one sample. Returns probe 0 (or 0 when the netlist has no probe).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick() {
        var u = inputs.AsSpan();
        Ports.Reflect(u);
        var waves = outputs.AsSpan();
        var k = Device.Ports;
        if (k == 0) {
            for (var j = 0; j < waves.Length; j++) {
                waves[j] = RTypeScattering.Dot(scattering.Row(j), u);
            }
        } else {
            var p = newton.prediction.AsSpan(0, k);
            for (var d = 0; d < k; d++) {
                p[d] = RTypeScattering.Dot(scattering.Row(scattering.DeviceRow + d), u);
            }

            Solve(k);
            var r = newton.currents.AsSpan(0, k);
            for (var j = 0; j < waves.Length; j++) {
                var row = scattering.Row(j);
                waves[j] = RTypeScattering.Dot(row, u) + RTypeScattering.Dot(row[u.Length..], r);
            }

            Device.Commit(newton.voltages.AsSpan(0, k));
        }

        Ports.Incident(waves);
        return scattering.ProbeCount > 0 ? Probe(0) : 0;
    }

    /// <summary>Probed voltage <paramref name="index" /> at the last sample.</summary>
    public readonly double Probe(int index) {
        var row = scattering.Row(scattering.ProbeRow + index);
        var u = inputs.AsSpan();
        return RTypeScattering.Dot(row, u) + RTypeScattering.Dot(row[u.Length..], newton.currents.AsSpan(0, Device.Ports));
    }

    /// <summary>Voltage across device port <paramref name="port" /> at the last sample.</summary>
    public readonly double DeviceVoltage(int port) => newton.voltages[port];

    /// <summary>Current into device port <paramref name="port" /> at the last sample.</summary>
    public readonly double DeviceCurrent(int port) =>
        newton.currents[port] + newton.conductances[port] * newton.voltages[port];

    /// <summary>
    ///     Sets the device port voltages Newton starts the next sample from, e.g. a known operating point, so that a
    ///     circuit switched on at full supply does not have to find it from zero.
    /// </summary>
    public readonly void SetInitialVoltages(ReadOnlySpan<double> voltages) {
        voltages.CopyTo(newton.voltages);
        voltages.CopyTo(newton.history);
    }

    public void Reset() {
        Ports.Reset();
        Device.Reset();
        newton.voltages.AsSpan().Clear();
        newton.history.AsSpan().Clear();
        newton.currents.AsSpan().Clear();
        inputs.AsSpan(0, scattering.VoltageSourceColumn).Clear(); // keep the source values
    }

    /// <summary>
    ///     Damped Newton on g(v) = v - p - F r(v), from the previous solution extrapolated linearly; leaves the solution in
    ///     the voltages and its residual currents r. See the remarks on the type.
    /// </summary>
    void Solve(int k) {
        const double ShortestStep = 1.0 / 16;
        var s = newton;
        var v = s.voltages.AsSpan(0, k);
        var r = s.currents.AsSpan(0, k);
        var current = s.deviceCurrents.AsSpan(0, k);
        var trial = s.residual.AsSpan(0, k);
        var gc = s.conductances.AsSpan(0, k);
        var jacobian = s.jacobian.AsSpan(0, k * k);
        var system = s.system.AsSpan(0, k * k);
        var step = s.step.AsSpan(0, k);
        var proposed = s.proposed.AsSpan(0, k);
        var start = s.start.AsSpan(0, k);
        var direction = s.direction.AsSpan(0, k);
        var history = s.history.AsSpan(0, k);
        var best = s.best.AsSpan(0, k);
        var bestCurrents = s.bestCurrents.AsSpan(0, k);
        var f = s.impedance.AsSpan(0, k * k);
        var matrix = scattering.Matrix;
        for (var d = 0; d < k; d++) {
            matrix.Slice((scattering.DeviceRow + d) * scattering.Columns + scattering.Inputs, k).CopyTo(f[(d * k)..]);
        }

        v.CopyTo(start);
        if (s.Extrapolate) {
            // The devices limit the extrapolated start like any Newton step, so an edge cannot fling a junction far
            // into conduction.
            for (var d = 0; d < k; d++) {
                proposed[d] = 2 * start[d] - history[d];
            }

            Device.Limit(start, proposed);
            proposed.CopyTo(v);
        }

        start.CopyTo(history);
        var bestNorm = double.PositiveInfinity;
        var iterations = 1;
        var norm = Residual(k, v, ref bestNorm);
        while (double.IsFinite(norm)) {
            // J = I - F (∂i/∂v - G_c), solved for the step -J⁻¹ g (the step buffer holds -g).
            for (var d = 0; d < k; d++) {
                for (var l = 0; l < k; l++) {
                    var sum = d == l ? 1.0 : 0.0;
                    for (var e = 0; e < k; e++) {
                        sum -= f[d * k + e] * (jacobian[e * k + l] - (e == l ? gc[e] : 0));
                    }

                    system[d * k + l] = sum;
                }
            }

            system.CopyTo(s.systemCopy);
            step.CopyTo(s.rhsCopy);
            if (!SolveSmall(system, k, step, s.pivots)) {
                break;
            }

            for (var d = 0; d < k; d++) {
                proposed[d] = v[d] + step[d];
            }

            Device.Limit(v, proposed);
            if (k > 1) {
                Constrain(k, v, step, proposed);
            }

            var converged = true;
            for (var d = 0; d < k; d++) {
                step[d] = proposed[d] - v[d];
                converged &= Math.Abs(step[d]) <= s.AbsoluteTolerance + s.RelativeTolerance * Math.Abs(v[d]);
            }

            if (converged) {
                // The currents at the new voltages, to second order in the step: r + (∂i/∂v - G_c) Δ.
                for (var d = 0; d < k; d++) {
                    var sum = trial[d];
                    for (var l = 0; l < k; l++) {
                        sum += (jacobian[d * k + l] - (d == l ? gc[d] : 0)) * step[l];
                    }

                    r[d] = sum;
                }

                proposed.CopyTo(v);
                s.Record(iterations, converged: true, finite: true);
                return;
            }

            // Backtrack until |g| decreases: the first try is the full (limited) step. Residual overwrites the step
            // buffer, so the direction is kept apart.
            step.CopyTo(direction);
            var lambda = 1.0;
            var accepted = false;
            while (iterations < s.MaxIterations) {
                iterations++;
                for (var d = 0; d < k; d++) {
                    proposed[d] = v[d] + lambda * direction[d];
                }

                var candidate = Residual(k, proposed, ref bestNorm);
                var plain = iterations <= s.LineSearchAfter || lambda <= ShortestStep;
                if (candidate < norm || (plain && double.IsFinite(candidate))) {
                    proposed.CopyTo(v);
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

        if (bestNorm <= NewtonSolver.AcceptableNorm) {
            Fallback(k);
            s.Record(iterations, converged: false, finite: true);
        } else if (double.IsFinite(bestNorm)) {
            best.CopyTo(v); // hold the previous currents (bounded, unlike a wild iterate's); start next time from the best
            s.Record(iterations, converged: false, finite: true);
        } else {
            start.CopyTo(v); // repeat the previous sample's solution, whose currents r still hold
            s.Record(iterations, converged: false, finite: false);
        }
    }

    /// <summary>
    ///     When the devices limited some ports' steps, re-solves the linearised system for the other ports with the
    ///     limited ones held at their limited values, so the step stays a Newton step of the constrained problem. Without
    ///     this, a limited junction leaves its neighbours stepping as if it had moved all the way, which sends a
    ///     transistor's other junction, or an op-amp's output, far off.
    /// </summary>
    void Constrain(int k, ReadOnlySpan<double> v, ReadOnlySpan<double> unlimited, Span<double> proposed) {
        var s = newton;
        Span<int> free = stackalloc int[k];
        var count = 0;
        for (var d = 0; d < k; d++) {
            if (proposed[d] == v[d] + unlimited[d]) {
                free[count++] = d;
            }
        }

        if (count == k || count == 0) {
            return;
        }

        var a = s.system.AsSpan(0, count * count);
        var b = s.direction.AsSpan(0, count);
        var full = s.systemCopy;
        for (var i = 0; i < count; i++) {
            var row = free[i];
            var rhs = s.rhsCopy[row];
            for (var d = 0; d < k; d++) {
                if (proposed[d] != v[d] + unlimited[d]) {
                    rhs -= full[row * k + d] * (proposed[d] - v[d]);
                }
            }

            b[i] = rhs;
            for (var j = 0; j < count; j++) {
                a[i * count + j] = full[row * k + free[j]];
            }
        }

        if (!SolveSmall(a, count, b, s.pivots)) {
            return;
        }

        for (var i = 0; i < count; i++) {
            proposed[free[i]] = v[free[i]] + b[i];
        }

        Device.Limit(v, proposed);
    }

    /// <summary>
    ///     Newton gave up: one more linearisation at the best point found, solved without limiting. The result
    ///     satisfies the network exactly with the devices replaced by their tangents there, so it cannot inject energy
    ///     the way an inconsistent current would; a singular tangent leaves the best point itself.
    /// </summary>
    void Fallback(int k) {
        var s = newton;
        var v = s.voltages.AsSpan(0, k);
        var r = s.currents.AsSpan(0, k);
        var best = s.best.AsSpan(0, k);
        var trial = s.residual.AsSpan(0, k);
        var gc = s.conductances.AsSpan(0, k);
        var jacobian = s.jacobian.AsSpan(0, k * k);
        var system = s.system.AsSpan(0, k * k);
        var step = s.step.AsSpan(0, k);
        var f = s.impedance.AsSpan(0, k * k);
        best.CopyTo(v);
        s.bestCurrents.AsSpan(0, k).CopyTo(r);
        var ignored = double.PositiveInfinity;
        Residual(k, v, ref ignored);
        for (var d = 0; d < k; d++) {
            for (var l = 0; l < k; l++) {
                var sum = d == l ? 1.0 : 0.0;
                for (var e = 0; e < k; e++) {
                    sum -= f[d * k + e] * (jacobian[e * k + l] - (e == l ? gc[e] : 0));
                }

                system[d * k + l] = sum;
            }
        }

        if (!SolveSmall(system, k, step, s.pivots)) {
            return;
        }

        for (var d = 0; d < k; d++) {
            var sum = trial[d];
            for (var l = 0; l < k; l++) {
                sum += (jacobian[d * k + l] - (d == l ? gc[d] : 0)) * step[l];
            }

            if (!double.IsFinite(sum) || !double.IsFinite(step[d])) {
                return;
            }

            s.proposed[d] = sum;
        }

        for (var d = 0; d < k; d++) {
            r[d] = s.proposed[d];
            v[d] += step[d];
        }
    }

    /// <summary>
    ///     Evaluates the devices at <paramref name="v" />: currents, Jacobian, residual currents r = i - G_c v, and -g into
    ///     the step buffer. Returns |g|² scaled by the tolerances, and remembers the best point seen.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    double Residual(int k, ReadOnlySpan<double> v, ref double bestNorm) {
        var s = newton;
        var current = s.deviceCurrents.AsSpan(0, k);
        var trial = s.residual.AsSpan(0, k);
        var gc = s.conductances.AsSpan(0, k);
        var jacobian = s.jacobian.AsSpan(0, k * k);
        var step = s.step.AsSpan(0, k);
        var p = s.prediction.AsSpan(0, k);
        var f = s.impedance.AsSpan(0, k * k);
        jacobian.Clear();
        Device.Evaluate(v, current, jacobian, k);
        for (var d = 0; d < k; d++) {
            trial[d] = current[d] - gc[d] * v[d];
        }

        var norm = 0.0;
        for (var d = 0; d < k; d++) {
            var g = v[d] - p[d];
            for (var e = 0; e < k; e++) {
                g -= f[d * k + e] * trial[e];
            }

            step[d] = -g;
            var scaled = g / (s.AbsoluteTolerance + s.RelativeTolerance * Math.Abs(v[d]));
            norm += scaled * scaled;
        }

        if (norm < bestNorm) {
            bestNorm = norm;
            v.CopyTo(s.best);
            trial.CopyTo(s.bestCurrents);
        }

        return norm;
    }

    /// <summary>Solves the k × k system in place: closed forms for one and two unknowns, LU beyond.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool SolveSmall(Span<double> a, int k, Span<double> b, Span<int> pivots) {
        switch (k) {
            case 1:
                if (!(Math.Abs(a[0]) > 1e-300)) {
                    return false;
                }

                b[0] /= a[0];
                return true;
            case 2: {
                var determinant = a[0] * a[3] - a[1] * a[2];
                if (!(Math.Abs(determinant) > 1e-300)) {
                    return false;
                }

                var x0 = (b[0] * a[3] - b[1] * a[1]) / determinant;
                b[1] = (a[0] * b[1] - a[2] * b[0]) / determinant;
                b[0] = x0;
                return true;
            }
            default:
                return DenseLu.SolveInPlace(a, k, pivots, b);
        }
    }
}

/// <summary>
///     Settings, statistics and work buffers of the Newton solver of an <see cref="RTypeRoot{TPorts,TDevice}" />. The
///     settings may change between samples; the statistics accumulate until <see cref="ResetStatistics" />.
/// </summary>
public sealed class NewtonSolver {
    /// <summary>
    ///     Scaled |g|² below which an unconverged sample still takes its best iterate (|g| within about a thousand
    ///     tolerances); above it, the sample repeats the previous solution.
    /// </summary>
    internal const double AcceptableNorm = 1e6;

    internal readonly double[] voltages;
    internal readonly double[] currents; // the solution's residual currents r = i - G_c v
    internal readonly double[] prediction;
    internal readonly double[] conductances;
    internal readonly double[] deviceCurrents;
    internal readonly double[] residual;
    internal readonly double[] jacobian;
    internal readonly double[] system;
    internal readonly double[] step;
    internal readonly double[] proposed;
    internal readonly double[] start;
    internal readonly double[] best;
    internal readonly double[] bestCurrents;
    internal readonly double[] direction;
    internal readonly double[] systemCopy;
    internal readonly double[] rhsCopy;
    internal readonly double[] history; // the solution before last, for the extrapolated start
    internal readonly double[] impedance; // F, copied out of the scattering matrix per sample
    internal readonly int[] pivots;
    long samples;
    long iterations;
    int lastIterations;
    int mostIterations;
    long unconverged;
    long nonFinite;

    /// <summary>Iterations per sample before giving up and keeping the best iterate.</summary>
    public int MaxIterations { get; set; } = 32;

    /// <summary>Convergence: every Newton step below this many volts plus <see cref="RelativeTolerance" />.</summary>
    /// <remarks>
    ///     The error left after a converged step Δ is second order, about Δ² times the devices' curvature (1 / 2 n Vt for
    ///     a junction), so the microvolt default leaves errors around 1e-11 V.
    /// </remarks>
    public double AbsoluteTolerance { get; set; } = 1e-6;

    public double RelativeTolerance { get; set; } = 1e-6;

    /// <summary>
    ///     Start Newton from the last two solutions extrapolated linearly (2 v[n-1] - v[n-2]) rather than from the last
    ///     one: on oversampled audio this saves about one iteration per sample.
    /// </summary>
    public bool Extrapolate { get; set; } = true;

    /// <summary>
    ///     Iterations of plain (limited) Newton before a step must also reduce |g| (a backtracking line search). Junctions
    ///     climbing a steep exponential legitimately raise |g| for a while, so the line search only steps in for samples
    ///     that are struggling, e.g. an op-amp bouncing between its rails.
    /// </summary>
    public int LineSearchAfter { get; set; } = 16;

    /// <summary>Samples solved since the last <see cref="ResetStatistics" />.</summary>
    public long Samples => samples;

    /// <summary>Device evaluations (Newton iterations) in total.</summary>
    public long Iterations => iterations;

    public int LastIterations => lastIterations;

    public int MostIterations => mostIterations;

    /// <summary>Samples that hit <see cref="MaxIterations" /> (or a singular Jacobian) and kept the best iterate.</summary>
    public long Unconverged => unconverged;

    /// <summary>Samples where a device returned a non-finite value and the previous solution was repeated.</summary>
    public long NonFinite => nonFinite;

    public double MeanIterations => Samples == 0 ? 0 : (double)Iterations / Samples;

    internal NewtonSolver(int ports) {
        var k = Math.Max(ports, 1);
        voltages = new double[k];
        currents = new double[k];
        prediction = new double[k];
        conductances = new double[k];
        deviceCurrents = new double[k];
        residual = new double[k];
        jacobian = new double[k * k];
        system = new double[k * k];
        step = new double[k];
        proposed = new double[k];
        start = new double[k];
        best = new double[k];
        bestCurrents = new double[k];
        direction = new double[k];
        systemCopy = new double[k * k];
        rhsCopy = new double[k];
        history = new double[k];
        impedance = new double[k * k];
        pivots = new int[k];
    }

    public void ResetStatistics() {
        samples = 0;
        iterations = 0;
        lastIterations = 0;
        mostIterations = 0;
        unconverged = 0;
        nonFinite = 0;
    }

    internal void Record(int count, bool converged, bool finite) {
        samples++;
        iterations += count;
        lastIterations = count;
        mostIterations = Math.Max(mostIterations, count);
        if (!converged && finite) {
            unconverged++;
        }

        if (!finite) {
            nonFinite++;
        }
    }
}
