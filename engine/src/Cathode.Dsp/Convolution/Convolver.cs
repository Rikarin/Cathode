using System.Numerics;
using System.Runtime.InteropServices;

namespace Cathode.Dsp.Convolution;

/// <summary>
///     Zero-latency mono FIR convolution for impulse responses (cabinets, rooms), usable with any callback size.
/// </summary>
/// <remarks>
///     <para>
///         The first <see cref="PartitionSize" /> taps run as a direct-form FIR. The remaining taps run as uniform
///         partitioned overlap-save convolution (Wefers 2014), whose inherent one-partition delay lines up exactly with
///         those later taps (the two-stage idea from Gardner, JAES 1995). The result is exact, adds no latency, and does
///         not depend on how the input is split into callbacks.
///     </para>
///     <para>Construction allocates; <see cref="Process" /> and <see cref="Reset" /> do not.</para>
/// </remarks>
public sealed class Convolver {
    /// <summary>Partition length B. The FFT size is 2B.</summary>
    public const int PartitionSize = 64;

    const int B = PartitionSize;
    const int N = 2 * B;
    const int Bins = B + 1; // a real signal's spectrum is Hermitian: bins 0..N/2 are enough

    readonly Fft fft = new(N);
    readonly float[] head; // taps 0..B-1, direct form
    readonly float[] history; // last B inputs, stored twice so a window is always contiguous
    int historyPosition;

    readonly int partitions; // partitions of taps B..L-1
    readonly float[][] irRe; // spectra of the tail partitions
    readonly float[][] irIm;
    readonly float[][] inRe; // frequency-domain delay line of input spectra
    readonly float[][] inIm;
    int fdlPosition;
    readonly float[] frame = new float[N]; // [previous block | current block]
    readonly float[] tailOut = new float[B];
    readonly float[] re = new float[N];
    readonly float[] im = new float[N];
    readonly float[] accRe = new float[Bins];
    readonly float[] accIm = new float[Bins];
    int position;

    /// <summary>Impulse response length in samples.</summary>
    public int Length { get; }

    public Convolver(ReadOnlySpan<float> impulseResponse) {
        if (impulseResponse.IsEmpty) {
            throw new ArgumentException("The impulse response is empty.", nameof(impulseResponse));
        }

        Length = impulseResponse.Length;
        head = new float[B];
        impulseResponse[..Math.Min(B, Length)].CopyTo(head);
        history = new float[2 * B];

        partitions = Math.Max(0, (Length - B + B - 1) / B);
        irRe = new float[partitions][];
        irIm = new float[partitions][];
        inRe = new float[partitions][];
        inIm = new float[partitions][];
        for (var p = 0; p < partitions; p++) {
            Array.Clear(re);
            Array.Clear(im);
            var start = B + p * B;
            impulseResponse.Slice(start, Math.Min(B, Length - start)).CopyTo(re);
            fft.Forward(re, im);
            irRe[p] = re[..Bins];
            irIm[p] = im[..Bins];
            inRe[p] = new float[Bins];
            inIm[p] = new float[Bins];
        }
    }

    public void Reset() {
        Array.Clear(history);
        Array.Clear(frame);
        Array.Clear(tailOut);
        foreach (var spectrum in inRe) {
            Array.Clear(spectrum);
        }

        foreach (var spectrum in inIm) {
            Array.Clear(spectrum);
        }

        historyPosition = 0;
        fdlPosition = 0;
        position = 0;
    }

    /// <summary>Convolves <paramref name="samples" /> in place.</summary>
    public void Process(Span<float> samples) {
        for (var i = 0; i < samples.Length; i++) {
            var x = samples[i];

            historyPosition = historyPosition == 0 ? B - 1 : historyPosition - 1;
            history[historyPosition] = x;
            history[historyPosition + B] = x;
            var y = Dot(head, history.AsSpan(historyPosition, B));

            if (partitions > 0) {
                y += tailOut[position];
                frame[B + position] = x;
            }

            samples[i] = y;
            if (++position == B) {
                position = 0;
                if (partitions > 0) {
                    RunTail();
                }
            }
        }
    }

    /// <summary>One overlap-save step over the tail partitions, producing the next block's tail output.</summary>
    void RunTail() {
        frame.CopyTo(re, 0);
        Array.Clear(im);
        fft.Forward(re, im);
        fdlPosition = fdlPosition == 0 ? partitions - 1 : fdlPosition - 1;
        re.AsSpan(0, Bins).CopyTo(inRe[fdlPosition]);
        im.AsSpan(0, Bins).CopyTo(inIm[fdlPosition]);

        Array.Clear(accRe);
        Array.Clear(accIm);
        for (var p = 0; p < partitions; p++) {
            var slot = fdlPosition + p;
            if (slot >= partitions) {
                slot -= partitions;
            }

            var xr = inRe[slot];
            var xi = inIm[slot];
            var hr = irRe[p];
            var hi = irIm[p];
            for (var k = 0; k < Bins; k++) {
                accRe[k] += xr[k] * hr[k] - xi[k] * hi[k];
                accIm[k] += xr[k] * hi[k] + xi[k] * hr[k];
            }
        }

        for (var k = 0; k < Bins; k++) {
            re[k] = accRe[k];
            im[k] = accIm[k];
        }

        for (var k = Bins; k < N; k++) {
            re[k] = accRe[N - k];
            im[k] = -accIm[N - k];
        }

        fft.Inverse(re, im);
        re.AsSpan(B, B).CopyTo(tailOut);
        frame.AsSpan(B, B).CopyTo(frame); // the current block becomes the previous one
    }

    static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b) {
        var sum = Vector<float>.Zero;
        var i = 0;
        var va = MemoryMarshal.Cast<float, Vector<float>>(a);
        var vb = MemoryMarshal.Cast<float, Vector<float>>(b);
        for (; i < va.Length; i++) {
            sum += va[i] * vb[i];
        }

        var result = Vector.Sum(sum);
        for (i *= Vector<float>.Count; i < a.Length; i++) {
            result += a[i] * b[i];
        }

        return result;
    }
}
