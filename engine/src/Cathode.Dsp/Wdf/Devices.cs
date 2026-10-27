using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Wdf;

/// <summary>
///     A nonlinear multiport in the Kirchhoff domain, for the root of an <see cref="RTypeRoot{TPorts,TDevice}" />: given
///     its K port voltages it returns the K port currents (each flowing in at the port's plus node) and their Jacobian.
///     Implemented by structs and combined with <see cref="Devices{TA,TB}" />, so the root's Newton solver calls them
///     without virtual dispatch.
/// </summary>
public interface INonlinearDevice {
    /// <summary>Number of ports K; the netlist declares them in the same order.</summary>
    int Ports { get; }

    /// <summary>
    ///     A conductance typical of port <paramref name="port" />, stamped into the R-type adaptor so that the linear network
    ///     stays regular (see <see cref="RTypeScattering" />). It does not change the solution.
    /// </summary>
    double Conductance(int port);

    /// <summary>
    ///     Port currents for port voltages <paramref name="voltages" />, and ∂i_j/∂v_k into
    ///     <paramref name="jacobian" />[j × <paramref name="stride" /> + k]. The Jacobian arrives zeroed.
    /// </summary>
    void Evaluate(ReadOnlySpan<double> voltages, Span<double> currents, Span<double> jacobian, int stride);

    /// <summary>
    ///     Damps a Newton step from <paramref name="previous" /> to <paramref name="proposed" /> in place, the way SPICE
    ///     limits junction voltages (pnjlim): exponential ports may not jump so far that their current explodes.
    /// </summary>
    void Limit(ReadOnlySpan<double> previous, Span<double> proposed);

    /// <summary>Accepts the converged voltages of a sample (devices with memory update their state here).</summary>
    void Commit(ReadOnlySpan<double> voltages);

    /// <summary>Clears any state.</summary>
    void Reset();

    /// <summary>
    ///     The first port of the block of ports that port <paramref name="port" /> belongs to: the Jacobian is block
    ///     diagonal, each port's current depending only on its own block's voltages. A single device is one block (the
    ///     default); <see cref="Devices{TA,TB}" /> puts its two devices in blocks of their own, so the root's Newton
    ///     solver skips the zeros between them. Read once, when the root is built.
    /// </summary>
    int BlockStart(int port) => 0;
}

/// <summary>No nonlinear device: an R-type root that is linear.</summary>
public struct NoDevice : INonlinearDevice {
    public readonly int Ports => 0;

    public readonly double Conductance(int port) => 0;

    public readonly void Evaluate(
        ReadOnlySpan<double> voltages,
        Span<double> currents,
        Span<double> jacobian,
        int stride
    ) { }

    public readonly void Limit(ReadOnlySpan<double> previous, Span<double> proposed) { }

    public readonly void Commit(ReadOnlySpan<double> voltages) { }

    public readonly void Reset() { }
}

/// <summary>
///     Two devices side by side: ports of <see cref="A" /> first, then <see cref="B" />. Nest it for more
///     (<c>Devices&lt;OpAmp, Devices&lt;DiodeClipper, Bjt&gt;&gt;</c>). The Jacobian is block diagonal: the devices only
///     interact through the network.
/// </summary>
public struct Devices<TA, TB>(TA a, TB b) : INonlinearDevice
    where TA : struct, INonlinearDevice
    where TB : struct, INonlinearDevice {
    public TA A = a;
    public TB B = b;

    public readonly int Ports => A.Ports + B.Ports;

    public readonly double Conductance(int port) =>
        port < A.Ports ? A.Conductance(port) : B.Conductance(port - A.Ports);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Evaluate(
        ReadOnlySpan<double> voltages,
        Span<double> currents,
        Span<double> jacobian,
        int stride
    ) {
        var k = A.Ports;
        A.Evaluate(voltages[..k], currents[..k], jacobian, stride);
        B.Evaluate(voltages[k..], currents[k..], jacobian[(k * stride + k)..], stride);
    }

    public readonly void Limit(ReadOnlySpan<double> previous, Span<double> proposed) {
        var k = A.Ports;
        A.Limit(previous[..k], proposed[..k]);
        B.Limit(previous[k..], proposed[k..]);
    }

    public void Commit(ReadOnlySpan<double> voltages) {
        var k = A.Ports;
        A.Commit(voltages[..k]);
        B.Commit(voltages[k..]);
    }

    public void Reset() {
        A.Reset();
        B.Reset();
    }

    public readonly int BlockStart(int port) =>
        port < A.Ports ? A.BlockStart(port) : A.Ports + B.BlockStart(port - A.Ports);
}

/// <summary>Numerical helpers shared by the device models.</summary>
public static class DeviceMath {
    /// <summary>Above this exponent <see cref="LimitedExp" /> continues linearly.</summary>
    public const double MaxExponent = 40;

    /// <summary>
    ///     e^x, continued by its tangent above <see cref="MaxExponent" />: C¹, convex and monotonic like the exponential,
    ///     so Newton's method still converges, but never overflows. Returns the value and writes the derivative.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double LimitedExp(double x, out double derivative) {
        if (x <= MaxExponent) {
            var e = Math.Exp(x);
            derivative = e;
            return e;
        }

        var top = Math.Exp(MaxExponent);
        derivative = top;
        return top * (1 + x - MaxExponent);
    }

    /// <summary>
    ///     Junction step limiting for Newton's method, after SPICE's pnjlim (Nagel, "SPICE2", 1975), for a junction with
    ///     emission voltage <paramref name="vt" /> (n Vt) and critical voltage <paramref name="critical" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Upwards, above the critical voltage, a step is compressed to n Vt ln(1 + Δ / n Vt): the voltage at which the
    ///         exponential reaches the current the tangent predicted, so the current cannot explode.
    ///     </para>
    ///     <para>
    ///         Downwards, from a conducting junction, a step of more than n Vt means the tangent predicts no current at
    ///         all: the junction then steps at least 10 n Vt down (a factor of 20 000 in current), or to Newton's own
    ///         proposal if that is lower. Smaller downward steps are Newton's own. Expanding them as well (to
    ///         n Vt ln(1 + Δ / n Vt), exact for a lone junction) saves an iteration or two on a one-port clipper, but in a
    ///         coupled circuit, where the tangent's Δ is not the junction's alone, it overshoots: a BJT stage or an op-amp
    ///         with feedback diodes under hard drive then leaves hundreds of samples unconverged instead of a handful
    ///         (the hard-drive stages of <c>NonlinearRootTests</c>). Only the path changes, never the solution.
    ///     </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double LimitJunction(double previous, double proposed, double vt, double critical) {
        var step = proposed - previous;
        if (step > 2 * vt && proposed > critical) {
            if (previous > 0) {
                var argument = 1 + step / vt;
                return previous + vt * Math.Log(argument);
            }

            return vt * Math.Log(proposed / vt);
        }

        if (step < -vt && previous > critical - 10 * vt) {
            return Math.Min(proposed, previous - 10 * vt);
        }

        return proposed;
    }

    /// <summary>The critical voltage of pnjlim: where the junction's curvature is largest, n Vt ln(n Vt / (√2 Is)).</summary>
    public static double CriticalVoltage(double saturationCurrent, double vt) =>
        vt * Math.Log(vt / (Math.Sqrt(2) * saturationCurrent));

    /// <summary>ln(1 + e^x) without overflow, and its derivative, the logistic function.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Softplus(double x, out double derivative) {
        if (x > 30) {
            derivative = 1;
            return x;
        }

        var e = Math.Exp(x);
        derivative = e / (1 + e);
        return Math.Log(1 + e);
    }
}
