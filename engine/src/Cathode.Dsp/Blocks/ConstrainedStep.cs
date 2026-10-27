using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     The constrained re-solve that <see cref="CircuitRoot{TSource,TDevice}" /> and
///     <see cref="Wdf.RTypeRoot{TPorts,TDevice}" /> share: when the devices limited some ports' Newton steps, the
///     linearised system is solved again for the free ports, with the limited ones held at their limited proposals.
/// </summary>
static class ConstrainedStep {
    /// <summary>
    ///     Gathers the free ports' system: their rows and columns of <paramref name="full" /> into
    ///     <paramref name="a" />, and into <paramref name="b" /> their right-hand side less the held ports' steps
    ///     through the matrix.
    /// </summary>
    /// <param name="full">The full linearised system, row-major.</param>
    /// <param name="rhs">Its right-hand side.</param>
    /// <param name="free">The free ports, <paramref name="count" /> of them.</param>
    /// <param name="count">The number of free ports.</param>
    /// <param name="k">The ports.</param>
    /// <param name="v">The iterate.</param>
    /// <param name="unlimited">The Newton step before limiting.</param>
    /// <param name="proposed">The limited proposal.</param>
    /// <param name="a">The free ports' matrix.</param>
    /// <param name="b">The free ports' right-hand side.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Gather(
        ReadOnlySpan<double> full,
        ReadOnlySpan<double> rhs,
        ReadOnlySpan<int> free,
        int count,
        int k,
        ReadOnlySpan<double> v,
        ReadOnlySpan<double> unlimited,
        ReadOnlySpan<double> proposed,
        Span<double> a,
        Span<double> b
    ) {
        for (var i = 0; i < count; i++) {
            var row = free[i];
            var sum = rhs[row];
            for (var d = 0; d < k; d++) {
#pragma warning disable SK2003 // Exact by design: the limiter leaves a free port's step bit for bit.
                if (proposed[d] != v[d] + unlimited[d]) {
#pragma warning restore SK2003
                    sum -= full[row * k + d] * (proposed[d] - v[d]);
                }
            }

            b[i] = sum;
            for (var c = 0; c < count; c++) {
                a[i * count + c] = full[row * k + free[c]];
            }
        }
    }
}
