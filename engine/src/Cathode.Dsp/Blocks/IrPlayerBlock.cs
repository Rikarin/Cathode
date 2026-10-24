using Cathode.Dsp.Cabinets;
using Cathode.Dsp.Convolution;
using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Plays impulse responses that are not a cabinet: an acoustic body, a room, a microphone or speaker correction, or
///     an effect captured as an IR. Longer IRs than the Cab block (up to <see cref="MaxIrLength" />), kept at the files'
///     own gain, with a dry/wet mix and low and high cut (6 to 24 dB/oct) on the wet signal. Zero latency.
/// </summary>
/// <remarks>
///     <para>
///         Two IR slots (<c>ir</c>, <c>ir2</c>), each with its own Length (cut with a short fade), Level and Pan. The
///         Mode combines them: <b>Parallel</b> sums them (each panned with the balance law), <b>Stereo</b> plays IR 1 on
///         the left and IR 2 on the right, and <b>Series</b> plays one after the other (their convolution, cut to
///         <see cref="MaxIrLength" />). With one slot loaded, every mode plays that IR alone. A stereo file feeds left
///         and right separately.
///     </para>
///     <para>
///         As in the Cab block, the slots are baked into one left and one right IR on the control thread (in
///         <see cref="IDspBlock.Prepare" /> and <see cref="Refresh" />) and crossfaded in by
///         <see cref="CabIrSwitcher" />, so the block convolves twice whatever the slots. Input Select feeds the IRs
///         the stereo input, its sum, or one side.
///     </para>
/// </remarks>
public sealed class IrPlayerBlock() : DspBlock(Info), IAssetConsumer, IControlRefresh {
    // Up to Input the parameters act on the audio thread; from Mode on they reshape the IR.
    public const int Mix = 0;
    public const int LowCut = 1;
    public const int HighCut = 2;
    public const int Level = 3;
    public const int Balance = 4;
    public const int LowSlope = 5;
    public const int HighSlope = 6;
    public const int Input = 7;
    public const int Mode = 8;

    /// <summary>Slot 1's first parameter; slot n's are at <c>SlotBase + n * SlotStride</c>.</summary>
    public const int SlotBase = 9;

    public const int SlotStride = 3;
    public const int Slots = 2;

    // Offsets within a slot.
    public const int Length = 0;
    public const int SlotLevel = 1;
    public const int Pan = 2;

    // Values of Mode.
    public const int Parallel = 0;
    public const int StereoMode = 1;
    public const int Series = 2;

    // Values of Input.
    public const int InputStereo = 0;
    public const int InputSum = 1;
    public const int InputLeft = 2;
    public const int InputRight = 3;

    /// <summary>Longest IR kept, in samples at the engine rate (about 680 ms at 48 kHz).</summary>
    public const int MaxIrLength = 32_768;

    /// <summary>A slot's Length at or above this keeps the whole IR.</summary>
    public const float FullLengthMs = 680f;

    /// <summary>Fade at a Length cut, samples.</summary>
    const int LengthFade = 64;

    static readonly IrShape Shape = new(MaxIrLength, UnitEnergy: false);
    static readonly string[] SlotAssets = ["ir", "ir2"];

    readonly Lock sync = new();
    readonly CabIrSwitcher switcher = new();
    readonly ImpulseResponseFile?[] files = new ImpulseResponseFile?[Slots];
    readonly float[][]?[] prepared = new float[][]?[Slots];
    readonly float[] targets = new float[SlotBase + Slots * SlotStride];
    readonly float[] built = new float[SlotBase + Slots * SlotStride];
    bool stale = true;
    bool ready;
    volatile bool playing;
    bool disposed;
    NativeBuffer mixLeft = NativeBuffer.Empty;
    NativeBuffer mixRight = NativeBuffer.Empty;
    NativeBuffer scratch = NativeBuffer.Empty;
    NativeBuffer wetLeft = NativeBuffer.Empty;
    NativeBuffer wetRight = NativeBuffer.Empty;
    SeriesConvolution? series;
    CutState lowCutLeft, lowCutRight, highCutLeft, highCutRight;
    float mix;
    OutputStage output;

    /// <summary>Mode choices, in order.</summary>
    public static IReadOnlyList<string> Modes { get; } = ["Parallel", "Stereo", "Series"];

