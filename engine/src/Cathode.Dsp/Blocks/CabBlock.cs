using Cathode.Dsp.Cabinets;
using Cathode.Dsp.Convolution;
using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Resampling;

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
///         The slots are linear, so they mix into impulse responses (level, pan, polarity, delay, and each slot's own
///         proximity, smoothing, low and high cut and IR length baked in). With the Input on Left, Right or Sum, both
///         sides carry the same signal and all four slots mix into one left and one right IR: two convolutions whatever
///         the slot count. With the Input on Stereo, as on the Axe-Fx, slots 1 and 3 hear the left input and slots 2
///         and 4 the right, each still panned into the stereo output: the left-fed slots mix into one pair and the
///         right-fed into another, four convolutions in all (<see cref="IrFeed.Cross" />), or two when only one side's
///         slots play. Each side's slot levels are then relative among themselves. IR files are resampled to the engine
///         rate, normalised to unit energy and truncated to <see cref="IrCapacity" /> (FullRes: 65,536 samples at
///         48 kHz, about 1.37 s); stereo IRs feed left and right separately. A modelled slot is resynthesised only when
///         its own acoustic parameters (or the Room) change; any other slot change only remixes.
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
///         Around the convolution, on the audio thread: the Input mode (left, right, their sum, or stereo), Motor
///         Drive (the voice coil's thermal compression, <see cref="VoiceCoil" />, before the speaker's response), Air
///         (the dry signal above Air Freq mixed back in, as the air between speaker and mic), the mic preamp
///         (<see cref="MicPreamp" />; Preamp Mode High Quality runs it 4× oversampled, Economy at the engine rate; a
///         change of mode rebuilds the graph, so only a High Quality cab holds the oversamplers), the master low and
///         high cuts and the Level. With the global cab modelling off (<see cref="BlockContext.CabsOff" />) the block
///         passes audio through untouched.
///     </para>
///     <para>
///         Room Mic Spacing: the modelled room is heard by a stereo pair of room mics that far apart. Each modelled
///         slot is then synthesised twice, its reflections reaching the left IR from half the spacing to one side and
///         the right IR from half to the other (<see cref="CabRoom.MicOffset" />); the direct sound is the slot's own
///         mic on both, so the image widens with the spacing while the close sound stays centred. At 0, or with no
///         Room, both sides share one IR as before. IR files and packs carry their own room.
///     </para>
///     <para>
///         Rebuilding runs on the control thread: in <see cref="IDspBlock.Prepare" />, and in
///         <see cref="Refresh" />, which the host calls after setting parameters. The new pair is crossfaded in over
///         20 ms by <see cref="CabIrSwitcher" />; the audio thread neither allocates nor waits. Zero latency: the
///         convolver runs its first 64 taps directly and the rest as uniformly partitioned FFT convolution. The new
///         pair starts with empty history, so for a long (FullRes) IR the old pair's tail is cut at the end of the fade:
///         the switch is click-free but not gapless for reverberant IRs.
///     </para>
/// </remarks>
public sealed class CabBlock() : DspBlock(Info), IAssetConsumer, IControlRefresh, IBlockMeters {
    // Global parameters run on the audio thread up to Motor Time; from Room on they change the IRs (they resynthesise
    // the modelled slots), except Preamp Mode, the audio thread's. Input also changes how the slots are mixed.
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
    public const int MotorDrive = 14;
    public const int MotorTime = 15;
    public const int Room = 16;
    public const int RoomSize = 17;
    public const int RoomShape = 18;
    public const int Floor = 19;
    public const int HighDamping = 20;
    public const int LowDamping = 21;
    public const int Diffusion = 22;
    public const int PreampMode = 23;
    public const int MicSpacing = 24;

    /// <summary>Index of slot 1's first parameter; slot n's are at <c>SlotBase + n * SlotStride</c>.</summary>
    public const int SlotBase = 25;

    public const int SlotStride = 22;
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
    public const int Proximity = 18;
    public const int ProximityFreq = 19;
    public const int Smoothing = 20;
    public const int AutoAlign = 21;

    // Values of a slot's Source.
    public const int SourceIr = 0;
    public const int SourceDynaCab = 1;
    public const int SourcePack = 2;
    public const int SourceFlat = 3;

    // Values of Preamp Mode.
    public const int PreampEconomy = 0;
    public const int PreampHighQuality = 1;

    /// <summary>Widest Room Mic Spacing, cm.</summary>
    public const float MaxMicSpacing = 300f;

    // Values of Input.
    public const int InputLeft = 0;
    public const int InputRight = 1;
    public const int InputSum = 2;
    public const int InputStereo = 3;

    /// <summary>
    ///     Longest IR file kept (FullRes), in samples at 48 kHz: about 1.37 s. <see cref="IrCapacity" /> scales it to
    ///     the engine rate.
    /// </summary>
    public const int MaxIrLength = 65_536;

    /// <summary>A slot's IR Length at or above this keeps the whole IR.</summary>
    public const float FullLengthMs = 1_366f;

