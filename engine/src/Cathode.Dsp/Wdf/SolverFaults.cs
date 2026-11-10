namespace Cathode.Dsp.Wdf;

/// <summary>
///     What a nonlinear solver gave up on, counted since it was built: the numbers to ask for when a rig clicks, a
///     pot stops responding or an amp goes silent.
/// </summary>
/// <param name="Unconverged">Samples Newton ran out of iterations on and kept its best iterate.</param>
/// <param name="NonFinite">Samples a device returned a non-finite value on, the previous solution repeated.</param>
/// <param name="FailedRecomputes">
///     Component changes that left the network singular, the previous matrix kept.
/// </param>
public readonly record struct SolverFaults(long Unconverged, long NonFinite, long FailedRecomputes) {
    /// <summary>
    ///     The ids a block reports the three counts under as its own meters (<see cref="IBlockMeters" />), in the
    ///     order <see cref="Write" /> writes them. They are counts, not dB, and 0 on a healthy rig.
    /// </summary>
    public static IReadOnlyList<string> MeterIds { get; } =
        ["solverUnconverged", "solverNonFinite", "solverFailedRecomputes"];

    public long Total => Unconverged + NonFinite + FailedRecomputes;

    /// <summary>The three counts as a block's meter values, in the order of <see cref="MeterIds" />.</summary>
    public void Write(Span<float> values) {
        values[0] = Unconverged;
        values[1] = NonFinite;
        values[2] = FailedRecomputes;
    }

    /// <summary>A root's counters (<see cref="NewtonSolver" />); nothing for a stage without one.</summary>
    public static SolverFaults Of(NewtonSolver? newton) =>
        newton is null ? default : new(newton.Unconverged, newton.NonFinite, newton.FailedRecomputes);

    public static SolverFaults operator +(SolverFaults a, SolverFaults b) =>
        new(a.Unconverged + b.Unconverged, a.NonFinite + b.NonFinite, a.FailedRecomputes + b.FailedRecomputes);
}
