using Cathode.Dsp.Blocks;
using Cathode.Dsp.Tests.Circuits;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The drive catalogue's component-level stages against the reference simulator, built here independently from the
///     same component values: every tone network's frequency response across its pot, the Tube Screamer clipping amp
///     against the full op-amp device (which also checks that its linear op-amp model loses nothing), and a Fuzz Face
///     in the time domain from its operating point. Then the block-level properties the conformance suite checks only
///     for the default type, for every circuit type.
/// </summary>
public class DriveStageTests(ITestOutputHelper output) {
    const double Rate = 48_000;
    const int Length = 32_768;
    const double Floor = RTypeScattering.EndStop; // the R-type adaptor's minimum resistance, for a pot at its end stop

    static readonly double[] Frequencies = [30, 100, 300, 700, 1_000, 2_000, 4_000, 8_000, 12_000, 18_000];

    public static TheoryData<string, double> ToneSettings {
        get {
            var data = new TheoryData<string, double>();
            foreach (var name in new[] {
                         "TS808 OD", "Rodent Dist", "DS1 Distortion", "Pi Fuzz", "Chiron OD", "Governor Dist"
                     }) {
                foreach (var tone in new[] { 0.0, 0.3, 0.7, 1.0 }) {
                    data.Add(name, tone);
                }
            }

            return data;
        }
    }

    public static TheoryData<int> CircuitTypes =>
        [..Enumerable.Range(DriveBlock.CircuitTypes, DriveCircuits.All.Count)];

    [Theory]
    [MemberData(nameof(ToneSettings))]
    public void Tone_network_matches_the_reference_response(string name, double tone) {
        var network = Circuit(name).Tone;
        var stage = new ToneStage();
        stage.Configure(network, tone == 0.5 ? 0.4 : 0.5, Rate); // reach the setting by a recompute, as a knob would
        stage.Configure(network, tone, Rate);
        var impulse = new float[Length];
        for (var i = 0; i < Length; i++) {
            impulse[i] = (float)stage.Tick(i == 0 ? 1 : 0);
        }

        var (circuit, probe) = Reference(network, tone);
        var reference = new ReferenceSimulator(circuit);
        var worst = 0.0;
        var line = $"{name} ({network.Kind}) at tone {tone}:";
        foreach (var f in Frequencies) {
            var warped = 2 * Rate * Math.Tan(Math.PI * f / Rate);
            var expected = Analysis.ToDb(reference.Ac(new(0, warped), 0, probe).Magnitude);
            var actual = Analysis.ToDb(Analysis.MagnitudeAt(impulse, f, Rate));
            line += $" {f} Hz {actual:0.0} dB";
            worst = Math.Max(worst, Math.Abs(actual - expected));
        }

        output.WriteLine($"{line}; worst {worst:0.0000} dB from the reference");
        Assert.True(worst < 0.05, $"{worst:0.000} dB from the reference");
    }

    [Theory]
    [InlineData("TS808 OD", 0.0, 0.05)]
    [InlineData("TS808 OD", 1.0, 0.05)]
    [InlineData("TS808 OD", 1.0, 0.5)]
    [InlineData("TS Mod OD", 0.7, 0.3)]
    public void Screamer_stage_matches_the_reference_with_the_full_op_amp(string name, double drive, double amplitude) {
        // The WDF's op-amp is linear (its gain-bandwidth from the model); the reference's is the full device with its
        // slew rate and rails. With diodes across the feedback neither matters, which is the reason for the linear
        // model.
        const double StageRate = 192_000;
        const int Oversampling = 16;
        var values = Circuit(name).Stages[0];
        var stage = new OpAmpStage();
        stage.Configure(values, drive, 1e3, StageRate, false);
        Assert.Equal(OpAmpSolve.FeedbackDiodes, stage.Solve);

        double Input(double t) =>
            amplitude * (0.7 * Math.Sin(2 * Math.PI * 1_000 * t) + 0.3 * Math.Sin(2 * Math.PI * 220 * t));

        var (circuit, outputNode) = ScreamerReference(values, drive, Input, StageRate * Oversampling);
        var reference = new ReferenceSimulator(circuit);
        reference.ZeroState();
        var errorDb = Compare(stage.Tick, Input, reference, outputNode, StageRate, Oversampling, 9_600, out var peak);
        output.WriteLine($"{name} drive {drive}, {amplitude} V: output {peak:0.000} V peak, error {errorDb:0.0} dB");
        Assert.Equal(0, reference.NewtonFailures);
        Assert.True(errorDb < -40, $"{errorDb:0.0} dB from the reference");
    }