    /// <summary>Low cut at or below this is off.</summary>
    public const float LowCutOff = 10f;

    /// <summary>High cut at or above this is off.</summary>
    public const float HighCutOff = 20_000f;

    /// <summary>Longest slot delay as a mic distance, metres (the Axe-Fx's 3.4 m).</summary>
    public const double MaxMicDistance = 3.4;

    /// <summary>Longest slot delay, ms: <see cref="MaxMicDistance" /> at the speed of sound, about 9.9 ms.</summary>
    public const float MaxDelayMs = (float)(MaxMicDistance / CabAcoustics.SpeedOfSound * 1_000);

    /// <summary>Motor Time Constant's range, seconds.</summary>
    public const float MinMotorTime = 0.05f;

    public const float MaxMotorTime = 1f;

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

    /// <summary>
    ///     Proximity on an IR, IR pack or Flat slot: the near-field boost <c>1 + a ω_c / (jω + ω_f)</c> of a
    ///     pressure-gradient mic, held finite below this floor as <see cref="CabAcoustics" /> holds it.
    /// </summary>
    const double ProximityFloorHz = 12;

    /// <summary>Room left after a slot's IR for its proximity boost and smoothing to ring on, seconds.</summary>
    const double TailSeconds = 0.0853;

    /// <summary>A short smoothing FFT serves IRs up to this many samples at 48 kHz (DynaCabs, short files).</summary>
    const int ShortSmoothing = 16_384;

    /// <summary>The preamp input meter's fall after a peak: seconds to fall by 1/e.</summary>
    const double MeterRelease = 0.3;

    /// <summary>The meters' floor, dB.</summary>
    const float MeterFloorDb = -96f;

    /// <summary>Half-length of the windowed-sinc fractional delay used when mixing slots.</summary>
    const int SincHalf = 8;

    /// <summary>Fade at an IR Length cut, samples.</summary>
    const int LengthFade = 64;

    static readonly string[] SlotAssets = ["slot1", "slot2", "slot3", "slot4"];
    static readonly string[] Meters = ["preampIn", "motorReduction"];

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
    IrSmoother? longSmoother;
    NativeBuffer slotIrs = NativeBuffer.Empty;
    NativeBuffer slotScratch = NativeBuffer.Empty;
    NativeBuffer flat = NativeBuffer.Empty;
    NativeBuffer mixLeft = NativeBuffer.Empty;
    NativeBuffer mixRight = NativeBuffer.Empty;
    NativeBuffer mixLeft2 = NativeBuffer.Empty;
    NativeBuffer mixRight2 = NativeBuffer.Empty;
    NativeBuffer dryLeft = NativeBuffer.Empty;
    NativeBuffer dryRight = NativeBuffer.Empty;
    NativeBuffer bypassLeft = NativeBuffer.Empty;
    NativeBuffer bypassRight = NativeBuffer.Empty;
    CutState lowCutLeft, lowCutRight, highCutLeft, highCutRight;
    SvfState airLeft, airRight;
    MicPreamp preampLeft, preampRight;
    VoiceCoil coilLeft, coilRight;
    Oversampler4x? upLeft, upRight;
    NativeBuffer slotRoomIrs = NativeBuffer.Empty; // the right room mic's slot IRs, while Room Mic Spacing is on
    IrFeed feed;
    int capacity;
    int tail;
    float outputGain = 1f;
    float airGain;
    float drive;
    float saturation;
    float motorDrive;
    float preampMeter;
    float motorMeter;
    bool cabsOff;
    bool disposed;

