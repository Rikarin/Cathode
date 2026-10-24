using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Resampling;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Drive pedal: three original voicings with antiderivative anti-aliased clippers, followed by a catalogue of circuit
///     models (<see cref="DriveCircuits" />) built from wave digital filters. The original voicings:
///     <list type="bullet">
///         <item>
///             <b>Overdrive</b> (TS-style): the clipped path is high-passed around 720 Hz and summed with a clean path,
///             giving the mid hump and keeping the lows tight.
///         </item>
///         <item><b>Distortion</b> (Rat-style): high gain into a hard clipper.</item>
///         <item><b>Fuzz</b>: a biased, asymmetric soft clipper followed by a DC blocker.</item>
///     </list>
///     The whole nonlinear path (pre-filter, gain, clipper, clean-path sum, DC blocker) runs at 4x the sample rate
///     through IIR half-band oversampling, then a tone control (low-pass from 600 Hz to 8 kHz), output level and mix.
///     At partial mix the dry signal takes the same up/down filtering, so the two paths cannot comb.
///     <para>
///         The circuit types (index <see cref="CircuitTypes" /> onwards) run their stages, coupling, bias and DC blocker
///         oversampled (4x, or 2x for the costliest, <see cref="DriveCircuit.Oversampling" />); Drive turns each
///         circuit's gain pot (or input gain), and Tone works the pedal's own tone network at the base rate. They read
///         their knobs on a fixed grid of <see cref="SubBlock" /> frames and recompute a network's components only when a
///         smoothed knob has moved, so their output does not depend on the host's block size. Every type has the same
///         (zero) latency.
///     </para>
///     <para>
///         Input Vol is the guitar's volume knob: the source the pedal sees is the pickup behind an audio-taper volume pot
///         (<see cref="DriveCircuits.Guitar" />). Into a component-level first stage it is a source resistance, so a Fuzz
///         Face's low input impedance cleans up as the knob rolls back, as on the real pedal; elsewhere it is the level
///         and the loading of a nominal input resistance. At 10 (the default) the original voicings are untouched.
///     </para>
///     <para>The guide's EQ and Advanced pages follow, every control stock (doing nothing) at its default:</para>
///     <list type="bullet">
///         <item>
///             <b>EQ 1</b>, on the driven signal after the tone control: Low Cut and High Cut (12 dB/oct, off at their
///             ends), Bass (low shelf, 100 Hz), Mid (peak at Mid Freq), High Mid (peak, 2.5 kHz) and Treble (high shelf,
///             5 kHz), ±12 dB, a band at 0 dB skipped.
///         </item>
///         <item>
///             <b>EQ 2</b>: a ten-band 2/3-octave graphic EQ (<see cref="GraphicEqualizer" />), before the drive (Pre) or
///             after EQ 1 (Post), or off. Pre shapes what the clipping hears; the dry signal of a partial mix passes
///             neither EQ.
///         </item>
///         <item>
///             <b>Clip Type</b> (original voicings): another clipping curve in place of the type's (Soft, Hard,
///             Asymmetric, Germanium, LED, Tube, Op-Amp, FET, Rectifier, Variable, HV or LV Tube, Silicon, 4558 Diode or none); the type's filtering and clean blend stay.
///             <b>Clip Shape</b> bends the curve towards a soft knee (−) or a hard clip (+).
///         </item>
///         <item>
///             <b>Diode Type</b>, <b>Diode Count</b> (circuits with clipping diodes): every clipping diode replaced by
///             another model, or each stack by that many in series (<see cref="DriveCircuits.Variant" />).
///         </item>
///         <item>
///             <b>Bass Response</b> (%): the input coupling corner into the clipping (the voicings' pre-filter, a
///             circuit's coupling capacitor): lower for a fatter drive, higher for a tighter one.
///         </item>
///         <item><b>Dry Level</b>: the clean input summed after the clipping, inside the circuit (before the tone).</item>
///         <item><b>Bias</b>: an offset ahead of the clipping, for even harmonics (DC-blocked after).</item>
///         <item>
///             <b>Slew Rate</b> (V/µs; 10 is off): an op-amp circuit's op-amps slew at it (the RAT's LM308 in place of its
///             own 0.3 V/µs; a linear op-amp stage limits its output to it); any other type is slew-limited after the
///             clipping, at the oversampled rate.
///         </item>
///         <item>
///             <b>Bass Pot</b>, <b>Middle Pot</b> (5, the pedal as built): the bass and middle pots of a circuit with a
///             real tone stack (the Guv'nor's), which its Tone (treble) pot works with.
///         </item>
///         <item>
///             <b>Bit Reduce</b> and <b>Sample Rate</b>: a lo-fi word length and sample-and-hold rate on the driven signal
///             (<see cref="Decimator" />); 0 bits and 48 kHz are off.
///         </item>
///     </list>
/// </summary>
public sealed class DriveBlock() : DspBlock(Info), IOversampledBlock {
    public const int Type = 0;
    public const int Drive = 1;
    public const int Tone = 2;
    public const int Level = 3;
    public const int Mix = 4;

