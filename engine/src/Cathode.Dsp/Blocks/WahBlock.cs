using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Wah. The first three types are filter approximations: a resonant band-pass swept exponentially over the voicing's
///     range by the pedal position, with some low-pass "body" mixed in as in the inductor circuit. Every other type models
///     a pedal at component level: the inductor wahs as their circuit (a <see cref="WahCircuit" /> per channel, voiced by
///     one of <see cref="Circuits" />, the pedal turning its pot), the inductor-less Morley and Parapedal as their op-amp
///     band-pass (an <see cref="ActiveWahCircuit" />, voiced by one of <see cref="ActiveCircuits" />). Either way the
///     filter follows the smoothed position every <see cref="SubBlock" /> samples, so an expression pedal or modifier can
///     drive it smoothly.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>
///             Min and Max Frequency scale the heel and toe frequencies (100 % is the voicing's own): the filter
///             voicings' directly, the circuits' through their inductor, scaled at each pedal position so the resonance
///             moves by Min at the heel and Max at the toe (<see cref="SweepScale" />); the inductor-less circuits take
///             the same scale through their capacitors. Resonance Q and Q Tracking reshape their peak; on the inductor
///             circuits they scale R7, the resistor across the inductor that damps the resonator, on the inductor-less
///             ones their damping (see <see cref="ActiveWahCircuit.Trim" />).
///         </item>
///         <item>
///             Drive: the filter types saturate (<see cref="SoftDrive" />) ahead of the filter; the circuits are driven
///             harder (up to 18 dB more at the input, the same less at the output), so their transistors clip.
///         </item>
///         <item>
///             Inductor Bias: a DC current through the winding lowers the core's permeability, and so the inductance,
///             to 40 % at full bias; the sweep moves up by up to 1.6 times. Modelled as a linear inductance (the core's
///             saturation curve is not), on the inductor circuits as their L1, on the filter types and the
///             inductor-less circuits (which have no inductor to bias) as the frequency shift it would give.
///         </item>
///         <item>An 8-band octave graphic EQ (63 Hz to 8 kHz, ±12 dB) follows; flat bands cost nothing.</item>
///     </list>
/// </remarks>
public sealed class WahBlock() : DspBlock(Info) {
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

    /// <summary>
    ///     The first type added as a component-level circuit (the Cry Baby; the V846 follows). The five types before it
    ///     from Clyde on were filter voicings first and are circuits now, in place, so <see cref="IsCircuit" /> is the test.
    /// </summary>
    public const int FirstCircuit = 8;

    const int SubBlock = 16;
    const float MaxCircuitDrive = 8f; // +18 dB
    const float MaxBiasDrop = 0.6f;
    const float EqQ = 1.41f; // one octave
    const float PeakGain = 2f;
    const float Body = 0.15f;

    /// <summary>Volts at the circuit's input for a full-scale sample: a hot pickup, enough to push the transistors.</summary>
    const double InputVolts = 1;

    /// <summary>Heel-down frequency, toe-down frequency and Q per filter voicing (types 0 to 2).</summary>
    static readonly (float Low, float High, float Q)[] Voicings = [
        (350f, 2_200f, 5f), // Classic (Cry Baby style)
        (450f, 2_000f, 4.5f), // Vox style
        (300f, 3_000f, 6f) // Wide
    ];

    enum Kind : byte {
        Filter,
        Inductor,
        Active
    }

    /// <summary>
    ///     What each type is, in type order (presets store the index): a filter voicing, an inductor circuit
    ///     (<see cref="Circuits" />) or an inductor-less one (<see cref="ActiveCircuits" />), and its index in that list.
    ///     Clyde to Parapedal were filter voicings until they became circuits in place.
    /// </summary>
    static readonly (Kind Kind, int Index)[] Types = [
        (Kind.Filter, 0), // Classic
        (Kind.Filter, 1), // Vox
        (Kind.Filter, 2), // Wide
        (Kind.Inductor, 2), // Clyde
        (Kind.Inductor, 3), // Colorsound
        (Kind.Active, 0), // Morley
        (Kind.Inductor, 4), // V845
        (Kind.Active, 1), // Parapedal
        (Kind.Inductor, 0), // Cry Baby
        (Kind.Inductor, 1) // V846
    ];

