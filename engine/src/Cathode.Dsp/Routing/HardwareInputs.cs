namespace Cathode.Dsp.Routing;

/// <summary>
///     The audio interface's inputs as the engine sees them (docs/08-blocks-parity.md §2): six stereo sources named as
///     on the Axe-Fx III, Input 1–4 and the USB pairs 5/6 and 7/8. The host maps device channels onto them (the Setup
///     page's input map); by default only Input 1 is mapped, to the device's first channel, and it is also the pair the
///     grid input reads. The engine copies every source in at the start of each callback and sets <see cref="Offset" />
///     before each sub-block; blocks read them through their graph's <see cref="InputPort" />. A source without device
///     channels reads as silence.
/// </summary>
public sealed class HardwareInputs : IDisposable {
    /// <summary>Largest callback the native shim delivers (CTH_MAX_FRAMES).</summary>
    public const int MaxFrames = 4_096;

    /// <summary>How many stereo sources there are.</summary>
    public const int Count = 6;

    /// <summary>The source that the grid input reads (and the only one mapped by default).</summary>
    public const int Input1 = 0;

    /// <summary>The sources' names, in order: what an input choice lists after its other options.</summary>
    public static readonly string[] Names = ["Input 1", "Input 2", "Input 3", "Input 4", "USB 5/6", "USB 7/8"];

    readonly NativeBuffer[] left = new NativeBuffer[Count];
    readonly NativeBuffer[] right = new NativeBuffer[Count];
    int clearedFrames;

    /// <summary>Where the current sub-block starts within the callback.</summary>
    public int Offset { get; set; }

    /// <summary>One bit per source (bit 0 = Input 1) that has device channels in this callback.</summary>
    public int Connected { get; private set; } = 1 << Input1;

    public HardwareInputs() {
        for (var s = 0; s < Count; s++) {
            left[s] = new(MaxFrames);
            right[s] = new(MaxFrames);
        }
    }

    /// <summary>
    ///     Audio thread, at the start of a callback. <paramref name="sources" /> holds every source planar, left then
    ///     right, <see cref="Count" /> × 2 channels of <paramref name="frames" /> samples (see
    ///     <c>AudioIO.Sources</c>), or is empty, in which case Input 1 is <paramref name="inputLeft" /> and
    ///     <paramref name="inputRight" /> and the others are silent. <paramref name="connected" /> is the mask of
    ///     sources with device channels.
    /// </summary>
    public void BeginCallback(
        ReadOnlySpan<float> inputLeft,
        ReadOnlySpan<float> inputRight,
        ReadOnlySpan<float> sources,
        int connected,
        int frames
    ) {
        Offset = 0;
        if (sources.Length >= Count * 2 * frames) {
            for (var s = 0; s < Count; s++) {
                sources.Slice(2 * s * frames, frames).CopyTo(left[s].Span);
                sources.Slice((2 * s + 1) * frames, frames).CopyTo(right[s].Span);
            }

            Connected = connected | (1 << Input1);
            clearedFrames = 0;
            return;
        }

        inputLeft.CopyTo(left[Input1].Span);
        inputRight.CopyTo(right[Input1].Span);
        if (clearedFrames < frames) {
            // Nothing writes the other sources without a device map, so clearing them once per size is enough.
            for (var s = 1; s < Count; s++) {
                left[s].Span[..frames].Clear();
                right[s].Span[..frames].Clear();
            }

            clearedFrames = frames;
        }

        Connected = 1 << Input1;
    }

    /// <summary>Audio thread: source <paramref name="source" />'s left channel for the current sub-block.</summary>
    public ReadOnlySpan<float> Left(int source, int frames) => left[source].Span.Slice(Offset, frames);

    public ReadOnlySpan<float> Right(int source, int frames) => right[source].Span.Slice(Offset, frames);

    public void Dispose() {
        for (var s = 0; s < Count; s++) {
            left[s].Dispose();
            right[s].Dispose();
        }
    }
}

/// <summary>
///     One graph's view of the <see cref="HardwareInputs" />: blocks that read a hardware input
///     (<see cref="ISideInputBlock" /> with <see cref="SideInputs.Hardware" />) get it from the graph compiler. When the
///     graph is replaced and spills over under the next one, the engine fades its port out (<see cref="FadeOut" />), as
///     it fades the grid input, so the old preset stops hearing the player and its tails can ring out.
/// </summary>
public sealed class InputPort(HardwareInputs inputs, double sampleRate) {
    /// <summary>How long the fade-out of a replaced graph's inputs takes (the engine's swap fade).</summary>
    public const double FadeMs = 20;

    readonly float step = (float)(1.0 / Math.Max(1.0, FadeMs * 0.001 * sampleRate));
    float gain = 1f;
    bool fading;

    /// <summary>Whether <paramref name="source" /> has device channels (unmapped sources read as silence).</summary>
    public bool IsConnected(int source) => (uint)source < HardwareInputs.Count && (inputs.Connected & (1 << source)) != 0;

    /// <summary>
    ///     Audio thread: adds source <paramref name="source" /> times <paramref name="gain" /> into the buffers (one
    ///     sub-block). Out-of-range sources add nothing.
    /// </summary>
    public void Add(int source, Span<float> left, Span<float> right, float gain = 1f) {
        if ((uint)source >= HardwareInputs.Count || this.gain <= 0f) {
            return;
        }

        var frames = left.Length;
        var fromLeft = inputs.Left(source, frames);
        var fromRight = inputs.Right(source, frames);
        if (!fading) {
            for (var i = 0; i < frames; i++) {
                left[i] += fromLeft[i] * gain;
                right[i] += fromRight[i] * gain;
            }

            return;
        }

        var g = this.gain;
        for (var i = 0; i < frames; i++) {
            g = Math.Max(0f, g - step);
            left[i] += fromLeft[i] * gain * g;
            right[i] += fromRight[i] * gain * g;
        }
    }

    /// <summary>Audio thread: copies source <paramref name="source" /> into the buffers (silence when out of range).</summary>
    public void Read(int source, Span<float> left, Span<float> right) {
        left.Clear();
        right.Clear();
        Add(source, left, right);
    }

    /// <summary>Audio thread: from the next sub-block on, every read fades to silence over <see cref="FadeMs" />.</summary>
    public void FadeOut() => fading = true;

    /// <summary>Audio thread, after each sub-block of the graph: advances the fade.</summary>
    public void Advance(int frames) {
        if (fading) {
            gain = Math.Max(0f, gain - step * frames);
        }
    }
}