    [Theory]
    [InlineData("Face Fuzz", 0.5, 0.005)]
    [InlineData("Face Fuzz", 0.5, 0.02)]
    [InlineData("NKT Face Fuzz", 0.5, 0.005)]
    [InlineData("Si Face Fuzz", 0.5, 0.005)]
    public void Fuzz_face_matches_the_reference_from_its_operating_point(string name, double fuzz, double amplitude) {
        const double StageRate = 96_000; // the fuzzes run at 2x
        const int Oversampling = 16;
        var values = Circuit(name).Stages[0];
        var stage = new FuzzPairStage();
        stage.Configure(values, fuzz, StageSource.Driven(8e3), StageRate);

        double Input(double t) =>
            amplitude * (0.7 * Math.Sin(2 * Math.PI * 1_000 * t) + 0.3 * Math.Sin(2 * Math.PI * 220 * t));

        var (circuit, outputNode, junctions) = FaceReference(values, fuzz, Input);
        var reference = new ReferenceSimulator(circuit);
        reference.OperatingPoint();
        for (var d = 0; d < 4; d++) {
            var expected = reference.Voltage(junctions[d].Plus) - reference.Voltage(junctions[d].Minus);
            var actual = stage.Root.DeviceVoltage(d);
            output.WriteLine($"{name} junction {d}: WDF {actual:0.00000} V, reference {expected:0.00000} V");
            Assert.True(Math.Abs(actual - expected) < 1e-3, $"junction {d}: {actual} V against {expected} V");
        }

        stage.Root.ResetStatistics();
        var errorDb = Compare(stage.Tick, Input, reference, outputNode, StageRate, Oversampling, 3_840, out var peak);
        output.WriteLine(
            $"{name} fuzz {fuzz}, {amplitude * 1e3} mV: output {peak:0.000} V peak, error {errorDb:0.0} dB, "
            + $"{stage.Root.MeanIterations:0.00} iterations per sample, {stage.Root.Unconverged} unconverged; reference "
            + $"{reference.NewtonFailures} Newton failures"
        );
        Assert.Equal(0, reference.NewtonFailures);
        Assert.Equal(0, stage.Root.Unconverged);
        Assert.True(errorDb < -60, $"{errorDb:0.0} dB from the reference");
    }

    [Fact]
    public void Rolling_the_guitar_volume_back_cleans_up_a_fuzz_face_far_more_than_an_overdrive() {
        static (double Db, double Thd) Measure(string name, float drive, float volume) {
            using var block = new DriveBlock();
            block.Params.SetTarget(DriveBlock.Type, Type(name));
            block.Params.SetTarget(DriveBlock.Drive, drive);
            block.Params.SetTarget(DriveBlock.InputVolume, volume);
            block.Prepare(new(Rate, 64));
            var input = Signals.CoherentSine(32, 8_192, 0.1f); // about 190 Hz, a firmly picked note
            var rendered = BlockRender.Mono(block, Signals.Concat(input, input, input), 64).AsSpan(16_384);
            return (Analysis.ToDb(Analysis.Rms(rendered)), Analysis.Thd(rendered, 32));
        }

        var ratios = new Dictionary<string, double>();
        foreach (var name in new[] { "Face Fuzz", "Si Face Fuzz", "TS808 OD" }) {
            var line = $"{name,-13} drive 7:";
            var full = Measure(name, 7f, 10f);
            foreach (var volume in new[] { 10f, 8f, 6f, 4f, 2f }) {
                var (db, thd) = Measure(name, 7f, volume);
                line += $" vol {volume}: {db:0.0} dB THD {thd:P0};";
            }

            output.WriteLine(line);
            ratios[name] = Measure(name, 7f, 8f).Thd / full.Thd;
        }

        Assert.True(
            ratios["Face Fuzz"] < 0.25,
            $"the Fuzz Face keeps {ratios["Face Fuzz"]:P0} of its distortion at volume 8"
        );
        Assert.True(
            ratios["TS808 OD"] > 0.25,
            $"the TS808 keeps only {ratios["TS808 OD"]:P0} of its distortion at volume 8"
        );
        Assert.True(ratios["Face Fuzz"] < ratios["TS808 OD"], "the Fuzz Face should clean up more than the TS808");
    }