    /// <summary>
    ///     A slot's Source choices, in order (<see cref="SourceIr" />, <see cref="SourceDynaCab" />,
    ///     <see cref="SourcePack" />, <see cref="SourceFlat" />: a unit impulse that leaves the sound as it is).
    /// </summary>
    public static IReadOnlyList<string> Sources { get; } = ["IR", "DynaCab", "IR Pack", "Flat"];

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
            ParamDescriptor.Choice("preamp", "Preamp Type", MicPreamp.Types),
            ParamDescriptor.Float("drive", "Preamp Drive", 0f, MicPreamp.MaxDriveDb, 0f, "dB"),
            ParamDescriptor.Float("saturation", "Preamp Saturation", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("prebass", "Preamp Bass", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("premid", "Preamp Mid", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("pretreble", "Preamp Treble", -12f, 12f, 0f, "dB"),
            BlockParams.Level,
            ParamDescriptor.Float("motor", "Motor Drive", 0f, 100f, 0f, "%"),
            IrParam(
                ParamDescriptor.Float(
                    "motortime",
                    "Motor Time Constant",
                    MinMotorTime,
                    MaxMotorTime,
                    0.5f,
                    "s",
                    ParamTaper.Log
                )
            ),
            IrParam(ParamDescriptor.Float("room", "Room", 0f, 100f, 0f, "%")),
            IrParam(ParamDescriptor.Float("roomsize", "Room Size", 50f, 200f, 100f, "%", ParamTaper.Log)),
            IrParam(ParamDescriptor.Float("roomshape", "Room Shape", -100f, 100f, 0f, "%")),
            IrParam(ParamDescriptor.Float("floor", "Floor Reflection", 0f, 200f, 100f, "%")),
            IrParam(ParamDescriptor.Float("hfdamp", "Room HF Damping", 0f, 100f, 50f, "%")),
            IrParam(ParamDescriptor.Float("lfdamp", "Room LF Damping", 0f, 500f, 0f, "Hz")),
            IrParam(ParamDescriptor.Float("diffusion", "Room Diffusion", 0f, 100f, 0f, "%")),
            ParamDescriptor.Choice("preampMode", "Preamp Mode", ["Economy", "High Quality"]) with { Recompile = true },
            IrParam(ParamDescriptor.Float("micspacing", "Room Mic Spacing", 0f, MaxMicSpacing, 0f, "cm")),
            ..Slot(1, 5, 3, 0, 25, 1, false),
            ..Slot(2, 5, 3, 5, 50, 4, true),
            ..Slot(3, 5, 0, 1, 35, 2, true),
            ..Slot(4, 5, 3, 8, 0, 12, true)
        ],
        maxInstances: 2,
        pages: [
            new(
                "Basic",
                [
                    "input", "lowcut", "lowslope", "highcut", "highslope", "air", "airfreq", "motor", "motortime"
                ]
            ),
            new("Preamp", ["preampMode", "preamp", "drive", "saturation", "prebass", "premid", "pretreble"]),
            new("Room", ["room", "roomsize", "roomshape", "floor", "hfdamp", "lfdamp", "diffusion", "micspacing"])
        ]
    );

    public IReadOnlyList<string> AssetSlots => SlotAssets;

    /// <summary>
    ///     <c>preampIn</c>: the level into the mic preamp's curve, dB relative to its onset of clipping (0 is where the
    ///     chosen type starts to clip); <c>motorReduction</c>: Motor Drive's gain reduction, dB.
    /// </summary>
    public IReadOnlyList<string> MeterIds => Meters;

    /// <summary>How the last build feeds the inputs to the IRs (tests, diagnostics).</summary>
    public IrFeed Feed => feed;

    /// <summary>Changes the audio thread has crossfaded in since Prepare (tests, diagnostics).</summary>
    public int IrSwaps => switcher.Swaps;

    /// <summary>Audio thread: true while a new IR is being crossfaded in.</summary>
    public bool IsFading => switcher.IsFading;

    /// <summary>Length of a modelled slot's IR at the prepared rate, before slot delays.</summary>
    public int Taps => synthesizer?.Taps ?? 0;

    /// <summary>Control thread: true when the last build found nothing to play, so the block passes audio through.</summary>
    public bool IsPassThrough { get; private set; }

    /// <summary>Parameter index of <paramref name="offset" /> in slot <paramref name="slot" /> (0-based).</summary>
    public static int SlotParam(int slot, int offset) => SlotBase + slot * SlotStride + offset;

    /// <summary>
    ///     The DynaCab enclosure and speaker (indices into <see cref="CabCatalogue" />) of the first unmuted slot that
    ///     synthesises one, or (-1, -1) when none does: what an amp's Auto speaker load follows
    ///     (<see cref="ICabFollower" />). Allocation-free; any thread.
    /// </summary>
    public (int Enclosure, int Speaker) DynaCab {
        get {
            for (var slot = 0; slot < Slots; slot++) {
                if ((int)Params.Current(SlotParam(slot, Source)) == SourceDynaCab
                    && Params.Current(SlotParam(slot, Mute)) < 0.5f) {
                    return ((int)Params.Current(SlotParam(slot, Cab)), (int)Params.Current(SlotParam(slot, Speaker)));
                }
            }

            return (-1, -1);
        }
    }

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
#pragma warning disable SK2152 // An extension test, not path identity: a .WAV file is a wave file on every file system.
        if (string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase)) {
#pragma warning restore SK2152
            file = ImpulseResponses.Load(path);
            grid = CabGrid.Single(Path.GetFileNameWithoutExtension(path), file.Source);
        } else {
            grid = CabGrid.Load(path);
        }

