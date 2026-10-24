using Cathode.Dsp.Blocks;
using Cathode.Dsp.Filters;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Drive and Dynamic Distortion parity items of stream PB (docs/08 §6.5–6.6): the remaining clip types, a tone
///     stack's own bass and middle pots, and Slew Rate on each op-amp. Defaults are stock (<see cref="AmpDefaultsTests" />).
/// </summary>
public class DriveRound3Tests {
    const double Rate = 48_000;

    static readonly ClipperShape[] NewShapes = [
        ClipperShape.Fet, ClipperShape.Rectifier, ClipperShape.Variable, ClipperShape.HvTube, ClipperShape.LvTube,
        ClipperShape.Silicon, ClipperShape.OpAmpDiode
    ];

    public static TheoryData<ClipperShape> Shapes => [..NewShapes];

    [Theory]
    [MemberData(nameof(Shapes))]
    public void New_clip_curves_are_continuous_monotonic_and_their_antiderivatives_match(ClipperShape shape) {
        // The anti-aliased clipper on a slow ramp is the curve itself (ADAA's difference quotient of F is f).
        var clipper = new AdaaClipper();
        var previous = double.NegativeInfinity;
        for (var x = -4.0; x <= 4.0; x += 1e-3) {
            var y = clipper.Process(shape, (float)x);
            var exact = AdaaClipper.Function(shape, x - 0.5e-3);
            if (x > -3.99) {
                // From the second sample on (the first averages from the reset state at 0).
                Assert.Equal(exact, y, 2e-3);
                Assert.True(y >= previous - 1e-6, $"{shape} not monotonic at {x}");
            }

            previous = y;
        }

        Assert.Equal(0, AdaaClipper.Function(shape, 0), 1e-12);
    }