    [Theory]
    [MemberData(nameof(CircuitTypes))]
    public void Output_does_not_depend_on_block_size_and_reset_is_exact(int type) {
        var input = Signals.Noise(9_600, 11, 0.3f);
        using var fixedSize = Create(type);
        using var variableSize = Create(type);
        var random = new Random(5);
        var reference = BlockRender.Mono(fixedSize, input, 64);
        var varied = BlockRender.Stereo(variableSize, input, input, _ => random.Next(1, 65)).Left;
        fixedSize.Reset();
        var again = BlockRender.Mono(fixedSize, input, 64);

        Assert.True(Analysis.NullResidualDb(reference, varied) < -140, Name(type));
        Assert.Equal(reference, again);
    }

    [Theory]
    [MemberData(nameof(CircuitTypes))]
    public void Turning_every_knob_does_not_allocate(int type) {
        using var block = Create(type);
        var left = Signals.Noise(64, 1, 0.3f);
        var right = Signals.Noise(64, 2, 0.3f);
        var context = new BlockContext(Rate, 120, 0);
        var step = 0;

        void Turn() {
            for (var i = 0; i < 400; i++) {
                if (i % 20 == 0) {
                    step++;
                    block.Params.SetTarget(DriveBlock.Drive, step % 11);
                    block.Params.SetTarget(DriveBlock.Tone, step * 3 % 11);
                    block.Params.SetTarget(DriveBlock.InputVolume, 10 - step % 7);
                }

                block.Process(new(left, right), context);
            }
        }

        Assert.Equal(0, AllocationProbe.Measure(Turn));
    }

    static double Compare(
        Func<double, double> tick,
        Func<double, double> input,
        ReferenceSimulator reference,
        int outputNode,
        double rate,
        int oversampling,
        int samples,
        out double peak
    ) {
        var wdf = new double[samples];
        var expected = new double[samples];
        var h = 1 / (rate * oversampling);
        double mean = 0;
        for (var n = 0; n < samples; n++) {
            var t = n / rate;
            for (var s = 1; s <= oversampling && n > 0; s++) {
                reference.Step(t - (oversampling - s) * h, h);
            }

            wdf[n] = tick(input(t));
            expected[n] = reference.Voltage(outputNode);
            mean += expected[n] / samples;
        }

        double error = 0, energy = 0;
        peak = 0;
        for (var n = 0; n < samples; n++) {
            error += (wdf[n] - expected[n]) * (wdf[n] - expected[n]);
            energy += (expected[n] - mean) * (expected[n] - mean);
            peak = Math.Max(peak, Math.Abs(expected[n] - mean));
        }

        return 10 * Math.Log10(error / energy);
    }