    static readonly string[] FilterNames = ["Classic", "Vox", "Wide"];

    static readonly float[] EqFrequencies = [63f, 125f, 250f, 500f, 1_000f, 2_000f, 4_000f, 8_000f];

    readonly EqBand[] eq = [new(), new(), new(), new(), new(), new(), new(), new()];
    SvfState bandLeft, bandRight, lowLeft, lowRight;
    WetCuts lowCut;
    WahCircuit? circuitLeft, circuitRight;
    ActiveWahCircuit? activeLeft, activeRight;
    int configured = -1;
    int countdown;
    float mix = 1f;
    OutputStage output;

    /// <summary>The inductor circuits. Append only: the type table refers to them by index.</summary>
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
        },
        // Vox Clyde McCoy: the V846's schematic (Vox used one drawing for both), with the "nasal" Halo inductor (its
        // inductance guessed at 450 mH, 30 Ω) and the 100k across it that original Clydes are reported with (33k and
        // 68k were also fitted): a higher sweep and the most vocal peak of the family.
        new("Clyde", "Vox Clyde McCoy") {
            Supply = 9,
            Emitter1 = 470,
            BiasGround = 100e3,
            QResistor = 100e3,
            Inductance = 0.45,
            InductorResistance = 30,
            Transistor = BjtModel.BC108,
            OutputDb = -15
        },
        // Sola Sound Colorsound wah, the original inductor version: the Thomas Organ circuit with BC10x transistors. No
        // traced values to hand, so guessed to its character, wide and smooth: a smaller tuning capacitor (8.2 nF) and a
        // 470 mH inductor lift the toe, a 220 Ω emitter resistor gives more loop gain (a deeper heel) and a 27k across
        // the inductor keeps the peak broad.
        new("Colorsound", "Sola Sound Colorsound wah") {
            Supply = 9,
            Emitter1 = 220,
            BiasGround = 100e3,
            QResistor = 27e3,
            Tuning = 8.2e-9,
            Inductance = 0.47,
            InductorResistance = 29,
            Transistor = BjtModel.BC108,
            OutputDb = -16
        },
        // Vox V845, the budget sibling: the same two-transistor circuit with cheaper parts. Guessed: a 660 mH inductor
        // with a thinner, more resistive winding (60 Ω), an 820 Ω emitter resistor (less loop gain: a narrower sweep)
        // and a 27k across the inductor (a milder peak).
        new("V845", "Vox V845") {
            Supply = 9,
            Emitter1 = 820,
            BiasGround = 100e3,
            QResistor = 27e3,
            Inductance = 0.66,
            InductorResistance = 60,
            Transistor = BjtModel.BC108,
            OutputDb = -5
        }
    ];

    /// <summary>The inductor-less circuits. Append only: the type table refers to them by index.</summary>
    public static IReadOnlyList<ActiveWahVoicing> ActiveCircuits { get; } = [
        // Morley optical wah: a lamp behind a vane on the treadle lights a CdS cell, the shunt resistor of a one-op-amp
        // multiple-feedback band-pass, then a make-up stage. Values guessed for its known range (about 330 Hz to
        // 3.4 kHz): 33k into the band-pass, 75k feedback and 10 nF capacitors (constant bandwidth, so the Q rises with
        // the frequency), a cell of 300 ohms lit to 500k dark, gain 1.5 after it, a 9 V supply.
        new("Morley", "Morley optical (power) wah", ActiveWahTopology.Optical) {
            OutputFeedback = 4.7e3, OutputGround = 10e3, OutputDb = 2
        },
        // Tycobrahe / Dunlop Parapedal: inductor-less, two op-amps tuned together by a dual-gang pot. The loop is a
        // two-integrator band-pass; each gang 25k plus 3.3k in series with 22 nF integrating capacitors (about
        // 260 Hz to 2.2 kHz), the damping set by a 47k / 10k ratio (Q 4.7 over the whole sweep) and the gain by 47k
        // over 22k.
        new("Parapedal", "Tycobrahe Parapedal", ActiveWahTopology.DualGang) {
            InputResistor = 22e3, Capacitor = 22e-9, OutputDb = 1
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
                    ..Types.Select(static t => t.Kind switch {
                            Kind.Filter => FilterNames[t.Index],
                            Kind.Inductor => Circuits[t.Index].Name,
                            _ => ActiveCircuits[t.Index].Name
                        }
                    )
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
            ParamDescriptor.Float("minFreq", "Min Frequency", 50f, 200f, 100f, "%"),
            ParamDescriptor.Float("maxFreq", "Max Frequency", 50f, 200f, 100f, "%"),
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
        maxInstances: 2,
        pages: [
            new(
                "Basic",
                [
                    "position", "minFreq", "maxFreq", "resonance", "qTracking", "taper", "lowCut", "drive",
                    "inductorBias"
                ]
            ),
            new("Graphic EQ", ["eq1", "eq2", "eq3", "eq4", "eq5", "eq6", "eq7", "eq8"])
        ]
    );

    /// <summary>The number of filter-approximation voicings, types 0 to <c>FilterVoicings - 1</c>.</summary>
    public static int FilterVoicings => Voicings.Length;

    /// <summary>The number of types.</summary>
    public static int TypeCount => Types.Length;

    /// <summary>Whether <paramref name="type" /> is modelled at component level (an inductor or inductor-less circuit).</summary>
    public static bool IsCircuit(int type) => Types[type].Kind != Kind.Filter;

    /// <summary>The inductor circuit of <paramref name="type" />, or null.</summary>
    public static WahVoicing? InductorCircuit(int type) =>
        Types[type].Kind == Kind.Inductor ? Circuits[Types[type].Index] : null;

    /// <summary>The inductor-less circuit of <paramref name="type" />, or null.</summary>
    public static ActiveWahVoicing? ActiveCircuit(int type) =>
        Types[type].Kind == Kind.Active ? ActiveCircuits[Types[type].Index] : null;

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

    /// <summary>The pedal position (0..100 %) through the Control Taper: linear, or bunched toward heel or toe.</summary>
    public static float Tapered(int taper, float position) {
        var p = Math.Clamp(position, 0f, 100f) / 100f;
        return 100f
            * taper switch {
                1 => p * p,
                2 => 1 - (1 - p) * (1 - p),
                _ => p
            };
    }

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

    /// <summary>One sample of an inductor-less circuit: its inverting band-pass's output, polarity restored and trimmed.</summary>
    static float Voice(float x, ActiveWahCircuit circuit, float trim, float drive, float mix) {
        var wet = (float)(-trim * circuit.Tick(x * InputVolts * drive));
        return x * (1 - mix) + wet * mix;
    }

    void ProcessFilter(in StereoBuffer io, int type) {
        var taper = (int)Params.Current(Taper);
        var baseQ = Voicings[type].Q * Params.Advance(Resonance, io.Length);
        var tracking = Params.Advance(QTracking, io.Length) / 100f;
        var minScale = Params.Advance(MinFrequency, io.Length) / 100f;
        var maxScale = Params.Advance(MaxFrequency, io.Length) / 100f;
        var drive = Params.Advance(Drive, io.Length) / 100f;
        var bias = Params.Advance(InductorBias, io.Length) / 100f;
        var biasShift =
            bias > 0 ? 1f / MathF.Sqrt(BiasedInductance(bias)) : 1f; // the frequency goes as one over root L
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
    ///     The component-level types. The pot (or the optical wah's vane) moves on a fixed grid of
    ///     <see cref="SubBlock" /> samples counted across audio blocks, so the output does not depend on how the host
    ///     slices them.
    /// </summary>
    void ProcessCircuit(in StereoBuffer io, int type) {
        if (type != configured) {
            Configure(type);
        }

        var active = Types[type].Kind == Kind.Active;
        var drive = 1f + (MaxCircuitDrive - 1f) * Params.Advance(Drive, io.Length) / 100f;
        var outputDb = active ? ActiveCircuits[Types[type].Index].OutputDb : Circuits[Types[type].Index].OutputDb;
        var trim = (float)Decibels.ToGain(outputDb) / drive;
        var inductance = BiasedInductance(Params.Advance(InductorBias, io.Length) / 100f);
        var minScale = Params.Advance(MinFrequency, io.Length) / 100.0;
        var maxScale = Params.Advance(MaxFrequency, io.Length) / 100.0;
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
                var scaled = inductance / Square(SweepScale(minScale, maxScale, toe));
                if (active) {
                    activeLeft!.Trim(scaled, q);
                    activeRight!.Trim(scaled, q);
                    activeLeft.SetPosition(toe);
                    activeRight.SetPosition(toe);
                } else {
                    circuitLeft!.Trim(scaled, q);
                    circuitRight!.Trim(scaled, q);
                    circuitLeft.SetPosition(toe);
                    circuitRight.SetPosition(toe);
                }

                countdown = SubBlock;
            }

            var length = Math.Min(countdown, io.Length - start);
            if (active) {
                var left = activeLeft!;
                var right = activeRight!;
                for (var i = start; i < start + length; i++) {
                    currentMix += mixStep;
                    io.Left[i] = Voice(io.Left[i], left, trim, drive, currentMix);
                    io.Right[i] = Voice(io.Right[i], right, trim, drive, currentMix);
                }
            } else {
                var left = circuitLeft!;
                var right = circuitRight!;
                for (var i = start; i < start + length; i++) {
                    currentMix += mixStep;
                    io.Left[i] = Voice(io.Left[i], left, trim, drive, currentMix);
                    io.Right[i] = Voice(io.Right[i], right, trim, drive, currentMix);
                }
            }

            Params.Advance(Position, length);
            countdown -= length;
            start += length;
        }

        this.mix = mix;
    }

    /// <summary>
    ///     How far Min and Max Frequency move a circuit's resonance at pedal position <paramref name="toe" /> (0 … 1): Min
    ///     at the heel, Max at the toe, geometrically between. The circuit takes it as its inductor, scaled by one over
    ///     its square (the resonance goes as one over root L).
    /// </summary>
    public static double SweepScale(double minScale, double maxScale, double toe) =>
        minScale == maxScale ? minScale : Math.Pow(minScale, 1 - toe) * Math.Pow(maxScale, toe);

    static double Square(double x) => x * x;

    void Configure(int type) {
        var toe = Tapered((int)Params.Current(Taper), Params.Current(Position)) / 100.0;
        var (kind, index) = Types[type];
        if (kind == Kind.Active) {
            activeLeft!.Configure(ActiveCircuits[index]);
            activeRight!.Configure(ActiveCircuits[index]);
            activeLeft.SetPosition(toe);
            activeRight.SetPosition(toe);
        } else {
            circuitLeft!.Configure(Circuits[index]);
            circuitRight!.Configure(Circuits[index]);
            circuitLeft.SetPosition(toe);
            circuitRight.SetPosition(toe);
        }

        configured = type;
        countdown = SubBlock;
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        circuitLeft = new(Circuits[0], spec.SampleRate);
        circuitRight = new(Circuits[0], spec.SampleRate);
        activeLeft = new(ActiveCircuits[0], spec.SampleRate, SubBlock);
        activeRight = new(ActiveCircuits[0], spec.SampleRate, SubBlock);
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

        if (IsCircuit(type)) {
            Configure(type);
        } else {
            circuitLeft.Reset();
            circuitRight!.Reset();
            activeLeft!.Reset();
            activeRight!.Reset();
            countdown = 0;
        }
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

        if (IsCircuit(type)) {
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
}
