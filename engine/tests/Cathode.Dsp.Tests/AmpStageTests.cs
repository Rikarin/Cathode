using System.Numerics;
using Cathode.Dsp.Amp;
using Cathode.Dsp.Tests.Circuits;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The amp stage library (<see cref="Cathode.Dsp.Amp" />) against the reference MNA simulator: tone stacks against
///     its AC analysis, operating points against its DC solve and datasheet values, triode and power stages in the time
///     domain at moderate drive against its trapezoidal transient run 16 times faster with exact (untabulated) tubes.
/// </summary>
public class AmpStageTests(ITestOutputHelper output) {
    const double Rate = 96_000; // the circuit amp's rate at 48 kHz
    const int Oversampling = 16;

    public static TheoryData<int, double, double, double> ToneStackSettings => new() {
        { 0, 0.5, 0.5, 0.5 },
        { 0, 0.0, 1.0, 0.2 },
        { 0, 1.0, 0.0, 1.0 },
        { 1, 0.5, 0.5, 0.5 },
        { 1, 0.1, 0.9, 0.8 },
        { 1, 0.9, 0.1, 0.0 },
        { 2, 0.5, 0.5, 0.5 },
        { 2, 1.0, 0.3, 0.1 },
        { 2, 0.0, 0.7, 1.0 }
    };

