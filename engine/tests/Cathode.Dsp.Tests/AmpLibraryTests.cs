using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Tests.Circuits;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The stage-library extensions of Phase 7 (docs/06 §5.1) against the reference MNA simulator, like the first
///     stages: the new tube tables, the general network stage (passive networks with pots, switches and inductors; a
///     cathode follower not driving a stack; an EF86 pentode stage; a diode clipper; a cold clipper with its plate
///     bypass), the cathodyne, grid current into the phase inverter, and the power-section variants (single-ended,
///     ultralinear, triode strapping, Simul-Class, separate cathodes, a cathodyne-driven pair).
/// </summary>
public partial class AmpStageTests {
    public static TheoryData<string> NewTubes => ["12AY7", "5751", "6V6GT", "KT66", "KT88", "6550"];

    [Theory]
    [MemberData(nameof(NewTubes))]
    public void New_tube_tables_match_koren_to_1e_6(string name) {
        var random = new Random(7);
        var worst = 0.0;
        if (name is "12AY7" or "5751") {
            var model = name == "12AY7" ? TriodeModel.TwelveAY7 : TriodeModel.FiveSevenFiveOne;
            var exact = new Triode(model);
            var fast = new FastTriode(model);
            Span<double> v = stackalloc double[2];
            Span<double> i1 = stackalloc double[2];
            Span<double> i2 = stackalloc double[2];
            Span<double> j1 = stackalloc double[4];
            Span<double> j2 = stackalloc double[4];
            for (var n = 0; n < 100_000; n++) {
                v[0] = -10 + 12 * random.NextDouble();
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
        } else {
            var model = name switch {
                "6V6GT" => PentodeModel.SixV6GT,
                "KT66" => PentodeModel.KT66,
                "KT88" => PentodeModel.KT88,
                _ => PentodeModel.SixtyFiveFifty
            };
            var tables = PentodeTables.For(model);
            var exact = new Pentode(model);
            Span<double> v = stackalloc double[3];
            Span<double> i = stackalloc double[3];
            Span<double> j = stackalloc double[9];
            for (var n = 0; n < 100_000; n++) {
                v[0] = -80 + 85 * random.NextDouble();
                v[1] = 200 + 300 * random.NextDouble();
                v[2] = 900 * random.NextDouble();
                j.Clear();
                exact.Evaluate(v, i, j, 3);
                var plate = tables.PlateCurrent(v[0], v[1], v[2], out var gridSlope, out var screenSlope, out var plateSlope);
                var screen = tables.ScreenCurrent(v[0], v[1], out var screenGridSlope);
                worst = Math.Max(worst, Math.Abs(plate - i[2]) / (i[2] + 1e-9));
                worst = Math.Max(worst, Math.Abs(screen - i[1]) / (i[1] + 1e-9));
                worst = Math.Max(worst, Math.Abs(gridSlope - j[6]) / (Math.Abs(j[6]) + 1e-8));
                worst = Math.Max(worst, Math.Abs(screenSlope - j[7]) / (Math.Abs(j[7]) + 1e-8));
                worst = Math.Max(worst, Math.Abs(plateSlope - j[8]) / (Math.Abs(j[8]) + 1e-8));
                worst = Math.Max(worst, Math.Abs(screenGridSlope - j[3]) / (Math.Abs(j[3]) + 1e-8));
            }
        }

        output.WriteLine($"{name}: worst relative error {worst:E2}");
        Assert.True(worst < 1e-6, $"{worst:E2}");
    }

    [Fact]
    public void Fitted_tubes_meet_their_datasheet_points() {
        static (double Current, double Slope) Triode(TriodeModel m, double vg, double vp) {
            var t = new Triode(m);
            var low = OperatingPoint.PlateCurrent(t, vg - 0.01, vp);
            var high = OperatingPoint.PlateCurrent(t, vg + 0.01, vp);
            return (OperatingPoint.PlateCurrent(t, vg, vp), (high - low) / 0.02);
        }

        var twelveAY7 = Triode(TriodeModel.TwelveAY7, -4, 250);
        var fiveSevenFiveOne = Triode(TriodeModel.FiveSevenFiveOne, -3, 250);
        (string Name, double Current, double Expected, double Slope, double ExpectedSlope)[] points = [
            ("12AY7 250 V -4 V", twelveAY7.Current, 3e-3, twelveAY7.Slope, 1.75e-3),
            ("5751 250 V -3 V", fiveSevenFiveOne.Current, 1e-3, fiveSevenFiveOne.Slope, 1.2e-3),
            ("6V6GT 250/250 V -12.5 V", KorenPentode.Plate(PentodeModel.SixV6GT, -12.5, 250, 250), 45e-3, 0, 0),
            ("6V6GT screen", KorenPentode.Screen(PentodeModel.SixV6GT, -12.5, 250), 4.5e-3, 0, 0),
            ("KT66 250/250 V -15 V", KorenPentode.Plate(PentodeModel.KT66, -15, 250, 250), 85e-3, 0, 0),
            ("EF86 250/140 V -2 V", KorenPentode.Plate(PentodeModel.EF86, -2, 140, 250), 3e-3, 0, 0),
            ("EF86 screen", KorenPentode.Screen(PentodeModel.EF86, -2, 140), 0.6e-3, 0, 0)
        ];
        foreach (var (name, current, expected, slope, expectedSlope) in points) {
            output.WriteLine($"{name}: {current * 1e3:0.000} mA (datasheet {expected * 1e3:0.000})" +
                (expectedSlope > 0 ? $", {slope * 1e3:0.000} mS ({expectedSlope * 1e3:0.000})" : ""));
            Assert.InRange(current, 0.98 * expected, 1.02 * expected);
            if (expectedSlope > 0) {
                Assert.InRange(slope, 0.95 * expectedSlope, 1.05 * expectedSlope);
            }
        }
    }

    [Theory]
    [InlineData("plexi V2A", 8.0)]
    [InlineData("ac30 V1A", 0.5)]
    public void Network_stage_reproduces_the_triode_stage(string name, double amplitude) {
        var design = TriodeDesign(name);
        var controls = new AmpControls(0.6, 0.5, 0.5, 0.5, 1, true);
        var stage = new TriodeStage(design, Rate);
        var network = design.ToNetwork().Create(Rate);
        foreach (var s in new AmpStage[] { stage, network }) {
            s.SetSourceResistance(30e3);
            s.Update(controls);
            s.Reset(0);
        }

        output.WriteLine(
            $"{name}: plate {stage.RestOutput:0.000} V / {network.RestOutput:0.000} V, output resistance " +
            $"{stage.OutputResistance / 1e3:0.00}k / {network.OutputResistance / 1e3:0.00}k"
        );
        Assert.Equal(stage.RestOutput, network.RestOutput, 1e-6 * stage.RestOutput);
        // The network stage reads each capacitor at its 1 kHz reactance; the triode stage takes a bypass cap as a short.
        Assert.Equal(stage.OutputResistance, network.OutputResistance, 0.3 * stage.OutputResistance);

        double error = 0, energy = 0;
        for (var n = 0; n < 4_800; n++) {
            var x = Drive(amplitude, n / Rate);
            var (a, b) = (stage.Tick(x), network.Tick(x));
            error += (a - b) * (a - b);
            energy += (a - stage.RestOutput) * (a - stage.RestOutput);
        }

        var db = 10 * Math.Log10(error / energy);
        output.WriteLine($"  difference {db:0.0} dB");
        Assert.True(db < -90, $"{db:0.0} dB");
    }

    /// <summary>
    ///     A passive tone network that is not a TMB stack: a rheostat (bass) into a Big-Muff-style low-pass/high-pass
    ///     pair blended by a pot (treble), a switched cap (switch 1) on the low-pass side, into a 1M load.
    /// </summary>
    static NetworkStageDesign BlendNetwork() {
        var net = new StageNetlist();
        var (series, low, high) = (net.Node(), net.Node(), net.Node());
        net.Rheostat(net.Input, series, new(250e3, PotTaper.Audio), PotRole.Bass);
        net.Resistor(series, low, 22e3);
        net.Capacitor(low, StageNetlist.Ground, 10e-9, CapacitorRole.Stack);
        net.SwitchedCapacitor(low, StageNetlist.Ground, 22e-9, SwitchRole.Switch1);
        net.Capacitor(net.Input, high, 3.9e-9, CapacitorRole.Stack);
        net.Resistor(high, StageNetlist.Ground, 22e3);
        var wiper = net.Pot(high, low, new(100e3, PotTaper.Linear), PotRole.Treble);
        net.Resistor(wiper, StageNetlist.Ground, 1e6);
        net.Output = wiper;
        return new("blend", net);
    }

    /// <summary>A Mark-style EQ band: a series LC to ground through a slider (control 1), across a 10k-fed node.</summary>
    static NetworkStageDesign ResonantNetwork() {
        var net = new StageNetlist();
        var (node, coil, cap) = (net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, node, 10e3);
        net.Inductor(node, coil, 0.39);
        net.Capacitor(coil, cap, 0.47e-6);
        net.Rheostat(cap, StageNetlist.Ground, new(50e3, PotTaper.Linear), PotRole.Control1);
        net.Resistor(node, StageNetlist.Ground, 100e3);
        net.Output = node;
        return new("resonant", net);
    }

    public static TheoryData<string, double, double, bool> NetworkSettings => new() {
        { "blend", 0.5, 0.5, false },
        { "blend", 0.1, 0.9, true },
        { "blend", 0.9, 0.2, true },
        { "resonant", 0.0, 0.5, false },
        { "resonant", 0.3, 0.5, false },
        { "resonant", 1.0, 0.5, false }
    };

    [Theory]
    [MemberData(nameof(NetworkSettings))]
    public void Passive_networks_match_the_reference_response(string name, double knob, double treble, bool switched) {
        const int Length = 65_536;
        double[] frequencies = [30, 60, 100, 200, 400, 700, 1_000, 2_000, 4_000, 8_000, 15_000];
        var design = name == "blend" ? BlendNetwork() : ResonantNetwork();
        var controls = new AmpControls(0.5, knob, 0.5, treble, 1, false, knob, Switch1: switched);
        var settings = new PreampSettings(StackFrequency: 1.3);
        var stage = design.Create(Rate);
        stage.SetSourceResistance(1e3);
        stage.Apply(settings);
        stage.Update(new(0.2, 0.7, 0.5, 0.3, 1, false)); // reach the setting by a recompute, as a knob would
        stage.Update(controls);
        stage.Reset(0);
        var impulse = new float[Length];
        for (var i = 0; i < Length; i++) {
            impulse[i] = (float)stage.Tick(i == 0 ? 1 : 0);
        }

        var (circuit, probe) = AmpReferences.Network(design, controls, settings, 1e3, static _ => 0);
        var reference = new ReferenceSimulator(circuit);
        var worst = 0.0;
        foreach (var f in frequencies) {
            var warped = 2 * Rate * Math.Tan(Math.PI * f / Rate);
            var expected = Analysis.ToDb(reference.Ac(new(0, warped), 0, probe).Magnitude);
            var actual = Analysis.ToDb(Analysis.MagnitudeAt(impulse, f, Rate));
            worst = Math.Max(worst, Math.Abs(actual - expected));
        }

        output.WriteLine($"{name} knob {knob} treble {treble} switch {switched}: worst {worst:0.000000} dB");
        Assert.True(worst < 0.01, $"{worst:0.000} dB from the reference");
    }

    /// <summary>The SLO-100's third stage: a cold clipper (39k unbypassed) with a 1 nF plate bypass, 220k stopper, 330k leak.</summary>
    static readonly TriodeStageDesign ColdClipper = new(
        "cold",
        TriodeModel.Ecc83,
        360,
        100e3,
        39e3,
        InputResistance: 220e3,
        CouplingCap: 22e-9,
        GridLeak: 330e3,
        PlateBypass: 1e-9
    );

    /// <summary>A cathode follower not driving a stack, DC-coupled to the previous plate: 100k to ground, AC out to 1M.</summary>
    static NetworkStageDesign Follower() {
        var net = new StageNetlist();
        var (supply, cathode, output) = (net.Node(), net.Node(), net.Node());
        net.Supply(supply, 330);
        net.Resistor(cathode, StageNetlist.Ground, 100e3);
        net.Capacitor(cathode, output, 22e-9);
        net.Resistor(output, StageNetlist.Ground, 1e6);
        net.Triode(supply, net.Input, cathode, TriodeModel.Ecc83);
        net.Output = output;
        return new("follower", net);
    }

    /// <summary>The AC15's EF86 stage: 220k plate, 1M screen resistor with 0.1 µF bypass, 2.2k with 25 µF, 1M grid leak.</summary>
    static NetworkStageDesign Ef86() {
        var net = new StageNetlist();
        var (supply, plate, screen, cathode) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(supply, 300);
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Resistor(supply, plate, 220e3);
        net.Resistor(supply, screen, 1e6);
        net.Capacitor(screen, StageNetlist.Ground, 0.1e-6);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.2e3);
        net.Capacitor(cathode, StageNetlist.Ground, 25e-6);
        net.Pentode(plate, screen, net.Input, cathode, PentodeModel.EF86);
        net.Output = plate;
        return new("EF86", net, 68e3);
    }

