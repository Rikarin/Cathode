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
///     <para>
///         All state, including the impulse response's spectra, lives in native memory (<see cref="NativeBuffer" />), so
///         loading a cab during a preset change leaves next to nothing for the GC. Dispose it with its block.
///         <see cref="Process" /> and <see cref="Reset" /> do not allocate.
///     </para>
/// </remarks>
public sealed class Convolver : IDisposable {
    /// <summary>Partition length B. The FFT size is 2B.</summary>
    public const int PartitionSize = 64;

    const int B = PartitionSize;
    const int N = 2 * B;
    const int Bins = B + 1; // a real signal's spectrum is Hermitian: bins 0..N/2 are enough

    /// <summary>Twiddles and bit reversal only: read-only after construction, so every convolver shares one.</summary>
    static readonly Fft Fft = new(N);

    readonly NativeBuffer impulse; // the whole impulse response, for inspection
    readonly NativeBuffer history; // last B inputs, stored twice so a window is always contiguous
    int historyPosition;

    readonly int partitions; // partitions of taps B..L-1
    readonly NativeBuffer irRe; // spectra of the tail partitions, Bins floats each
    readonly NativeBuffer irIm;
    readonly NativeBuffer inRe; // frequency-domain delay line of input spectra, Bins floats each
    readonly NativeBuffer inIm;
    int fdlPosition;
    readonly NativeBuffer frame = new(N); // [previous block | current block]
    readonly NativeBuffer tailOut = new(B);
    readonly NativeBuffer re = new(N);
    readonly NativeBuffer im = new(N);
    readonly NativeBuffer accRe = new(Bins);
    readonly NativeBuffer accIm = new(Bins);
    int position;

    /// <summary>Impulse response length in samples.</summary>
    public int Length { get; }

    /// <summary>The impulse response being convolved.</summary>
    public ReadOnlySpan<float> ImpulseResponse => impulse.Span;

    public Convolver(ReadOnlySpan<float> impulseResponse) {
        if (impulseResponse.IsEmpty) {
            throw new ArgumentException("The impulse response is empty.", nameof(impulseResponse));
        }

        Length = impulseResponse.Length;
        impulse = new(Length);
        impulseResponse.CopyTo(impulse.Span);
        history = new(2 * B);

        partitions = Math.Max(0, (Length - B + B - 1) / B);
        irRe = new(partitions * Bins);
        irIm = new(partitions * Bins);
        inRe = new(partitions * Bins);
        inIm = new(partitions * Bins);
        var re = this.re.Span;
        var im = this.im.Span;
        for (var p = 0; p < partitions; p++) {
            re.Clear();
            im.Clear();
            var start = B + p * B;
            impulseResponse.Slice(start, Math.Min(B, Length - start)).CopyTo(re);
            Fft.Forward(re, im);
            re[..Bins].CopyTo(Spectrum(irRe, p));
            im[..Bins].CopyTo(Spectrum(irIm, p));
        }

        re.Clear();
        im.Clear();
    }

    public void Reset() {
        history.Clear();
        frame.Clear();
        tailOut.Clear();
        inRe.Clear();
        inIm.Clear();
        historyPosition = 0;
        fdlPosition = 0;
        position = 0;
    }

    public void Dispose() {
        foreach (var buffer in (ReadOnlySpan<NativeBuffer>)[
                     impulse, history, irRe, irIm, inRe, inIm, frame, tailOut, re, im, accRe, accIm
                 ]) {
            buffer.Dispose();
        }
    }

    /// <summary>Convolves <paramref name="samples" /> in place.</summary>
    public void Process(Span<float> samples) {
        var head = impulse.Span[..Math.Min(B, Length)];
        var history = this.history.Span;
        var frame = this.frame.Span;
        var tailOut = this.tailOut.Span;
        for (var i = 0; i < samples.Length; i++) {
            var x = samples[i];

            historyPosition = historyPosition == 0 ? B - 1 : historyPosition - 1;
            history[historyPosition] = x;
            history[historyPosition + B] = x;
            var y = Dot(head, history.Slice(historyPosition, head.Length));

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

    static Span<float> Spectrum(NativeBuffer spectra, int partition) => spectra.Span.Slice(partition * Bins, Bins);

    /// <summary>One overlap-save step over the tail partitions, producing the next block's tail output.</summary>
    void RunTail() {
        var re = this.re.Span;
        var im = this.im.Span;
        var accRe = this.accRe.Span;
        var accIm = this.accIm.Span;
        var frame = this.frame.Span;
        frame.CopyTo(re);
        im.Clear();
        Fft.Forward(re, im);
        fdlPosition = fdlPosition == 0 ? partitions - 1 : fdlPosition - 1;
        re[..Bins].CopyTo(Spectrum(inRe, fdlPosition));
        im[..Bins].CopyTo(Spectrum(inIm, fdlPosition));

        accRe.Clear();
        accIm.Clear();
        for (var p = 0; p < partitions; p++) {
            var slot = fdlPosition + p;
            if (slot >= partitions) {
                slot -= partitions;
            }

            var xr = Spectrum(inRe, slot);
            var xi = Spectrum(inIm, slot);
            var hr = Spectrum(irRe, p);
            var hi = Spectrum(irIm, p);
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

        Fft.Inverse(re, im);
        re.Slice(B, B).CopyTo(tailOut.Span);
        frame.Slice(B, B).CopyTo(frame); // the current block becomes the previous one
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
