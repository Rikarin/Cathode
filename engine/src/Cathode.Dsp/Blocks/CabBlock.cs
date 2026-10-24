using Cathode.Dsp.Cabinets;
using Cathode.Dsp.Convolution;
using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Cabinet simulation, as the Axe-Fx III Cab block: four slots mixed with level, pan, delay, polarity, mute and solo.
///     Each slot plays one <see cref="Sources" /> entry: an impulse response file (<c>IR</c>), a modelled speaker in a
///     cabinet miked at a continuous position (cap to edge), distance and angle (<c>DynaCab</c>:
///     <see cref="CabAcoustics" />, <see cref="CabCatalogue" />), or a user's multi-position IR pack interpolated at
///     the slot's position and distance (<c>IR Pack</c>: <see cref="CabGrid" />). A slot's file goes into its asset
///     slot, <c>slot1</c> … <c>slot4</c>: a <c>.wav</c> (an IR, or a one-point pack) or a <c>.cabgrid</c>.
/// </summary>
/// <remarks>
///     <para>
///         The slots are linear and the input is one speaker signal per side, so all four mix into one left and one
///         right IR (level, pan, polarity, delay, each slot's own low and high cut and IR length baked in): the block
///         convolves twice whatever the slot count, for as long as its longest slot. IR files are resampled to the
///         engine rate, normalised to unit energy and truncated to <see cref="MaxIrLength" />; stereo IRs feed left and
///         right separately. A modelled slot is resynthesised only when its own acoustic parameters (or Proximity or
///         Room) change; any other slot change only remixes.
///     </para>
///     <para>
///         Slot levels are relative, as on the Axe-Fx: the levels of the slots that play are scaled to sum to unity, so
///         with one slot its level does nothing, and with more it sets the blend, not the loudness. The block's Level
///         sets the loudness.
///     </para>
///     <para>
///         A slot whose source has nothing to play (an IR or IR pack slot with no file) contributes nothing. When no
///         audible slot has anything to play the block passes audio through untouched, as a cab with no IR always has;
///         muting every slot silences it.
///     </para>
///     <para>
///         Around the convolution, on the audio thread: the Input mode (left, right, their sum, or stereo), Air (the
///         dry signal above Air Freq mixed back in, as the air between speaker and mic), the mic preamp
///         (<see cref="MicPreamp" />), the master low and high cuts and the Level.
///     </para>
///     <para>
///         Rebuilding runs on the control thread: in <see cref="IDspBlock.Prepare" />, and in
///         <see cref="Refresh" />, which the host calls after setting parameters. The new pair is crossfaded in over
///         20 ms by <see cref="CabIrSwitcher" />; the audio thread neither allocates nor waits. Zero latency.
///     </para>
/// </remarks>
public sealed class CabBlock() : DspBlock(Info), IAssetConsumer, IControlRefresh {
    // Global parameters run on the audio thread up to Level; from Proximity on they change the IRs: up to Diffusion
    // they resynthesise the modelled slots, AutoDelay and Smoothing only remix.
    public const int LowCut = 0;
    public const int LowSlope = 1;
    public const int HighCut = 2;
    public const int HighSlope = 3;
    public const int Input = 4;
    public const int Air = 5;
    public const int AirFreq = 6;
    public const int Preamp = 7;
    public const int Drive = 8;
    public const int Saturation = 9;
    public const int PreampBass = 10;
    public const int PreampMid = 11;
    public const int PreampTreble = 12;
    public const int Level = 13;
    public const int Proximity = 14;
    public const int ProximityFreq = 15;
    public const int Room = 16;
    public const int RoomSize = 17;
    public const int RoomShape = 18;
    public const int Floor = 19;
    public const int HighDamping = 20;
    public const int LowDamping = 21;
    public const int Diffusion = 22;
    public const int AutoDelay = 23;
    public const int Smoothing = 24;

    /// <summary>Index of slot 1's first parameter; slot n's are at <c>SlotBase + n * SlotStride</c>.</summary>
    public const int SlotBase = 25;

    public const int SlotStride = 18;
    public const int Slots = 4;

