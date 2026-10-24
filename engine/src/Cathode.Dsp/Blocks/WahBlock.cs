using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Wah. The first types are filter approximations: a resonant band-pass swept exponentially over the voicing's range
///     by the pedal position, with some low-pass "body" mixed in as in the inductor circuit. The types after them model
///     the inductor circuit itself at component level (a <see cref="WahCircuit" /> per channel, voiced by one of
///     <see cref="Circuits" />), the pedal turning its pot. Either way the filter follows the smoothed position every
///     <see cref="SubBlock" /> samples, so an expression pedal or modifier can drive it smoothly.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>
///             Min and Max Frequency scale the filter voicings' heel and toe frequencies (100 % is the voicing's own).
///             Resonance Q and Q Tracking reshape their peak; on the circuits they scale R7, the resistor across the
///             inductor that damps the resonator.
///         </item>
///         <item>
///             Drive: the filter types saturate (<see cref="SoftDrive" />) ahead of the filter; the circuits are driven
///             harder (up to 18 dB more at the input, the same less at the output), so their transistors clip.
///         </item>
///         <item>
///             Inductor Bias: a DC current through the winding lowers the core's permeability, and so the inductance,
///             to 40 % at full bias; the sweep moves up by up to 1.6 times. Modelled as a linear inductance (the core's
///             saturation curve is not), on the circuits as their L1 and on the filter types as the frequency shift
///             it gives.
///         </item>
///         <item>An 8-band octave graphic EQ (63 Hz to 8 kHz, ±12 dB) follows; flat bands cost nothing.</item>
///     </list>
/// </remarks>
public sealed class WahBlock : DspBlock {
    public const int Type = 0;
    public const int Position = 1;
    public const int Mix = 2;
    public const int Level = 3;
    public const int Balance = 4;
    public const int Resonance = 5;
    public const int QTracking = 6;
    public const int Taper = 7;
    public const int LowCut = 8;
    public const int MinFrequency = 9;
    public const int MaxFrequency = 10;
    public const int Drive = 11;
    public const int InductorBias = 12;
    public const int Eq1 = 13;

    public const int EqBands = 8;

    /// <summary>The first type modelled at component level: type <c>FirstCircuit + i</c> is <see cref="Circuits" />[i].</summary>
    public const int FirstCircuit = 8;

    const int SubBlock = 16;
    const float MaxCircuitDrive = 8f; // +18 dB
    const float MaxBiasDrop = 0.6f;
    const float EqQ = 1.41f; // one octave
    const float PeakGain = 2f;
    const float Body = 0.15f;

    /// <summary>Volts at the circuit's input for a full-scale sample: a hot pickup, enough to push the transistors.</summary>
    const double InputVolts = 1;

    /// <summary>Heel-down frequency, toe-down frequency and Q per voicing.</summary>
    /// <remarks>
    ///     The later five are voicings of the McCoy "Clyde", the Colorsound, an optical Morley, the Vox V845 and the
    ///     Maestro Parapedal from the character those pedals are known for (sweep range and how vocal the peak is), not
    ///     from measurements: the Clyde peaky and high, the Colorsound wide and smooth, the Morley very wide and gentle,
    ///     the V845 narrower than its sibling and the Parapedal low and throaty.
    /// </remarks>
    static readonly (float Low, float High, float Q)[] Voicings = [
        (350f, 2_200f, 5f), // Classic (Cry Baby style)
        (450f, 2_000f, 4.5f), // Vox style
        (300f, 3_000f, 6f), // Wide
        (420f, 2_400f, 7f), // Clyde
        (380f, 2_600f, 4f), // Colorsound
        (320f, 3_400f, 3f), // Morley
        (480f, 1_900f, 4f), // V845
        (280f, 2_200f, 5.5f) // Parapedal
    ];

    static readonly float[] EqFrequencies = [63f, 125f, 250f, 500f, 1_000f, 2_000f, 4_000f, 8_000f];

