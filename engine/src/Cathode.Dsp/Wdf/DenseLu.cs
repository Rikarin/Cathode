namespace Cathode.Dsp.Wdf;

/// <summary>
///     In-place LU factorisation with partial pivoting of small dense row-major matrices, and the matching solves. No
///     allocation: the caller owns every buffer, so R-type adaptors can refactor on the audio thread.
/// </summary>
public static class DenseLu {
    /// <summary>
    ///     Factors the n × n matrix <paramref name="a" /> into L U in place (unit lower triangle below the diagonal),
    ///     recording the row swaps in <paramref name="pivots" />. Returns false when a pivot falls below
    ///     <paramref name="tiny" /> times the largest entry, i.e. the matrix is singular to working precision.
    /// </summary>
    public static bool Factor(Span<double> a, int n, Span<int> pivots, double tiny = 1e-300) {
        var largest = 0.0;
        for (var i = 0; i < n * n; i++) {
            largest = Math.Max(largest, Math.Abs(a[i]));
        }

        var threshold = tiny * Math.Max(largest, double.Epsilon);
        for (var k = 0; k < n; k++) {
            var pivot = k;
            var best = Math.Abs(a[k * n + k]);
            for (var i = k + 1; i < n; i++) {
                var candidate = Math.Abs(a[i * n + k]);
                if (candidate > best) {
                    best = candidate;
                    pivot = i;
                }
            }

            pivots[k] = pivot;
            if (!(best > threshold)) {
                return false; // also catches NaN
            }

            if (pivot != k) {
                for (var j = 0; j < n; j++) {
                    (a[k * n + j], a[pivot * n + j]) = (a[pivot * n + j], a[k * n + j]);
                }
            }

            var inverse = 1 / a[k * n + k];
            for (var i = k + 1; i < n; i++) {
                var factor = a[i * n + k] * inverse;
                a[i * n + k] = factor;
                if (factor == 0) {
                    continue;
                }

                for (var j = k + 1; j < n; j++) {
                    a[i * n + j] -= factor * a[k * n + j];
                }
            }
        }

        return true;
    }

    /// <summary>
    ///     Solves A X = B in place for the row-major n × <paramref name="columns" /> right-hand side
    ///     <paramref name="b" />, given the factors from <see cref="Factor" />.
    /// </summary>
    public static void Solve(ReadOnlySpan<double> lu, int n, ReadOnlySpan<int> pivots, Span<double> b, int columns) {
        for (var k = 0; k < n; k++) {
            var pivot = pivots[k];
            if (pivot != k) {
                for (var j = 0; j < columns; j++) {
                    (b[k * columns + j], b[pivot * columns + j]) = (b[pivot * columns + j], b[k * columns + j]);
                }
            }
        }

        // Forward substitution with the unit lower triangle, then back substitution with the upper one.
        for (var i = 1; i < n; i++) {
            for (var k = 0; k < i; k++) {
                var factor = lu[i * n + k];
                if (factor == 0) {
                    continue;
                }

                for (var j = 0; j < columns; j++) {
                    b[i * columns + j] -= factor * b[k * columns + j];
                }
            }
        }

        for (var i = n - 1; i >= 0; i--) {
            for (var k = i + 1; k < n; k++) {
                var factor = lu[i * n + k];
                if (factor == 0) {
                    continue;
                }

                for (var j = 0; j < columns; j++) {
                    b[i * columns + j] -= factor * b[k * columns + j];
                }
            }

            var inverse = 1 / lu[i * n + i];
            for (var j = 0; j < columns; j++) {
                b[i * columns + j] *= inverse;
            }
        }
    }

    /// <summary>Solves the n × n system A x = b in place for one right-hand side. Returns false if A is singular.</summary>
    public static bool SolveInPlace(Span<double> a, int n, Span<int> pivots, Span<double> b) {
        if (!Factor(a, n, pivots)) {
            return false;
        }

        Solve(a, n, pivots, b, 1);
        return true;
    }
}
