using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Cathode.Dsp.Wdf;

/// <summary>
///     The root of a wave digital filter whose top is an R-type adaptor holding K nonlinear device ports, after Werner,
///     Nangia, Smith and Abel, "Resolving wave digital filters with multiple/multiport nonlinearities" (DAFx-15). Use
///     <see cref="NoDevice" /> for a linear circuit: the root then only scatters. A complete circuit: set the sources,
///     call
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
///         If Newton has not converged after <see cref="NewtonSolver.MaxIterations" /> on a single port and its
///         iterates bracket the root, the bracket is bisected to the tolerance (Newton can cycle across a kink).
///         Otherwise the sample is counted in <see cref="NewtonSolver.Unconverged" />: a nearly converged best iterate
///         is linearised once more and used;
///         otherwise the previous sample's currents are held (bounded, where a wild iterate's might not be) and the next
///         sample starts from the best point. If the devices return a non-finite value, the previous solution is repeated
///         and the sample counted in <see cref="NewtonSolver.NonFinite" />. Either way the output stays finite.
///     </para>
/// </remarks>
public struct RTypeRoot<TPorts, TDevice>
    where TPorts : struct, IWdfPortList
    where TDevice : struct, INonlinearDevice {
    /// <summary>The subtrees, in netlist port order.</summary>
    public TPorts Ports;

    /// <summary>The nonlinear devices, in netlist device-port order.</summary>
    public TDevice Device;

    readonly double[] inputs;
    readonly double[] outputs;

    /// <summary>The matrix and its structure, for inspection.</summary>
    public RTypeScattering Scattering { get; }

    /// <summary>Newton settings and statistics.</summary>
    public NewtonSolver Newton { get; }

    public RTypeRoot(WdfNetlist netlist, TPorts ports, TDevice device) {
        Scattering = new(netlist);
        if (Scattering.HasUp) {
            throw new ArgumentException("A root has no Up port.", nameof(netlist));
        }

        if (Scattering.PortCount != ports.Count) {
            throw new ArgumentException(
                $"The netlist has {Scattering.PortCount} ports, the port list {ports.Count}.",
                nameof(ports)
            );
        }

        if (Scattering.DeviceCount != device.Ports) {
            throw new ArgumentException(
                $"The netlist has {Scattering.DeviceCount} device ports, the devices {device.Ports}.",
                nameof(device)
            );
        }

        Ports = ports;
        Device = device;
        for (var k = 0; k < device.Ports; k++) {
            Scattering.SetDeviceConductance(k, Math.Max(device.Conductance(k), 1e-15));
        }

        Newton = new(device.Ports);
        for (var k = 0; k < device.Ports; k++) {
            Newton.conductances[k] = Math.Max(device.Conductance(k), 1e-15);
            Newton.blockStart[k] = device.BlockStart(k);
        }

        for (var k = 0; k < device.Ports; k++) {
            var end = k + 1;
            while (end < device.Ports && Newton.blockStart[end] == Newton.blockStart[k]) {
                end++;
            }

            Newton.blockEnd[k] = end;
        }

        Newton.pairs = device.Ports % 2 == 0;
        for (var k = 0; k < device.Ports; k++) {
            Newton.pairs &= Newton.blockStart[k] == (k & ~1) && Newton.blockEnd[k] == (k | 1) + 1;
        }

        inputs = new double[Scattering.Inputs];
        outputs = new double[Scattering.PortCount];
        Propagate();
    }

    /// <summary>Recomputes the port resistances and the scattering matrix after a component change. Allocation-free.</summary>
    public void Propagate() {
        Ports.Propagate(Scattering.PortResistances);
        Scattering.Recompute();
        // F, the impedance the network presents to the devices, for the Newton solver.
        var k = Device.Ports;
        var matrix = Scattering.Matrix;
        for (var d = 0; d < k; d++) {
            var row = (Scattering.DeviceRow + d) * Scattering.Columns + Scattering.Inputs;
            matrix.Slice(row, k).CopyTo(Newton.impedance.AsSpan(d * k));
        }

        Newton.factored = false;
    }

    /// <summary>Sets ideal voltage source <paramref name="index" /> (in volts) for the coming samples.</summary>
    public readonly void SetVoltage(int index, double volts) => inputs[Scattering.VoltageSourceColumn + index] = volts;

    /// <summary>Sets ideal current source <paramref name="index" /> (in amperes) for the coming samples.</summary>
    public readonly void SetCurrent(int index, double amps) => inputs[Scattering.CurrentSourceColumn + index] = amps;

    /// <summary>Changes internal resistor <paramref name="index" />; call <see cref="Propagate" /> afterwards.</summary>
    public readonly void SetResistance(int index, double ohms) => Scattering.SetResistance(index, ohms);

    /// <summary>Changes the gain of VCVS <paramref name="index" />; call <see cref="Propagate" /> afterwards.</summary>
    public readonly void SetGain(int index, double gain) => Scattering.SetGain(index, gain);

    /// <summary>Evaluates one sample. Returns probe 0 (or 0 when the netlist has no probe).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick() {
        var u = inputs.AsSpan();
        Ports.Reflect(u);
        var waves = outputs.AsSpan();
        var k = Device.Ports;

        // The rows are read in place (the same products in the same order as RTypeScattering.Dot, without its bounds
        // checks: the matrix has Rows × Columns entries and every row starts with the inputs, then the devices).
        var columns = Scattering.Columns;
        var count = u.Length;
        ref var matrix = ref MemoryMarshal.GetReference(Scattering.Matrix);
        ref var input = ref MemoryMarshal.GetArrayDataReference(inputs);
        if (k == 0) {
            var j = 0;
            for (; j + 2 <= waves.Length; j += 2) {
                (waves[j], waves[j + 1]) = DotTwo(ref Unsafe.Add(ref matrix, j * columns), columns, ref input, count);
            }

            if (j < waves.Length) {
                waves[j] = Dot(ref Unsafe.Add(ref matrix, j * columns), ref input, count);
            }
        } else {
            var p = Newton.prediction;
            if (Newton.Linearise) {
                Array.Copy(p, Newton.previousPrediction, k);
            }

            var deviceRow = Scattering.DeviceRow;
            var d = 0;
            for (; d + 2 <= k; d += 2) {
                (p[d], p[d + 1]) = DotTwo(
                    ref Unsafe.Add(ref matrix, (deviceRow + d) * columns),
                    columns,
                    ref input,
                    count
                );
            }

            if (d < k) {
                p[d] = Dot(ref Unsafe.Add(ref matrix, (deviceRow + d) * columns), ref input, count);
            }

            if (!(k == 1 ? SolveOne() : k == 2 && SolveTwo())) {
                Solve(k);
            }

            ref var r = ref MemoryMarshal.GetArrayDataReference(Newton.currents);
            var j = 0;
            for (; j + 2 <= waves.Length; j += 2) {
                ref var row = ref Unsafe.Add(ref matrix, j * columns);
                var (fromInputs, nextFromInputs) = DotTwo(ref row, columns, ref input, count);
                var (fromDevices, nextFromDevices) = DotTwo(ref Unsafe.Add(ref row, count), columns, ref r, k);
                (waves[j], waves[j + 1]) = (fromInputs + fromDevices, nextFromInputs + nextFromDevices);
            }

            if (j < waves.Length) {
                ref var row = ref Unsafe.Add(ref matrix, j * columns);
                waves[j] = Dot(ref row, ref input, count) + Dot(ref Unsafe.Add(ref row, count), ref r, k);
            }

            Device.Commit(Newton.voltages.AsSpan(0, k));
        }

        Ports.Incident(waves);
        return Scattering.ProbeCount > 0 ? Probe(0) : 0;
    }

    /// <summary>Probed voltage <paramref name="index" /> at the last sample.</summary>
    public readonly double Probe(int index) {
        var count = inputs.Length;
        ref var row = ref Unsafe.Add(
            ref MemoryMarshal.GetReference(Scattering.Matrix),
            (Scattering.ProbeRow + index) * Scattering.Columns
        );
        return Dot(ref row, ref MemoryMarshal.GetArrayDataReference(inputs), count)
            + Dot(
                ref Unsafe.Add(ref row, count),
                ref MemoryMarshal.GetArrayDataReference(Newton.currents),
                Device.Ports
            );
    }

    /// <summary>Voltage across device port <paramref name="port" /> at the last sample.</summary>
    public readonly double DeviceVoltage(int port) => Newton.voltages[port];

    /// <summary>Current into device port <paramref name="port" /> at the last sample.</summary>
    public readonly double DeviceCurrent(int port) =>
        Newton.currents[port] + Newton.conductances[port] * Newton.voltages[port];

    /// <summary>
    ///     Sets the device port voltages Newton starts the next sample from, e.g. a known operating point, so that a
    ///     circuit switched on at full supply does not have to find it from zero.
    /// </summary>
    public readonly void SetInitialVoltages(ReadOnlySpan<double> voltages) {
        voltages.CopyTo(Newton.voltages);
        voltages.CopyTo(Newton.history);
        Newton.primed = false;
    }

    public void Reset() {
        Newton.primed = false;
        Newton.factored = false;
        Ports.Reset();
        Device.Reset();
        Newton.voltages.AsSpan().Clear();
        Newton.history.AsSpan().Clear();
        Newton.currents.AsSpan().Clear();
        inputs.AsSpan(0, Scattering.VoltageSourceColumn).Clear(); // keep the source values
    }

    /// <summary><see cref="RTypeScattering.Dot" /> on references: the same sum, in the same order.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double Dot(ref double row, ref double u, int count) {
        var sum = 0.0;
        for (var j = 0; j < count; j++) {
            sum += Unsafe.Add(ref row, j) * Unsafe.Add(ref u, j);
        }

        return sum;
    }

    /// <summary>
    ///     <see cref="Dot" /> on two consecutive rows (<paramref name="columns" /> apart) at once: each its own sum in
    ///     its own order, so the same results, with the vector read once for both.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static (double First, double Second) DotTwo(ref double row, int columns, ref double u, int count) {
        ref var next = ref Unsafe.Add(ref row, columns);
        var (first, second) = (0.0, 0.0);
        for (var j = 0; j < count; j++) {
            var x = Unsafe.Add(ref u, j);
            first += Unsafe.Add(ref row, j) * x;
            second += Unsafe.Add(ref next, j) * x;
        }

        return (first, second);
    }

    /// <summary>
    ///     <see cref="Solve" /> for one device port, written out: the same arithmetic in the same order (so the same
    ///     result to the last bit) without the general case's loops and spans. It covers the common case, plain limited
    ///     Newton steps converging within <see cref="NewtonSolver.LineSearchAfter" /> iterations, or with the bracket
    ///     safeguard (<see cref="NewtonSolver.SafeguardAfter" />, which only this path has) within
    ///     <see cref="NewtonSolver.MaxIterations" />; anything else (a singular step, a non-finite residual, a slow
    ///     sample without a bracket) puts the sample back as it was and returns false, and the general solver takes it
    ///     from the start.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool SolveOne() {
        var s = Newton;
        var f = Scattering.Matrix[Scattering.DeviceRow * Scattering.Columns + Scattering.Inputs];
        var (v, h, p, current, jacobian) = (s.voltages, s.history, s.prediction, s.deviceCurrents, s.jacobian);
        var (gc, absolute, relative) = (s.conductances[0], s.AbsoluteTolerance, s.RelativeTolerance);
        var (entry, previous) = (v[0], h[0]);
        // The spans once (in the power amp's tick the JIT may run out of inlining budget for AsSpan).
        var voltages = v.AsSpan(0, 1);
        var starts = s.start.AsSpan(0, 1);
        var proposals = s.proposed.AsSpan(0, 1);
        var currents = current.AsSpan(0, 1);
        var slopes = jacobian.AsSpan(0, 1);
        var x = entry;
        if (s is { Linearise: true, primed: true }) {
            // One Newton step on the last sample's Jacobian for the change in p: x = v + M⁻¹ Δp.
            var m = 1.0;
            m -= f * (jacobian[0] - gc);
            s.start[0] = entry;
            s.proposed[0] = Math.Abs(m) > 1e-300 ? entry + (p[0] - s.previousPrediction[0]) / m : entry;
            Device.Limit(starts, proposals);
            x = s.proposed[0];
        } else if (s.Extrapolate) {
            s.start[0] = entry;
            s.proposed[0] = 2 * entry - previous;
            Device.Limit(starts, proposals);
            x = s.proposed[0];
        }

        h[0] = entry;
        var iterations = 1;

        // The residual at x: the device's current and slope, r = i - G_c v, g = v - p - F r.
        v[0] = x;
        jacobian[0] = 0;
        Device.Evaluate(voltages, currents, slopes, 1);
        var trial = current[0] - gc * x;
        var g = x - p[0];
        g -= f * trial;
        var scaled = g / (absolute + relative * Math.Abs(x));
        var norm = 0.0;
        norm += scaled * scaled;
        var (below, above) = (double.NaN, double.NaN); // where g < 0 and g > 0, for the safeguard
        while (double.IsFinite(norm)) {
            if (g < 0) {
                below = x;
            } else if (g > 0) {
                above = x;
            }

            var j = jacobian[0];
            var a = 1.0;
            a -= f * (j - gc);
            if (!(Math.Abs(a) > 1e-300)) {
                break;
            }

            var proposed = x + -g / a;
            s.proposed[0] = proposed;
            Device.Limit(voltages, proposals);
            proposed = s.proposed[0];
            var bracketed = double.IsFinite(below) && double.IsFinite(above);
            if (iterations >= s.SafeguardAfter
                && bracketed
                && !(proposed > Math.Min(below, above) && proposed < Math.Max(below, above))) {
                // Newton is cycling across a kink: bisect the bracket instead of leaving it.
                proposed = 0.5 * (below + above);
            }

            var step = proposed - x;
            if (Math.Abs(step) <= absolute + relative * Math.Abs(x)) {
                var r = trial;
                r += (j - gc) * step;
                s.currents[0] = r;
                v[0] = proposed;
                s.Record(iterations, true, true);
                return true;
            }

            iterations++;
            if ((iterations > s.LineSearchAfter && !(bracketed && iterations > s.SafeguardAfter))
                || iterations > s.MaxIterations) {
                break;
            }

            x += 1.0 * step;
            v[0] = x;
            jacobian[0] = 0;
            Device.Evaluate(voltages, currents, slopes, 1);
            trial = current[0] - gc * x;
            g = x - p[0];
            g -= f * trial;
            scaled = g / (absolute + relative * Math.Abs(x));
            norm = 0.0;
            norm += scaled * scaled;
        }

        v[0] = entry;
        h[0] = previous;
        return false;
    }

    /// <summary><see cref="SolveOne" /> for two device ports (a triode).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool SolveTwo() {
        var s = Newton;
        var matrix = Scattering.Matrix;
        var row = Scattering.DeviceRow * Scattering.Columns + Scattering.Inputs;
        var (f00, f01) = (matrix[row], matrix[row + 1]);
        row += Scattering.Columns;
        var (f10, f11) = (matrix[row], matrix[row + 1]);
        var (v, h, p, current, jacobian) = (s.voltages, s.history, s.prediction, s.deviceCurrents, s.jacobian);
        var (gc0, gc1) = (s.conductances[0], s.conductances[1]);
        var (absolute, relative) = (s.AbsoluteTolerance, s.RelativeTolerance);
        var (entry0, entry1, previous0, previous1) = (v[0], v[1], h[0], h[1]);
        var (x0, x1) = (entry0, entry1);
        if (s is { Linearise: true, primed: true }) {
            // One Newton step on the last sample's Jacobian for the change in p: x = v + M⁻¹ Δp.
            var (j00, j01, j10, j11) = (jacobian[0], jacobian[1], jacobian[2], jacobian[3]);
            var m00 = 1 - f00 * (j00 - gc0) - f01 * j10;
            var m01 = -f00 * j01 - f01 * (j11 - gc1);
            var m10 = -f10 * (j00 - gc0) - f11 * j10;
            var m11 = 1 - f10 * j01 - f11 * (j11 - gc1);
            var (d0, d1) = (p[0] - s.previousPrediction[0], p[1] - s.previousPrediction[1]);
            var determinant = m00 * m11 - m01 * m10;
            (s.start[0], s.start[1]) = (entry0, entry1);
            (s.proposed[0], s.proposed[1]) = Math.Abs(determinant) > 1e-300
                ? (entry0 + (d0 * m11 - d1 * m01) / determinant, entry1 + (m00 * d1 - m10 * d0) / determinant)
                : (entry0, entry1);
            Device.Limit(s.start.AsSpan(0, 2), s.proposed.AsSpan(0, 2));
            (x0, x1) = (s.proposed[0], s.proposed[1]);
        } else if (s.Extrapolate) {
            (s.start[0], s.start[1]) = (entry0, entry1);
            (s.proposed[0], s.proposed[1]) = (2 * entry0 - previous0, 2 * entry1 - previous1);
            Device.Limit(s.start.AsSpan(0, 2), s.proposed.AsSpan(0, 2));
            (x0, x1) = (s.proposed[0], s.proposed[1]);
        }

        (h[0], h[1]) = (entry0, entry1);
        var iterations = 1;
        while (true) {
            // The residual at (x0, x1): the device's currents and Jacobian, r = i - G_c v, g = v - p - F r.
            (v[0], v[1]) = (x0, x1);
            (jacobian[0], jacobian[1], jacobian[2], jacobian[3]) = (0.0, 0.0, 0.0, 0.0);
            Device.Evaluate(v.AsSpan(0, 2), current.AsSpan(0, 2), jacobian.AsSpan(0, 4), 2);
            var trial0 = current[0] - gc0 * x0;
            var trial1 = current[1] - gc1 * x1;
            var g0 = x0 - p[0];
            g0 -= f00 * trial0;
            g0 -= f01 * trial1;
            var g1 = x1 - p[1];
            g1 -= f10 * trial0;
            g1 -= f11 * trial1;
            var scaled0 = g0 / (absolute + relative * Math.Abs(x0));
            var scaled1 = g1 / (absolute + relative * Math.Abs(x1));
            var norm = 0.0;
            norm += scaled0 * scaled0;
            norm += scaled1 * scaled1;
            if (!double.IsFinite(norm)) {
                break;
            }

            var (j00, j01, j10, j11) = (jacobian[0], jacobian[1], jacobian[2], jacobian[3]);
            var a00 = 1.0;
            a00 -= f00 * (j00 - gc0);
            a00 -= f01 * (j10 - 0.0);
            var a01 = 0.0;
            a01 -= f00 * (j01 - 0.0);
            a01 -= f01 * (j11 - gc1);
            var a10 = 0.0;
            a10 -= f10 * (j00 - gc0);
            a10 -= f11 * (j10 - 0.0);
            var a11 = 1.0;
            a11 -= f10 * (j01 - 0.0);
            a11 -= f11 * (j11 - gc1);
            var (b0, b1) = (-g0, -g1);
            var determinant = a00 * a11 - a01 * a10;
            if (!(Math.Abs(determinant) > 1e-300)) {
                break;
            }

            var u0 = (b0 * a11 - b1 * a01) / determinant;
            var u1 = (a00 * b1 - a10 * b0) / determinant;
            var (proposed0, proposed1) = (x0 + u0, x1 + u1);
            (s.proposed[0], s.proposed[1]) = (proposed0, proposed1);
            Device.Limit(v.AsSpan(0, 2), s.proposed.AsSpan(0, 2));
            if (s.proposed[0] != proposed0 || s.proposed[1] != proposed1) {
                // A port was limited: re-solve the other with it held, as the general solver does.
                (s.systemCopy[0], s.systemCopy[1], s.systemCopy[2], s.systemCopy[3]) = (a00, a01, a10, a11);
                (s.rhsCopy[0], s.rhsCopy[1]) = (b0, b1);
                (s.step[0], s.step[1]) = (u0, u1);
                Constrain(2, v.AsSpan(0, 2), s.step.AsSpan(0, 2), s.proposed.AsSpan(0, 2));
            }

            (proposed0, proposed1) = (s.proposed[0], s.proposed[1]);
            var (step0, step1) = (proposed0 - x0, proposed1 - x1);
            var converged = true;
            converged &= Math.Abs(step0) <= absolute + relative * Math.Abs(x0);
            converged &= Math.Abs(step1) <= absolute + relative * Math.Abs(x1);
            if (converged) {
                var r0 = trial0;
                r0 += (j00 - gc0) * step0;
                r0 += (j01 - 0.0) * step1;
                var r1 = trial1;
                r1 += (j10 - 0.0) * step0;
                r1 += (j11 - gc1) * step1;
                (s.currents[0], s.currents[1]) = (r0, r1);
                (v[0], v[1]) = (proposed0, proposed1);
                s.Record(iterations, true, true);
                return true;
            }

            iterations++;
            if (iterations > s.LineSearchAfter || iterations > s.MaxIterations) {
                break;
            }

            x0 += 1.0 * step0;
            x1 += 1.0 * step1;
        }

        (v[0], v[1], h[0], h[1]) = (entry0, entry1, previous0, previous1);
        return false;
    }

    /// <summary>
    ///     Damped Newton on g(v) = v - p - F r(v), from the previous solution extrapolated linearly; leaves the solution in
    ///     the voltages and its residual currents r. See the remarks on the type.
    /// </summary>
    void Solve(int k) {
        const double ShortestStep = 1.0 / 16;
        var s = Newton;
        var v = s.voltages.AsSpan(0, k);
        var r = s.currents.AsSpan(0, k);
        _ = s.deviceCurrents.AsSpan(0, k);
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
        var (absolute, relative) = (s.AbsoluteTolerance, s.RelativeTolerance); // read once: the JIT may not inline them
        var (most, lineSearch, reuse) = (s.MaxIterations, s.LineSearchAfter, s.ReuseFactors);
        v.CopyTo(start);
        if (s is { Linearise: true, primed: true }) {
            // One Newton step on the last sample's Jacobian for the change in p: v + M⁻¹ Δp, limited. When the last
            // sample converged, that Jacobian's system is still factored.
            for (var d = 0; d < k; d++) {
                step[d] = s.prediction[d] - s.previousPrediction[d];
            }

            bool solved;
            if (k > 2 && s.factored) {
                DenseLu.Solve(system, k, s.pivots, step, 1);
                solved = true;
            } else {
                BuildSystem(k);
                solved = SolveSmall(system, k, step, s.pivots);
            }

            if (solved) {
                for (var d = 0; d < k; d++) {
                    proposed[d] = start[d] + step[d];
                }

                Device.Limit(start, proposed);
                proposed.CopyTo(v);
            }
        } else if (s.Extrapolate) {
            // The devices limit the extrapolated start like any Newton step, so an edge cannot fling a junction far
            // into conduction.
            for (var d = 0; d < k; d++) {
                proposed[d] = 2 * start[d] - history[d];
            }

            Device.Limit(start, proposed);
            proposed.CopyTo(v);
        }

        var chord = reuse && k > 2;
        s.factored &= chord;
        start.CopyTo(history);
        var bestNorm = double.PositiveInfinity;
        var iterations = 1;
        var norm = Residual(k, v, ref bestNorm);
        var (below, above) = (double.NaN, double.NaN); // one port: points where g < 0 and g > 0, for the bracket
        Bracket(k, v, ref below, ref above);
        while (double.IsFinite(norm)) {
            if (chord && s.factored && norm <= NewtonSolver.ChordNorm * k && Chord(k)) {
                s.Record(iterations, true, true);
                return;
            }

            // J = I - F (∂i/∂v - G_c), solved for the step -J⁻¹ g (the step buffer holds -g).
            BuildSystem(k);
            step.CopyTo(s.rhsCopy);
            if (!SolveSmall(system, k, step, s.pivots)) {
                break;
            }

            s.factored = true; // until Constrain borrows the buffers, or (unless they are reused) the next evaluation
            for (var d = 0; d < k; d++) {
                proposed[d] = v[d] + step[d];
            }

            Device.Limit(v, proposed);
            if (k > 1) {
                Constrain(k, v, step, proposed, true);
            }

            var converged = true;
            for (var d = 0; d < k; d++) {
                step[d] = proposed[d] - v[d];
                converged &= Math.Abs(step[d]) <= absolute + relative * Math.Abs(v[d]);
            }

            if (converged) {
                // The currents at the new voltages, to second order in the step: r + (∂i/∂v - G_c) Δ.
                for (var d = 0; d < k; d++) {
                    var sum = trial[d];
                    for (var l = s.blockStart[d]; l < s.blockEnd[d]; l++) {
                        sum += (jacobian[d * k + l] - (d == l ? gc[d] : 0)) * step[l];
                    }

                    r[d] = sum;
                }

                proposed.CopyTo(v);
                s.Record(iterations, true, true);
                return;
            }

            s.factored &= reuse;

            // Backtrack until |g| decreases: the first try is the full (limited) step. Residual overwrites the step
            // buffer, so the direction is kept apart.
            step.CopyTo(direction);
            var lambda = 1.0;
            var accepted = false;
            while (iterations < most) {
                iterations++;
                for (var d = 0; d < k; d++) {
                    proposed[d] = v[d] + lambda * direction[d];
                }

                var candidate = Residual(k, proposed, ref bestNorm);
                Bracket(k, proposed, ref below, ref above);
                var plain = iterations <= lineSearch || lambda <= ShortestStep;
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

        s.factored = false; // the fallbacks borrow the buffers
        if (k == 1 && Bisect(below, above, ref iterations)) {
            s.Record(iterations, true, true);
        } else if (bestNorm <= NewtonSolver.AcceptableNorm) {
            Fallback(k);
            s.Record(iterations, false, true);
        } else if (double.IsFinite(bestNorm)) {
            best.CopyTo(
                v
            ); // hold the previous currents (bounded, unlike a wild iterate's); start next time from the best
            s.Record(iterations, false, true);
        } else {
            start.CopyTo(v); // repeat the previous sample's solution, whose currents r still hold
            s.Record(iterations, false, false);
        }
    }

    /// <summary>
    ///     A chord step (<see cref="NewtonSolver.ReuseFactors" />): the factored system of a recent Jacobian solved for
    ///     this iterate's residual, which saves building and factoring one. Taken only when it converges unlimited, with
    ///     the currents linearised on this iterate's own Jacobian; otherwise the step buffer gets -g back and a full
    ///     Newton step follows.
    /// </summary>
    bool Chord(int k) {
        var s = Newton;
        var v = s.voltages.AsSpan(0, k);
        var step = s.step.AsSpan(0, k);
        var proposed = s.proposed.AsSpan(0, k);
        var jacobian = s.jacobian.AsSpan(0, k * k);
        var gc = s.conductances.AsSpan(0, k);
        var (absolute, relative) = (s.AbsoluteTolerance, s.RelativeTolerance);
        step.CopyTo(s.rhsCopy);
        DenseLu.Solve(s.system.AsSpan(0, k * k), k, s.pivots, step, 1);
        for (var d = 0; d < k; d++) {
            proposed[d] = v[d] + step[d];
        }

        Device.Limit(v, proposed);
        var converged = true;
        for (var d = 0; d < k; d++) {
            converged &= proposed[d] == v[d] + step[d];
            step[d] = proposed[d] - v[d];
            converged &= Math.Abs(step[d]) <= absolute + relative * Math.Abs(v[d]);
        }

        if (!converged) {
            s.rhsCopy.AsSpan(0, k).CopyTo(step);
            return false;
        }

        for (var d = 0; d < k; d++) {
            var sum = s.residual[d];
            for (var l = s.blockStart[d]; l < s.blockEnd[d]; l++) {
                sum += (jacobian[d * k + l] - (d == l ? gc[d] : 0)) * step[l];
            }

            s.currents[d] = sum;
        }

        proposed.CopyTo(v);
        return true;
    }

    /// <summary>
    ///     When the devices limited some ports' steps, re-solves the linearised system for the other ports with the
    ///     limited ones held at their limited values, so the step stays a Newton step of the constrained problem. Without
    ///     this, a limited junction leaves its neighbours stepping as if it had moved all the way, which sends a
    ///     transistor's other junction, or an op-amp's output, far off.
    /// </summary>
    /// <param name="k">The ports.</param>
    /// <param name="v">The iterate.</param>
    /// <param name="unlimited">The Newton step before limiting.</param>
    /// <param name="proposed">The limited proposal, re-solved in place.</param>
    /// <param name="rebuild">
    ///     Whether to rebuild the unfactored system from the Jacobian (the general solver); otherwise the caller has
    ///     put it in the system copy.
    /// </param>
    void Constrain(
        int k,
        ReadOnlySpan<double> v,
        ReadOnlySpan<double> unlimited,
        Span<double> proposed,
        bool rebuild = false
    ) {
        var s = Newton;
        var held = 0; // usually none: then there is nothing to re-solve
        for (var d = 0; d < k; d++) {
            held += proposed[d] == v[d] + unlimited[d] ? 0 : 1;
        }

        if (held == 0 || held == k) {
            return;
        }

        Span<int> free = stackalloc int[k];
        var count = 0;
        for (var d = 0; d < k; d++) {
            if (proposed[d] == v[d] + unlimited[d]) {
                free[count++] = d;
            }
        }

        s.factored = false; // the system and pivot buffers are borrowed
        if (rebuild) {
            BuildSystem(k, s.systemCopy);
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
    ///     Remembers, for one port, the last point <paramref name="v" /> where g (as <see cref="Residual" /> left it, -g in
    ///     the step buffer) was negative and where it was positive.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    readonly void Bracket(int k, ReadOnlySpan<double> v, ref double below, ref double above) {
        if (k != 1) {
            return;
        }

        var g = -Newton.step[0];
        if (g < 0) {
            below = v[0];
        } else if (g > 0) {
            above = v[0];
        }
    }

    /// <summary>
    ///     Newton cycled on one port (a kink in the device, such as grid conduction under deep global feedback, can
    ///     make it bounce between two points): if its iterates bracket the root, bisects the bracket to the tolerance.
    ///     g is continuous, so this always converges. Leaves the solution and its currents as a converged Newton
    ///     step would, and returns false when there is no bracket.
    /// </summary>
    bool Bisect(double below, double above, ref int iterations) {
        const int MostBisections = 80;
        if (!double.IsFinite(below) || !double.IsFinite(above)) {
            return false;
        }

        var s = Newton;
        var v = s.voltages.AsSpan(0, 1);
        var ignored = double.PositiveInfinity;
        for (var n = 0; n < MostBisections; n++) {
            v[0] = 0.5 * (below + above);
            iterations++;
            Residual(1, v, ref ignored);
            var g = -s.step[0];
            if (!double.IsFinite(g)) {
                return false;
            }

            if (g < 0) {
                below = v[0];
            } else {
                above = v[0];
            }

            if (g == 0 || Math.Abs(above - below) <= s.AbsoluteTolerance + s.RelativeTolerance * Math.Abs(v[0])) {
                s.currents[0] = s.residual[0];
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Newton gave up: one more linearisation at the best point found, solved without limiting. The result
    ///     satisfies the network exactly with the devices replaced by their tangents there, so it cannot inject energy
    ///     the way an inconsistent current would; a singular tangent leaves the best point itself.
    /// </summary>
    void Fallback(int k) {
        var s = Newton;
        var v = s.voltages.AsSpan(0, k);
        var r = s.currents.AsSpan(0, k);
        var best = s.best.AsSpan(0, k);
        var trial = s.residual.AsSpan(0, k);
        var gc = s.conductances.AsSpan(0, k);
        var jacobian = s.jacobian.AsSpan(0, k * k);
        var system = s.system.AsSpan(0, k * k);
        var step = s.step.AsSpan(0, k);
        best.CopyTo(v);
        s.bestCurrents.AsSpan(0, k).CopyTo(r);
        var ignored = double.PositiveInfinity;
        Residual(k, v, ref ignored);
        BuildSystem(k);
        if (!SolveSmall(system, k, step, s.pivots)) {
            return;
        }

        for (var d = 0; d < k; d++) {
            var sum = trial[d];
            for (var l = s.blockStart[d]; l < s.blockEnd[d]; l++) {
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
    ///     The Newton system J = I - F (∂i/∂v - G_c) at the last evaluation, into <paramref name="target" /> (the system
    ///     buffer by default). ∂i/∂v is block diagonal (<see cref="INonlinearDevice.BlockStart" />): each column sums
    ///     over its own block only, in the same order as a full sum, so the result is the same to the bit.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    readonly void BuildSystem(int k, double[]? target = null) {
        var s = Newton;

        // On references (the solver's buffers hold k × k): this runs once or twice per iteration.
        ref var f = ref MemoryMarshal.GetArrayDataReference(s.impedance);
        ref var jacobian = ref MemoryMarshal.GetArrayDataReference(s.jacobian);
        ref var gc = ref MemoryMarshal.GetArrayDataReference(s.conductances);
        ref var system = ref MemoryMarshal.GetArrayDataReference(target ?? s.system);
        if (s.pairs && Vector128.IsHardwareAccelerated) {
            BuildPairs(k, ref f, ref jacobian, ref gc, ref system);
            return;
        }

        ref var first = ref MemoryMarshal.GetArrayDataReference(s.blockStart);
        ref var end = ref MemoryMarshal.GetArrayDataReference(s.blockEnd);
        for (var d = 0; d < k; d++) {
            ref var row = ref Unsafe.Add(ref f, d * k);
            for (var l = 0; l < k; l++) {
                var sum = d == l ? 1.0 : 0.0;
                var last = Unsafe.Add(ref end, l);
                for (var e = Unsafe.Add(ref first, l); e < last; e++) {
                    var slope = Unsafe.Add(ref jacobian, e * k + l) - (e == l ? Unsafe.Add(ref gc, e) : 0);
                    sum -= Unsafe.Add(ref row, e) * slope;
                }

                Unsafe.Add(ref system, d * k + l) = sum;
            }
        }
    }

    /// <summary>
    ///     <see cref="BuildSystem" /> when every device block is a pair of ports: both columns of a block at once, each
    ///     lane the same subtractions in the same order as the scalar sum (the slope's diagonal less G_c, the other
    ///     entries less 0, which leaves them as they are), so the same bits.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void BuildPairs(int k, ref double f, ref double jacobian, ref double gc, ref double system) {
        var identity0 = Vector128.Create(1.0, 0.0);
        var identity1 = Vector128.Create(0.0, 1.0);
        for (var b = 0; b < k; b += 2) {
            // The block's two slope rows, (∂i_b/∂v_b - G_c, ∂i_b/∂v_b+1) and (∂i_b+1/∂v_b, ∂i_b+1/∂v_b+1 - G_c).
            var upper = Vector128.Subtract(
                Vector128.LoadUnsafe(ref Unsafe.Add(ref jacobian, b * k + b)),
                Vector128.Create(Unsafe.Add(ref gc, b), 0.0)
            );
            var lower = Vector128.Subtract(
                Vector128.LoadUnsafe(ref Unsafe.Add(ref jacobian, (b + 1) * k + b)),
                Vector128.Create(0.0, Unsafe.Add(ref gc, b + 1))
            );
            for (var d = 0; d < k; d++) {
                ref var row = ref Unsafe.Add(ref f, d * k + b);
                var sum = d == b ? identity0 : d == b + 1 ? identity1 : Vector128<double>.Zero;
                sum = Vector128.Subtract(sum, Vector128.Multiply(Vector128.Create(row), upper));
                sum = Vector128.Subtract(sum, Vector128.Multiply(Vector128.Create(Unsafe.Add(ref row, 1)), lower));
                sum.StoreUnsafe(ref Unsafe.Add(ref system, d * k + b));
            }
        }
    }

    /// <summary>
    ///     Evaluates the devices at <paramref name="v" />: currents, Jacobian, residual currents r = i - G_c v, and -g into
    ///     the step buffer. Returns |g|² scaled by the tolerances, and remembers the best point seen.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    double Residual(int k, ReadOnlySpan<double> v, ref double bestNorm) {
        var s = Newton;
        var current = s.deviceCurrents.AsSpan(0, k);
        var trial = s.residual.AsSpan(0, k);
        var gc = s.conductances.AsSpan(0, k);
        var jacobian = s.jacobian.AsSpan(0, k * k);
        var step = s.step.AsSpan(0, k);
        var p = s.prediction.AsSpan(0, k);
        var f = s.impedance.AsSpan(0, k * k);
        // The devices only write their own blocks, and only blocks are read: the entries between them stay 0.
        ref var entries = ref MemoryMarshal.GetReference(jacobian);
        for (var d = 0; d < k; d++) {
            for (var l = s.blockStart[d]; l < s.blockEnd[d]; l++) {
                Unsafe.Add(ref entries, d * k + l) = 0;
            }
        }

        Device.Evaluate(v, current, jacobian, k);
        for (var d = 0; d < k; d++) {
            trial[d] = current[d] - gc[d] * v[d];
        }

        var norm = 0.0;
        var (absolute, relative) = (s.AbsoluteTolerance, s.RelativeTolerance);
        ref var r = ref MemoryMarshal.GetReference(trial);
        ref var rows = ref MemoryMarshal.GetReference(f);
        var port = 0;
        for (; port + 2 <= k; port += 2) {
            // Two rows at once, each its own sum in its own order (the currents read once for both).
            var (g, next) = (v[port] - p[port], v[port + 1] - p[port + 1]);
            ref var row = ref Unsafe.Add(ref rows, port * k);
            ref var below = ref Unsafe.Add(ref row, k);
            for (var e = 0; e < k; e++) {
                var drawn = Unsafe.Add(ref r, e);
                g -= Unsafe.Add(ref row, e) * drawn;
                next -= Unsafe.Add(ref below, e) * drawn;
            }

            (step[port], step[port + 1]) = (-g, -next);
            var scaled = g / (absolute + relative * Math.Abs(v[port]));
            var nextScaled = next / (absolute + relative * Math.Abs(v[port + 1]));
            norm += scaled * scaled;
            norm += nextScaled * nextScaled;
        }

        for (; port < k; port++) {
            var g = v[port] - p[port];
            ref var row = ref Unsafe.Add(ref rows, port * k);
            for (var e = 0; e < k; e++) {
                g -= Unsafe.Add(ref row, e) * Unsafe.Add(ref r, e);
            }

            step[port] = -g;
            var scaled = g / (absolute + relative * Math.Abs(v[port]));
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

    /// <summary>
    ///     Scaled |g|² per port below which a chord step may be tried (|g| within ten tolerances): the step is then
    ///     small whichever recent Jacobian computes it, so a factorisation from before a device switched cannot pass an
    ///     iterate as converged that a fresh one would move by more than a few tolerances.
    /// </summary>
    internal const double ChordNorm = 100;

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
    internal readonly double[] previousPrediction; // the last sample's p, for the linearised start
    internal bool primed; // whether the last sample's solution, Jacobian and p are there to start from
    internal readonly double[] impedance; // F, copied out of the scattering matrix when it changes
    internal readonly int[] pivots;
    internal readonly int[] blockStart; // per port: the first port of its device block
    internal readonly int[] blockEnd; // per port: one past the last port of its device block
    internal bool factored; // whether the system and pivot buffers hold the factors of the last Jacobian's system
    internal bool pairs; // whether the device blocks are all two ports (triodes, op-amps)

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
    ///     Start Newton from the last solution moved by the linearised circuit's response to this sample's change of
    ///     inputs (one Newton step on the last Jacobian, without a device evaluation) instead of extrapolating: on rough
    ///     signals (a cranked preamp, noise) it saves one or two iterations a sample, where extrapolating from the last two
    ///     samples guesses wrong. It takes precedence over <see cref="Extrapolate" />.
    /// </summary>
    public bool Linearise { get; set; }

    /// <summary>
    ///     Three or more ports: keep the last factored Newton system across iterations and samples and first try a chord
    ///     step on it (taken only when it converges unlimited), building and factoring a new one only when that fails.
    ///     The solution moves within the tolerances, so this is off by default (bit-for-bit renders); on a smooth signal
    ///     it saves most factorisations.
    /// </summary>
    public bool ReuseFactors { get; set; }

    /// <summary>
    ///     Iterations of plain (limited) Newton before a step must also reduce |g| (a backtracking line search). Junctions
    ///     climbing a steep exponential legitimately raise |g| for a while, so the line search only steps in for samples
    ///     that are struggling, e.g. an op-amp bouncing between its rails.
    /// </summary>
    public int LineSearchAfter { get; set; } = 16;

    /// <summary>
    ///     One port: iterations of plain Newton before a step that would leave the bracket the iterates have found (one
    ///     point where g &lt; 0, one where g &gt; 0) bisects it instead, and the sample carries on to
    ///     <see cref="MaxIterations" /> rather than restarting with the line search. A power amp under deep feedback
    ///     cycles across the grid-conduction kink on a few samples in a hundred, and these used to take 25 to 60
    ///     iterations; samples that converge sooner are untouched.
    /// </summary>
    public int SafeguardAfter { get; set; } = 8;

    /// <summary>Samples solved since the last <see cref="ResetStatistics" />.</summary>
    public long Samples { get; private set; }

    /// <summary>Device evaluations (Newton iterations) in total.</summary>
    public long Iterations { get; private set; }

    public int LastIterations { get; private set; }

    public int MostIterations { get; private set; }

    /// <summary>Samples that hit <see cref="MaxIterations" /> (or a singular Jacobian) and kept the best iterate.</summary>
    public long Unconverged { get; private set; }

    /// <summary>Samples where a device returned a non-finite value and the previous solution was repeated.</summary>
    public long NonFinite { get; private set; }

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
        previousPrediction = new double[k];
        impedance = new double[k * k];
        pivots = new int[k];
        blockStart = new int[k];
        blockEnd = new int[k];
    }

    public void ResetStatistics() {
        Samples = 0;
        Iterations = 0;
        LastIterations = 0;
        MostIterations = 0;
        Unconverged = 0;
        NonFinite = 0;
    }

    internal void Record(int count, bool converged, bool finite) {
        primed = finite;
        Samples++;
        Iterations += count;
        LastIterations = count;
        MostIterations = Math.Max(MostIterations, count);
        if (!converged && finite) {
            Unconverged++;
        }

        if (!finite) {
            NonFinite++;
        }
    }
}