    readonly EqBand[] eq = [new(), new(), new(), new(), new(), new(), new(), new()];
    SvfState bandLeft, bandRight, lowLeft, lowRight;
    WetCuts lowCut;
    WahCircuit? circuitLeft, circuitRight;
    int configured = -1;
    int countdown;
    float mix = 1f;
    OutputStage output;

    /// <summary>
    ///     The component-level voicings, in type order after the filter approximations. Append only: presets store the
    ///     type index.
    /// </summary>
    public static IReadOnlyList<WahVoicing> Circuits { get; } = [
        // Dunlop GCB-95 as analysed by Holters and Zölzer (DAFx-11): their component values, supply and fitted 2N5089;
        // the inductor is a red Fasel toroid (563 mH, 17.5 Ω as sold).
        new("Cry Baby", "Dunlop GCB-95 Cry Baby") {
            Supply = 8.15,
            Emitter1 = 390,
            BiasGround = 82e3,
            Inductance = 0.563,
            InductorResistance = 17.5,
            Transistor = new(BjtPolarity.Npn, 20.3e-15, 1_430, 4),
            OutputDb = -14
        },
        // Vox V846 after the Thomas Organ patent (US 3,530,224): 470 Ω emitter and 100k bias resistors, BC10x
        // transistors of more modest gain, a 9 V battery and a Halo-type inductor (about 580 mH, 29 Ω): less loop gain,
        // so a narrower sweep and a lower Q.
        new("V846", "Vox V846") {
            Supply = 9,
            Emitter1 = 470,
            BiasGround = 100e3,
            Inductance = 0.58,
            InductorResistance = 29,
            Transistor = BjtModel.BC108,
            OutputDb = -12
        }
    ];

