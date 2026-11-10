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
    public long Total => Unconverged + NonFinite + FailedRecomputes;

    /// <summary>A root's counters (<see cref="NewtonSolver" />); nothing for a stage without one.</summary>
    public static SolverFaults Of(NewtonSolver? newton) =>
        newton is null ? default : new(newton.Unconverged, newton.NonFinite, newton.FailedRecomputes);

    public static SolverFaults operator +(SolverFaults a, SolverFaults b) =>
        new(a.Unconverged + b.Unconverged, a.NonFinite + b.NonFinite, a.FailedRecomputes + b.FailedRecomputes);
}