    /// <summary>The guitar's volume knob, appended after the original parameters (presets store indices).</summary>
    public const int InputVolume = 5;

    public const int Balance = 6;
    public const int LowCut = 7;
    public const int HighCut = 8;
    public const int Bass = 9;
    public const int Mid = 10;
    public const int MidFreq = 11;
    public const int HighMid = 12;
    public const int Treble = 13;

    /// <summary>EQ 2's location: <see cref="EqOff" />, <see cref="EqPre" /> or <see cref="EqPost" />.</summary>
    public const int Eq2Location = 14;

    /// <summary>EQ 2's first band; the others follow (<see cref="GraphicEqualizer.MaxBands" /> in all).</summary>
    public const int Eq2Band = 15;

    public const int ClipType = 25;
    public const int ClipShape = 26;
    public const int DiodeType = 27;
    public const int DiodeCount = 28;
    public const int BassResponse = 29;
    public const int DryLevel = 30;
    public const int Bias = 31;
    public const int SlewRate = 32;
    public const int BitReduce = 33;
    public const int SampleRate = 34;
    public const int StackBass = 35;
    public const int StackMiddle = 36;

    /// <summary>Oversampling: Global (each type's own factor), 1x, 2x or 4x. Changes the latency, so it recompiles.</summary>
    public const int OversamplingSetting = 37;

    public const int EqOff = 0;
    public const int EqPre = 1;
    public const int EqPost = 2;

    /// <summary>A Slew Rate at or above this (V/µs) is off.</summary>
    public const float SlewOff = 10f;

    /// <summary>Volts (or full-scale units for a voicing) of Bias at 100 %.</summary>
    public const float FullBias = 0.5f;

    /// <summary>Index of the first circuit model: the three original voicings keep indices 0-2.</summary>
    public const int CircuitTypes = 3;

    /// <summary>Frames per knob reading of the circuit types, on a grid that ignores the host's block boundaries.</summary>
    public const int SubBlock = 32;

    /// <summary>EQ 2's band layout in <see cref="GraphicEqualizer.Layouts" />: ten bands at 2/3 octave.</summary>
    public const int Eq2Layout = 1;

    const int Factor = 4;
    const int Eq1Bands = 4;
    const float HighMidHz = 2_500f;

    /// <summary>The 2x half-band of the circuits that run at 2x: the first stage of <see cref="Oversampler4x" />.</summary>
    static readonly (float[], float[]) TwiceBranches =
        HalfBandStage.Split(HalfBand.Design(Oversampler4x.AttenuationDb, 0.04));

    static readonly string[] TypeNames =
        ["Overdrive", "Distortion", "Fuzz", ..DriveCircuits.All.Select(static c => c.Name)];

    readonly Channel left = new();
    readonly Channel right = new();
    readonly Oversampler4x dryLeft = new(1);
    readonly Oversampler4x dryRight = new(1);
    readonly HalfBandStage dryTwiceLeft = new(TwiceBranches);
    readonly HalfBandStage dryTwiceRight = new(TwiceBranches);
    readonly EqBand[] eq1 = [new(), new(), new(), new()];
    readonly float[] eq1Gains = new float[Eq1Bands];
    readonly bool[] eq1Moving = new bool[Eq1Bands];
    readonly GraphicEqualizer eq2 = new();
    readonly float[] eq2Gains = new float[GraphicEqualizer.MaxBands];
    readonly bool[] eq2Moving = new bool[GraphicEqualizer.MaxBands];
    WetCuts cuts;
    Decimator decimator;
    float gain = 1f;
    OutputStage output;
    float mix = 1f;
    float guitar = 1f;
    float[] dry = [];
    float[] secondDry = []; // both channels' dry signal, held until the wet is done
    float[] dryTwice = [];
    int phase; // position on the sub-block grid
    int factor = Factor; // oversampling of the configured circuit
    DriveCircuit? configured;
    int eq2Location;
    float midFrequency;
    int bitReduce;
    float decimateHz;
    int latency; // what the block reports: its largest factor's round trip, fixed at Prepare
    float[] padLeft = [];
    float[] padRight = [];
    int padAt;

