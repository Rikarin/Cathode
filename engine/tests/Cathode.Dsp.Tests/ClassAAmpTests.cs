using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Tests.Circuits;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The class-A family's stages (stream F3: AC30, AC15, DC-30) against the reference simulator: the top boost's gain
///     stage and its cathode follower with the treble/bass network, the EF86 channels with the brilliance switch and the
///     six-way tone rotary, a pair of paralleled triodes, the power amps with the cut and a master across the inverter's
///     outputs; and the operating points against the voltages on the schematics.
/// </summary>
public partial class AmpStageTests {
    /// <summary>The stages of a model's channel: <paramref name="on" /> picks a channel switch's other channel.</summary>
    static IReadOnlyList<AmpStageDesign> Chain(AmpDesign design, bool on) =>
        [..design.Preamp.SelectMany(s => s is ChannelSwitchStageDesign c ? on ? c.On : c.Off : [s])];

    public static TheoryData<string, int, double, double, double> ClassAStages => new() {
        // name, stage index in the chain, amplitude, knob (gain/treble/tone), bass
        { "AC30 top boost V1A", 1, 0.3, 0.6, 0.5 },
        { "AC30 top boost V1A", 1, 3.0, 0.9, 0.5 },
        { "AC30 top boost V1B", 2, 10.0, 0.8, 0.3 },
        { "AC30 top boost V1B", 2, 60.0, 0.2, 0.9 },
        { "AC30 normal volume", 1, 10.0, 0.5, 0.5 },
        { "AC15 EF86", 0, 0.05, 0.6, 0.5 },
        { "AC15 EF86", 0, 0.5, 1.0, 0.5 },
        { "DC-30 V1", 0, 0.3, 0.5, 0.5 },
        { "DC-30 V1", 0, 3.0, 0.5, 0.5 },
        { "DC-30 V2B", 2, 40.0, 0.7, 0.2 },
        { "DC-30 EF86", 0, 0.05, 0.0, 0.5 },
        { "DC-30 EF86", 0, 0.25, 1.0, 0.5 }
    };

    [Theory]
    [MemberData(nameof(ClassAStages))]
    public void Class_a_stages_match_the_reference_in_the_time_domain(
        string name,
        int index,
        double amplitude,
        double knob,
        double bass
    ) {
        var (design, on) = name switch {
            _ when name.StartsWith("AC30 normal", StringComparison.Ordinal) => (AC30.Design, true),
            _ when name.StartsWith("AC30", StringComparison.Ordinal) => (AC30.Design, false),
            _ when name.StartsWith("AC15", StringComparison.Ordinal) => (AC15.Design, false),
            "DC-30 EF86" => (DC30.Design, true),
            _ => (DC30.Design, false)
        };
        var chain = Chain(design, on);
        var ef86 = name.Contains("EF86", StringComparison.Ordinal);
        var controls = new AmpControls(ef86 ? 0.6 : knob, bass, 0.5, knob, 1, ef86, knob);

        // The stage's source: the previous stage at its operating point (its rest output and output resistance).
        var (rest, sourceResistance) = (0.0, 30e3);
        if (index > 0) {
            var previous = chain[index - 1].Create(Rate);
            previous.Update(controls);
            previous.SetSourceResistance(30e3);
            previous.Reset(0);
            (rest, sourceResistance) = (previous.RestOutput, previous.OutputResistance);
        }

        var stageDesign = (NetworkStageDesign)chain[index];
        var stage = stageDesign.Create(Rate);
        stage.SetSourceResistance(sourceResistance);
        stage.Update(controls);
        stage.Reset(rest);
        var (circuit, node) = AmpReferences.Network(
            stageDesign,
            controls,
            PreampSettings.Stock,
            sourceResistance,
            t => rest + Drive(amplitude, t)
        );
        var reference = new ReferenceSimulator(circuit);
        reference.OperatingPoint(40);
        output.WriteLine(
            $"{name}: rest output {stage.RestOutput:0.000} V (reference {reference.Voltage(node):0.000} V), source " +
            $"{rest:0.00} V behind {sourceResistance / 1e3:0.0}k, output resistance {stage.OutputResistance / 1e3:0.0}k"
        );
        Assert.Equal(reference.Voltage(node), stage.RestOutput, 0.002 * Math.Abs(reference.Voltage(node)) + 1e-6);

        var errorDb = CompareInTime(name, x => stage.Tick(rest + x), stage.Newton, circuit, node, amplitude);
        Assert.True(errorDb < -40, $"{name}: {errorDb:0.0} dB from the reference");
    }

    [Theory]
    [InlineData("AC15", 0.03, 0.0, 1.0)]
    [InlineData("AC15", 0.12, 0.6, 1.0)]
    [InlineData("DC-30", 0.08, 0.3, 1.0)]
    [InlineData("DC-30", 0.15, 0.0, 0.3)]
    public void Class_a_power_amps_match_the_reference_in_the_time_domain(
        string name,
        double amplitude,
        double cut,
        double master
    ) {
        var stiff = new PowerSupplyDesign(1e-3, 1, 1e-3, 1, 1e3, 1, 0);
        var design = (name == "AC15" ? AC15.Design : DC30.Design).Power with { PowerSupply = stiff };
        const double SourceResistance = 60e3;
        var power = new PowerAmp(design, Rate);
        power.SetSourceResistance(SourceResistance);
        power.Configure(new(0.5, 0, cut, 0.5, 0.5, 0.5, master));
        power.Reset();
        var (circuit, speaker, _) = AmpReferences.PowerAmp(
            design,
            power.Supply,
            power.ScreenSupply,
            power.GridBias,
            power.CathodeResistor,
            0.5,
            cut,
            t => Drive(amplitude, t),
            SourceResistance,
            master: master
        );
        output.WriteLine(
            $"{name}: B+ {power.Supply:0.0} V, cathode {power.CathodeVoltage:0.00} V across {power.CathodeResistor:0} Ω, " +
            $"idle {power.PlateCurrents.A * 1e3:0.0} mA per tube, inverter grids {power.PhaseInverter.RestGrid:0.00} V"
        );
        var errorDb = CompareInTime(name, power.Tick, power.Newton, circuit, speaker, amplitude, 8);
        Assert.True(errorDb < -35, $"{name}: {errorDb:0.0} dB from the reference");
    }

