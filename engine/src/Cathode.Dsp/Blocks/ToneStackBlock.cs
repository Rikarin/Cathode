using Cathode.Dsp.Amp;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     A passive amp tone stack on its own (the same exact networks as the Circuit Amp's: <see cref="ToneStackCircuit" />
///     ),
///     driven from a cathode follower's 1 kΩ into a 1 MΩ grid, for shaping a capture or a clean amp. Linear, so it runs
///     at the sample rate (the bilinear transform's warping moves the treble corners by a few percent at most), in
///     stereo. The stack's insertion loss with every knob at noon is made up, so that Level 0 dB is about unity at
///     1 kHz.
/// </summary>
public sealed class ToneStackBlock() : DspBlock(Info) {
    public const int Type = 0;
    public const int Bass = 1;
    public const int Middle = 2;
    public const int Treble = 3;
    public const int Level = 4;
    public const int Balance = 5;

    const double SourceResistance = 1e3;
    const double LoadResistance = 1e6;

    /// <summary>Each stack's gain at 1 kHz with the knobs at noon, made up at the output.</summary>
    static readonly double[] Makeup = [..ToneStackDesign.All.Select(static d => 1 / NoonGain(d))];

    readonly ToneStackCircuit[] left = Stacks();
    readonly ToneStackCircuit[] right = Stacks();
    int active = -1;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "tonestack",
        "Tone Stack",
        "EQ",
        [
            ParamDescriptor.Choice("type", "Type", [..ToneStackDesign.All.Select(static d => d.Name)]),
            ParamDescriptor.Float("bass", "Bass", 0f, 10f, 5f),
            ParamDescriptor.Float("middle", "Middle", 0f, 10f, 5f),
            ParamDescriptor.Float("treble", "Treble", 0f, 10f, 5f),
            BlockParams.Level,
            BlockParams.Balance
        ],
        maxInstances: 4
    );

    /// <summary>The level plus the current stack's make-up gain, in dB (silence stays silence).</summary>
    float WithMakeup(float levelDb) =>
        levelDb <= Decibels.SilenceDb
            ? levelDb
            : levelDb + (float)Decibels.FromGain(Makeup[Math.Clamp((int)Params.Current(Type), 0, Makeup.Length - 1)]);

    static ToneStackCircuit[] Stacks() => [..ToneStackDesign.All.Select(static d => new ToneStackCircuit(d, 48_000))];

    /// <summary>|H(1 kHz)| at noon, from the impulse response.</summary>
    static double NoonGain(ToneStackDesign design) {
        const double Rate = 48_000;
        var stack = new ToneStackCircuit(design, Rate);
        var (re, im) = (0.0, 0.0);
        for (var n = 0; n < 16_384; n++) {
            var h = stack.Tick(n == 0 ? 1 : 0);
            var phase = 2 * Math.PI * 1_000 * n / Rate;
            re += h * Math.Cos(phase);
            im -= h * Math.Sin(phase);
        }

        return Math.Sqrt(re * re + im * im);
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        for (var i = 0; i < left.Length; i++) {
            left[i].SetSampleRate(spec.SampleRate);
            right[i].SetSampleRate(spec.SampleRate);
        }
    }

    protected override void OnReset() {
        active = (int)Params.Current(Type);
        left[active].Reset();
        right[active].Reset();
        output.Reset(WithMakeup(Params.Current(Level)), Params.Current(Balance));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var type = (int)Params.Current(Type);
        if (type != active) {
            active = type; // a stack chosen again starts discharged
            left[type].Reset();
            right[type].Reset();
        }

        var bass = Params.Advance(Bass, io.Length) / 10.0;
        var middle = Params.Advance(Middle, io.Length) / 10.0;
        var treble = Params.Advance(Treble, io.Length) / 10.0;
        var (l, r) = (left[type], right[type]);
        l.Set(bass, middle, treble);
        r.Set(bass, middle, treble);
        for (var i = 0; i < io.Length; i++) {
            io.Left[i] = (float)Flush.Value(l.Tick(io.Left[i]));
            io.Right[i] = (float)Flush.Value(r.Tick(io.Right[i]));
        }

        l.FlushDenormals();
        r.FlushDenormals();
        output.Apply(io, WithMakeup(Params.Advance(Level, io.Length)), Params.Advance(Balance, io.Length));
    }
}
