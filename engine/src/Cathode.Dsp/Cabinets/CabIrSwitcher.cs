using Cathode.Dsp.Convolution;

namespace Cathode.Dsp.Cabinets;

/// <summary>Which inputs a <see cref="StereoIr" /> convolves.</summary>
public enum IrFeed {
    /// <summary>Left input through the left IR to the left output, right through the right IR to the right.</summary>
    Both,

    /// <summary>The left input alone feeds both IRs (the Cab's Stereo input with only slots 1 and 3 playing).</summary>
    Left,

    /// <summary>The right input alone feeds both IRs (only slots 2 and 4 playing).</summary>
    Right,

    /// <summary>
    ///     Each input through its own pair, summed per output: four convolutions (the Cab's Stereo input with slots on
    ///     both sides).
    /// </summary>
    Cross
}

/// <summary>
///     A left and right impulse response as a pair of convolvers, built on the control thread; or two pairs, one fed by
///     each input (<see cref="IrFeed.Cross" />); or the identity, which passes audio through untouched and costs
///     nothing.
/// </summary>
public sealed class StereoIr : IDisposable {
    readonly Convolver? left;
    readonly Convolver? right;
    readonly Convolver? leftToRight;
    readonly Convolver? rightToLeft;
    readonly NativeBuffer fromLeft = NativeBuffer.Empty;
    readonly NativeBuffer fromRight = NativeBuffer.Empty;

    public bool IsIdentity => left is null;

    public IrFeed Feed { get; }

    /// <summary>The identity: <see cref="Process" /> leaves the audio as it is.</summary>
    public StereoIr() { }

    public StereoIr(ReadOnlySpan<float> left, ReadOnlySpan<float> right, IrFeed feed = IrFeed.Both) {
        if (feed == IrFeed.Cross) {
            throw new ArgumentException("A cross feed takes two pairs.", nameof(feed));
        }

        this.left = new(left);
        this.right = new(right);
        Feed = feed;
    }

    /// <summary>
    ///     Two pairs: the left input through <paramref name="leftToLeft" /> and <paramref name="leftToRight" />, the right
    ///     input through <paramref name="rightToLeft" /> and <paramref name="rightToRight" />, for callbacks of up to
    ///     <paramref name="maxBlockSize" /> frames.
    /// </summary>
    public StereoIr(
        ReadOnlySpan<float> leftToLeft,
        ReadOnlySpan<float> leftToRight,
        ReadOnlySpan<float> rightToLeft,
        ReadOnlySpan<float> rightToRight,
        int maxBlockSize
    ) {
        left = new(leftToLeft);
        right = new(rightToRight);
        this.leftToRight = new(leftToRight);
        this.rightToLeft = new(rightToLeft);
        fromLeft = new(maxBlockSize);
        fromRight = new(maxBlockSize);
        Feed = IrFeed.Cross;
    }

    /// <summary>Audio thread: convolves the inputs as <see cref="Feed" /> says, in place.</summary>
    public void Process(Span<float> left, Span<float> right) {
        if (this.left is null) {
            return;
        }

        switch (Feed) {
            case IrFeed.Left:
                left.CopyTo(right);
                break;
            case IrFeed.Right:
                right.CopyTo(left);
                break;
            case IrFeed.Cross: {
                var a = fromLeft.Span[..left.Length];
                var b = fromRight.Span[..right.Length];
                left.CopyTo(a);
                right.CopyTo(b);
                leftToRight!.Process(a);
                rightToLeft!.Process(b);
                this.left.Process(left);
                this.right!.Process(right);
                for (var i = 0; i < left.Length; i++) {
                    left[i] += b[i];
                    right[i] += a[i];
                }

                return;
            }
        }

        this.left.Process(left);
        this.right!.Process(right);
    }

    public void Reset() {
        left?.Reset();
        right?.Reset();
        leftToRight?.Reset();
        rightToLeft?.Reset();
    }

    public void Dispose() {
        left?.Dispose();
        right?.Dispose();
        leftToRight?.Dispose();
        rightToLeft?.Dispose();
        fromLeft.Dispose();
        fromRight.Dispose();
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
        if (fading is null
            && Volatile.Read(ref retired) is null
            && Interlocked.Exchange(ref pending, null) is { } next) {
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
            active.Process(left, right);
            return;
        }

        var oldLeft = this.oldLeft.Span[..left.Length];
        var oldRight = this.oldRight.Span[..right.Length];
        left.CopyTo(oldLeft);
        right.CopyTo(oldRight);
        fading.Process(oldLeft, oldRight);
        active.Process(left, right);
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