    /// <summary>Input Select choices, in order.</summary>
    public static IReadOnlyList<string> Inputs { get; } = ["Stereo", "L+R", "Left", "Right"];

    public static BlockDescriptor Info { get; } = new(
        "irplayer",
        "IR Player",
        "Cab",
        [
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("lowcut", "Low Cut", CabBlock.LowCutOff, 500f, CabBlock.LowCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("highcut", "High Cut", 1_000f, CabBlock.HighCutOff, CabBlock.HighCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Choice("lowslope", "Low Cut Slope", CutFilter.Slopes, CutFilter.Slope12),
            ParamDescriptor.Choice("highslope", "High Cut Slope", CutFilter.Slopes, CutFilter.Slope12),
            ParamDescriptor.Choice("input", "Input Select", Inputs),
            ParamDescriptor.Choice("mode", "Mode", Modes),
            ..Slot(1),
            ..Slot(2)
        ],
        maxInstances: 2
    );

    public IReadOnlyList<string> AssetSlots => SlotAssets;

    /// <summary>Changes the audio thread has crossfaded in since Prepare (tests, diagnostics).</summary>
    public int IrSwaps => switcher.Swaps;

    /// <summary>Parameter index of <paramref name="offset" /> in slot <paramref name="slot" /> (0-based).</summary>
    public static int SlotParam(int slot, int offset) => SlotBase + slot * SlotStride + offset;

    /// <summary>The IR file of <paramref name="slot" /> as it is mixed, for tests and the editor's plot.</summary>
    public float[]? PreparedImpulseResponse(int channel = 0, int slot = 0) =>
        prepared[slot] is { } channels ? channels[Math.Min(channel, channels.Length - 1)] : null;

    public void LoadAsset(string slot, string path) {
        var index = Array.IndexOf(SlotAssets, slot);
        if (index < 0) {
            throw new ArgumentException($"Unknown asset slot '{slot}'.", nameof(slot));
        }

        var file = ImpulseResponses.Load(path);
        lock (sync) {
            files[index] = file;
            prepared[index] = ready ? file.Prepare(Spec.SampleRate, Shape) : null;
            stale = true;
        }
    }

    public void Refresh() {
        lock (sync) {
            if (!ready || disposed) {
                return;
            }

            switcher.Collect();
            ReadTargets();
            if (NeedsBuild()) {
                switcher.Publish(Build());
            }
        }
    }

    /// <summary>
    ///     Control thread: the left and right IRs for the current targets (building and publishing them first if they
    ///     changed), for tests; a unit impulse when nothing is loaded. Prepare first.
    /// </summary>
    public (float[] Left, float[] Right) ImpulseResponse() {
        lock (sync) {
            if (!ready) {
                throw new InvalidOperationException("Prepare the block first.");
            }

            ReadTargets();
            if (NeedsBuild()) {
                switcher.Publish(Build());
            }

            return playing ? (Used(mixLeft.Span, mixRight.Span), Used(mixRight.Span, mixLeft.Span)) : ([1f], [1f]);
        }
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        lock (sync) {
            mixLeft.Dispose();
            mixRight.Dispose();
            scratch.Dispose();
            wetLeft.Dispose();
            wetRight.Dispose();
            mixLeft = new(MaxIrLength);
            mixRight = new(MaxIrLength);
            scratch = new(2 * MaxIrLength);
            wetLeft = new(spec.MaxBlockSize);
            wetRight = new(spec.MaxBlockSize);
            for (var s = 0; s < Slots; s++) {
                prepared[s] = files[s]?.Prepare(spec.SampleRate, Shape);
            }

            ready = true;
            stale = true;
            ReadTargets();
            switcher.Prepare(spec, Build());
        }
    }

    protected override void OnReset() {
        switcher.Reset();
        lowCutLeft.Reset();
        lowCutRight.Reset();
        highCutLeft.Reset();
        highCutRight.Reset();
        mix = Params.Current(Mix) / 100f;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var mix = Params.Advance(Mix, io.Length) / 100f;
        var lowCut = Params.Advance(LowCut, io.Length);
        var highCut = Params.Advance(HighCut, io.Length);
        var lowSlope = (int)Params.Current(LowSlope);
        var highSlope = (int)Params.Current(HighSlope);
        if (playing) {
            var left = wetLeft.Span[..io.Length];
            var right = wetRight.Span[..io.Length];
            switch ((int)Params.Current(Input)) {
                case InputSum:
                    for (var i = 0; i < io.Length; i++) {
                        left[i] = right[i] = 0.5f * (io.Left[i] + io.Right[i]);
                    }

                    break;
                case InputLeft:
                    io.Left.CopyTo(left);
                    io.Left.CopyTo(right);
                    break;
                case InputRight:
                    io.Right.CopyTo(left);
                    io.Right.CopyTo(right);
                    break;
                default:
                    io.Left.CopyTo(left);
                    io.Right.CopyTo(right);
                    break;
            }

            switcher.Process(left, right);
            if (lowCut > CabBlock.LowCutOff) {
                var c = CutFilter.Design(true, Spec.SampleRate, lowCut, lowSlope);
                lowCutLeft.Process(c, left);
                lowCutRight.Process(c, right);
            }

            if (highCut < CabBlock.HighCutOff) {
                var c = CutFilter.Design(false, Spec.SampleRate, highCut, highSlope);
                highCutLeft.Process(c, left);
                highCutRight.Process(c, right);
            }

            Blend(io.Left, left, this.mix, mix);
            Blend(io.Right, right, this.mix, mix);
        }

        this.mix = mix;
        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            lock (sync) {
                disposed = true;
                switcher.Dispose();
                series?.Dispose();
                mixLeft.Dispose();
                mixRight.Dispose();
                scratch.Dispose();
                wetLeft.Dispose();
                wetRight.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    /// <summary><c>dry = dry + (wet - dry) * mix</c>, with the mix ramped across the block.</summary>
    internal static void Blend(Span<float> dry, ReadOnlySpan<float> wet, float from, float to) {
        var step = (to - from) / dry.Length;
        var mix = from;
        for (var i = 0; i < dry.Length; i++) {
            mix += step;
            dry[i] += (wet[i] - dry[i]) * mix;
        }
    }

    static IEnumerable<ParamDescriptor> Slot(int n) => [
        ParamDescriptor.Float($"length{n}", $"IR {n} Length", 5f, FullLengthMs, FullLengthMs, "ms", ParamTaper.Log),
        ParamDescriptor.Float($"level{n}", $"IR {n} Level", Decibels.SilenceDb, 12f, 0f, "dB"),
        ParamDescriptor.Float($"pan{n}", $"IR {n} Pan", -100f, 100f, 0f, "%")
    ];

    /// <summary>One past the last non-zero sample of either side, at least 1.</summary>
    static int UsedLength(ReadOnlySpan<float> left, ReadOnlySpan<float> right) {
        for (var i = left.Length - 1; i > 0; i--) {
            if (left[i] != 0f || right[i] != 0f) {
                return i + 1;
            }
        }

        return 1;
    }

    static float[] Used(ReadOnlySpan<float> side, ReadOnlySpan<float> other) => side[..UsedLength(side, other)].ToArray();

    static void Add(ReadOnlySpan<float> source, Span<float> destination, float gain) {
        for (var i = 0; i < source.Length; i++) {
            destination[i] += source[i] * gain;
        }
    }

    void ReadTargets() {
        for (var i = 0; i < targets.Length; i++) {
            targets[i] = Params.GetTarget(i);
        }
    }

    bool NeedsBuild() {
        if (stale) {
            return true;
        }

        for (var i = Mode; i < targets.Length; i++) {
            if (targets[i] != built[i]) {
                return true;
            }
        }

        return false;
    }

    float SlotTarget(int slot, int offset) => targets[SlotParam(slot, offset)];

    /// <summary>Bakes the slots into a new pair for the current targets; the identity when nothing is loaded.</summary>
    StereoIr Build() {
        stale = false;
        Array.Copy(targets, built, targets.Length);
        var left = mixLeft.Span;
        var right = mixRight.Span;
        left.Clear();
        right.Clear();
        var first = prepared[0];
        var second = prepared[1];
        playing = first is not null || second is not null;
        if (!playing) {
            return new();
        }

        var mode = (int)targets[Mode];
        if (first is not null && second is not null && mode == Series) {
            series ??= new(2 * MaxIrLength);
            var gain1 = Pans(0);
            var gain2 = Pans(1);
            series.Convolve(Shaped(0, first[0]), Shaped(1, second[0], true), left, gain1.Left * gain2.Left);
            series.Convolve(Shaped(0, first[^1]), Shaped(1, second[^1], true), right, gain1.Right * gain2.Right);
        } else if (first is not null && second is not null && mode == StereoMode) {
            Add(Shaped(0, first[0]), left, Decibels.ToGain(SlotTarget(0, SlotLevel)));
            Add(Shaped(1, second[^1]), right, Decibels.ToGain(SlotTarget(1, SlotLevel)));
        } else {
            for (var s = 0; s < Slots; s++) {
                if (prepared[s] is not { } channels) {
                    continue;
                }

                var (gainLeft, gainRight) = Pans(s);
                Add(Shaped(s, channels[0]), left, gainLeft);
                Add(Shaped(s, channels[^1]), right, gainRight);
            }
        }

        var length = UsedLength(left, right);
        return new(left[..length], right[..length]);
    }

    /// <summary>A slot's level, with its pan on the balance law: the centre is unity on both sides.</summary>
    (float Left, float Right) Pans(int slot) {
        var gain = Decibels.ToGain(SlotTarget(slot, SlotLevel));
        var pan = SlotTarget(slot, Pan) / 100f;
        return (gain * Math.Min(1f, 1f - pan), gain * Math.Min(1f, 1f + pan));
    }

    /// <summary>
    ///     <paramref name="ir" /> cut to the slot's Length with a short fade, in a scratch buffer (the second half of it
    ///     with <paramref name="upper" />, for the second operand of a series convolution); or the IR itself, whole.
    /// </summary>
    ReadOnlySpan<float> Shaped(int slot, float[] ir, bool upper = false) {
        var lengthMs = SlotTarget(slot, Length);
        var length = lengthMs >= FullLengthMs ? ir.Length : Math.Min(ir.Length, Math.Max(1, (int)(lengthMs / 1_000.0 * Spec.SampleRate)));
        if (length == ir.Length) {
            return ir;
        }

        var shaped = scratch.Span.Slice(upper ? MaxIrLength : 0, length);
        ir.AsSpan(0, shaped.Length).CopyTo(shaped);
        var fade = Math.Min(LengthFade, shaped.Length);
        for (var i = 0; i < fade; i++) {
            shaped[shaped.Length - fade + i] *= 0.5f * (1f + MathF.Cos(MathF.PI * (i + 1) / fade));
        }

        return shaped;
    }

    /// <summary>Offline FFT convolution of two IRs (control thread), in native memory.</summary>
    sealed class SeriesConvolution(int size) : IDisposable {
        readonly NativeFft fft = new(size);
        readonly NativeBuffer re1 = new(size);
        readonly NativeBuffer im1 = new(size);
        readonly NativeBuffer re2 = new(size);
        readonly NativeBuffer im2 = new(size);

        /// <summary>Adds <paramref name="a" /> ⊛ <paramref name="b" /> × <paramref name="gain" /> into <paramref name="destination" />, cut to its length.</summary>
        public void Convolve(ReadOnlySpan<float> a, ReadOnlySpan<float> b, Span<float> destination, float gain) {
            var ar = re1.Span;
            var ai = im1.Span;
            var br = re2.Span;
            var bi = im2.Span;
            ar.Clear();
            ai.Clear();
            br.Clear();
            bi.Clear();
            a.CopyTo(ar);
            b.CopyTo(br);
            fft.Forward(ar, ai);
            fft.Forward(br, bi);
            for (var i = 0; i < ar.Length; i++) {
                var re = ar[i] * br[i] - ai[i] * bi[i];
                ai[i] = ar[i] * bi[i] + ai[i] * br[i];
                ar[i] = re;
            }

            fft.Inverse(ar, ai);
            var length = Math.Min(destination.Length, a.Length + b.Length - 1);
            for (var i = 0; i < length; i++) {
                destination[i] += ar[i] * gain;
            }

            // A cut tail fades, as a Length cut does.
            if (a.Length + b.Length - 1 > destination.Length) {
                var fade = Math.Min(LengthFade, length);
                for (var i = 0; i < fade; i++) {
                    destination[length - fade + i] *= 0.5f * (1f + MathF.Cos(MathF.PI * (i + 1) / fade));
                }
            }
        }

        public void Dispose() {
            fft.Dispose();
            re1.Dispose();
            im1.Dispose();
            re2.Dispose();
            im2.Dispose();
        }
    }
}
