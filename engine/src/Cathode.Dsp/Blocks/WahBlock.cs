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
public sealed class WahBlock : DspBlock {
    public const int Type = 0;
    public const int Position = 1;
    public const int Mix = 2;
    public const int Level = 3;

    /// <summary>The first type modelled at component level: type <c>FirstCircuit + i</c> is <see cref="Circuits" />[i].</summary>
    public const int FirstCircuit = 3;

    const int SubBlock = 16;
    const float PeakGain = 2f;
    const float Body = 0.15f;

    /// <summary>Volts at the circuit's input for a full-scale sample: a hot pickup, enough to push the transistors.</summary>
    const double InputVolts = 1;

    /// <summary>Heel-down frequency, toe-down frequency and Q per voicing.</summary>
    static readonly (float Low, float High, float Q)[] Voicings = [
        (350f, 2_200f, 5f), // Classic (Cry Baby style)
        (450f, 2_000f, 4.5f), // Vox style
        (300f, 3_000f, 6f) // Wide
    ];

    SvfState bandLeft, bandRight, lowLeft, lowRight;
    WahCircuit? circuitLeft, circuitRight;
    int configured = -1;
    int countdown;
    float mix = 1f;
    float outputGain = 1f;

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
            ParamDescriptor.Choice("type", "Type", ["Classic", "Vox", "Wide", ..Circuits.Select(static c => c.Name)]),
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
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB")
        ],
        maxInstances: 2
    );

    public WahBlock()
        : base(Info) { }

    /// <summary>Resonant frequency for a pedal position (0..100 %) of one of the filter-approximation voicings.</summary>
    public static float Frequency(int type, float position) {
        var (low, high, _) = Voicings[type];
        return low * MathF.Pow(high / low, Math.Clamp(position, 0f, 100f) / 100f);
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
    static float Voice(float x, WahCircuit circuit, float trim, float mix) {
        var wet = (float)(-trim * circuit.Tick(x * InputVolts));
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
        mix = Params.Current(Mix) / 100f;
        outputGain = Decibels.ToGain(Params.Current(Level));
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

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var type = (int)Params.Current(Type);
        if (type >= FirstCircuit) {
            ProcessCircuit(io, type);
        } else {
            ProcessFilter(io, type);
        }

        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, this.outputGain, outputGain);
        GainRamp.Apply(io.Right, this.outputGain, outputGain);
        this.outputGain = outputGain;
    }

    void ProcessFilter(in StereoBuffer io, int type) {
        var q = Voicings[type].Q;
        var mix = Params.Advance(Mix, io.Length) / 100f;
        var mixStep = (mix - this.mix) / io.Length;
        var currentMix = this.mix;

        for (var start = 0; start < io.Length; start += SubBlock) {
            var length = Math.Min(SubBlock, io.Length - start);
            var frequency = Frequency(type, Params.Advance(Position, length));
            var band = SvfCoefficients.Design(BiquadType.BandPass, Spec.SampleRate, frequency, q);
            var low = SvfCoefficients.Design(BiquadType.LowPass, Spec.SampleRate, frequency, 0.7071);
            for (var i = start; i < start + length; i++) {
                currentMix += mixStep;
                io.Left[i] = Voice(io.Left[i], band, low, ref bandLeft, ref lowLeft, currentMix);
                io.Right[i] = Voice(io.Right[i], band, low, ref bandRight, ref lowRight, currentMix);
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
        var trim = (float)Decibels.ToGain(Circuits[type - FirstCircuit].OutputDb);
        var mix = Params.Advance(Mix, io.Length) / 100f;
        var mixStep = (mix - this.mix) / io.Length;
        var currentMix = this.mix;

        var start = 0;
        while (start < io.Length) {
            if (countdown == 0) {
                var toe = Params.Current(Position) / 100.0;
                left.SetPosition(toe);
                right.SetPosition(toe);
                countdown = SubBlock;
            }

            var length = Math.Min(countdown, io.Length - start);
            for (var i = start; i < start + length; i++) {
                currentMix += mixStep;
                io.Left[i] = Voice(io.Left[i], left, trim, currentMix);
                io.Right[i] = Voice(io.Right[i], right, trim, currentMix);
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
        var toe = Params.Current(Position) / 100.0;
        circuitLeft.SetPosition(toe);
        circuitRight.SetPosition(toe);
        configured = type;
        countdown = SubBlock;
    }
}
