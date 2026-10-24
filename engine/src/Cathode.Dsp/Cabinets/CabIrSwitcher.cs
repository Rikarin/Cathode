using Cathode.Dsp.Convolution;

namespace Cathode.Dsp.Cabinets;

/// <summary>A left and right impulse response as a pair of convolvers, built on the control thread.</summary>
public sealed class StereoIr : IDisposable {
    public Convolver Left { get; }

    public Convolver Right { get; }

    public StereoIr(ReadOnlySpan<float> left, ReadOnlySpan<float> right) {
        Left = new(left);
        Right = new(right);
    }

    public void Reset() {
        Left.Reset();
        Right.Reset();
    }

    public void Dispose() {
        Left.Dispose();
        Right.Dispose();
    }
}

/// <summary>
///     Convolves with the current <see cref="StereoIr" /> and changes to a new one click-free. The control thread
///     <see cref="Publish" />es a new pair; the audio thread picks it up at the start of a callback and crossfades from
///     the old pair to the new over <see cref="FadeLength" /> samples (equal gain: the two responses are strongly
///     correlated), then hands the old pair back for the control thread to <see cref="Collect" /> and dispose.
///     Ownership moves only through <see cref="Interlocked.Exchange{T}(ref T, T)" />, so neither side locks, and the
///     audio thread never allocates or frees.
/// </summary>
/// <remarks>
///     A new pair starts with empty history, so for its first IR length it lacks the tail of what came before; the
///     fade starts it at zero gain and a cab IR's energy is mostly in its first milliseconds, so the fade hides it.
///     The audio thread takes a new pair only when it is not fading and the retired slot is empty, so at most three
///     pairs exist and the retired slot is always free when a fade ends.
/// </remarks>
public sealed class CabIrSwitcher : IDisposable {
    StereoIr? active;
    StereoIr? fading;
    StereoIr? pending;
    StereoIr? retired;
    int faded;
    NativeBuffer oldLeft = NativeBuffer.Empty;
    NativeBuffer oldRight = NativeBuffer.Empty;

    /// <summary>Crossfade length in samples (20 ms).</summary>
    public int FadeLength { get; private set; } = 960;

    /// <summary>Audio thread: true while a change is being crossfaded.</summary>
    public bool IsFading => fading is not null;

    /// <summary>Changes taken by the audio thread since <see cref="Prepare" /> (for tests and diagnostics).</summary>
    public int Swaps { get; private set; }

    /// <summary>Control thread, audio stopped: sizes the fade buffers and starts on <paramref name="initial" />.</summary>
    public void Prepare(in ProcessSpec spec, StereoIr initial) {
        DisposePairs();
        oldLeft.Dispose();
        oldRight.Dispose();
        oldLeft = new(spec.MaxBlockSize);
        oldRight = new(spec.MaxBlockSize);
        FadeLength = Math.Max(64, (int)Math.Round(0.02 * spec.SampleRate));
        active = initial;
        faded = 0;
        Swaps = 0;
    }

    /// <summary>Control thread: queues <paramref name="next" />, replacing (and disposing) one not yet taken.</summary>
    public void Publish(StereoIr next) => Interlocked.Exchange(ref pending, next)?.Dispose();

    /// <summary>Control thread: disposes a pair the audio thread has finished with.</summary>
    public void Collect() => Interlocked.Exchange(ref retired, null)?.Dispose();

    /// <summary>Audio thread: ends any fade on the newest pair and clears the convolvers' state.</summary>
    public void Reset() {
        if (fading is not null) {
            Volatile.Write(ref retired, fading);
            fading = null;
        }

        active?.Reset();
    }

    /// <summary>Audio thread: convolves left with the left IR and right with the right IR, in place.</summary>
    public void Process(Span<float> left, Span<float> right) {
        if (fading is null && Volatile.Read(ref retired) is null && Interlocked.Exchange(ref pending, null) is { } next) {
            next.Reset();
            fading = active;
            active = next;
            faded = 0;
            Swaps++;
        }

        if (active is null) {
            return;
        }

        if (fading is null) {
            active.Left.Process(left);
            active.Right.Process(right);
            return;
        }

        var oldLeft = this.oldLeft.Span[..left.Length];
        var oldRight = this.oldRight.Span[..right.Length];
        left.CopyTo(oldLeft);
        right.CopyTo(oldRight);
        fading.Left.Process(oldLeft);
        fading.Right.Process(oldRight);
        active.Left.Process(left);
        active.Right.Process(right);
        var step = 1f / FadeLength;
        for (var i = 0; i < left.Length; i++) {
            var mix = Math.Min(1f, (faded + i + 1) * step);
            left[i] = oldLeft[i] + (left[i] - oldLeft[i]) * mix;
            right[i] = oldRight[i] + (right[i] - oldRight[i]) * mix;
        }

        faded += left.Length;
        if (faded >= FadeLength) {
            Volatile.Write(ref retired, fading);
            fading = null;
        }
    }

    public void Dispose() {
        DisposePairs();
        oldLeft.Dispose();
        oldRight.Dispose();
    }

    void DisposePairs() {
        active?.Dispose();
        fading?.Dispose();
        Interlocked.Exchange(ref pending, null)?.Dispose();
        Interlocked.Exchange(ref retired, null)?.Dispose();
        active = null;
        fading = null;
    }
}
