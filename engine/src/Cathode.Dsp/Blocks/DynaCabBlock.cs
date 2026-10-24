using Cathode.Dsp.Cabinets;
using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     DynaCab-style cabinet: four mic slots, each a speaker in a cabinet miked with a chosen mic at a continuous
///     position (cap to edge), distance and angle, mixed with level, pan, delay, polarity, mute and solo. The IRs are
///     synthesised from physical models (<see cref="CabAcoustics" />, <see cref="CabCatalogue" />), or interpolated
///     from a user's multi-position IR pack loaded into the slot's <c>grid</c> asset (<see cref="CabGrid" />).
/// </summary>
/// <remarks>
///     <para>
///         The slots are linear and the input is one speaker signal per side, so all four mix into one left and one
///         right IR (level, pan, polarity and delay baked in): the block convolves twice whatever the slot count. A
///         slot is resynthesised only when its own acoustic parameters (or Proximity or Room) change; a level, pan,
///         delay, mute or solo change only remixes.
///     </para>
///     <para>
///         Rebuilding runs on the control thread: in <see cref="IDspBlock.Prepare" />, and in
///         <see cref="Refresh" />, which the host calls after setting parameters. The new pair is crossfaded in over
///         20 ms by <see cref="CabIrSwitcher" />; the audio thread neither allocates nor waits. Zero latency.
///     </para>
/// </remarks>
public sealed class DynaCabBlock() : DspBlock(Info), IAssetConsumer, IControlRefresh {
    public const int LowCut = 0;
    public const int HighCut = 1;
    public const int Level = 2;
    public const int Proximity = 3;
    public const int Room = 4;
    public const int AutoDelay = 5;

    /// <summary>Index of slot 1's first parameter; slot n's are at <c>SlotBase + n * SlotStride</c>.</summary>
    public const int SlotBase = 6;

    public const int SlotStride = 12;
    public const int Slots = 4;

    // Offsets within a slot.
    public const int Cab = 0;
    public const int Speaker = 1;
    public const int Mic = 2;
    public const int Position = 3;
    public const int Distance = 4;
    public const int Angle = 5;
    public const int SlotLevel = 6;
    public const int Pan = 7;
    public const int Delay = 8;
    public const int Invert = 9;
    public const int Mute = 10;
    public const int Solo = 11;

    /// <summary>Longest slot delay, ms.</summary>
    public const float MaxDelayMs = 5f;

    /// <summary>Longest distance, inches.</summary>
    public const float MaxDistance = 12f;

    const double MetresPerInch = 0.0254;

    /// <summary>Half-length of the windowed-sinc fractional delay used when mixing slots.</summary>
    const int SincHalf = 8;

    static readonly string[] GridSlots = ["grid1", "grid2", "grid3", "grid4"];

    readonly Lock sync = new();
    readonly CabIrSwitcher switcher = new();
    readonly CabGrid?[] grids = new CabGrid?[Slots];
    readonly float[] targets = new float[SlotBase + Slots * SlotStride];
    readonly float[] built = new float[SlotBase + Slots * SlotStride];
    readonly double[] slotDelays = new double[Slots];
    readonly bool[] stale = new bool[Slots];
    CabIrSynthesizer? synthesizer;
    NativeBuffer slotIrs = NativeBuffer.Empty;
    NativeBuffer mixLeft = NativeBuffer.Empty;
    NativeBuffer mixRight = NativeBuffer.Empty;
    SvfState lowCutLeft, lowCutRight, highCutLeft, highCutRight;
    float outputGain = 1f;
    bool disposed;