    [Theory]
    [MemberData(nameof(ToneStackSettings))]
    public void Tone_stacks_match_the_reference_response(int kind, double bass, double middle, double treble) {
        const int Length = 65_536;
        double[] frequencies = [30, 60, 100, 200, 400, 700, 1_000, 2_000, 4_000, 8_000, 15_000, 20_000];
        var design = ToneStackDesign.All[kind];
        var stack = new ToneStackCircuit(design, Rate, 1e3, 1e6);
        stack.Set(0.3, 0.6, 0.4); // reach the setting by a recompute, as a knob would
        stack.Set(bass, middle, treble);
        var impulse = new float[Length];
        for (var i = 0; i < Length; i++) {
            impulse[i] = (float)stack.Tick(i == 0 ? 1 : 0);
        }

        var c = new ReferenceCircuit();
        var (source, input) = (c.Node(), c.Node());
        c.Source(source, 0, static _ => 0);
        c.Resistor(source, input, 1e3);
        var probe = AmpReferences.Stack(c, input, design, bass, middle, treble);
        c.Resistor(probe, 0, 1e6);
        var reference = new ReferenceSimulator(c);

        var worst = 0.0;
        foreach (var f in frequencies) {
            var warped = 2 * Rate * Math.Tan(Math.PI * f / Rate); // the bilinear transform's frequency warping
            var expected = Analysis.ToDb(reference.Ac(new(0, warped), 0, probe).Magnitude);
            var actual = Analysis.ToDb(Analysis.MagnitudeAt(impulse, f, Rate));
            worst = Math.Max(worst, Math.Abs(actual - expected));
        }

        output.WriteLine($"{design.Name} b {bass} m {middle} t {treble}: worst {worst:0.000000} dB");
        Assert.True(worst < 0.5, $"{worst:0.000} dB from the reference");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Tabulated_triodes_match_koren_to_1e_6(int index) {
        TriodeModel[] models = [TriodeModel.Ecc83, TriodeModel.Ecc81, TriodeModel.Ecc82];
        var model = models[index];
        var exact = new Triode(model);
        var fast = new FastTriode(model);
        var random = new Random(3);
        var worst = 0.0;
        Span<double> v = stackalloc double[2];
        Span<double> i1 = stackalloc double[2];
        Span<double> i2 = stackalloc double[2];
        Span<double> j1 = stackalloc double[4];
        Span<double> j2 = stackalloc double[4];
        for (var n = 0; n < 100_000; n++) {
            v[0] = -8 + 10 * random.NextDouble();
            v[1] = 450 * random.NextDouble();
            j1.Clear();
            j2.Clear();
            exact.Evaluate(v, i1, j1, 2);
            fast.Evaluate(v, i2, j2, 2);
            for (var k = 0; k < 2; k++) {
                worst = Math.Max(worst, Math.Abs(i1[k] - i2[k]) / (Math.Abs(i1[k]) + 1e-9));
            }

            for (var k = 0; k < 4; k++) {
                worst = Math.Max(worst, Math.Abs(j1[k] - j2[k]) / (Math.Abs(j1[k]) + 1e-8));
            }
        }

        output.WriteLine($"{model}: worst relative error {worst:E2}");
        Assert.True(worst < 1e-6, $"{worst:E2}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Tabulated_pentodes_match_koren_to_1e_6(int index) {
        PentodeModel[] models = [PentodeModel.EL34, PentodeModel.SixL6GC, PentodeModel.EL84];
        var model = models[index];
        var tables = PentodeTables.For(model);
        var exact = new Pentode(model);
        var random = new Random(5);
        var worst = 0.0;
        Span<double> v = stackalloc double[3];
        Span<double> i = stackalloc double[3];
        Span<double> j = stackalloc double[9];
        for (var n = 0; n < 100_000; n++) {
            v[0] = -80 + 85 * random.NextDouble();
            v[1] = 200 + 300 * random.NextDouble();
            v[2] = 900 * random.NextDouble();
            j.Clear();
            exact.Evaluate(v, i, j, 3);
            var factor = tables.ScreenFactor(v[1]);
            var plate = tables.PlateCurrent(factor, v[0], v[1], v[2], out var gridSlope, out var plateSlope);
            var screen = tables.ScreenCurrent(v[0], v[1]);
            worst = Math.Max(worst, Math.Abs(plate - i[2]) / (i[2] + 1e-9));
            worst = Math.Max(worst, Math.Abs(screen - i[1]) / (i[1] + 1e-9));
            worst = Math.Max(worst, Math.Abs(gridSlope - j[6]) / (Math.Abs(j[6]) + 1e-8));
            worst = Math.Max(worst, Math.Abs(plateSlope - j[8]) / (Math.Abs(j[8]) + 1e-8));
        }

        output.WriteLine($"{model}: worst relative error {worst:E2}");
        Assert.True(worst < 1e-6, $"{worst:E2}");
    }

    /// <summary>12AX7, 100k plate, 1.5k bypassed cathode, 1M grid leak, 22 nF coupling: the textbook stage.</summary>
    static readonly TriodeStageDesign Textbook = new(
        "textbook",
        TriodeModel.Ecc83,
        300,
        100e3,
        1.5e3,
        CathodeBypass: 22e-6,
        CouplingCap: 22e-9,
        GridLeak: 1e6
    );

    public static TheoryData<string> TriodeStages => ["textbook", "blackface V2A", "plexi V1A", "plexi V2A", "ac30 V1A"];

    [Theory]
    [MemberData(nameof(TriodeStages))]
    public void Triode_stage_operating_points_match_the_reference_and_the_datasheet(string name) {
        var design = TriodeDesign(name);
        var stage = new TriodeStage(design, Rate);
        stage.Update(new(0.6, 0.5, 0.5, 0.5, 1, true));
        stage.Reset(0);
        var (circuit, plate, grid, cathode) = AmpReferences.Triode(design, 0.6, true, 0, static _ => 0);
        var reference = new ReferenceSimulator(circuit);
        reference.OperatingPoint();
        output.WriteLine(
            $"{name}: plate {stage.RestOutput:0.00} V (reference {reference.Voltage(plate):0.00}), cathode " +
            $"{stage.RestCathode:0.000} V ({reference.Voltage(cathode):0.000}), {stage.RestCurrent * 1e3:0.000} mA, " +
            $"output resistance {stage.OutputResistance / 1e3:0.0}k"
        );
        Assert.Equal(reference.Voltage(plate), stage.RestOutput, 0.002 * reference.Voltage(plate));
        Assert.Equal(reference.Voltage(cathode), stage.RestCathode, 0.002 * reference.Voltage(cathode));

        // A 12AX7 with 100k and 1.5k at 250-300 V sits at 150-200 V and about 1 mA (RCA's and Merlin Blencowe's
        // load-line examples).
        if (design.PlateResistor == 100e3 && design.CathodeResistor == 1.5e3) {
            Assert.InRange(stage.RestOutput, 150, 210);
            Assert.InRange(stage.RestCurrent, 0.7e-3, 1.3e-3);
        }

        // Settled from the reset state with the input at rest, the stage stays at its operating point.
        for (var i = 0; i < 9_600; i++) {
            stage.Tick(0);
        }

        Assert.Equal(reference.Voltage(plate), stage.Tick(0), 0.002 * reference.Voltage(plate));
        Assert.Equal(reference.Voltage(grid), stage.Grid, 1e-3);
    }

    [Theory]
    [InlineData("textbook", 1.0)]
    [InlineData("blackface V2A", 0.5)]
    [InlineData("plexi V2A", 8.0)]
    [InlineData("ac30 V1A", 0.5)]
    public void Triode_stages_match_the_reference_in_the_time_domain(string name, double amplitude) {
        var design = TriodeDesign(name);
        const double SourceResistance = 30e3;
        var stage = new TriodeStage(design, Rate);
        stage.SetSourceResistance(SourceResistance);
        stage.Update(new(0.6, 0.5, 0.5, 0.5, 1, true));
        stage.Reset(0);
        var (circuit, plate, _, _) = AmpReferences.Triode(design, 0.6, true, SourceResistance, t => Drive(amplitude, t));
        var errorDb = CompareInTime(name, stage.Tick, stage.Newton, circuit, plate, amplitude);
        Assert.True(errorDb < -40, $"{name}: {errorDb:0.0} dB from the reference");
    }

    [Theory]
    [InlineData("plexi V2B", 15.0)]
    [InlineData("ac30 V2B", 15.0)]
    [InlineData("blackface V1A", 0.5)]
    public void Tone_stack_stages_match_the_reference_in_the_time_domain(string name, double amplitude) {
        var (design, rest, sourceResistance) = name switch {
            "plexi V2B" => ((ToneStackStageDesign)AmpDesign.Plexi.Preamp[2], 175.1, 30.7e3),
            "ac30 V2B" => ((ToneStackStageDesign)AmpDesign.AC30.Preamp[2], 132.8, 112e3),
            _ => ((ToneStackStageDesign)AmpDesign.Blackface.Preamp[0], 0.0, 0.0)
        };
        var controls = new AmpControls(0.7, 0.3, 0.6, 0.8, 0.6, true);
        var stage = new ToneStackStage(design, Rate);
        stage.SetSourceResistance(sourceResistance);
        stage.Update(controls);
        var (circuit, wiper, driven) = AmpReferences.ToneStackStage(
            design,
            controls,
            sourceResistance,
            t => rest + Drive(amplitude, t)
        );

        // Operating point first.
        stage.Reset(rest);
        var reference = new ReferenceSimulator(circuit);
        reference.OperatingPoint();
        output.WriteLine($"{name}: driven node {stage.RestDriven:0.00} V (reference {reference.Voltage(driven):0.00} V)");
        Assert.Equal(reference.Voltage(driven), stage.RestDriven, 0.002 * reference.Voltage(driven));

        var errorDb = CompareInTime(name, x => stage.Tick(rest + x), stage.Newton, circuit, wiper, amplitude);
        Assert.True(errorDb < -40, $"{name}: {errorDb:0.0} dB from the reference");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Long_tail_pair_table_matches_the_reference_dc_sweep(int index) {
        var design = AmpDesign.All[index].Power.PhaseInverter;
        var pair = LongTailPair.For(design);
        output.WriteLine(
            $"{AmpDesign.All[index].Name} inverter: grids {pair.RestGrid:0.00} V, cathode {pair.RestCathode:0.00} V, plates " +
            $"{pair.RestPlateA:0.00}/{pair.RestPlateB:0.00} V, output impedance {pair.OutputImpedance.Zaa / 1e3:0.0}k, " +
            $"{pair.OutputImpedance.Zab / 1e3:0.0}k, " +
            $"{pair.OutputImpedance.Zba / 1e3:0.0}k, {pair.OutputImpedance.Zbb / 1e3:0.0}k"
        );
        var worst = 0.0;
        foreach (var vd in new[] { -20.0, -6, -3, -1, -0.3, 0, 0.3, 1, 2 }) {
            var (circuit, a, b) = AmpReferences.LongTailPair(design, pair.RestGrid + vd, pair.RestGrid);
            var reference = new ReferenceSimulator(circuit);
            reference.OperatingPoint(40);
            var (ea, eb) = pair.Evaluate(vd, out _, out _);
            var errorA = pair.RestPlateA + ea - reference.Voltage(a);
            var errorB = pair.RestPlateB + eb - reference.Voltage(b);
            output.WriteLine(
                $"  vd {vd,5:0.0}: plates {pair.RestPlateA + ea:0.00}/{pair.RestPlateB + eb:0.00} V, reference " +
                $"{reference.Voltage(a):0.00}/{reference.Voltage(b):0.00} V"
            );
            if (vd >= -6) {
                worst = Math.Max(worst, Math.Max(Math.Abs(errorA), Math.Abs(errorB)));
            }
        }

        // The table leaves out grid current: it flows into the first grid beyond vd ≈ +2 V, and into the second when
        // the first side is cut off hard (vd ≈ -20 V with the 12AT7's high tail current).
        Assert.True(worst < 0.05, $"{worst:0.000} V from the reference");

        // The interpolation against the exact solution, between table points.
        var triode = new Triode(design.Tube);
        var interpolation = 0.0;
        for (var vd = -30.0; vd < 30; vd += 0.0731) {
            var (ia, ib, _) = LongTailPair.Solve(triode, design, pair.RestGrid + vd, pair.RestGrid);
            var (ea, eb) = pair.Evaluate(vd, out _, out _);
            interpolation = Math.Max(interpolation, Math.Abs(design.Supply - ia * design.PlateResistorA - pair.RestPlateA - ea));
            interpolation = Math.Max(interpolation, Math.Abs(design.Supply - ib * design.PlateResistorB - pair.RestPlateB - eb));
        }

        output.WriteLine($"  interpolation error {interpolation:E2} V");
        Assert.True(interpolation < 1e-3, $"{interpolation:E2} V");
    }

    [Theory]
    [InlineData(1, 0.4, 0.5)]
    [InlineData(0, 0.6, 0.8)]
    [InlineData(2, 0.15, 0.5)]
    public void Power_amps_match_the_reference_in_the_time_domain(int index, double amplitude, double presence) {
        // The design with a stiff supply: the reference has no sag.
        var design = AmpDesign.All[index].Power with { PowerSupply = new(1e-3, 1, 1e-3, 1, 1e3, 1, 0) };
        var power = new PowerAmp(design, Rate);
        power.Configure(new(presence, 0, 0, 0.5, 0.5, 0.5));
        power.Reset();
        var (circuit, speaker) = AmpReferences.PowerAmp(
            design,
            power.Supply,
            power.ScreenSupply,
            power.GridBias,
            power.CathodeResistor,
            presence,
            0,
            t => Drive(amplitude, t)
        );
        output.WriteLine(
            $"{design.Name}: B+ {power.Supply:0.0} V, screen {power.ScreenSupply:0.0} V, grid bias {power.GridBias:0.00} V, " +
            $"cathode {power.CathodeVoltage:0.00} V, idle {power.PlateCurrents.A * 1e3:0.0} mA per tube"
        );
        var errorDb = CompareInTime(design.Name, power.Tick, power.Newton, circuit, speaker, amplitude, 8);
        Assert.True(errorDb < -30, $"{design.Name}: {errorDb:0.0} dB from the reference");
    }

    static TriodeStageDesign TriodeDesign(string name) => name switch {
        "textbook" => Textbook,
        "blackface V2A" => (TriodeStageDesign)AmpDesign.Blackface.Preamp[1],
        "plexi V1A" => (TriodeStageDesign)AmpDesign.Plexi.Preamp[0],
        "plexi V2A" => (TriodeStageDesign)AmpDesign.Plexi.Preamp[1],
        _ => (TriodeStageDesign)AmpDesign.AC30.Preamp[0]
    };

    /// <summary>1 kHz with a 220 Hz component, so the stage sees a changing slope as well as a level.</summary>
    static double Drive(double amplitude, double t) =>
        amplitude * (0.7 * Math.Sin(2 * Math.PI * 1_000 * t) + 0.3 * Math.Sin(2 * Math.PI * 220 * t));

    /// <summary>
    ///     Runs the WDF stage and the reference (from its DC operating point) for 20 ms and returns the error energy
    ///     relative to the output's AC energy, in dB.
    /// </summary>
    double CompareInTime(
        string name,
        Func<double, double> tick,
        NewtonSolver? newton,
        ReferenceCircuit circuit,
        int node,
        double amplitude,
        int oversampling = Oversampling
    ) {
        var reference = new ReferenceSimulator(circuit);
        reference.OperatingPoint(40);
        const int Samples = 1_920; // 20 ms
        var wdf = new double[Samples];
        var expected = new double[Samples];
        var h = 1 / (Rate * oversampling);
        double mean = 0;
        for (var n = 0; n < Samples; n++) {
            var t = n / Rate;
            for (var s = 1; s <= oversampling && n > 0; s++) {
                reference.Step(t - (oversampling - s) * h, h);
            }

            wdf[n] = tick(Drive(amplitude, t));
            expected[n] = reference.Voltage(node);
            mean += expected[n] / Samples;
        }

        double error = 0, energy = 0, peak = 0;
        for (var n = 0; n < Samples; n++) {
            error += (wdf[n] - expected[n]) * (wdf[n] - expected[n]);
            energy += (expected[n] - mean) * (expected[n] - mean);
            peak = Math.Max(peak, Math.Abs(expected[n] - mean));
        }

        var errorDb = 10 * Math.Log10(error / energy);
        output.WriteLine(
            $"{name}: output {peak:0.000} V peak, error {errorDb:0.0} dB, Newton {newton?.MeanIterations:0.00} iterations " +
            $"per sample (unconverged {newton?.Unconverged}), reference failures {reference.NewtonFailures}"
        );
        Assert.Equal(0, reference.NewtonFailures);
        return errorDb;
    }
}