    /// <summary>A diode clipper: 10k in, a 1N4007 each way and 1 nF to ground.</summary>
    static NetworkStageDesign Clipper() {
        var net = new StageNetlist();
        var node = net.Node();
        net.Resistor(net.Input, node, 10e3);
        net.Diodes(node, StageNetlist.Ground, DiodeModel.Rectifier, 1, 1);
        net.Capacitor(node, StageNetlist.Ground, 1e-9);
        net.Output = node;
        return new("clipper", net);
    }

    [Theory]
    [InlineData("cold clipper", 2.0, 0.0, -40.0)]
    [InlineData("cold clipper", 30.0, 0.0, -40.0)]
    [InlineData("follower", 10.0, 180.0, -40.0)]
    [InlineData("follower", 120.0, 180.0, -30.0)]
    [InlineData("EF86", 0.3, 0.0, -40.0)]
    [InlineData("EF86", 3.0, 0.0, -35.0)]
    [InlineData("clipper", 5.0, 0.0, -40.0)]
    public void Network_stages_match_the_reference_in_the_time_domain(
        string name,
        double amplitude,
        double rest,
        double limitDb
    ) {
        var design = name switch {
            "cold clipper" => ColdClipper.ToNetwork(),
            "follower" => Follower(),
            "EF86" => Ef86(),
            _ => Clipper()
        };
        const double SourceResistance = 30e3;
        var controls = new AmpControls(0.6, 0.5, 0.5, 0.5, 1, true);
        var stage = name == "cold clipper" ? ColdClipper.Create(Rate) : design.Create(Rate);
        stage.SetSourceResistance(SourceResistance);
        stage.Update(controls);
        stage.Reset(rest);
        var (circuit, node) = AmpReferences.Network(
            design,
            controls,
            PreampSettings.Stock,
            SourceResistance,
            t => rest + Drive(amplitude, t)
        );
        var reference = new ReferenceSimulator(circuit);
        reference.OperatingPoint(40);
        output.WriteLine(
            $"{name}: rest output {stage.RestOutput:0.000} V (reference {reference.Voltage(node):0.000} V), output " +
            $"resistance {stage.OutputResistance / 1e3:0.0}k"
        );
        Assert.Equal(reference.Voltage(node), stage.RestOutput, 0.002 * Math.Abs(reference.Voltage(node)) + 1e-6);

        var errorDb = CompareInTime(name, x => stage.Tick(rest + x), stage.Newton, circuit, node, amplitude);
        Assert.True(errorDb < limitDb, $"{name}: {errorDb:0.0} dB from the reference");
    }

