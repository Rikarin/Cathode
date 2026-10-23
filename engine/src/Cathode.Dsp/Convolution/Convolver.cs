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

    readonly Fft _fft = new(N);
    readonly float[] _head; // taps 0..B-1, direct form
    readonly float[] _history; // last B inputs, stored twice so a window is always contiguous
    int _historyPosition;

    readonly int _partitions; // partitions of taps B..L-1
    readonly float[][] _irRe; // spectra of the tail partitions
    readonly float[][] _irIm;
    readonly float[][] _inRe; // frequency-domain delay line of input spectra
    readonly float[][] _inIm;
    int _fdlPosition;
    readonly float[] _frame = new float[N]; // [previous block | current block]
    readonly float[] _tailOut = new float[B];
    readonly float[] _re = new float[N];
    readonly float[] _im = new float[N];
    readonly float[] _accRe = new float[Bins];
    readonly float[] _accIm = new float[Bins];
    int _position;

    public Convolver(ReadOnlySpan<float> impulseResponse) {
        if (impulseResponse.IsEmpty) {
            throw new ArgumentException("The impulse response is empty.", nameof(impulseResponse));
        }

        Length = impulseResponse.Length;
        _head = new float[B];
        impulseResponse[..Math.Min(B, Length)].CopyTo(_head);
        _history = new float[2 * B];

        _partitions = Math.Max(0, (Length - B + B - 1) / B);
        _irRe = new float[_partitions][];
        _irIm = new float[_partitions][];
        _inRe = new float[_partitions][];
        _inIm = new float[_partitions][];
        for (var p = 0; p < _partitions; p++) {
            Array.Clear(_re);
            Array.Clear(_im);
            var start = B + p * B;
            impulseResponse.Slice(start, Math.Min(B, Length - start)).CopyTo(_re);
            _fft.Forward(_re, _im);
            _irRe[p] = _re[..Bins];
            _irIm[p] = _im[..Bins];
            _inRe[p] = new float[Bins];
            _inIm[p] = new float[Bins];
        }
    }

    /// <summary>Impulse response length in samples.</summary>
    public int Length { get; }

    public void Reset() {
        Array.Clear(_history);
        Array.Clear(_frame);
        Array.Clear(_tailOut);
        foreach (var spectrum in _inRe) {
            Array.Clear(spectrum);
        }

        foreach (var spectrum in _inIm) {
            Array.Clear(spectrum);
        }

        _historyPosition = 0;
        _fdlPosition = 0;
        _position = 0;
    }

    /// <summary>Convolves <paramref name="samples" /> in place.</summary>
    public void Process(Span<float> samples) {
        for (var i = 0; i < samples.Length; i++) {
            var x = samples[i];

            _historyPosition = _historyPosition == 0 ? B - 1 : _historyPosition - 1;
            _history[_historyPosition] = x;
            _history[_historyPosition + B] = x;
            var y = Dot(_head, _history.AsSpan(_historyPosition, B));

            if (_partitions > 0) {
                y += _tailOut[_position];
                _frame[B + _position] = x;
            }

            samples[i] = y;
            if (++_position == B) {
                _position = 0;
                if (_partitions > 0) {
                    RunTail();
                }
            }
        }
    }

    /// <summary>One overlap-save step over the tail partitions, producing the next block's tail output.</summary>
    void RunTail() {
        _frame.CopyTo(_re, 0);
        Array.Clear(_im);
        _fft.Forward(_re, _im);
        _fdlPosition = _fdlPosition == 0 ? _partitions - 1 : _fdlPosition - 1;
        _re.AsSpan(0, Bins).CopyTo(_inRe[_fdlPosition]);
        _im.AsSpan(0, Bins).CopyTo(_inIm[_fdlPosition]);

        Array.Clear(_accRe);
        Array.Clear(_accIm);
        for (var p = 0; p < _partitions; p++) {
            var slot = _fdlPosition + p;
            if (slot >= _partitions) {
                slot -= _partitions;
            }

            var xr = _inRe[slot];
            var xi = _inIm[slot];
            var hr = _irRe[p];
            var hi = _irIm[p];
            for (var k = 0; k < Bins; k++) {
                _accRe[k] += xr[k] * hr[k] - xi[k] * hi[k];
                _accIm[k] += xr[k] * hi[k] + xi[k] * hr[k];
            }
        }

        for (var k = 0; k < Bins; k++) {
            _re[k] = _accRe[k];
            _im[k] = _accIm[k];
        }

        for (var k = Bins; k < N; k++) {
            _re[k] = _accRe[N - k];
            _im[k] = -_accIm[N - k];
        }

        _fft.Inverse(_re, _im);
        _re.AsSpan(B, B).CopyTo(_tailOut);
        _frame.AsSpan(B, B).CopyTo(_frame); // the current block becomes the previous one
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