        lock (sync) {
            files[index] = file;
            prepared[index] = file is not null && synthesizer is not null
                ? file.Prepare(Spec.SampleRate, new(IrCapacity(Spec.SampleRate)))
                : null;
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

    /// <summary>The longest IR file kept at <paramref name="sampleRate" />: <see cref="MaxIrLength" /> at 48 kHz.</summary>
    public static int IrCapacity(double sampleRate) =>
        (int)Math.Ceiling(MaxIrLength * sampleRate / CabIrSynthesizer.ReferenceRate);

    public void ReadMeters(Span<float> values) {
        values[0] = preampMeter > 0f ? MathF.Max(MeterFloorDb, 20f * MathF.Log10(preampMeter)) : MeterFloorDb;
        values[1] = motorMeter;
    }

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

            return (Sum(mixLeft.Span, mixLeft2.Span), Sum(mixRight.Span, mixRight2.Span));
        }
    }

    /// <summary>
    ///     Control thread: the block's current IR as one file would hold it, for export: the response to the same
    ///     signal on both inputs (the left-fed and right-fed pairs summed), with the slots' proximity, smoothing and
    ///     cuts and the master low and high cuts baked in, trimmed after its last sample within 120 dB of the peak.
    ///     Prepare first.
    /// </summary>
    public (float[] Left, float[] Right) ExportImpulseResponse() {
        var (left, right) = ImpulseResponse();
        var length = left.Length + (int)(TailSeconds * Spec.SampleRate);
        var outLeft = new float[length];
        var outRight = new float[length];
        left.CopyTo(outLeft, 0);
        right.CopyTo(outRight, 0);
        float lowCut, highCut;
        int lowSlope, highSlope;
        lock (sync) {
            (lowCut, lowSlope, highCut, highSlope) =
                (targets[LowCut], (int)targets[LowSlope], targets[HighCut], (int)targets[HighSlope]);
        }

        foreach (var side in (float[][])[outLeft, outRight]) {
            if (lowCut > LowCutOff) {
                var state = new CutState();
                state.Process(CutFilter.Design(true, Spec.SampleRate, lowCut, lowSlope), side);
            }

            if (highCut < HighCutOff) {
                var state = new CutState();
                state.Process(CutFilter.Design(false, Spec.SampleRate, highCut, highSlope), side);
            }
        }

        var peak = 0f;
        for (var i = 0; i < length; i++) {
            peak = MathF.Max(peak, MathF.Max(MathF.Abs(outLeft[i]), MathF.Abs(outRight[i])));
        }

        var floor = peak * 1e-6f;
        var used = 1;
        for (var i = length - 1; i > 0; i--) {
            if (MathF.Abs(outLeft[i]) > floor || MathF.Abs(outRight[i]) > floor) {
                used = i + 1;
                break;
            }
        }

        return (outLeft[..used], outRight[..used]);
    }

    /// <summary>
    ///     Control thread: each slot as the Align page shows it (docs/08-blocks-parity.md §5): the head of its IR as it is
    ///     mixed (resampled, shaped, normalised, before its level, pan and delay) and where its onset falls, the first
    ///     sample within 20 dB of its peak. Building first if the targets changed. Prepare first.
    /// </summary>
    public IReadOnlyList<CabSlotAlignment> Alignment(double headMs = 10) {
        lock (sync) {
            if (synthesizer is null) {
                throw new InvalidOperationException("Prepare the block first.");
            }

            ReadTargets();
            if (NeedsBuild()) {
                switcher.Publish(Build());
            }

            var taps = synthesizer.Taps;
            var anySolo = false;
            var earliest = double.MaxValue;
            for (var s = 0; s < Slots; s++) {
                anySolo |= SlotTarget(s, Solo) >= 0.5f;
                if (HasResponse(s) && IsModelled(SourceOf(s))) {
                    earliest = Math.Min(earliest, slotDelays[s]);
                }
            }

            var head = (int)Math.Round(headMs / 1_000.0 * Spec.SampleRate);
            var result = new CabSlotAlignment[Slots];
            for (var s = 0; s < Slots; s++) {
                var source = SourceOf(s);
                var plays = HasResponse(s);
                var ir = !plays ? []
                    : source == SourceIr ? Shaped(s, prepared[s]![0])
                    : source == SourceFlat ? Shaped(s, flat.Span)
                    : Shaped(s, slotIrs.Span.Slice(s * taps, taps));
                var samples = ir[..Math.Min(head, ir.Length)].ToArray();
                var unaligned = plays && IsModelled(source) && SlotTarget(s, AutoAlign) < 0.5f;
                result[s] = new(
                    s,
                    plays,
                    IsAudible(s, anySolo),
                    Onset(samples) * 1_000.0 / Spec.SampleRate,
                    unaligned ? (slotDelays[s] - earliest) * 1_000.0 : 0,
                    SlotTarget(s, Delay),
                    samples
                );
            }

            return result;
        }
    }

    /// <summary>The first sample within 20 dB of the peak, or 0 for silence.</summary>
    static int Onset(ReadOnlySpan<float> ir) {
        var peak = 0f;
        foreach (var v in ir) {
            peak = Math.Max(peak, Math.Abs(v));
        }

        for (var i = 0; i < ir.Length; i++) {
            if (Math.Abs(ir[i]) >= 0.1f * peak && peak > 0f) {
                return i;
            }
        }

        return 0;
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
            ParamDescriptor.Switch(id + "solo", name + "Solo"),
            IrParam(ParamDescriptor.Float(id + "proximity", name + "Proximity", 0f, 200f, 0f, "%")),
            IrParam(
                ParamDescriptor.Float(id + "proxfreq", name + "Proximity Freq", 20f, 500f, 100f, "Hz", ParamTaper.Log)
            ),
            IrParam(ParamDescriptor.Float(id + "smoothing", name + "Smoothing", 0f, 100f, 0f, "%")),
            ParamDescriptor.Switch(id + "align", name + "Auto-Align", true)
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

    static float[] Sum(ReadOnlySpan<float> a, ReadOnlySpan<float> b) {
        var sum = a.ToArray();
        for (var i = 0; i < sum.Length; i++) {
            sum[i] += b[i];
        }

        return sum;
    }

    static void Accumulate(ReadOnlySpan<float> source, Span<float> destination) {
        for (var i = 0; i < source.Length; i++) {
            destination[i] += source[i];
        }
    }

    /// <summary>The most a slot's IR can be shifted: its delay plus the longest distance delay (12 in plus recess).</summary>
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

        for (var i = Room; i < targets.Length; i++) {
            if (i == PreampMode) {
                continue; // the audio thread's
            }

            if (targets[i] != built[i]) {
                return true;
            }
        }

        return targets[Input] != built[Input];
    }

    int SourceOf(int slot) => (int)targets[SlotParam(slot, Source)];

    float SlotTarget(int slot, int offset) => targets[SlotParam(slot, offset)];

    /// <summary>True when <paramref name="slot" />'s source has something to play.</summary>
    bool HasResponse(int slot) =>
        SourceOf(slot) switch {
            SourceIr => prepared[slot] is not null,
            SourcePack => grids[slot] is not null,
            _ => true // a DynaCab or Flat slot always has
        };

    /// <summary>Resynthesises the modelled slots whose sound changed and mixes all four into a new pair.</summary>
    StereoIr Build() {
        var synthesizer = this.synthesizer!;
        var taps = synthesizer.Taps;
        var globalsChanged = false;
        for (var i = Room; i <= Diffusion; i++) {
            globalsChanged |= targets[i] != built[i];
        }

        globalsChanged |= targets[MicSpacing] != built[MicSpacing];

        for (var s = 0; s < Slots; s++) {
            var source = SourceOf(s);
            var changed = stale[s] || globalsChanged;
            for (var o = Source; o <= Angle && !changed; o++) {
                changed = targets[SlotParam(s, o)] != built[SlotParam(s, o)];
            }

            if (changed && HasResponse(s) && IsModelled(source)) {
                var ir = slotIrs.Span.Slice(s * taps, taps);
                if (source == SourcePack) {
                    slotDelays[s] = synthesizer.Synthesize(
                        grids[s]!,
                        SlotTarget(s, Position) / 100.0,
                        SlotTarget(s, Distance),
                        ir
                    );
                } else if (HasRoomPair()) {
                    // A stereo pair of room mics: the reflections reach each side from half the spacing away.
                    var half = targets[MicSpacing] / 200.0;
                    slotDelays[s] = synthesizer.Synthesize(Placement(s, -half), ir);
                    synthesizer.Synthesize(Placement(s, half), slotRoomIrs.Span.Slice(s * taps, taps));
                } else {
                    slotDelays[s] = synthesizer.Synthesize(Placement(s), ir);
                }
            }

            stale[s] = false;
        }

        Array.Copy(targets, built, targets.Length);
        feed = Mix();
        if (IsPassThrough) {
            return new();
        }

        var length = UsedLength(mixLeft.Span, mixRight.Span);
        var length2 = UsedLength(mixLeft2.Span, mixRight2.Span);
        return feed switch {
            IrFeed.Cross => new(
                mixLeft.Span[..length],
                mixRight.Span[..length],
                mixLeft2.Span[..length2],
                mixRight2.Span[..length2],
                Spec.MaxBlockSize
            ),
            IrFeed.Right => new(mixLeft2.Span[..length2], mixRight2.Span[..length2], IrFeed.Right),
            _ => new(mixLeft.Span[..length], mixRight.Span[..length], feed)
        };
    }

    /// <summary>A DynaCab or IR pack slot: synthesised here, with a travel time Auto-Align can take out.</summary>
    static bool IsModelled(int source) => source is SourceDynaCab or SourcePack;

    /// <summary>Whether the modelled slots get a separate right room mic (Room Mic Spacing with some room).</summary>
    bool HasRoomPair() => targets[MicSpacing] > 0f && targets[Room] > 0f;

    MicPlacement Placement(int slot, double roomMicOffset = 0) =>
        new(
            CabCatalogue.Speakers[(int)SlotTarget(slot, Speaker)],
            CabCatalogue.Enclosures[(int)SlotTarget(slot, Cab)],
            CabCatalogue.Mics[(int)SlotTarget(slot, Mic)],
            SlotTarget(slot, Position) / 100.0,
            SlotTarget(slot, Distance) * MetresPerInch,
            SlotTarget(slot, Angle) * Math.PI / 180,
            true, // a DynaCab's proximity is physical: its Distance sets it
            targets[Room] / 100.0,
            1,
            1,
            new CabRoom(
                targets[RoomSize] / 100.0,
                targets[RoomShape] / 100.0,
                targets[Floor] / 100.0,
                Math.Pow(4, (targets[HighDamping] - 50) / 50.0),
                targets[LowDamping],
                targets[Diffusion] / 100.0 * FullDiffusion,
                roomMicOffset
            )
        );

    /// <summary>
    ///     Mixes the slots into <c>mixLeft</c>/<c>mixRight</c> (every slot, or the left-fed ones with a stereo input)
    ///     and <c>mixLeft2</c>/<c>mixRight2</c> (the right-fed ones), and returns how they are to be convolved.
    /// </summary>
    IrFeed Mix() {
        mixLeft.Clear();
        mixRight.Clear();
        mixLeft2.Clear();
        mixRight2.Clear();
        var taps = synthesizer!.Taps;
        var stereo = (int)targets[Input] == InputStereo;
        var anySolo = false;
        for (var s = 0; s < Slots; s++) {
            anySolo |= SlotTarget(s, Solo) >= 0.5f;
        }

        // A modelled slot with Auto-Align off is delayed by its mic's travel time, less the nearest modelled mic's (so
        // the block adds no latency); an IR file carries its own onset. The levels of the slots that play are relative:
        // scaled to sum to unity, per input side with a stereo input.
        var earliest = double.MaxValue;
        var audible = false;
        var playing = false;
        Span<float> levels = stackalloc float[2];
        Span<bool> sides = stackalloc bool[2];
        for (var s = 0; s < Slots; s++) {
            if (!IsAudible(s, anySolo)) {
                continue;
            }

            audible = true;
            if (HasResponse(s)) {
                playing = true;
                var side = stereo ? s % 2 : 0;
                sides[side] = true;
                levels[side] += Decibels.ToGain(SlotTarget(s, SlotLevel));
                if (IsModelled(SourceOf(s))) {
                    earliest = Math.Min(earliest, slotDelays[s]);
                }
            }
        }

        IsPassThrough = audible && !playing;
        if (IsPassThrough) {
            mixLeft[0] = 1f;
            mixRight[0] = 1f;
            return IrFeed.Both;
        }

        for (var s = 0; s < Slots; s++) {
            if (!IsAudible(s, anySolo) || !HasResponse(s)) {
                continue;
            }

            var side = stereo ? s % 2 : 0;
            var left = side == 0 ? mixLeft.Span : mixLeft2.Span;
            var right = side == 0 ? mixRight.Span : mixRight2.Span;
            var gain = levels[side] > 0f ? Decibels.ToGain(SlotTarget(s, SlotLevel)) / levels[side] : 0f;
            if (SlotTarget(s, Invert) >= 0.5f) {
                gain = -gain;
            }

            // Balance law: the centre is unity on both sides, so a centred mic keeps its level.
            var pan = SlotTarget(s, Pan) / 100f;
            var source = SourceOf(s);
            var unaligned = IsModelled(source) && SlotTarget(s, AutoAlign) < 0.5f;
            var seconds = SlotTarget(s, Delay) / 1_000.0 + (unaligned ? slotDelays[s] - earliest : 0);
            var shift = SincHalf + seconds * Spec.SampleRate;
            var gainLeft = gain * (pan > 0 ? 1 - pan : 1);
            var gainRight = gain * (pan < 0 ? 1 + pan : 1);
            if (source == SourceIr) {
                var channels = prepared[s]!;
                AddShifted(Shaped(s, channels[0]), left, shift, gainLeft);
                AddShifted(Shaped(s, channels[^1]), right, shift, gainRight);
            } else {
                var ir = Shaped(s, source == SourceFlat ? flat.Span : slotIrs.Span.Slice(s * taps, taps));
                AddShifted(ir, left, shift, gainLeft);
                if (source == SourceDynaCab && HasRoomPair()) {
                    ir = Shaped(s, slotRoomIrs.Span.Slice(s * taps, taps));
                }

                AddShifted(ir, right, shift, gainRight);
            }
        }

        // The SincHalf lead-in is silence at an integer shift; drop it so the block keeps zero latency.
        foreach (var mix in (ReadOnlySpan<NativeBuffer>)[mixLeft, mixRight, mixLeft2, mixRight2]) {
            var span = mix.Span;
            span[SincHalf..].CopyTo(span);
            span[^SincHalf..].Clear();
        }

        if (!stereo) {
            return IrFeed.Both;
        }

        return sides[0] && sides[1] ? IrFeed.Cross : sides[0] ? IrFeed.Left : IrFeed.Right;
    }

    /// <summary>
    ///     <paramref name="ir" /> cut to the slot's IR Length, through its proximity boost (not on a DynaCab, whose
    ///     Distance sets it) and its own low and high cut, with a short fade where it was cut, and smoothed, in the
    ///     scratch buffer; or <paramref name="ir" /> itself when the slot does none of these. Proximity and smoothing
    ///     ring on past the end, into the scratch buffer's tail.
    /// </summary>
    ReadOnlySpan<float> Shaped(int slot, ReadOnlySpan<float> ir) {
        var lowCut = SlotTarget(slot, SlotLowCut);
        var highCut = SlotTarget(slot, SlotHighCut);
        var lengthMs = SlotTarget(slot, Length);
        var proximity = SourceOf(slot) == SourceDynaCab ? 0.0 : SlotTarget(slot, Proximity) / 100.0;
        var octaves = SlotTarget(slot, Smoothing) / 100.0 * FullSmoothing;
        var length = lengthMs >= FullLengthMs
            ? ir.Length
            : Math.Max(1, Math.Min(ir.Length, (int)(lengthMs / 1_000.0 * Spec.SampleRate)));
        if (lowCut <= LowCutOff && highCut >= HighCutOff && length == ir.Length && proximity <= 0 && octaves <= 0) {
            return ir;
        }

        var buffer = slotScratch.Span;
        var used = length;
        ir[..length].CopyTo(buffer);
        if (proximity > 0) {
            used = length + tail;
            buffer[length..used].Clear();
            ProximityBoost(buffer[..used], proximity, SlotTarget(slot, ProximityFreq));
        }

        var shaped = buffer[..used];
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

        if (length < ir.Length || proximity > 0) {
            var fade = Math.Min(LengthFade, used);
            for (var i = 0; i < fade; i++) {
                shaped[used - fade + i] *= 0.5f * (1f + MathF.Cos(MathF.PI * (i + 1) / fade));
            }
        }

        if (octaves > 0) {
            var smoother = used <= this.smoother!.MaxLength ? this.smoother : LongSmoother();
            used = smoother.Smooth(buffer, used, octaves);
        }

        return buffer[..used];
    }

    /// <summary>
    ///     The near-field boost of a pressure-gradient mic, <c>(s + ω_f + a ω_c) / (s + ω_f)</c> by the bilinear
    ///     transform, in place: unity in the treble, +3 dB at <paramref name="hz" /> for <paramref name="amount" /> 1,
    ///     rising 6 dB/oct below it down to the floor.
    /// </summary>
    void ProximityBoost(Span<float> ir, double amount, double hz) {
        var c = 2 * Spec.SampleRate;
        var floor = 2 * Math.PI * ProximityFloorHz;
        var zero = floor + amount * 2 * Math.PI * hz;
        var b0 = (c + zero) / (c + floor);
        var b1 = (zero - c) / (c + floor);
        var a1 = (floor - c) / (c + floor);
        double x1 = 0, y1 = 0;
        for (var i = 0; i < ir.Length; i++) {
            double x = ir[i];
            var y = b0 * x + b1 * x1 - a1 * y1;
            x1 = x;
            y1 = y;
            ir[i] = (float)y;
        }
    }

    /// <summary>The smoother for FullRes IRs, made the first time one is smoothed (its FFT is large).</summary>
    IrSmoother LongSmoother() => longSmoother ??= new(capacity + 2 * tail);

    bool IsAudible(int slot, bool anySolo) =>
        SlotTarget(slot, Mute) < 0.5f && (!anySolo || SlotTarget(slot, Solo) >= 0.5f);


    protected override void OnPrepare(in ProcessSpec spec) {
        lock (sync) {
            DisposeBuffers();
            synthesizer = new(spec.SampleRate);
            var taps = synthesizer.Taps;
            capacity = Math.Max(IrCapacity(spec.SampleRate), taps);
            tail = (int)Math.Ceiling(TailSeconds * spec.SampleRate);
            slotIrs = new(Slots * taps);
            slotRoomIrs = new(Slots * taps);
            // The oversamplers exist only in High Quality: changing the mode rebuilds the graph (Recompile), so an
            // Economy cab allocates nothing for them.
            if ((int)Params.GetTarget(PreampMode) == PreampHighQuality) {
                upLeft ??= new(spec.MaxBlockSize);
                upRight ??= new(spec.MaxBlockSize);
                upLeft.Resize(spec.MaxBlockSize);
                upRight.Resize(spec.MaxBlockSize);
            } else {
                (upLeft, upRight) = (null, null);
            }

            slotScratch = new(capacity + 2 * tail);
            flat = new(1);
            flat[0] = 1f;
            var length = capacity
                + 2 * tail
                + (int)Math.Ceiling(MaxShiftSeconds() * spec.SampleRate)
                + 2 * SincHalf;
            smoother = new(Math.Max(taps, (int)(ShortSmoothing * spec.SampleRate / CabIrSynthesizer.ReferenceRate)));
            mixLeft = new(length);
            mixRight = new(length);
            mixLeft2 = new(length);
            mixRight2 = new(length);
            dryLeft = new(spec.MaxBlockSize);
            dryRight = new(spec.MaxBlockSize);
            bypassLeft = new(spec.MaxBlockSize);
            bypassRight = new(spec.MaxBlockSize);
            var shape = new IrShape(IrCapacity(spec.SampleRate));
            for (var s = 0; s < Slots; s++) {
                prepared[s] = files[s]?.Prepare(spec.SampleRate, shape);
            }

            Array.Fill(stale, true);
            ReadTargets();
            switcher.Prepare(spec, Build());
        }
    }

    protected override void OnReset() {
        ClearState();
        outputGain = Decibels.ToGain(Params.Current(Level));
        airGain = AirGain(Params.Current(Air));
        drive = Params.Current(Drive);
        saturation = Params.Current(Saturation) / 100f;
        motorDrive = Params.Current(MotorDrive) / 100f;
        cabsOff = false;
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        // Cab modelling off (a global setting, for a real cab): pass through untouched, crossfading over the callback
        // where it changes. Coming back, the convolvers start from silence rather than from a stale history.
        var off = context.CabsOff;
        if (off && cabsOff) {
            return;
        }

        var changing = off != cabsOff;
        if (changing) {
            io.Left.CopyTo(bypassLeft.Span);
            io.Right.CopyTo(bypassRight.Span);
            if (!off) {
                ClearState();
            }
        }

        Model(io);
        if (changing) {
            var dryLeft = bypassLeft.Span[..io.Length];
            var dryRight = bypassRight.Span[..io.Length];
            var step = 1f / io.Length;
            for (var i = 0; i < io.Length; i++) {
                var wet = off ? 1f - (i + 1) * step : (i + 1) * step;
                io.Left[i] = dryLeft[i] + (io.Left[i] - dryLeft[i]) * wet;
                io.Right[i] = dryRight[i] + (io.Right[i] - dryRight[i]) * wet;
            }

            cabsOff = off;
        }
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            lock (sync) {
                disposed = true;
                switcher.Dispose();
                DisposeBuffers();
                synthesizer = null;
                smoother = null;
                longSmoother = null;
                foreach (var grid in grids) {
                    grid?.Dispose();
                }
            }
        }

        base.Dispose(disposing);
    }

    /// <summary>Clears every filter, convolver and meter: <see cref="OnReset" />, and turning cab modelling back on.</summary>
    void ClearState() {
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
        coilLeft.Reset();
        coilRight.Reset();
        upLeft?.Reset();
        upRight?.Reset();
        preampMeter = 0f;
        motorMeter = 0f;
    }

    /// <summary>The cab itself: input routing, voice coil, convolution with air, mic preamp, master cuts and level.</summary>
    void Model(in StereoBuffer io) {
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

        // Motor Drive: the voice coils heat up with the power they take, before the speaker's response.
        var motorDrive = Params.Advance(MotorDrive, io.Length) / 100f;
        var motorTime = Params.Advance(MotorTime, io.Length);
        if (motorDrive > 0f || this.motorDrive > 0f || coilLeft.IsWarm || coilRight.IsWarm) {
            coilLeft.Process(Spec.SampleRate, this.motorDrive, motorDrive, motorTime, io.Left);
            coilRight.Process(Spec.SampleRate, this.motorDrive, motorDrive, motorTime, io.Right);
            motorMeter = MathF.Max(coilLeft.ReductionDb, coilRight.ReductionDb);
        } else {
            motorMeter = 0f;
        }

        this.motorDrive = motorDrive;

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
        if (type != MicPreamp.None && upLeft is not null && upRight is not null) {
            // High Quality runs the waveshaper at four times the rate, so its harmonics fold back far less.
            var rate = 4 * Spec.SampleRate;
            var twice = upLeft.Up(io.Left);
            preampLeft.Process(type, rate, this.drive, drive, this.saturation, saturation, twice);
            upLeft.Down(io.Left);
            twice = upRight.Up(io.Right);
            preampRight.Process(type, rate, this.drive, drive, this.saturation, saturation, twice);
            upRight.Down(io.Right);
        } else {
            preampLeft.Process(type, Spec.SampleRate, this.drive, drive, this.saturation, saturation, io.Left);
            preampRight.Process(type, Spec.SampleRate, this.drive, drive, this.saturation, saturation, io.Right);
        }

        this.drive = drive;
        this.saturation = saturation;
        var release = MathF.Exp((float)(-io.Length / (MeterRelease * Spec.SampleRate)));
        preampMeter = MathF.Max(preampMeter * release, MathF.Max(preampLeft.InputPeak, preampRight.InputPeak));
        if (preampMeter < 1e-6f) {
            preampMeter = 0f;
        }

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

    void DisposeBuffers() {
        synthesizer?.Dispose();
        smoother?.Dispose();
        longSmoother?.Dispose();
        longSmoother = null;
        foreach (var buffer in (ReadOnlySpan<NativeBuffer>) [
                     slotIrs, slotRoomIrs, slotScratch, flat, mixLeft, mixRight, mixLeft2, mixRight2, dryLeft, dryRight,
                     bypassLeft, bypassRight
                 ]) {
            buffer.Dispose();
        }
    }
}

/// <summary>
///     One Cab slot on the Align page (<see cref="CabBlock.Alignment" />): whether it has something to play and is heard,
///     its onset in its own IR, the delay Auto-Align off adds and the slot's own Delay (both in ms), and its IR's head.
/// </summary>
public sealed record CabSlotAlignment(
    int Slot,
    bool Plays,
    bool Audible,
    double OnsetMs,
    double AutoDelayMs,
    double DelayMs,
    float[] Head
);