    [Fact]
    public void Cathodyne_table_matches_the_reference_dc_sweep() {
        var design = new CathodyneDesign(TriodeModel.Ecc83, 330, 56e3, 56e3, 1.5e3);
        var inverter = (Cathodyne)PhaseInverter.For(design);
        output.WriteLine(
            $"cathodyne: grid {inverter.RestGrid:0.00} V, cathode {inverter.RestCathode:0.00} V, plate " +
            $"{inverter.RestPlateA:0.00} V, junction {inverter.RestPlateB:0.00} V, output impedance " +
            $"{inverter.OutputImpedance.Zaa / 1e3:0.0}k, {inverter.OutputImpedance.Zab / 1e3:0.0}k, " +
            $"{inverter.OutputImpedance.Zba / 1e3:0.00}k, {inverter.OutputImpedance.Zbb / 1e3:0.00}k"
        );
        var worst = 0.0;
        foreach (var vd in new[] { -40.0, -20, -6, -1, 0, 1, 6, 20, 40 }) {
            var (circuit, a, b) = AmpReferences.Cathodyne(design, inverter.RestGrid + vd);
            var reference = new ReferenceSimulator(circuit);
            reference.OperatingPoint(40);
            var (ea, eb) = inverter.Evaluate(vd, out _, out _);
            output.WriteLine(
                $"  vd {vd,5:0.0}: plate {inverter.RestPlateA + ea:0.00} V, junction {inverter.RestPlateB + eb:0.00} V, " +
                $"reference {reference.Voltage(a):0.00}/{reference.Voltage(b):0.00} V"
            );

            // The table leaves out grid current, which flows once the plate has fallen to meet the cathode.
            if (inverter.GridToCathode(vd, out _) < -0.5) {
                worst = Math.Max(worst, Math.Abs(inverter.RestPlateA + ea - reference.Voltage(a)));
                worst = Math.Max(worst, Math.Abs(inverter.RestPlateB + eb - reference.Voltage(b)));
            }
        }

        Assert.True(worst < 0.05, $"{worst:0.000} V from the reference");

        // A cathodyne's outputs are nearly equal and opposite; the plate output's impedance is the higher.
        var (pa, pb) = inverter.Evaluate(1, out _, out _);
        Assert.InRange(-pa / pb, 0.9, 1.1);
        Assert.True(inverter.OutputImpedance.Zaa > 5 * inverter.OutputImpedance.Zbb);
    }