    /// <summary>A tone network as the reference circuit, from its documented topology. Source 0 drives it.</summary>
    static (ReferenceCircuit Circuit, int Probe) Reference(ToneNetwork n, double tone) {
        var c = new ReferenceCircuit();
        var source = c.Node();
        var input = c.Node();
        c.Source(source, 0, static _ => 0);
        c.Resistor(source, input, n.Source);
        var amount = n.AudioTaper ? DriveCircuits.AudioTaper(tone) : tone;
        static double Pot(double ohms) => Math.Max(ohms, Floor);
        switch (n.Kind) {
            case ToneKind.HighCut: {
                var (mid, output) = (c.Node(), c.Node());
                c.Resistor(input, mid, n.Resistor1);
                c.Resistor(mid, output, Pot(n.Pot * (n.AudioTaper ? DriveCircuits.AudioTaper(1 - tone) : 1 - tone)));
                c.Capacitor(output, 0, n.Capacitor1);
                c.Resistor(output, 0, n.Load);
                return (c, output);
            }

            case ToneKind.Blend: {
                var (low, high, wiper) = (c.Node(), c.Node(), c.Node());
                c.Resistor(input, low, n.Resistor1);
                c.Capacitor(low, 0, n.Capacitor1);
                c.Capacitor(input, high, n.Capacitor2);
                c.Resistor(high, 0, n.Resistor2);
                c.Resistor(low, wiper, Pot(n.Pot * amount));
                c.Resistor(wiper, high, Pot(n.Pot * (1 - amount)));
                c.Resistor(wiper, 0, n.Load);
                return (c, wiper);
            }

            case ToneKind.Screamer: {
                var (plus, minus, output, wiper, shunt) = (c.Node(), c.Node(), c.Node(), c.Node(), c.Node());
                c.Resistor(input, plus, n.Resistor1);
                c.Capacitor(plus, 0, n.Capacitor1);
                c.Resistor(plus, wiper, Pot(n.Pot * amount));
                c.Resistor(wiper, minus, Pot(n.Pot * (1 - amount)));
                c.Resistor(wiper, shunt, n.Resistor2);
                c.Capacitor(shunt, 0, n.Capacitor2);
                c.Resistor(output, minus, n.Resistor3);
                c.Resistor(output, 0, n.Load);
                c.Vcvs(output, 0, plus, minus, ToneStage.OpenLoopGain);
                return (c, output);
            }

            case ToneKind.Treble: {
                var (minus, output, inputEnd, outputEnd, wiper) = (c.Node(), c.Node(), c.Node(), c.Node(), c.Node());
                c.Resistor(input, minus, n.Resistor1);
                c.Resistor(output, minus, n.Resistor2);
                c.Capacitor(input, inputEnd, n.Capacitor1);
                c.Capacitor(output, outputEnd, n.Capacitor2);
                c.Resistor(inputEnd, wiper, Pot(n.Pot * (1 - amount)));
                c.Resistor(wiper, outputEnd, Pot(n.Pot * amount));
                c.Resistor(wiper, minus, n.Resistor3);
                c.Resistor(output, 0, n.Load);
                c.Vcvs(output, 0, 0, minus, ToneStage.OpenLoopGain);
                return (c, output);
            }

            case ToneKind.Stack: {
                var (top, wiper, bottom, slope, mid) = (c.Node(), c.Node(), c.Node(), c.Node(), c.Node());
                c.Capacitor(input, top, n.Capacitor1);
                c.Resistor(input, slope, n.Resistor1);
                c.Capacitor(slope, bottom, n.Capacitor2);
                c.Capacitor(slope, mid, n.Capacitor3);
                c.Resistor(top, wiper, Pot(n.Pot * (1 - amount)));
                c.Resistor(wiper, bottom, Pot(n.Pot * amount));
                c.Resistor(bottom, mid, n.Resistor2 * DriveCircuits.AudioTaper(0.5));
                c.Resistor(mid, 0, n.Resistor3 * 0.5);
                c.Resistor(wiper, 0, n.Load);
                return (c, wiper);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(n), n.Kind, null);
        }
    }