    public static BlockDescriptor Info { get; } = new(
        "drive",
        "Drive",
        "Drive",
        [
            ParamDescriptor.Choice("type", "Type", TypeNames),
            ParamDescriptor.Float("drive", "Drive", 0f, 10f, 5f),
            ParamDescriptor.Float("tone", "Tone", 0f, 10f, 5f),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("inputVol", "Input Vol", 0f, 10f, 10f),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            WetCuts.LowCutParam("lowCut", "Low Cut"),
            WetCuts.HighCutParam("highCut", "High Cut"),
            ParamDescriptor.Float("bass", "Bass", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("mid", "Mid", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("midFreq", "Mid Freq", 200f, 5_000f, 800f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("highMid", "High Mid", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("treble", "Treble", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Choice("eq2", "EQ 2", ["Off", "Pre", "Post"]),
            ..GraphicEqualizer.Layouts[Eq2Layout]
                .Labels.Select(static (l, b) => ParamDescriptor.Float(
                        $"geq{b + 1}",
                        $"EQ 2 {l} Hz",
                        -12f,
                        12f,
                        0f,
                        "dB"
                    )
                ),
            ForTypes(
                ParamDescriptor.Choice(
                    "clipType",
                    "Clip Type",
                    [
                        "Default", "Soft", "Hard", "Asymmetric", "Germanium", "LED", "Tube", "Op-Amp", "FET", "Rectifier",
                        "Variable", "HV Tube", "LV Tube", "Silicon", "4558 Diode", "None"
                    ]
                ),
                static t => t < CircuitTypes
            ),
            ForTypes(
                ParamDescriptor.Float("clipShape", "Clip Shape", -100f, 100f, 0f, "%"),
                static t => t < CircuitTypes
            ),
            ForTypes(ParamDescriptor.Choice("diodeType", "Diode Type", DriveCircuits.DiodeTypes), HasDiodes),
            ForTypes(ParamDescriptor.Choice("diodeCount", "Diode Count", DriveCircuits.DiodeCounts), HasDiodes),
            ParamDescriptor.Float("bassResponse", "Bass Response", 25f, 400f, 100f, "%", ParamTaper.Log),
            ParamDescriptor.Float("dryLevel", "Dry Level", Decibels.SilenceDb, 12f, Decibels.SilenceDb, "dB"),
            ParamDescriptor.Float("bias", "Bias", -100f, 100f, 0f, "%"),
            ParamDescriptor.Float("slewRate", "Slew Rate", 0.01f, SlewOff, SlewOff, "V/µs", ParamTaper.Log),
            ParamDescriptor.Float("bitReduce", "Bit Reduce", 0f, 20f, 0f, "bits") with {
                Smoothing = ParamSmoothing.Step
            },
            ParamDescriptor.Float(
                "sampleRate",
                "Sample Rate",
                500f,
                Decimator.RateOff,
                Decimator.RateOff,
                "Hz",
                ParamTaper.Log
            ),
            ForTypes(ParamDescriptor.Float("stackBass", "Bass Pot", 0f, 10f, 5f), HasStack),
            ForTypes(ParamDescriptor.Float("stackMid", "Middle Pot", 0f, 10f, 5f), HasStack),
            ParamDescriptor.Choice("oversampling", "Oversampling", ["Global", "1x", "2x", "4x"]) with {
                Recompile = true
            }
        ],
        maxInstances: 4
    );

    /// <summary>Disables antiderivative anti-aliasing, for measuring what it buys.</summary>
    public bool Antialias { get; set; } = true;

    /// <summary>Disables oversampling, for measuring what it buys. Set before <see cref="IDspBlock.Prepare" />.</summary>
    public bool Oversample { get; set; } = true;

    /// <summary>The oversamplers' filters (the global setting); set before <see cref="IDspBlock.Prepare" />.</summary>
    public OversamplingPhase OversamplingPhase { get; set; }

    /// <summary>
    ///     The round trip of the largest factor the Oversampling setting allows (0 with minimum-phase filters). A type
    ///     that runs at a smaller factor is delayed to match, so the latency never changes with the type.
    /// </summary>
    public override int LatencySamples => latency;

    /// <summary>The clipping curve of voicing <paramref name="type" /> under Clip Type <paramref name="clipType" />.</summary>
    public static ClipperShape ShapeOf(int type, int clipType) =>
        clipType > 0
            ? (ClipperShape)(clipType - 1)
            : type switch {
                0 => ClipperShape.Soft,
                1 => ClipperShape.Hard,
                _ => ClipperShape.Asymmetric
            };

    /// <summary><paramref name="descriptor" /> shown only on the types <paramref name="shown" /> accepts.</summary>
    static ParamDescriptor ForTypes(ParamDescriptor descriptor, Func<int, bool> shown) =>
        descriptor with {
            TypeLabels =
                [..Enumerable.Range(0, TypeNames.Length).Select(t => shown(t) ? descriptor.Name : string.Empty)]
        };

    /// <summary>Whether circuit type <paramref name="type" /> has a bass/middle/treble stack with real bass and middle pots.</summary>
    static bool HasStack(int type) =>
        type >= CircuitTypes && DriveCircuits.All[type - CircuitTypes].Tone.Kind == ToneKind.Stack;

    static bool HasDiodes(int type) =>
        type >= CircuitTypes && DriveCircuits.HasDiodes(DriveCircuits.All[type - CircuitTypes]);

    /// <summary>Slew Rate in V/µs as the largest step per sample at <paramref name="innerRate" />; 0 when off.</summary>
    static double SlewStep(float voltsPerMicrosecond, double innerRate) =>
        voltsPerMicrosecond >= SlewOff ? 0 : voltsPerMicrosecond * 1e6 / innerRate;

    /// <summary>Drive 0..10 mapped to a gain range per type, in dB.</summary>
    static float InputGain(int type, float drive) {
        if (type >= CircuitTypes) {
            var circuit = DriveCircuits.All[type - CircuitTypes];
            return (float)Decibels.ToGain(circuit.MinGainDb + (circuit.MaxGainDb - circuit.MinGainDb) * drive / 10);
        }

        var (minDb, maxDb) = type switch {
            0 => (0f, 40f),
            1 => (6f, 60f),
            _ => (10f, 50f)
        };
        return Decibels.ToGain(minDb + (maxDb - minDb) * drive / 10f);
    }

    /// <summary>
    ///     The circuit types run both channels through one loop: each channel is a long dependency chain, and
    ///     interleaving two independent chains lets the core overlap them. Knobs are read once per sub-block of the grid.
    /// </summary>
    void ProcessCircuits(in StereoBuffer io, int type, DriveCircuit circuit) {
        configured = circuit;
        UseFactor(Desired(circuit.Oversampling));

        var rate = Spec.SampleRate;
        var innerRate = rate * factor;
        var dcBlock = SvfCoefficients.Design(BiquadType.HighPass, innerRate, 10, 0.7071);
        for (var offset = 0; offset < io.Length;) {
            var length = Math.Min(SubBlock - phase, io.Length - offset);
            var chunk = io.Slice(offset, length);
            var drive = Params.Advance(Drive, length);
            var tone = Params.Advance(Tone, length);
            var volume = Params.Advance(InputVolume, length);
            var mix = Params.Advance(Mix, length) / 100f;
            var level = Params.Advance(Level, length);
            var balance = Params.Advance(Balance, length);
            var tweaks = new CircuitTweaks(
                Params.Advance(BassResponse, length) / 100.0,
                Params.Advance(Bias, length) / 100.0 * FullBias,
                Decibels.ToGain(Params.Advance(DryLevel, length)),
                SlewStep(Params.Advance(SlewRate, length), innerRate),
                Params.Advance(StackBass, length) / 10.0,
                Params.Advance(StackMiddle, length) / 10.0
            );
            ReadPage(length);
            var gain = InputGain(type, drive);
            var guitar = (float)DriveCircuits.Guitar(volume).Level;
            left.Circuit.Configure(circuit, drive, tone, volume, innerRate, rate, Antialias, tweaks);
            right.Circuit.Configure(circuit, drive, tone, volume, innerRate, rate, Antialias, tweaks);

            var leftDry = dry.AsSpan(0, length);
            var rightDry = secondDry.AsSpan(0, length);
            var blend = this.mix < 1f || mix < 1f;
            if (blend) {
                CopyDry(chunk.Left, leftDry, dryLeft, dryTwiceLeft);
                CopyDry(chunk.Right, rightDry, dryRight, dryTwiceRight);
                GainRamp.Apply(leftDry, this.guitar, guitar); // the dry path hears the guitar's volume too
                GainRamp.Apply(rightDry, this.guitar, guitar);
            }

            Pre(chunk);
            var innerLeft = left.Up(chunk.Left, factor);
            var innerRight = right.Up(chunk.Right, factor);
            CircuitChannel.ProcessInner(left.Circuit, right.Circuit, innerLeft, innerRight, this.gain, gain, dcBlock);
            left.Down(chunk.Left, factor);
            right.Down(chunk.Right, factor);
            left.Circuit.ProcessOuter(chunk.Left);
            right.Circuit.ProcessOuter(chunk.Right);
            Post(chunk);

            if (blend) {
                MixRamp.Blend(leftDry, chunk.Left, MixLaw.ConstantPower, this.mix, mix);
                MixRamp.Blend(rightDry, chunk.Right, MixLaw.ConstantPower, this.mix, mix);
            }

            output.Apply(chunk, level, balance);
            this.gain = gain;
            this.mix = mix;
            this.guitar = guitar;
            offset += length;
            phase = (phase + length) % SubBlock;
        }
    }

    /// <summary>
    ///     Advances the EQ pages and the lo-fi controls over <paramref name="length" /> frames (once per block for the
    ///     voicings, once per sub-block for the circuits) and designs the cuts.
    /// </summary>
    void ReadPage(int length) {
        cuts.Design(Spec.SampleRate, Params.Advance(LowCut, length), Params.Advance(HighCut, length));
        var midMoving = Params.IsRamping(MidFreq);
        midFrequency = Params.Advance(MidFreq, length);
        ReadBand(0, Bass, length, false);
        ReadBand(1, Mid, length, midMoving);
        ReadBand(2, HighMid, length, false);
        ReadBand(3, Treble, length, false);
        eq2Location = (int)Params.Current(Eq2Location);
        for (var b = 0; b < GraphicEqualizer.MaxBands; b++) {
            eq2Moving[b] = Params.IsRamping(Eq2Band + b);
            eq2Gains[b] = Params.Advance(Eq2Band + b, length);
        }

        bitReduce = (int)Params.Current(BitReduce);
        decimateHz = Params.Advance(SampleRate, length);
    }

    void ReadBand(int band, int index, int length, bool moving) {
        eq1Moving[band] = moving || Params.IsRamping(index);
        eq1Gains[band] = Params.Advance(index, length);
    }

    /// <summary>EQ 2 at Pre: ahead of the drive.</summary>
    void Pre(in StereoBuffer chunk) {
        if (eq2Location == EqPre) {
            eq2.Process(chunk, Spec.SampleRate, Eq2Layout, eq2Gains, eq2Moving, 1f, false, false);
        }
    }

    /// <summary>EQ 1, EQ 2 at Post, then the bit and rate reduction, on the driven signal.</summary>
    void Post(in StereoBuffer chunk) {
        var rate = Spec.SampleRate;
        for (var i = 0; i < chunk.Length; i++) {
            cuts.Tick(ref chunk.Left[i], ref chunk.Right[i]);
        }

        cuts.FlushDenormals();
        eq1[0].Process(chunk, BiquadType.LowShelf, rate, 100f, 0.7071f, eq1Gains[0], eq1Moving[0]);
        eq1[1].Process(chunk, BiquadType.Peak, rate, midFrequency, 0.7f, eq1Gains[1], eq1Moving[1]);
        eq1[2].Process(chunk, BiquadType.Peak, rate, HighMidHz, 0.7f, eq1Gains[2], eq1Moving[2]);
        eq1[3].Process(chunk, BiquadType.HighShelf, rate, 5_000f, 0.7071f, eq1Gains[3], eq1Moving[3]);
        if (eq2Location == EqPost) {
            eq2.Process(chunk, rate, Eq2Layout, eq2Gains, eq2Moving, 1f, false, false);
        }

        if (!Decimator.IsOff(bitReduce, decimateHz)) {
            decimator.Process(chunk, rate, bitReduce, decimateHz);
        }
    }

    /// <summary>The factor to run at: the Oversampling setting, or <paramref name="natural" /> (the type's own) on Global.</summary>
    int Desired(int natural) =>
        !Oversample ? 1 : (int)Params.GetTarget(OversamplingSetting) switch {
            1 => 1,
            2 => 2,
            3 => 4,
            _ => natural
        };

    void UseFactor(int wanted) {
        if (wanted != factor) {
            factor = wanted; // the other factor's filters hold stale state
            ResetOversamplers();
        }
    }

    /// <summary>The up/down round trip at <paramref name="oversampling" />, in samples.</summary>
    int RoundTrip(int oversampling) =>
        oversampling switch {
            4 => dryLeft.LatencySamples,
            2 => dryTwiceLeft.RoundTripLatency,
            _ => 0
        };

    /// <summary>Delays the output by what the running factor's round trip falls short of <see cref="LatencySamples" />.</summary>
    void Pad(in StereoBuffer io) {
        var pad = latency - RoundTrip(factor);
        if (pad <= 0) {
            return;
        }

        var size = padLeft.Length;
        for (var i = 0; i < io.Length; i++) {
            padLeft[padAt] = io.Left[i];
            padRight[padAt] = io.Right[i];
            var read = padAt - pad;
            if (read < 0) {
                read += size;
            }

            io.Left[i] = padLeft[read];
            io.Right[i] = padRight[read];
            padAt = padAt + 1 == size ? 0 : padAt + 1;
        }
    }

    /// <summary>The dry signal through the same up/down filters as the processed path, so the two cannot comb.</summary>
    void CopyDry(ReadOnlySpan<float> samples, Span<float> dry, Oversampler4x four, HalfBandStage two) {
        samples.CopyTo(dry);
        switch (factor) {
            case 4:
                four.Up(dry);
                four.Down(dry);
                break;
            case 2: {
                var twice = dryTwice.AsSpan(0, 2 * dry.Length);
                two.Up(dry, twice);
                two.Down(twice, dry);
                break;
            }
        }
    }

    void ResetOversamplers() {
        left.ResetOversampler();
        right.ResetOversampler();
        dryLeft.Reset();
        dryRight.Reset();
        dryTwiceLeft.Reset();
        dryTwiceRight.Reset();
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        if (dry.Length != spec.MaxBlockSize) {
            dry = new float[spec.MaxBlockSize];
            secondDry = new float[spec.MaxBlockSize];
            dryTwice = new float[2 * spec.MaxBlockSize];
        }

        left.Allocate(spec.MaxBlockSize);
        right.Allocate(spec.MaxBlockSize);
        dryLeft.Resize(spec.MaxBlockSize);
        dryRight.Resize(spec.MaxBlockSize);
        if (dryLeft.Phase != OversamplingPhase) {
            left.SetPhase(OversamplingPhase);
            right.SetPhase(OversamplingPhase);
            dryLeft.SetPhase(OversamplingPhase);
            dryRight.SetPhase(OversamplingPhase);
            dryTwiceLeft.SetPhase(OversamplingPhase, Oversampler4x.FirstTransition);
            dryTwiceRight.SetPhase(OversamplingPhase, Oversampler4x.FirstTransition);
        }

        latency = RoundTrip(Desired(Factor));
        if (padLeft.Length != latency + 1) {
            padLeft = new float[latency + 1];
            padRight = new float[latency + 1];
        }
    }

    protected override void OnReset() {
        left.Reset();
        right.Reset();
        ResetOversamplers();
        foreach (var band in eq1) {
            band.Reset();
        }

        eq2.Reset();
        cuts.Reset();
        decimator.Reset();
        gain = InputGain((int)Params.Current(Type), Params.Current(Drive));
        output.Reset(Params.Current(Level), Params.Current(Balance));
        mix = Params.Current(Mix) / 100f;
        guitar = (float)DriveCircuits.Guitar(Params.Current(InputVolume)).Level;
        phase = 0;
        factor = Factor;
        configured = null;
        Array.Clear(padLeft);
        Array.Clear(padRight);
        padAt = 0;
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        // The circuits are modelled in volts: the input calibration scales what they get, and the output comes back
        // to the graph's units, so the drive follows the guitar and not the interface's gain.
        var volts = (float)context.InputVolts;
        if (volts != 1f) {
            Scale(io, volts);
        }

        ProcessDrive(io);
        if (volts != 1f) {
            Scale(io, 1f / volts);
        }

        Pad(io);
    }

    static void Scale(in StereoBuffer io, float gain) {
        for (var i = 0; i < io.Length; i++) {
            io.Left[i] *= gain;
            io.Right[i] *= gain;
        }
    }

    void ProcessDrive(in StereoBuffer io) {
        var type = (int)Params.Current(Type);
        if (type >= CircuitTypes) {
            var circuit = DriveCircuits.Variant(
                type - CircuitTypes,
                (int)Params.Current(DiodeType),
                (int)Params.Current(DiodeCount)
            );
            ProcessCircuits(io, type, circuit);
            return;
        }

        if (configured is not null) {
            configured = null;
            left.Circuit.Release(); // a circuit chosen again later starts discharged
            right.Circuit.Release();
        }

        UseFactor(Desired(Factor));
        var rate = Spec.SampleRate;
        var innerRate = rate * factor;
        var shape = ShapeOf(type, (int)Params.Current(ClipType));
        var drive = Params.Advance(Drive, io.Length);
        var toneValue = Params.Advance(Tone, io.Length);
        var guitar = (float)DriveCircuits.Guitar(Params.Advance(InputVolume, io.Length)).Level;
        var gain = InputGain(type, drive);
        var toneHz = 600f * MathF.Pow(8_000f / 600f, toneValue / 10f);
        var tone = SvfCoefficients.Design(BiquadType.LowPass, rate, toneHz, 0.7071);
        var bass = Params.Advance(BassResponse, io.Length) / 100f;
        var preHz = type switch {
            0 => 720f,
            1 => 60f,
            _ => 30f
        };
        var pre = SvfCoefficients.Design(BiquadType.HighPass, innerRate, bass == 1f ? preHz : preHz * bass, 0.7071);
        var dcBlock = SvfCoefficients.Design(BiquadType.HighPass, innerRate, 10, 0.7071);
        var mix = Params.Advance(Mix, io.Length) / 100f;
        var advanced = new Advanced(
            Params.Advance(ClipShape, io.Length) / 100f,
            Params.Advance(Bias, io.Length) / 100f * FullBias,
            Decibels.ToGain(Params.Advance(DryLevel, io.Length)),
            (float)SlewStep(Params.Advance(SlewRate, io.Length), innerRate)
        );
        ReadPage(io.Length);

        GainRamp.Apply(io.Left, this.guitar, guitar); // the guitar's volume: nothing at all at 10
        GainRamp.Apply(io.Right, this.guitar, guitar);
        var leftDry = dry.AsSpan(0, io.Length);
        var rightDry = secondDry.AsSpan(0, io.Length);
        var blend = this.mix < 1f || mix < 1f;
        if (blend) {
            CopyDry(io.Left, leftDry, dryLeft, dryTwiceLeft);
            CopyDry(io.Right, rightDry, dryRight, dryTwiceRight);
        }

        Pre(io);
        left.Process(io.Left, factor, type, shape, this.gain, gain, pre, tone, dcBlock, Antialias, advanced);
        right.Process(io.Right, factor, type, shape, this.gain, gain, pre, tone, dcBlock, Antialias, advanced);
        Post(io);
        if (blend) {
            MixRamp.Blend(leftDry, io.Left, MixLaw.ConstantPower, this.mix, mix);
            MixRamp.Blend(rightDry, io.Right, MixLaw.ConstantPower, this.mix, mix);
        }

        this.gain = gain;
        this.mix = mix;
        this.guitar = guitar;
        phase = (phase + io.Length) % SubBlock;

        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }

    /// <summary>The voicings' Advanced page for one block.</summary>
    /// <param name="Hardness">Clip Shape, −1..1 (0 = the curve itself).</param>
    /// <param name="Bias">Offset ahead of the clipper, in full-scale units.</param>
    /// <param name="Dry">Clean input summed after the clipper (linear).</param>
    /// <param name="Slew">Largest step per oversampled sample (0 = no limit).</param>
    readonly record struct Advanced(float Hardness, float Bias, float Dry, float Slew);

    sealed class Channel {
        readonly Oversampler4x oversampler = new(1);
        readonly HalfBandStage twice = new(TwiceBranches);
        float[] twiceBuffer = [];
        SvfState pre;
        SvfState tone;
        SvfState dc;
        AdaaClipper clipper;
        float slewed;

        /// <summary>State of the circuit types, which share this channel's oversampler.</summary>
        public CircuitChannel Circuit { get; } = new();

        public void Allocate(int maxBlockSize) {
            oversampler.Resize(maxBlockSize);
            if (twiceBuffer.Length != 2 * maxBlockSize) {
                twiceBuffer = new float[2 * maxBlockSize];
            }
        }

        public void ResetOversampler() {
            oversampler.Reset();
            twice.Reset();
        }

        public void SetPhase(OversamplingPhase phase) {
            oversampler.SetPhase(phase);
            twice.SetPhase(phase, Oversampler4x.FirstTransition);
        }

        public void Reset() {
            oversampler.Reset();
            twice.Reset();
            pre.Reset();
            tone.Reset();
            dc.Reset();
            clipper.Reset();
            slewed = 0;
            Circuit.Reset();
        }

        public void Process(
            Span<float> samples,
            int factor,
            int type,
            ClipperShape shape,
            float fromGain,
            float toGain,
            in SvfCoefficients pre,
            in SvfCoefficients tone,
            in SvfCoefficients dcBlock,
            bool antialias,
            in Advanced advanced
        ) {
            var inner = Up(samples, factor);
            var step = (toGain - fromGain) / inner.Length;
            var bias = advanced.Bias;
            var offset =
                bias == 0f ? 0f : (float)AdaaClipper.Function(shape, bias); // the bias's own DC, removed at once
            var biased = bias != 0f;
            for (var i = 0; i < inner.Length; i++) {
                var x = inner[i];
                var gain = fromGain + step * (i + 1);
                var driven = (float)this.pre.Tick(pre, x) * gain;
                var y = biased || advanced.Hardness != 0f
                    ? clipper.Process(shape, driven + bias, advanced.Hardness, antialias) - offset
                    : clipper.Process(shape, driven, antialias);
                if (type == 0) {
                    y = 0.5f * (y + x); // TS: clipped mids plus the clean path
                }

                if (type == 2 || biased) {
                    y = (float)dc.Tick(dcBlock, y); // fuzz (or a Bias): remove the offset
                }

                if (advanced.Dry > 0f) {
                    y += advanced.Dry * x;
                }

                if (advanced.Slew > 0f) {
                    y = slewed + Math.Clamp(y - slewed, -advanced.Slew, advanced.Slew);
                    slewed = y;
                }

                inner[i] = y;
            }

            Down(samples, factor);
            for (var i = 0; i < samples.Length; i++) {
                samples[i] = (float)this.tone.Tick(tone, samples[i]);
            }

            this.pre.FlushDenormals();
            this.tone.FlushDenormals();
            dc.FlushDenormals();
            if (MathF.Abs(slewed) < 1e-30f) {
                slewed = 0;
            }
        }

        /// <summary>The span to run the circuit on: the block oversampled by <paramref name="factor" /> (1, 2 or 4).</summary>
        public Span<float> Up(Span<float> samples, int factor) {
            switch (factor) {
                case 4:
                    return oversampler.Up(samples);
                case 2: {
                    var inner = twiceBuffer.AsSpan(0, 2 * samples.Length);
                    twice.Up(samples, inner);
                    return inner;
                }
                default:
                    return samples;
            }
        }

        public void Down(Span<float> samples, int factor) {
            switch (factor) {
                case 4:
                    oversampler.Down(samples);
                    oversampler.FlushDenormals();
                    break;
                case 2:
                    twice.Down(twiceBuffer.AsSpan(0, 2 * samples.Length), samples);
                    twice.FlushDenormals();
                    break;
            }
        }
    }
}