    public static TheoryData<string> PowerVariants => [
        "single-ended 6V6",
        "ultralinear EL34",
        "triode switch",
        "simul-class",
        "separate cathodes",
        "cathodyne 6V6",
        "inverter grid current",
        "inverter grid current with feedback"
    ];

    [Theory]
    [MemberData(nameof(PowerVariants))]
    public void Power_amp_variants_match_the_reference_in_the_time_domain(string name) {
        var stiff = new PowerSupplyDesign(1e-3, 1, 1e-3, 1, 1e3, 1, 0);
        var plexi = Plexi.Design.Power with { PowerSupply = stiff };
        var sixV6 = AC30.Design.Power with {
            Name = "2 x 6V6",
            Tube = PentodeModel.SixV6GT,
            TubesPerSide = 1,
            Supply = 360,
            IdleCurrent = 0.035,
            Transformer = new(8e3),
            RatedPower = 15,
            PowerSupply = stiff,
            InverterInput = null
        };
        var (design, amplitude, sourceResistance, triode) = name switch {
            "single-ended 6V6" => (sixV6 with {
                Topology = PowerTopology.SingleEnded,
                TubesPerSide = 1,
                Transformer = new(5e3),
                CathodeBypass = 25e-6,
                RatedPower = 5
            }, 6.0, 40e3, false),
            "ultralinear EL34" => (plexi with { UltralinearTap = 0.43, ScreenResistor = 1 }, 0.4, 0.0, false),
            "triode switch" => (plexi with { TriodeSwitch = SwitchRole.Switch1, ScreenResistor = 1 }, 0.6, 0.0, true),
            "simul-class" => (Blackface.Design.Power with {
                Strapped = PentodeModel.EL34,
                StrappedPerSide = 1,
                PowerSupply = stiff
            }, 0.6, 0.0, false),
            "separate cathodes" => (AC30.Design.Power with {
                SeparateCathodes = true,
                PowerSupply = stiff,
                InverterInput = null
            }, 0.15, 0.0, false),
            "cathodyne 6V6" => (sixV6 with {
                PhaseInverter = new CathodyneDesign(TriodeModel.Ecc83, 330, 56e3, 56e3, 1.5e3),
                CutCap = 0
            }, 2.0, 0.0, false),
            "inverter grid current" => (plexi with { InverterInput = new(22e-9, 1e6), HasFeedback = false }, 7.0, 30e3, false),
            _ => (plexi with { InverterInput = new(22e-9, 1e6) }, 7.0, 30e3, false)
        };
        var power = new PowerAmp(design, Rate);
        power.SetSourceResistance(Math.Max(sourceResistance, 1));
        power.Configure(new PowerControls(0.5, 0, 0, 0.5, 0.5, 0.5));
        power.Apply(PowerSettings.Stock with { Triode = triode });
        power.Reset();
        var (circuit, speaker, grid) = AmpReferences.PowerAmp(
            design,
            power.Supply,
            power.ScreenSupply,
            power.GridBias,
            power.CathodeResistor,
            0.5,
            0,
            t => Drive(amplitude, t),
            sourceResistance,
            triode
        );
        output.WriteLine(
            $"{name}: B+ {power.Supply:0.0} V, grid bias {power.GridBias:0.00} V, cathode {power.CathodeVoltage:0.00} V, " +
            $"idle {power.PlateCurrents.A * 1e3:0.0} mA per tube"
        );
        var errorDb = CompareInTime(name, power.Tick, power.Newton, circuit, speaker, amplitude, 8);
        if (design.InverterInput is null) {
            Assert.True(errorDb < -30, $"{name}: {errorDb:0.0} dB from the reference");
            return;
        }

        // Driven well into the inverter's grid current the power tubes block hard too, and the output's error says more about
        // that than about the inverter; its grid shows the input network: coupling cap, bootstrapped leak, grid conduction.
        var rest = power.PhaseInverter.RestGrid;
        var fresh = new PowerAmp(design, Rate);
        fresh.SetSourceResistance(sourceResistance);
        fresh.Configure(new PowerControls(0.5, 0, 0, 0.5, 0.5, 0.5));
        fresh.Reset();
        var gridDb = CompareInTime(
            "inverter grid",
            x => {
                fresh.Tick(x);
                return rest + fresh.InverterGrid;
            },
            null,
            circuit,
            grid,
            amplitude,
            8
        );
        var unloadedDb = CompareInTime("inverter grid, unloaded", x => rest + x, null, circuit, grid, amplitude, 8);
        Assert.True(gridDb < -35, $"inverter grid: {gridDb:0.0} dB from the reference");
        Assert.True(gridDb < unloadedDb - 6, $"{gridDb:0.0} dB with the input network, {unloadedDb:0.0} dB without");
    }
}