    /// <summary>The TS clipping amp with the full op-amp device on ±4.5 V and the feedback diodes.</summary>
    static (ReferenceCircuit Circuit, int Output) ScreamerReference(
        DriveStage s,
        double drive,
        Func<double, double> input,
        double rate
    ) {
        var c = new ReferenceCircuit();
        var (plus, minus, output, leg) = (c.Node(), c.Node(), c.Node(), c.Node());
        var source = c.Node();
        c.Source(source, 0, input);
        c.Resistor(source, plus, 1e3);
        c.Resistor(plus, 0, s.InputBias);
        c.Resistor(output, minus, s.Feedback + s.FeedbackPot * DriveCircuits.AudioTaper(drive));
        c.Capacitor(output, minus, s.FeedbackCapacitance);
        c.Resistor(minus, leg, s.GroundResistance);
        c.Capacitor(leg, 0, s.GroundCapacitance);
        c.Resistor(output, 0, s.Load);
        c.Device(new OpAmp(s.Amplifier, -s.Supply / 2, s.Supply / 2, rate), (plus, minus), (output, 0));
        var d = s.Diodes;
        c.Device(new DiodeClipper(d.Forward, d.ForwardCount, d.Reverse, d.ReverseCount), (output, minus));
        return (c, output);
    }

    /// <summary>
    ///     The Fuzz Face pair driven through 8k, with its junctions as (plus, minus) node pairs in device order.
    /// </summary>
    static (ReferenceCircuit Circuit, int Output, (int Plus, int Minus)[] Junctions) FaceReference(
        DriveStage s,
        double fuzz,
        Func<double, double> input
    ) {
        var c = new ReferenceCircuit();
        var (inputNode, firstBase, firstCollector, secondEmitter) = (c.Node(), c.Node(), c.Node(), c.Node());
        var (secondCollector, junction, wiper, output, supply) = (c.Node(), c.Node(), c.Node(), c.Node(), c.Node());
        var source = c.Node();
        c.Source(source, 0, input);
        c.Resistor(source, inputNode, 8e3);
        c.Capacitor(inputNode, firstBase, s.InputCapacitance);
        c.Capacitor(wiper, 0, s.EmitterCapacitance);
        c.Capacitor(secondCollector, output, s.OutputCapacitance);
        c.Resistor(firstCollector, supply, s.Collector);
        c.Resistor(secondCollector, junction, s.SecondCollector);
        c.Resistor(junction, supply, s.CollectorSupply);
        c.Resistor(secondEmitter, firstBase, s.Feedback);
        c.Resistor(secondEmitter, wiper, Math.Max((1 - fuzz) * s.Emitter, Floor));
        c.Resistor(wiper, 0, Math.Max(fuzz * s.Emitter, Floor));
        c.Resistor(output, 0, s.Load);
        var volts = s.Transistor.Polarity == BjtPolarity.Npn ? s.Supply : -s.Supply;
        c.Source(supply, 0, _ => volts);
        (int, int)[] junctions = [
            (firstBase, 0),
            (firstBase, firstCollector),
            (firstCollector, secondEmitter),
            (firstCollector, secondCollector)
        ];
        c.Device(new Bjt(s.Transistor), junctions[0], junctions[1]);
        c.Device(new Bjt(s.Transistor), junctions[2], junctions[3]);
        return (c, output, junctions);
    }

    static DriveCircuit Circuit(string name) => DriveCircuits.All.Single(c => c.Name == name);

    static int Type(string name) => DriveBlock.CircuitTypes + DriveCircuits.All.ToList().FindIndex(c => c.Name == name);

    static string Name(int type) => DriveBlock.Info.Params[DriveBlock.Type].Options![type];

    static DriveBlock Create(int type) {
        var block = new DriveBlock();
        block.Params.SetTarget(DriveBlock.Type, type);
        block.Params.SetTarget(DriveBlock.Drive, 6f);
        block.Params.SetTarget(DriveBlock.Tone, 4f);
        block.Params.SetTarget(DriveBlock.InputVolume, 8f);
        block.Prepare(new(Rate, 64));
        return block;
    }
}