    public static BlockDescriptor Info { get; } = new(
        "wah",
        "Wah",
        "Filter",
        [
            ParamDescriptor.Choice(
                "type",
                "Type",
                [
                    "Classic",
                    "Vox",
                    "Wide",
                    "Clyde",
                    "Colorsound",
                    "Morley",
                    "V845",
                    "Parapedal",
                    ..Circuits.Select(static c => c.Name)
                ]
            ),
            new(
                "position",
                "Position",
                ParamKind.Float,
                0f,
                100f,
                50f,
                "%",
                Smoothing: ParamSmoothing.Linear,
                SmoothingMs: 10f
            ),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Float("resonance", "Resonance Q", 0.25f, 4f, 1f, "x", ParamTaper.Log),
            ParamDescriptor.Float("qTracking", "Q Tracking", 0f, 100f, 0f, "%"),
            ParamDescriptor.Choice("taper", "Control Taper", ["Linear", "Log", "Reverse Log"]),
            WetCuts.LowCutParam("lowCut", "Low Cut", 500f),
            ParamDescriptor.Float("minFreq", "Min Frequency", 50f, 200f, 100f, "%") with {
                TypeLabels = FilterOnly("Min Frequency")
            },
            ParamDescriptor.Float("maxFreq", "Max Frequency", 50f, 200f, 100f, "%") with {
                TypeLabels = FilterOnly("Max Frequency")
            },
            ParamDescriptor.Float("drive", "Drive", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("inductorBias", "Inductor Bias", 0f, 100f, 0f, "%"),
            ..EqFrequencies.Select(static (f, i) => ParamDescriptor.Float(
                    $"eq{i + 1}",
                    f < 1_000f ? $"EQ {f:0} Hz" : $"EQ {f / 1_000f:0} kHz",
                    -12f,
                    12f,
                    0f,
                    "dB"
                )
            )
        ],
        maxInstances: 2
    );

    public WahBlock()
        : base(Info) { }

    /// <summary>The number of filter-approximation voicings, which come before the circuits.</summary>
    public static int FilterVoicings => Voicings.Length;

    /// <summary>
    ///     Resonant frequency for a pedal position (0..100 %) of one of the filter-approximation voicings, its heel and
    ///     toe frequencies scaled by <paramref name="minScale" /> and <paramref name="maxScale" />.
    /// </summary>
    public static float Frequency(int type, float position, float minScale = 1f, float maxScale = 1f) {
        var (low, high, _) = Voicings[type];
        if (minScale != 1f || maxScale != 1f) {
            (low, high) = (low * minScale, Math.Max(low * minScale, high * maxScale));
        }

        return low * MathF.Pow(high / low, Math.Clamp(position, 0f, 100f) / 100f);
    }

    /// <summary>The inductance left by Inductor Bias <paramref name="bias" /> (0..1), as a fraction of the unbiased.</summary>
    public static float BiasedInductance(float bias) => 1f - MaxBiasDrop * Math.Clamp(bias, 0f, 1f);

    static string[] FilterOnly(string name) =>
        [..Enumerable.Range(0, Voicings.Length + Circuits.Count).Select(t => t < Voicings.Length ? name : "")];

    static float Voice(
        float x,
        in SvfCoefficients band,
        in SvfCoefficients low,
        ref SvfState bandState,
        ref SvfState lowState,
        float mix
    ) {
        var wet = (float)(PeakGain * bandState.Tick(band, x) + Body * lowState.Tick(low, x));
        return x * (1 - mix) + wet * mix;
    }

    /// <summary>
    ///     One sample of a circuit: the collector output, polarity restored (Q1 inverts) so that Mix blends rather than
    ///     cancels, trimmed, and with the Newton solver's numerical floor flushed to zero.
    /// </summary>
    static float Voice(float x, WahCircuit circuit, float trim, float drive, float mix) {
        var wet = (float)(-trim * circuit.Tick(x * InputVolts * drive));
        if (Math.Abs(wet) < 1e-20f) {
            wet = 0;
        }

        return x * (1 - mix) + wet * mix;
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        circuitLeft = new(Circuits[0], spec.SampleRate);
        circuitRight = new(Circuits[0], spec.SampleRate);
        configured = FirstCircuit;
    }

    protected override void OnReset() {
        bandLeft.Reset();
        bandRight.Reset();
        lowLeft.Reset();
        lowRight.Reset();
        lowCut.Reset();
        foreach (var band in eq) {
            band.Reset();
        }

        mix = Params.Current(Mix) / 100f;
        output.Reset(Params.Current(Level), Params.Current(Balance));
        var type = (int)Params.Current(Type);
        if (circuitLeft is null) {
            return;
        }

        if (type >= FirstCircuit) {
            Configure(type);
        } else {
            circuitLeft.Reset();
            circuitRight!.Reset();
            countdown = 0;
        }
    }

    /// <summary>The pedal position (0..100 %) through the Control Taper: linear, or bunched toward heel or toe.</summary>
    public static float Tapered(int taper, float position) {
        var p = Math.Clamp(position, 0f, 100f) / 100f;
        return 100f * taper switch {
            1 => p * p,
            2 => 1 - (1 - p) * (1 - p),
            _ => p
        };
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var type = (int)Params.Current(Type);
        lowCut.Design(Spec.SampleRate, Params.Advance(LowCut, io.Length), WetCuts.HighOff);
        if (Params.Current(LowCut) > WetCuts.LowOff) {
            // The input coupling capacitor: everything the wah hears, and so its wet and dry alike, loses its lows.
            for (var i = 0; i < io.Length; i++) {
                lowCut.Tick(ref io.Left[i], ref io.Right[i]);
            }

            lowCut.FlushDenormals();
        }

        if (type >= FirstCircuit) {
            ProcessCircuit(io, type);
        } else {
            ProcessFilter(io, type);
        }

        for (var b = 0; b < EqBands; b++) {
            var moving = Params.IsRamping(Eq1 + b);
            var gain = Params.Advance(Eq1 + b, io.Length);
            eq[b].Process(io, BiquadType.Peak, Spec.SampleRate, EqFrequencies[b], EqQ, gain, moving);
        }

        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }

    void ProcessFilter(in StereoBuffer io, int type) {
        var taper = (int)Params.Current(Taper);
        var baseQ = Voicings[type].Q * Params.Advance(Resonance, io.Length);
        var tracking = Params.Advance(QTracking, io.Length) / 100f;
        var minScale = Params.Advance(MinFrequency, io.Length) / 100f;
        var maxScale = Params.Advance(MaxFrequency, io.Length) / 100f;
        var drive = Params.Advance(Drive, io.Length) / 100f;
        var bias = Params.Advance(InductorBias, io.Length) / 100f;
        var biasShift = bias > 0 ? 1f / MathF.Sqrt(BiasedInductance(bias)) : 1f; // the frequency goes as one over root L
        var mix = Params.Advance(Mix, io.Length) / 100f;
        var mixStep = (mix - this.mix) / io.Length;
        var currentMix = this.mix;

        for (var start = 0; start < io.Length; start += SubBlock) {
            var length = Math.Min(SubBlock, io.Length - start);
            var position = Tapered(taper, Params.Advance(Position, length));
            var frequency = Frequency(type, position, minScale, maxScale) * biasShift;
            var q = baseQ * (1 - 0.5f * tracking * position / 100f); // some wahs get less peaky toward the toe
            var band = SvfCoefficients.Design(BiquadType.BandPass, Spec.SampleRate, frequency, q);
            var low = SvfCoefficients.Design(BiquadType.LowPass, Spec.SampleRate, frequency, 0.7071);
            for (var i = start; i < start + length; i++) {
                currentMix += mixStep;
                var left = SoftDrive.Apply(io.Left[i], drive);
                var right = SoftDrive.Apply(io.Right[i], drive);
                io.Left[i] = Voice(left, band, low, ref bandLeft, ref lowLeft, currentMix);
                io.Right[i] = Voice(right, band, low, ref bandRight, ref lowRight, currentMix);
            }
        }

        bandLeft.FlushDenormals();
        bandRight.FlushDenormals();
        lowLeft.FlushDenormals();
        lowRight.FlushDenormals();
        this.mix = mix;
    }

    /// <summary>
    ///     The component-level types. The pot moves on a fixed grid of <see cref="SubBlock" /> samples counted across
    ///     audio blocks, so the output does not depend on how the host slices them.
    /// </summary>
    void ProcessCircuit(in StereoBuffer io, int type) {
        if (type != configured) {
            Configure(type);
        }

        var left = circuitLeft!;
        var right = circuitRight!;
        var drive = 1f + (MaxCircuitDrive - 1f) * Params.Advance(Drive, io.Length) / 100f;
        var trim = (float)Decibels.ToGain(Circuits[type - FirstCircuit].OutputDb) / drive;
        var inductance = BiasedInductance(Params.Advance(InductorBias, io.Length) / 100f);
        var resonance = Params.Advance(Resonance, io.Length);
        var tracking = Params.Advance(QTracking, io.Length) / 100f;
        var mix = Params.Advance(Mix, io.Length) / 100f;
        var mixStep = (mix - this.mix) / io.Length;
        var currentMix = this.mix;

        var start = 0;
        while (start < io.Length) {
            if (countdown == 0) {
                var toe = Tapered((int)Params.Current(Taper), Params.Current(Position)) / 100.0;
                var q = resonance * (1 - 0.5 * tracking * toe); // less peaky toward the toe, as on the filter types
                left.Trim(inductance, q);
                right.Trim(inductance, q);
                left.SetPosition(toe);
                right.SetPosition(toe);
                countdown = SubBlock;
            }

            var length = Math.Min(countdown, io.Length - start);
            for (var i = start; i < start + length; i++) {
                currentMix += mixStep;
                io.Left[i] = Voice(io.Left[i], left, trim, drive, currentMix);
                io.Right[i] = Voice(io.Right[i], right, trim, drive, currentMix);
            }

            Params.Advance(Position, length);
            countdown -= length;
            start += length;
        }

        this.mix = mix;
    }

    void Configure(int type) {
        var voicing = Circuits[type - FirstCircuit];
        circuitLeft!.Configure(voicing);
        circuitRight!.Configure(voicing);
        var toe = Tapered((int)Params.Current(Taper), Params.Current(Position)) / 100.0;
        circuitLeft.SetPosition(toe);
        circuitRight.SetPosition(toe);
        configured = type;
        countdown = SubBlock;
    }
}