    /// <summary>
    ///     Operating points against the schematics' voltages: Vox OS/010 and OS/065 (AC30), AmpBooks' AC15 and Matchless
    ///     EF86 pages. The 12AX7's Koren model runs about 15 % less current than Vox's tubes did, so their plates sit
    ///     up to 10 % high; the EF86 is fitted to the AC15's stage, so it lands within 2 %.
    /// </summary>
    [Fact]
    public void Class_a_operating_points_match_the_schematics() {
        var controls = new AmpControls(0.5, 0.5, 0.5, 0.5, 1, false);
        (string Name, double Volts, double Expected, double Tolerance)[] points = [
            ("AC30 V1 plate", Rest(AC30.Design, false, 0, 1), 170, 0.08),
            ("AC30 top boost plate", Rest(AC30.Design, false, 1, 3), 180, 0.10),
            ("AC30 top boost cathode", Rest(AC30.Design, false, 1, 4), 1.65, 0.20),
            ("AC30 follower cathode", Rest(AC30.Design, false, 2, 3), 180, 0.08),
            ("AC30 inverter grids", LongTailPair.For((LongTailPairDesign)AC30.Design.Power.PhaseInverter).RestGrid, 55, 0.05),
            ("AC30 inverter plates", LongTailPair.For((LongTailPairDesign)AC30.Design.Power.PhaseInverter).RestPlateA, 230, 0.03),
            ("AC15 EF86 plate", Rest(AC15.Design, false, 0, 3), 90, 0.02),
            ("AC15 EF86 screen", Rest(AC15.Design, false, 0, 4), 105, 0.02),
            ("AC15 EF86 cathode", Rest(AC15.Design, false, 0, 5), 2.4, 0.02),
            ("AC15 inverter plates", LongTailPair.For((LongTailPairDesign)AC15.Design.Power.PhaseInverter).RestPlateA, 220, 0.06),
            ("DC-30 EF86 plate", Rest(DC30.Design, true, 0, 3), 125, 0.08),
            ("DC-30 EF86 screen", Rest(DC30.Design, true, 0, 4), 60, 0.15)
        ];
        foreach (var (name, volts, expected, tolerance) in points) {
            output.WriteLine($"{name}: {volts:0.00} V (schematic {expected} V)");
        }

        foreach (var (name, volts, expected, tolerance) in points) {
            Assert.True(Math.Abs(volts - expected) <= tolerance * expected, $"{name}: {volts:0.00} V against {expected} V");
        }

        double Rest(AmpDesign design, bool on, int index, int node) {
            var chain = Chain(design, on);
            var (rest, resistance) = (0.0, 0.0);
            for (var i = 0; i <= index; i++) {
                var s = chain[i].Create(Rate);
                s.Update(controls);
                s.SetSourceResistance(resistance);
                s.Reset(rest);
                if (i == index) {
                    return s switch {
                        TriodeStage t => node == 1 ? t.RestOutput : t.RestCathode,
                        NetworkStage<FastTriode> n => n.RestVoltage(node),
                        NetworkStage<Wdf.Pentode> n => n.RestVoltage(node),
                        _ => double.NaN
                    };
                }

                (rest, resistance) = (s.RestOutput, s.OutputResistance);
            }

            return double.NaN;
        }
    }

    [Fact]
    public void Channel_switch_plays_one_channel_and_settles_the_other() {
        var stage = (ChannelSwitchStage)AC30.Design.Preamp[1].Create(Rate);
        stage.SetSourceResistance(50e3);
        stage.Update(new(0.5, 0.5, 0.5, 0.5, 1, false));
        stage.Reset(180);
        Assert.False(stage.IsOn);
        Assert.Equal(2, stage.Active.Count);
        var brilliant = Play();

        stage.Update(new(0.5, 0.5, 0.5, 0.5, 1, false, Switch1: true));
        Assert.True(stage.IsOn);
        Assert.Single(stage.Active);
        Assert.Equal(0, stage.Tick(180), 1e-6); // the Normal channel starts at its operating point: silent
        var normal = Play();

        // The top boost gains more than the Normal channel even through its stack at noon (which loses about 25 dB).
        output.WriteLine($"1 kHz at 0.1 V: Brilliant {brilliant:0.000} V, Normal {normal:0.000} V peak");
        Assert.True(brilliant > 2 * normal, $"{brilliant:0.000} against {normal:0.000}");

        double Play() {
            var peak = 0.0;
            for (var n = 0; n < 9_600; n++) {
                var y = stage.Tick(180 + 0.1 * Math.Sin(2 * Math.PI * 1_000 * n / Rate));
                peak = n > 4_800 ? Math.Max(peak, Math.Abs(y)) : peak;
            }

            return peak;
        }
    }
}