    // Offsets within a slot: Source … Angle shape a modelled IR; the rest only change the mix.
    public const int Source = 0;
    public const int Cab = 1;
    public const int Speaker = 2;
    public const int Mic = 3;
    public const int Position = 4;
    public const int Distance = 5;
    public const int Angle = 6;
    public const int SlotLowCut = 7;
    public const int SlotLowSlope = 8;
    public const int SlotHighCut = 9;
    public const int SlotHighSlope = 10;
    public const int Length = 11;
    public const int SlotLevel = 12;
    public const int Pan = 13;
    public const int Delay = 14;
    public const int Invert = 15;
    public const int Mute = 16;
    public const int Solo = 17;

    // Values of a slot's Source.
    public const int SourceIr = 0;
    public const int SourceDynaCab = 1;
    public const int SourcePack = 2;

    // Values of Input.
    public const int InputLeft = 0;
    public const int InputRight = 1;
    public const int InputSum = 2;
    public const int InputStereo = 3;

    /// <summary>Longest IR file kept, in samples at the engine rate (about 170 ms at 48 kHz).</summary>
    public const int MaxIrLength = 8_192;

    /// <summary>A slot's IR Length at or above this keeps the whole IR.</summary>
    public const float FullLengthMs = 170f;

    /// <summary>Low cut at or below this is off.</summary>
    public const float LowCutOff = 10f;

    /// <summary>High cut at or above this is off.</summary>
    public const float HighCutOff = 20_000f;

    /// <summary>Longest slot delay, ms.</summary>
    public const float MaxDelayMs = 5f;

    /// <summary>Longest distance, inches.</summary>
    public const float MaxDistance = 12f;

    /// <summary>Air at 100 %: the high-passed dry signal at -12 dB.</summary>
    const float FullAir = 0.25f;

    /// <summary>Smoothing at 100 %, octaves.</summary>
    const double FullSmoothing = 1;

    /// <summary>Diffusion at 100 %: a wall reflection's arrival spread by 0.2 ms, coherent only below about 1 kHz.</summary>
    const double FullDiffusion = 0.2e-3;

    /// <summary>The preamp tone controls' bands: a bass shelf, a broad mid peak and a treble shelf.</summary>
    const float PreampBassHz = 120f;

    const float PreampMidHz = 800f;
    const float PreampTrebleHz = 4_000f;

    const double MetresPerInch = 0.0254;

    /// <summary>Half-length of the windowed-sinc fractional delay used when mixing slots.</summary>
    const int SincHalf = 8;

    /// <summary>Fade at an IR Length cut, samples.</summary>
    const int LengthFade = 64;

    static readonly IrShape Shape = new(MaxIrLength);
    static readonly string[] SlotAssets = ["slot1", "slot2", "slot3", "slot4"];

    readonly Lock sync = new();
    readonly CabIrSwitcher switcher = new();
    readonly ImpulseResponseFile?[] files = new ImpulseResponseFile?[Slots];
    readonly float[][]?[] prepared = new float[][]?[Slots];
    readonly CabGrid?[] grids = new CabGrid?[Slots];
    readonly float[] targets = new float[SlotBase + Slots * SlotStride];
    readonly float[] built = new float[SlotBase + Slots * SlotStride];
    readonly double[] slotDelays = new double[Slots];
    readonly bool[] stale = new bool[Slots];
    readonly EqBand preampBass = new();
    readonly EqBand preampMid = new();
    readonly EqBand preampTreble = new();
    CabIrSynthesizer? synthesizer;
    IrSmoother? smoother;
    NativeBuffer slotIrs = NativeBuffer.Empty;
    NativeBuffer slotScratch = NativeBuffer.Empty;
    NativeBuffer mixLeft = NativeBuffer.Empty;
    NativeBuffer mixRight = NativeBuffer.Empty;
    NativeBuffer dryLeft = NativeBuffer.Empty;
    NativeBuffer dryRight = NativeBuffer.Empty;
    CutState lowCutLeft, lowCutRight, highCutLeft, highCutRight;
    SvfState airLeft, airRight;
    MicPreamp preampLeft, preampRight;
    float outputGain = 1f;
    float airGain;
    float drive;
    float saturation;
    bool passThrough;
    bool disposed;

    /// <summary>
    ///     A slot's Source choices, in order (<see cref="SourceIr" />, <see cref="SourceDynaCab" />,
    ///     <see cref="SourcePack" />).
    /// </summary>
    public static IReadOnlyList<string> Sources { get; } = ["IR", "DynaCab", "IR Pack"];

    /// <summary>Input choices, in order (<see cref="InputLeft" /> … <see cref="InputStereo" />).</summary>
    public static IReadOnlyList<string> Inputs { get; } = ["Left", "Right", "Sum L+R", "Stereo"];