    public static BlockDescriptor Info { get; } = new(
        "dynacab",
        "DynaCab",
        "Cab",
        [
            ParamDescriptor.Float("lowcut", "Low Cut", CabBlock.LowCutOff, 500f, CabBlock.LowCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("highcut", "High Cut", 1_000f, CabBlock.HighCutOff, CabBlock.HighCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Switch("proximity", "Proximity", true),
            ParamDescriptor.Float("room", "Room", 0f, 100f, 0f, "%"),
            ParamDescriptor.Switch("autodelay", "Auto Delay"),
            ..Slot(1, 5, 3, 0, 25, 1, false),
            ..Slot(2, 5, 3, 5, 50, 4, true),
            ..Slot(3, 5, 0, 1, 35, 2, true),
            ..Slot(4, 5, 3, 8, 0, 12, true)
        ],
        maxInstances: 2
    );

    public IReadOnlyList<string> AssetSlots => GridSlots;

    /// <summary>Changes the audio thread has crossfaded in since Prepare (tests, diagnostics).</summary>
    public int IrSwaps => switcher.Swaps;

    /// <summary>Audio thread: true while a new IR is being crossfaded in.</summary>
    public bool IsFading => switcher.IsFading;

    /// <summary>IR length at the prepared rate, before slot delays.</summary>
    public int Taps => synthesizer?.Taps ?? 0;

    /// <summary>Parameter index of <paramref name="offset" /> in mic slot <paramref name="slot" /> (0-based).</summary>
    public static int SlotParam(int slot, int offset) => SlotBase + slot * SlotStride + offset;

    /// <summary>Control thread: loads a multi-position IR pack (or a single IR) into <c>grid1</c> … <c>grid4</c>.</summary>
    public void LoadAsset(string slot, string path) {
        var index = Array.IndexOf(GridSlots, slot);
        if (index < 0) {
            throw new ArgumentException($"Unknown asset slot '{slot}'.", nameof(slot));
        }

        var grid = CabGrid.Load(path);
        lock (sync) {
            grids[index]?.Dispose();
            grids[index] = grid;
            stale[index] = true;
        }
    }

    /// <summary>The user grid in <paramref name="slot" />, if one is loaded.</summary>
    public CabGrid? Grid(int slot) => grids[slot];

    public void Refresh() {
        lock (sync) {
            if (synthesizer is null || disposed) {
                return;
            }

            switcher.Collect();
            ReadTargets();
            if (!NeedsBuild()) {
                return;
            }

            switcher.Publish(Build());
        }
    }

    /// <summary>
    ///     Control thread: the left and right IRs for the current targets (building and publishing them first if they
    ///     changed), for tests and plots. Prepare first.
    /// </summary>
    public (float[] Left, float[] Right) ImpulseResponse() {
        lock (sync) {
            if (synthesizer is null) {
                throw new InvalidOperationException("Prepare the block first.");
            }

            ReadTargets();
            if (NeedsBuild()) {
                switcher.Publish(Build());
            }

            return (mixLeft.Span.ToArray(), mixRight.Span.ToArray());
        }
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        lock (sync) {
            synthesizer?.Dispose();
            slotIrs.Dispose();
            mixLeft.Dispose();
            mixRight.Dispose();
            synthesizer = new(spec.SampleRate);
            var taps = synthesizer.Taps;
            slotIrs = new(Slots * taps);
            var length = taps + (int)Math.Ceiling(MaxShiftSeconds() * spec.SampleRate) + 2 * SincHalf;
            mixLeft = new(length);
            mixRight = new(length);
            Array.Fill(stale, true);
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
        outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        switcher.Process(io.Left, io.Right);

        var lowCut = Params.Advance(LowCut, io.Length);
        if (lowCut > CabBlock.LowCutOff) {
            var c = SvfCoefficients.Design(BiquadType.HighPass, Spec.SampleRate, lowCut, 0.7071);
            lowCutLeft.Process(c, io.Left);
            lowCutRight.Process(c, io.Right);
        }

        var highCut = Params.Advance(HighCut, io.Length);
        if (highCut < CabBlock.HighCutOff) {
            var c = SvfCoefficients.Design(BiquadType.LowPass, Spec.SampleRate, highCut, 0.7071);
            highCutLeft.Process(c, io.Left);
            highCutRight.Process(c, io.Right);
        }

        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, this.outputGain, outputGain);
        GainRamp.Apply(io.Right, this.outputGain, outputGain);
        this.outputGain = outputGain;
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            lock (sync) {
                disposed = true;
                switcher.Dispose();
                synthesizer?.Dispose();
                synthesizer = null;
                slotIrs.Dispose();
                mixLeft.Dispose();
                mixRight.Dispose();
                foreach (var grid in grids) {
                    grid?.Dispose();
                }
            }
        }

        base.Dispose(disposing);
    }

    static IEnumerable<ParamDescriptor> Slot(
        int n,
        int cab,
        int speaker,
        int mic,
        float position,
        float distance,
        bool muted
    ) {
        var name = $"Mic {n} ";
        var id = $"m{n}";
        return [
            ParamDescriptor.Choice(id + "cab", name + "Cab", [..CabCatalogue.Enclosures.Select(static e => e.Name)], cab),
            ParamDescriptor.Choice(id + "speaker", name + "Speaker", [..CabCatalogue.Speakers.Select(static s => s.Name)], speaker),
            ParamDescriptor.Choice(id + "mic", name + "Mic", [..CabCatalogue.Mics.Select(static m => m.Name)], mic),
            ParamDescriptor.Float(id + "position", name + "Position", 0f, 100f, position, "%"),
            ParamDescriptor.Float(id + "distance", name + "Distance", 0f, MaxDistance, distance, "in"),
            ParamDescriptor.Float(id + "angle", name + "Angle", 0f, 60f, 0f, "°"),
            ParamDescriptor.Float(id + "level", name + "Level", Decibels.SilenceDb, 12f, 0f, "dB"),
            ParamDescriptor.Float(id + "pan", name + "Pan", -100f, 100f, 0f, "%"),
            ParamDescriptor.Float(id + "delay", name + "Delay", 0f, MaxDelayMs, 0f, "ms"),
            ParamDescriptor.Switch(id + "invert", name + "Invert"),
            ParamDescriptor.Switch(id + "mute", name + "Mute", muted),
            ParamDescriptor.Switch(id + "solo", name + "Solo")
        ];
    }

    /// <summary>The most a slot's IR can be shifted: its delay plus the longest auto delay (12 in plus recess).</summary>
    static double MaxShiftSeconds() => MaxDelayMs / 1_000.0 + (MaxDistance * MetresPerInch + 0.1) / CabAcoustics.SpeedOfSound;

    static float Sinc(double x) => x == 0 ? 1f : (float)(Math.Sin(Math.PI * x) / (Math.PI * x));

    /// <summary>Adds <paramref name="ir" /> × <paramref name="gain" /> into <paramref name="mix" />, <paramref name="shift" /> samples late.</summary>
    static void AddShifted(ReadOnlySpan<float> ir, Span<float> mix, double shift, float gain) {
        var whole = (int)Math.Floor(shift);
        var fraction = shift - whole;
        if (fraction < 1e-6) {
            for (var i = 0; i < ir.Length && i + whole < mix.Length; i++) {
                mix[i + whole] += ir[i] * gain;
            }

            return;
        }

        // Windowed sinc (Blackman) taps for the fractional part; the output starts SincHalf earlier than the
        // integer delay, which MaxShiftSeconds' margin leaves room for (shift >= SincHalf by construction).
        Span<float> taps = stackalloc float[2 * SincHalf];
        for (var t = 0; t < taps.Length; t++) {
            var x = t - SincHalf + 1 - fraction;
            var window = 0.42 + 0.5 * Math.Cos(Math.PI * x / SincHalf) + 0.08 * Math.Cos(2 * Math.PI * x / SincHalf);
            taps[t] = Sinc(x) * (float)window * gain;
        }

        for (var i = 0; i < ir.Length; i++) {
            var v = ir[i];
            var start = i + whole - SincHalf + 1;
            for (var t = 0; t < taps.Length; t++) {
                var at = start + t;
                if ((uint)at < (uint)mix.Length) {
                    mix[at] += v * taps[t];
                }
            }
        }
    }

    void ReadTargets() {
        for (var i = 0; i < targets.Length; i++) {
            targets[i] = Params.GetTarget(i);
        }
    }

    bool NeedsBuild() {
        for (var s = 0; s < Slots; s++) {
            if (stale[s]) {
                return true;
            }
        }

        for (var i = Proximity; i < targets.Length; i++) {
            if (targets[i] != built[i]) {
                return true;
            }
        }

        return false;
    }

    /// <summary>Resynthesises the slots whose sound changed and mixes all four into a new pair.</summary>
    StereoIr Build() {
        var synthesizer = this.synthesizer!;
        var taps = synthesizer.Taps;
        var globalsChanged = targets[Proximity] != built[Proximity] || targets[Room] != built[Room];
        for (var s = 0; s < Slots; s++) {
            var changed = stale[s] || globalsChanged;
            for (var o = Cab; o <= Angle && !changed; o++) {
                changed = targets[SlotParam(s, o)] != built[SlotParam(s, o)];
            }

            if (changed) {
                var ir = slotIrs.Span.Slice(s * taps, taps);
                slotDelays[s] = grids[s] is { } grid
                    ? synthesizer.Synthesize(grid, targets[SlotParam(s, Position)] / 100.0, targets[SlotParam(s, Distance)], ir)
                    : synthesizer.Synthesize(Placement(s), ir);
                stale[s] = false;
            }
        }

        Array.Copy(targets, built, targets.Length);
        Mix();
        return new(mixLeft.Span, mixRight.Span);
    }

    MicPlacement Placement(int slot) =>
        new(
            CabCatalogue.Speakers[(int)targets[SlotParam(slot, Speaker)]],
            CabCatalogue.Enclosures[(int)targets[SlotParam(slot, Cab)]],
            CabCatalogue.Mics[(int)targets[SlotParam(slot, Mic)]],
            targets[SlotParam(slot, Position)] / 100.0,
            targets[SlotParam(slot, Distance)] * MetresPerInch,
            targets[SlotParam(slot, Angle)] * Math.PI / 180,
            targets[Proximity] >= 0.5f,
            targets[Room] / 100.0
        );

    void Mix() {
        var left = mixLeft.Span;
        var right = mixRight.Span;
        left.Clear();
        right.Clear();
        var taps = synthesizer!.Taps;
        var anySolo = false;
        for (var s = 0; s < Slots; s++) {
            anySolo |= targets[SlotParam(s, Solo)] >= 0.5f;
        }

        var auto = targets[AutoDelay] >= 0.5f;
        var earliest = double.MaxValue;
        for (var s = 0; s < Slots; s++) {
            if (IsAudible(s, anySolo)) {
                earliest = Math.Min(earliest, slotDelays[s]);
            }
        }

        for (var s = 0; s < Slots; s++) {
            if (!IsAudible(s, anySolo)) {
                continue;
            }

            var gain = Decibels.ToGain(targets[SlotParam(s, SlotLevel)]);
            if (targets[SlotParam(s, Invert)] >= 0.5f) {
                gain = -gain;
            }

            // Balance law: the centre is unity on both sides, so a centred mic keeps its level.
            var pan = targets[SlotParam(s, Pan)] / 100f;
            var seconds = targets[SlotParam(s, Delay)] / 1_000.0 + (auto ? slotDelays[s] - earliest : 0);
            var shift = SincHalf + seconds * Spec.SampleRate;
            var ir = slotIrs.Span.Slice(s * taps, taps);
            AddShifted(ir, left, shift, gain * (pan > 0 ? 1 - pan : 1));
            AddShifted(ir, right, shift, gain * (pan < 0 ? 1 + pan : 1));
        }

        // The SincHalf lead-in is silence at an integer shift; drop it so the block keeps zero latency.
        left[SincHalf..].CopyTo(left);
        right[SincHalf..].CopyTo(right);
        left[^SincHalf..].Clear();
        right[^SincHalf..].Clear();
    }

    bool IsAudible(int slot, bool anySolo) =>
        targets[SlotParam(slot, Mute)] < 0.5f && (!anySolo || targets[SlotParam(slot, Solo)] >= 0.5f);
}
