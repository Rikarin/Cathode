using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
        if (a.Length < n * n || pivots.Length < n) {
            throw new ArgumentException("The buffers are smaller than the system.", nameof(a));
        }

        // On references, without bounds checks (the lengths are checked above).
        ref var m = ref MemoryMarshal.GetReference(a);
        var largest = 0.0;
        for (var i = 0; i < n * n; i++) {
            largest = Math.Max(largest, Math.Abs(Unsafe.Add(ref m, i)));
        }

        var threshold = tiny * Math.Max(largest, double.Epsilon);
        for (var k = 0; k < n; k++) {
            var pivot = k;
            var best = Math.Abs(Unsafe.Add(ref m, k * n + k));
            for (var i = k + 1; i < n; i++) {
                var candidate = Math.Abs(Unsafe.Add(ref m, i * n + k));
                if (candidate > best) {
                    best = candidate;
                    pivot = i;
                }
            }

            pivots[k] = pivot;
            if (!(best > threshold)) {
                return false; // also catches NaN
            }

            ref var top = ref Unsafe.Add(ref m, k * n);
            if (pivot != k) {
                ref var other = ref Unsafe.Add(ref m, pivot * n);
                for (var j = 0; j < n; j++) {
                    ref var a1 = ref Unsafe.Add(ref top, j);
                    ref var a2 = ref Unsafe.Add(ref other, j);
                    (a1, a2) = (a2, a1);
                }
            }

            var inverse = 1 / Unsafe.Add(ref top, k);
            for (var i = k + 1; i < n; i++) {
                ref var row = ref Unsafe.Add(ref m, i * n);
                var factor = Unsafe.Add(ref row, k) * inverse;
                Unsafe.Add(ref row, k) = factor;
                if (factor == 0) {
                    continue;
                }

                for (var j = k + 1; j < n; j++) {
                    Unsafe.Add(ref row, j) -= factor * Unsafe.Add(ref top, j);
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
        if (columns == 1) {
            Solve(lu, n, pivots, b);
            return;
        }

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

    /// <summary>
    ///     <see cref="Solve(ReadOnlySpan{double}, int, ReadOnlySpan{int}, Span{double}, int)" /> for one right-hand
    ///     side: the same operations in the same order (so the same result to the bit), on references, without the
    ///     column loops and bounds checks. A Newton solver with several ports calls it a few times a sample.
    /// </summary>
    public static void Solve(ReadOnlySpan<double> lu, int n, ReadOnlySpan<int> pivots, Span<double> b) {
        if (lu.Length < n * n || pivots.Length < n || b.Length < n) {
            throw new ArgumentException("The buffers are smaller than the system.", nameof(lu));
        }

        ref var a = ref MemoryMarshal.GetReference(lu);
        ref var x = ref MemoryMarshal.GetReference(b);
        ref var swaps = ref MemoryMarshal.GetReference(pivots);
        for (var k = 0; k < n; k++) {
            var pivot = Unsafe.Add(ref swaps, k);
            if (pivot != k) {
                (Unsafe.Add(ref x, k), Unsafe.Add(ref x, pivot)) = (Unsafe.Add(ref x, pivot), Unsafe.Add(ref x, k));
            }
        }

        for (var i = 1; i < n; i++) {
            ref var row = ref Unsafe.Add(ref a, i * n);
            var sum = Unsafe.Add(ref x, i);
            for (var k = 0; k < i; k++) {
                var factor = Unsafe.Add(ref row, k);
                if (factor != 0) {
                    sum -= factor * Unsafe.Add(ref x, k);
                }
            }

            Unsafe.Add(ref x, i) = sum;
        }

        for (var i = n - 1; i >= 0; i--) {
            ref var row = ref Unsafe.Add(ref a, i * n);
            var sum = Unsafe.Add(ref x, i);
            for (var k = i + 1; k < n; k++) {
                var factor = Unsafe.Add(ref row, k);
                if (factor != 0) {
                    sum -= factor * Unsafe.Add(ref x, k);
                }
            }

            var inverse = 1 / Unsafe.Add(ref row, i);
            Unsafe.Add(ref x, i) = sum * inverse;
        }
    }

    /// <summary>Solves the n × n system A x = b in place for one right-hand side. Returns false if A is singular.</summary>
    public static bool SolveInPlace(Span<double> a, int n, Span<int> pivots, Span<double> b) {
        if (!Factor(a, n, pivots)) {
            return false;
        }

        Solve(a, n, pivots, b);
        return true;
    }
}