    public static BlockDescriptor Info { get; } = new(
        "cab",
        "Cab",
        "Cab",
        [
            ParamDescriptor.Float("lowcut", "Low Cut", LowCutOff, 500f, LowCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Choice("lowslope", "Low Cut Slope", CutFilter.Slopes, CutFilter.Slope12),
            ParamDescriptor.Float("highcut", "High Cut", 1_000f, HighCutOff, HighCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Choice("highslope", "High Cut Slope", CutFilter.Slopes, CutFilter.Slope12),
            ParamDescriptor.Choice("input", "Input", Inputs, InputStereo),
            ParamDescriptor.Float("air", "Air", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("airfreq", "Air Freq", 1_000f, 20_000f, 6_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Choice("preamp", "Preamp Type", MicPreamp.Types, MicPreamp.None),
            ParamDescriptor.Float("drive", "Preamp Drive", 0f, MicPreamp.MaxDriveDb, 0f, "dB"),
            ParamDescriptor.Float("saturation", "Preamp Saturation", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("prebass", "Preamp Bass", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("premid", "Preamp Mid", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("pretreble", "Preamp Treble", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            IrParam(ParamDescriptor.Float("proximity", "Proximity", 0f, 200f, 100f, "%")),
            IrParam(ParamDescriptor.Float("proxfreq", "Proximity Freq", 25f, 400f, 100f, "%", ParamTaper.Log)),
            IrParam(ParamDescriptor.Float("room", "Room", 0f, 100f, 0f, "%")),
            IrParam(ParamDescriptor.Float("roomsize", "Room Size", 50f, 200f, 100f, "%", ParamTaper.Log)),
            IrParam(ParamDescriptor.Float("roomshape", "Room Shape", -100f, 100f, 0f, "%")),
            IrParam(ParamDescriptor.Float("floor", "Floor Reflection", 0f, 200f, 100f, "%")),
            IrParam(ParamDescriptor.Float("hfdamp", "Room HF Damping", 0f, 100f, 50f, "%")),
            IrParam(ParamDescriptor.Float("lfdamp", "Room LF Damping", 0f, 500f, 0f, "Hz")),
            IrParam(ParamDescriptor.Float("diffusion", "Room Diffusion", 0f, 100f, 0f, "%")),
            ParamDescriptor.Switch("autodelay", "Auto Delay"),
            IrParam(ParamDescriptor.Float("smoothing", "Smoothing", 0f, 100f, 0f, "%")),
            ..Slot(1, 5, 3, 0, 25, 1, false),
            ..Slot(2, 5, 3, 5, 50, 4, true),
            ..Slot(3, 5, 0, 1, 35, 2, true),
            ..Slot(4, 5, 3, 8, 0, 12, true)
        ],
        maxInstances: 2
    );

    public IReadOnlyList<string> AssetSlots => SlotAssets;

    /// <summary>Changes the audio thread has crossfaded in since Prepare (tests, diagnostics).</summary>
    public int IrSwaps => switcher.Swaps;

    /// <summary>Audio thread: true while a new IR is being crossfaded in.</summary>
    public bool IsFading => switcher.IsFading;

    /// <summary>Length of a modelled slot's IR at the prepared rate, before slot delays.</summary>
    public int Taps => synthesizer?.Taps ?? 0;

    /// <summary>Control thread: true when the last build found nothing to play, so the block passes audio through.</summary>
    public bool IsPassThrough => passThrough;

    /// <summary>Parameter index of <paramref name="offset" /> in slot <paramref name="slot" /> (0-based).</summary>
    public static int SlotParam(int slot, int offset) => SlotBase + slot * SlotStride + offset;

    /// <summary>
    ///     Control thread: loads <paramref name="path" /> into <c>slot1</c> … <c>slot4</c>. A <c>.wav</c> serves both an
    ///     IR slot and an IR Pack slot (as a one-point pack); a <c>.cabgrid</c> (or a pack's folder or manifest) only
    ///     an IR Pack slot.
    /// </summary>
    public void LoadAsset(string slot, string path) {
        var index = Array.IndexOf(SlotAssets, slot);
        if (index < 0) {
            throw new ArgumentException($"Unknown asset slot '{slot}'.", nameof(slot));
        }

        ImpulseResponseFile? file = null;
        CabGrid grid;
        if (string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase)) {
            file = ImpulseResponses.Load(path);
            grid = CabGrid.Single(Path.GetFileNameWithoutExtension(path), file.Source);
        } else {
            grid = CabGrid.Load(path);
        }

        lock (sync) {
            files[index] = file;
            prepared[index] = file is not null && synthesizer is not null ? file.Prepare(Spec.SampleRate, Shape) : null;
            grids[index]?.Dispose();
            grids[index] = grid;
            stale[index] = true;
        }
    }

    /// <summary>The IR pack in <paramref name="slot" />, if one is loaded.</summary>
    public CabGrid? Grid(int slot) => grids[slot];

    /// <summary>
    ///     The IR file in <paramref name="slot" /> as it is mixed (resampled, truncated, normalised), for tests and the
    ///     editor's plot; null before Prepare or without a <c>.wav</c>.
    /// </summary>
    public float[]? PreparedImpulseResponse(int slot, int channel = 0) =>
        prepared[slot] is { } channels ? channels[Math.Min(channel, channels.Length - 1)] : null;

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
    ///     changed), for tests and plots; a unit impulse when the block passes through. Prepare first.
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
            slotScratch.Dispose();
            mixLeft.Dispose();
            mixRight.Dispose();
            dryLeft.Dispose();
            dryRight.Dispose();
            smoother?.Dispose();
            synthesizer = new(spec.SampleRate);
            var taps = synthesizer.Taps;
            slotIrs = new(Slots * taps);
            slotScratch = new(Math.Max(MaxIrLength, taps));
            var length = Math.Max(MaxIrLength, taps)
                + (int)Math.Ceiling(MaxShiftSeconds() * spec.SampleRate)
                + 2 * SincHalf;
            smoother = new(length);
            mixLeft = new(length);
            mixRight = new(length);
            dryLeft = new(spec.MaxBlockSize);
            dryRight = new(spec.MaxBlockSize);
            for (var s = 0; s < Slots; s++) {
                prepared[s] = files[s]?.Prepare(spec.SampleRate, Shape);
            }

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
        airLeft.Reset();
        airRight.Reset();
        preampLeft.Reset();
        preampRight.Reset();
        preampBass.Reset();
        preampMid.Reset();
        preampTreble.Reset();
        outputGain = Decibels.ToGain(Params.Current(Level));
        airGain = AirGain(Params.Current(Air));
        drive = Params.Current(Drive);
        saturation = Params.Current(Saturation) / 100f;
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var input = (int)Params.Advance(Input, io.Length);
        if (input == InputLeft) {
            io.Left.CopyTo(io.Right);
        } else if (input == InputRight) {
            io.Right.CopyTo(io.Left);
        } else if (input == InputSum) {
            for (var i = 0; i < io.Length; i++) {
                var sum = 0.5f * (io.Left[i] + io.Right[i]);
                io.Left[i] = sum;
                io.Right[i] = sum;
            }
        }

        // Air: the dry signal above Air Freq, kept from before the convolution and mixed back after it.
        var airGain = AirGain(Params.Advance(Air, io.Length));
        var airFreq = Params.Advance(AirFreq, io.Length);
        var air = airGain > 0f || this.airGain > 0f;
        if (air) {
            if (this.airGain == 0f) {
                airLeft.Reset();
                airRight.Reset();
            }

            var c = SvfCoefficients.Design(BiquadType.HighPass, Spec.SampleRate, airFreq, 0.7071);
            var left = dryLeft.Span[..io.Length];
            var right = dryRight.Span[..io.Length];
            io.Left.CopyTo(left);
            io.Right.CopyTo(right);
            airLeft.Process(c, left);
            airRight.Process(c, right);
            GainRamp.Apply(left, this.airGain, airGain);
            GainRamp.Apply(right, this.airGain, airGain);
        }

        switcher.Process(io.Left, io.Right);
        if (air) {
            Accumulate(dryLeft.Span[..io.Length], io.Left);
            Accumulate(dryRight.Span[..io.Length], io.Right);
        }

        this.airGain = airGain;

        var type = (int)Params.Advance(Preamp, io.Length);
        var drive = Params.Advance(Drive, io.Length);
        var saturation = Params.Advance(Saturation, io.Length) / 100f;
        preampLeft.Process(type, Spec.SampleRate, this.drive, drive, this.saturation, saturation, io.Left);
        preampRight.Process(type, Spec.SampleRate, this.drive, drive, this.saturation, saturation, io.Right);
        this.drive = drive;
        this.saturation = saturation;
        Tone(io, preampBass, BiquadType.LowShelf, PreampBassHz, 0.7071f, PreampBass);
        Tone(io, preampMid, BiquadType.Peak, PreampMidHz, 0.7f, PreampMid);
        Tone(io, preampTreble, BiquadType.HighShelf, PreampTrebleHz, 0.7071f, PreampTreble);

        var lowCut = Params.Advance(LowCut, io.Length);
        var lowSlope = (int)Params.Advance(LowSlope, io.Length);
        if (lowCut > LowCutOff) {
            var c = CutFilter.Design(true, Spec.SampleRate, lowCut, lowSlope);
            lowCutLeft.Process(c, io.Left);
            lowCutRight.Process(c, io.Right);
        }

        var highCut = Params.Advance(HighCut, io.Length);
        var highSlope = (int)Params.Advance(HighSlope, io.Length);
        if (highCut < HighCutOff) {
            var c = CutFilter.Design(false, Spec.SampleRate, highCut, highSlope);
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
                smoother?.Dispose();
                smoother = null;
                slotIrs.Dispose();
                slotScratch.Dispose();
                mixLeft.Dispose();
                mixRight.Dispose();
                dryLeft.Dispose();
                dryRight.Dispose();
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
            ParamDescriptor.Choice(id + "source", name + "Source", Sources, SourceDynaCab),
            ParamDescriptor.Choice(
                id + "cab",
                name + "Cab",
                [..CabCatalogue.Enclosures.Select(static e => e.Name)],
                cab
            ),
            ParamDescriptor.Choice(
                id + "speaker",
                name + "Speaker",
                [..CabCatalogue.Speakers.Select(static s => s.Name)],
                speaker
            ),
            ParamDescriptor.Choice(id + "mic", name + "Mic", [..CabCatalogue.Mics.Select(static m => m.Name)], mic),
            ParamDescriptor.Float(id + "position", name + "Position", 0f, 100f, position, "%"),
            ParamDescriptor.Float(id + "distance", name + "Distance", 0f, MaxDistance, distance, "in"),
            ParamDescriptor.Float(id + "angle", name + "Angle", 0f, 60f, 0f, "°"),
            ParamDescriptor.Float(id + "lowcut", name + "Low Cut", LowCutOff, 500f, LowCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Choice(id + "lowslope", name + "Low Cut Slope", CutFilter.Slopes, CutFilter.Slope12),
            ParamDescriptor.Float(
                id + "highcut",
                name + "High Cut",
                1_000f,
                HighCutOff,
                HighCutOff,
                "Hz",
                ParamTaper.Log
            ),
            ParamDescriptor.Choice(id + "highslope", name + "High Cut Slope", CutFilter.Slopes, CutFilter.Slope12),
            ParamDescriptor.Float(
                id + "length",
                name + "IR Length",
                5f,
                FullLengthMs,
                FullLengthMs,
                "ms",
                ParamTaper.Log
            ),
            ParamDescriptor.Float(id + "level", name + "Level", Decibels.SilenceDb, 12f, 0f, "dB"),
            ParamDescriptor.Float(id + "pan", name + "Pan", -100f, 100f, 0f, "%"),
            ParamDescriptor.Float(id + "delay", name + "Delay", 0f, MaxDelayMs, 0f, "ms"),
            ParamDescriptor.Switch(id + "invert", name + "Invert"),
            ParamDescriptor.Switch(id + "mute", name + "Mute", muted),
            ParamDescriptor.Switch(id + "solo", name + "Solo")
        ];
    }

    static float AirGain(float percent) => FullAir * percent / 100f;

    /// <summary>A parameter that rebuilds the IRs: applied as a step, as a rebuild is not ramped.</summary>
    static ParamDescriptor IrParam(ParamDescriptor descriptor) => descriptor with { Smoothing = ParamSmoothing.Step };

    /// <summary>One preamp tone band; at 0 dB and still, it is skipped.</summary>
    void Tone(in StereoBuffer io, EqBand band, BiquadType type, float frequency, float q, int param) {
        var moving = Params.IsRamping(param);
        band.Process(io, type, Spec.SampleRate, frequency, q, Params.Advance(param, io.Length), moving);
    }

    static void Accumulate(ReadOnlySpan<float> source, Span<float> destination) {
        for (var i = 0; i < source.Length; i++) {
            destination[i] += source[i];
        }
    }

    /// <summary>The most a slot's IR can be shifted: its delay plus the longest auto delay (12 in plus recess).</summary>
    static double MaxShiftSeconds() =>
        MaxDelayMs / 1_000.0 + (MaxDistance * MetresPerInch + 0.1) / CabAcoustics.SpeedOfSound;

    static float Sinc(double x) => x == 0 ? 1f : (float)(Math.Sin(Math.PI * x) / (Math.PI * x));

    /// <summary>
    ///     Adds <paramref name="ir" /> × <paramref name="gain" /> into <paramref name="mix" />, <paramref name="shift" />
    ///     samples late.
    /// </summary>
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

    /// <summary>One past the last non-zero sample of either side, at least 1.</summary>
    static int UsedLength(ReadOnlySpan<float> left, ReadOnlySpan<float> right) {
        for (var i = left.Length - 1; i > 0; i--) {
            if (left[i] != 0f || right[i] != 0f) {
                return i + 1;
            }
        }

        return 1;
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

    int SourceOf(int slot) => (int)targets[SlotParam(slot, Source)];

    float SlotTarget(int slot, int offset) => targets[SlotParam(slot, offset)];

    /// <summary>True when <paramref name="slot" />'s source has something to play.</summary>
    bool HasResponse(int slot) =>
        SourceOf(slot) switch {
            SourceIr => prepared[slot] is not null,
            SourcePack => grids[slot] is not null,
            _ => true
        };

    /// <summary>Resynthesises the modelled slots whose sound changed and mixes all four into a new pair.</summary>
    StereoIr Build() {
        var synthesizer = this.synthesizer!;
        var taps = synthesizer.Taps;
        var globalsChanged = false;
        for (var i = Proximity; i < AutoDelay; i++) {
            globalsChanged |= targets[i] != built[i];
        }

        for (var s = 0; s < Slots; s++) {
            var source = SourceOf(s);
            var changed = stale[s] || globalsChanged;
            for (var o = Source; o <= Angle && !changed; o++) {
                changed = targets[SlotParam(s, o)] != built[SlotParam(s, o)];
            }

            if (changed && HasResponse(s) && source != SourceIr) {
                var ir = slotIrs.Span.Slice(s * taps, taps);
                slotDelays[s] = source == SourcePack
                    ? synthesizer.Synthesize(grids[s]!, SlotTarget(s, Position) / 100.0, SlotTarget(s, Distance), ir)
                    : synthesizer.Synthesize(Placement(s), ir);
            }

            stale[s] = false;
        }

        Array.Copy(targets, built, targets.Length);
        Mix();
        if (passThrough) {
            return new();
        }

        var length = UsedLength(mixLeft.Span, mixRight.Span);
        var octaves = targets[Smoothing] / 100.0 * FullSmoothing;
        if (octaves > 0) {
            length = Math.Max(
                smoother!.Smooth(mixLeft.Span, length, octaves),
                smoother.Smooth(mixRight.Span, length, octaves)
            );
        }

        return new(mixLeft.Span[..length], mixRight.Span[..length]);
    }

    MicPlacement Placement(int slot) =>
        new(
            CabCatalogue.Speakers[(int)SlotTarget(slot, Speaker)],
            CabCatalogue.Enclosures[(int)SlotTarget(slot, Cab)],
            CabCatalogue.Mics[(int)SlotTarget(slot, Mic)],
            SlotTarget(slot, Position) / 100.0,
            SlotTarget(slot, Distance) * MetresPerInch,
            SlotTarget(slot, Angle) * Math.PI / 180,
            targets[Proximity] > 0f,
            targets[Room] / 100.0,
            targets[Proximity] / 100.0,
            targets[ProximityFreq] / 100.0,
            new CabRoom(
                targets[RoomSize] / 100.0,
                targets[RoomShape] / 100.0,
                targets[Floor] / 100.0,
                Math.Pow(4, (targets[HighDamping] - 50) / 50.0),
                targets[LowDamping],
                targets[Diffusion] / 100.0 * FullDiffusion
            )
        );

    void Mix() {
        var left = mixLeft.Span;
        var right = mixRight.Span;
        left.Clear();
        right.Clear();
        var taps = synthesizer!.Taps;
        var anySolo = false;
        for (var s = 0; s < Slots; s++) {
            anySolo |= SlotTarget(s, Solo) >= 0.5f;
        }

        // Auto delay lines the modelled slots up by their mics' travel times; an IR file carries its own onset. The
        // levels of the slots that play are relative: scaled to sum to unity.
        var auto = targets[AutoDelay] >= 0.5f;
        var earliest = double.MaxValue;
        var audible = false;
        var playing = false;
        var levels = 0f;
        for (var s = 0; s < Slots; s++) {
            if (!IsAudible(s, anySolo)) {
                continue;
            }

            audible = true;
            if (HasResponse(s)) {
                playing = true;
                levels += Decibels.ToGain(SlotTarget(s, SlotLevel));
                if (SourceOf(s) != SourceIr) {
                    earliest = Math.Min(earliest, slotDelays[s]);
                }
            }
        }

        passThrough = audible && !playing;
        if (passThrough) {
            left[0] = 1f;
            right[0] = 1f;
            return;
        }

        var normalise = levels > 0f ? 1f / levels : 0f;
        for (var s = 0; s < Slots; s++) {
            if (!IsAudible(s, anySolo) || !HasResponse(s)) {
                continue;
            }

            var gain = Decibels.ToGain(SlotTarget(s, SlotLevel)) * normalise;
            if (SlotTarget(s, Invert) >= 0.5f) {
                gain = -gain;
            }

            // Balance law: the centre is unity on both sides, so a centred mic keeps its level.
            var pan = SlotTarget(s, Pan) / 100f;
            var file = SourceOf(s) == SourceIr;
            var seconds = SlotTarget(s, Delay) / 1_000.0 + (auto && !file ? slotDelays[s] - earliest : 0);
            var shift = SincHalf + seconds * Spec.SampleRate;
            var gainLeft = gain * (pan > 0 ? 1 - pan : 1);
            var gainRight = gain * (pan < 0 ? 1 + pan : 1);
            if (file) {
                var channels = prepared[s]!;
                AddShifted(Shaped(s, channels[0]), left, shift, gainLeft);
                AddShifted(Shaped(s, channels[^1]), right, shift, gainRight);
            } else {
                var ir = Shaped(s, slotIrs.Span.Slice(s * taps, taps));
                AddShifted(ir, left, shift, gainLeft);
                AddShifted(ir, right, shift, gainRight);
            }
        }

        // The SincHalf lead-in is silence at an integer shift; drop it so the block keeps zero latency.
        left[SincHalf..].CopyTo(left);
        right[SincHalf..].CopyTo(right);
        left[^SincHalf..].Clear();
        right[^SincHalf..].Clear();
    }

    /// <summary>
    ///     <paramref name="ir" /> through the slot's own low and high cut and cut to its IR Length (with a short fade),
    ///     in the scratch buffer; or <paramref name="ir" /> itself when the slot does none of these.
    /// </summary>
    ReadOnlySpan<float> Shaped(int slot, ReadOnlySpan<float> ir) {
        var lowCut = SlotTarget(slot, SlotLowCut);
        var highCut = SlotTarget(slot, SlotHighCut);
        var lengthMs = SlotTarget(slot, Length);
        var length = lengthMs >= FullLengthMs
            ? ir.Length
            : Math.Min(ir.Length, (int)(lengthMs / 1_000.0 * Spec.SampleRate));
        if (lowCut <= LowCutOff && highCut >= HighCutOff && length == ir.Length) {
            return ir;
        }

        var shaped = slotScratch.Span[..length];
        ir[..length].CopyTo(shaped);
        if (lowCut > LowCutOff) {
            var state = new CutState();
            state.Process(CutFilter.Design(true, Spec.SampleRate, lowCut, (int)SlotTarget(slot, SlotLowSlope)), shaped);
        }

        if (highCut < HighCutOff) {
            var state = new CutState();
            state.Process(
                CutFilter.Design(false, Spec.SampleRate, highCut, (int)SlotTarget(slot, SlotHighSlope)),
                shaped
            );
        }

        if (length < ir.Length) {
            var fade = Math.Min(LengthFade, length);
            for (var i = 0; i < fade; i++) {
                shaped[length - fade + i] *= 0.5f * (1f + MathF.Cos(MathF.PI * (i + 1) / fade));
            }
        }

        return shaped;
    }

    bool IsAudible(int slot, bool anySolo) =>
        SlotTarget(slot, Mute) < 0.5f && (!anySolo || SlotTarget(slot, Solo) >= 0.5f);
}