    [Fact]
    public void Clip_curves_have_their_shapes() {
        Assert.Equal(1, AdaaClipper.Function(ClipperShape.Variable, 0.5 * Math.PI), 1e-12);
        Assert.Equal(1, AdaaClipper.Function(ClipperShape.Variable, 3), 1e-12);
        // Asymmetric curves: the rectifier's negative half is a fifth of the positive.
        Assert.Equal(-0.2 * Math.Tanh(2), AdaaClipper.Function(ClipperShape.Rectifier, -2), 1e-12);
        Assert.True(AdaaClipper.Function(ClipperShape.HvTube, 3) > AdaaClipper.Function(ClipperShape.LvTube, 3) - 1e-12);
        Assert.True(-AdaaClipper.Function(ClipperShape.LvTube, -3) < AdaaClipper.Function(ClipperShape.LvTube, 3));
        Assert.Equal(1 - Math.Exp(-2), AdaaClipper.Function(ClipperShape.Silicon, 2), 1e-12);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Drive_clip_types_are_audible_and_bounded(ClipperShape shape) {
        var input = Signals.Sine(220, Rate, 9_600, 0.3f);
        var stock = RenderDrive(0, input);
        var clipped = RenderDrive(0, input, (DriveBlock.ClipType, (int)shape + 1));
        Assert.True(Analysis.AllFinite(clipped));
        Assert.True(Analysis.Peak(clipped) < 4);
        Assert.True(Residual(stock, clipped) > -40, $"{shape}: {Residual(stock, clipped):0.0} dB");
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Dynamic_distortion_takes_the_new_curves(ClipperShape shape) {
        var input = Signals.Sine(220, Rate, 9_600, 0.3f);
        using var soft = TestBlocks.Create<DynamicDistortionBlock>();
        using var other = TestBlocks.Create<DynamicDistortionBlock>((0, (int)shape));
        var a = BlockRender.Mono(soft, input, 64);
        var b = BlockRender.Mono(other, input, 64);
        Assert.True(Analysis.AllFinite(b));
        Assert.True(Residual(a, b) > -50, $"{shape}: {Residual(a, b):0.0} dB");
    }

    [Fact]
    public void Stack_bass_and_middle_pots_work_the_stack() {
        var type = TypeOf("Governor Dist");
        var labels = DriveBlock.Info.Params[DriveBlock.StackBass].TypeLabels!;
        Assert.Equal("Bass Pot", labels[type]);
        Assert.Equal(string.Empty, labels[TypeOf("Rodent Dist")]);

        // Quiet, low drive: the stack's response. Bass lifts and cuts 100 Hz; middle down cuts 700 Hz.
        double Level(double frequency, params (int, float)[] settings) =>
            SteadyDb(RenderDrive(type, Signals.Sine(frequency, Rate, 19_200, 0.002f), [(DriveBlock.Drive, 0f), ..settings]));

        var bass = Level(100);
        Assert.True(Level(100, (DriveBlock.StackBass, 10f)) > bass + 1);
        Assert.True(Level(100, (DriveBlock.StackBass, 0f)) < bass - 6);
        Assert.True(Level(700, (DriveBlock.StackMiddle, 0f)) < Level(700) - 3);
        Assert.Equal(Level(3_000), Level(3_000, (DriveBlock.StackBass, 5f)), 1e-6);
    }

    [Fact]
    public void Slew_rate_sets_the_rat_op_amps_own_slew() {
        var type = TypeOf("Rodent Dist");
        var input = Signals.Sine(3_000, Rate, 9_600, 0.5f);
        var stock = RenderDrive(type, input, (DriveBlock.Drive, 8f));
        var faster = RenderDrive(type, input, (DriveBlock.Drive, 8f), (DriveBlock.SlewRate, 5f));
        var slower = RenderDrive(type, input, (DriveBlock.Drive, 8f), (DriveBlock.SlewRate, 0.03f));
        Assert.True(Residual(stock, faster) > -40, $"{Residual(stock, faster):0.0} dB");
        // A slower op-amp cannot follow 3 kHz at full swing: a smaller, rounder output.
        Assert.True(SteadyDb(slower) < SteadyDb(stock) - 0.5, $"{SteadyDb(slower):0.0} against {SteadyDb(stock):0.0} dB");
        Assert.True(Residual(stock, slower) > -30, $"{Residual(stock, slower):0.0} dB");
    }

    [Fact]
    public void Slew_rate_limits_a_linear_op_amp_stage_at_its_output() {
        var stage = new OpAmpStage();
        var circuit = DriveCircuits.All[TypeOf("TS808 OD") - DriveBlock.CircuitTypes];
        var opAmpStage = circuit.Stages.First(static s => s.Kind == StageKind.OpAmp);
        const double Inner = 192_000;
        const double VoltsPerSecond = 0.1e6;
        stage.Configure(opAmpStage, 1, 1e3, Inner, true, VoltsPerSecond);
        var previous = 0.0;
        var most = 0.0;
        for (var i = 0; i < 4_000; i++) {
            var y = stage.Tick(i % 200 < 100 ? 1 : -1);
            most = Math.Max(most, Math.Abs(y - previous));
            previous = y;
        }

        Assert.True(most <= VoltsPerSecond / Inner + 1e-12, $"{most} V per sample");
    }

    [Fact]
    public void Op_amp_device_takes_a_slew_rate() {
        var amp = new OpAmp(OpAmpModel.LM308, -4.5, 4.5, 96_000);
        amp.SetSlewRate(0.1e6, 96_000);
        var moved = Commit(ref amp, 1);
        // One sample of a huge step moves at most the slew limit (the soft limiter stays below it).
        Assert.True(moved <= 0.1e6 / 96_000 + 1e-9 && moved > 0.5 * 0.1e6 / 96_000, $"{moved}");

        static double Commit(ref OpAmp amp, double input) {
            amp.Commit([input, 0]);
            return amp.InternalOutput;
        }
    }

    static int TypeOf(string name) =>
        DriveBlock.CircuitTypes + DriveCircuits.All.ToList().FindIndex(c => c.Name == name);

    static float[] RenderDrive(int type, float[] input, params (int Index, float Value)[] settings) {
        using var block = TestBlocks.Create<DriveBlock>([(DriveBlock.Type, type), ..settings]);
        return BlockRender.Mono(block, input, 64);
    }

    static double Residual(float[] reference, float[] changed) =>
        Analysis.NullResidualDb(reference, changed) - Analysis.ToDb(Analysis.Rms(reference));

    static double SteadyDb(float[] signal) => Analysis.ToDb(Analysis.Rms(signal.AsSpan(signal.Length / 2)));
}
