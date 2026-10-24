using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

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
        var largest = Largest(ref m, n * n);
        var threshold = tiny * Math.Max(largest, double.Epsilon);
        // An even order keeps the rows' pairs of entries at even offsets throughout, so that every two-lane load reads
        // what one two-lane store wrote (a load that straddles two stores waits for both to retire).
        var paired = Vector128.IsHardwareAccelerated && n % 2 == 0;
        for (var k = 0; k < n; k++) {
            var pivot = k;
            var best = Math.Abs(Unsafe.Add(ref m, k * n + k));
            for (var r = k + 1; r < n; r++) {
                var candidate = Math.Abs(Unsafe.Add(ref m, r * n + k));
                var greater = candidate > best;
                best = greater ? candidate : best;
                pivot = greater ? r : pivot;
            }

            pivots[k] = pivot;
            if (!(best > threshold)) {
                return false; // also catches NaN
            }

            ref var top = ref Unsafe.Add(ref m, k * n);
            if (pivot != k) {
                ref var other = ref Unsafe.Add(ref m, pivot * n);
                var j = 0;
                if (paired) {
                    for (; j < n; j += 2) {
                        var x = Vector128.LoadUnsafe(ref Unsafe.Add(ref top, j));
                        Vector128.LoadUnsafe(ref Unsafe.Add(ref other, j)).StoreUnsafe(ref Unsafe.Add(ref top, j));
                        x.StoreUnsafe(ref Unsafe.Add(ref other, j));
                    }
                }

                for (; j < n; j++) {
                    ref var a1 = ref Unsafe.Add(ref top, j);
                    ref var a2 = ref Unsafe.Add(ref other, j);
                    (a1, a2) = (a2, a1);
                }
            }

            var inverse = 1 / Unsafe.Add(ref top, k);
            var i = k + 1;
            if (paired) {
                // Two rows at a time, each with its own multiplier: they share the pivot row's loads.
                for (; i + 2 <= n; i += 2) {
                    ref var first = ref Unsafe.Add(ref m, i * n);
                    ref var second = ref Unsafe.Add(ref first, n);
                    var f0 = Unsafe.Add(ref first, k) * inverse;
                    var f1 = Unsafe.Add(ref second, k) * inverse;
                    Unsafe.Add(ref first, k) = f0;
                    Unsafe.Add(ref second, k) = f1;
                    if (f0 == 0 || f1 == 0) {
                        if (f0 != 0) {
                            Eliminate(ref first, ref top, f0, k + 1, n, paired);
                        }

                        if (f1 != 0) {
                            Eliminate(ref second, ref top, f1, k + 1, n, paired);
                        }

                        continue;
                    }

                    EliminateTwo(ref first, ref second, ref top, f0, f1, k + 1, n);
                }
            }

            for (; i < n; i++) {
                ref var row = ref Unsafe.Add(ref m, i * n);
                var factor = Unsafe.Add(ref row, k) * inverse;
                Unsafe.Add(ref row, k) = factor;
                if (factor == 0) {
                    continue;
                }

                Eliminate(ref row, ref top, factor, k + 1, n, paired);
            }
        }

        return true;
    }

    /// <summary><see cref="Eliminate" /> (paired) on two rows with their own factors, sharing the pivot row's loads.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void EliminateTwo(
        ref double first,
        ref double second,
        ref double top,
        double f0,
        double f1,
        int start,
        int end
    ) {
        var j = start;
        if ((j & 1) != 0) {
            var t = Unsafe.Add(ref top, j);
            Unsafe.Add(ref first, j) -= f0 * t;
            Unsafe.Add(ref second, j) -= f1 * t;
            j++;
        }

        var (v0, v1) = (Vector128.Create(f0), Vector128.Create(f1));
        for (; j < end; j += 2) {
            var pivot = Vector128.LoadUnsafe(ref Unsafe.Add(ref top, j));
            ref var a = ref Unsafe.Add(ref first, j);
            ref var b = ref Unsafe.Add(ref second, j);
            Vector128.Subtract(Vector128.LoadUnsafe(ref a), Vector128.Multiply(v0, pivot)).StoreUnsafe(ref a);
            Vector128.Subtract(Vector128.LoadUnsafe(ref b), Vector128.Multiply(v1, pivot)).StoreUnsafe(ref b);
        }
    }

    /// <summary>
    ///     The largest magnitude among <paramref name="count" /> entries (0 for none, NaN if one is NaN). Maximum is
    ///     exact in any order, so four running maxima, merged at the end, give the same value as one without its chain
    ///     of dependent steps.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double Largest(ref double m, int count) {
        var (l0, l1, l2, l3) = (0.0, 0.0, 0.0, 0.0);
        var i = 0;
        for (; i + 4 <= count; i += 4) {
            l0 = Math.Max(l0, Math.Abs(Unsafe.Add(ref m, i)));
            l1 = Math.Max(l1, Math.Abs(Unsafe.Add(ref m, i + 1)));
            l2 = Math.Max(l2, Math.Abs(Unsafe.Add(ref m, i + 2)));
            l3 = Math.Max(l3, Math.Abs(Unsafe.Add(ref m, i + 3)));
        }

        for (; i < count; i++) {
            l0 = Math.Max(l0, Math.Abs(Unsafe.Add(ref m, i)));
        }

        return Math.Max(Math.Max(l0, l1), Math.Max(l2, l3));
    }

    /// <summary>
    ///     row[j] -= factor × top[j] for j from <paramref name="start" /> to <paramref name="end" />. With
    ///     <paramref name="paired" /> (an even order), two entries at a time from an even j (an odd first one alone),
    ///     each lane the same multiply and subtract as one at a time, so the same bits.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Eliminate(ref double row, ref double top, double factor, int start, int end, bool paired) {
        var j = start;
        if (paired) {
            if ((j & 1) != 0) {
                Unsafe.Add(ref row, j) -= factor * Unsafe.Add(ref top, j);
                j++;
            }

            var f = Vector128.Create(factor);
            for (; j < end; j += 2) {
                ref var target = ref Unsafe.Add(ref row, j);
                var product = Vector128.Multiply(f, Vector128.LoadUnsafe(ref Unsafe.Add(ref top, j)));
                Vector128.Subtract(Vector128.LoadUnsafe(ref target), product).StoreUnsafe(ref target);
            }

            return;
        }

        for (; j < end; j++) {
            Unsafe.Add(ref row, j) -= factor * Unsafe.Add(ref top, j);
        }
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
