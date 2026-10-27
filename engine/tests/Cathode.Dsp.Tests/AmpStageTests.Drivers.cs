using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Tests.Circuits;
using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Tests;

/// <summary>
///     Stream M2's driver variants against the reference MNA simulator: the cathodyne-plus-push-pull driver and the
///     cascaded differential driver as DC sweeps of their exact circuits (the coupling caps' far sides held at their
///     rest voltages, as the tables assume), and the whole power amps in the time domain, the Major's with its preamp
///     stage and tone stack inside the feedback loop.
/// </summary>
public partial class AmpStageTests {
    public static TheoryData<string> DriverPowerAmps => ["Major 1967", "Major 1967 open loop", "Bass 400"];

    /// <summary>
    ///     The bias tremolo hook (<see cref="PowerAmpDesign.BiasTremolo" />) acts after the inverter, on the grid
    ///     networks, so it applies to the new drivers too: borrowed from the Vibrolux 6G11, it modulates both power
    ///     amps' output.
    /// </summary>
    [Theory]
    [InlineData("Major 1967")]
    [InlineData("Bass 400")]
    public void Bias_tremolo_modulates_through_the_new_drivers(string name) {
        var tremolo = Vibrolux6G11.Design.Power.BiasTremolo;
        Assert.NotNull(tremolo);
        var design = AmpDesign.All.Single(a => a.Name == name).Power with { BiasTremolo = tremolo };
        var power = new PowerAmp(design, Rate);
        power.Configure(new(0.5, 0, 0, 0.5, 0.5, 0.5, 1, 5, 1));
        power.ConfigureLoop(new(0.5, 0.5, 0.5, 0.5, 1, false));
        power.Reset();
        var (low, high) = (double.MaxValue, 0.0);
        var block = 0.0;
        for (var n = 1; n <= (int)Rate; n++) {
            var y = power.Tick(0.05 * Math.Sin(2 * Math.PI * 1_000 * n / Rate));
            block = Math.Max(block, Math.Abs(y));
            if (n % 960 == 0) {
                if (n > Rate / 4) {
                    (low, high) = (Math.Min(low, block), Math.Max(high, block));
                }

                block = 0;
            }
        }

        var swing = 20 * Math.Log10(high / low);
        output.WriteLine($"{name}: tremolo swings the 1 kHz level {swing:0.0} dB");
        Assert.True(power.TremoloRunning);
        Assert.True(swing > 3, $"{name}: {swing:0.0} dB");
    }

    [Fact]
    public void Cathodyne_driver_table_matches_the_reference_dc_sweep() {
        var design = (CathodyneDriverDesign)MarshallMajor.Design.Power.PhaseInverter;
        var driver = (CathodyneDriver)PhaseInverter.For(design);
        output.WriteLine(
            $"Major driver: cathodyne grid {driver.RestGrid:0.00} V, plate {driver.RestSplitter.Plate:0.0} V, junction "
            + $"{driver.RestSplitter.Junction:0.00} V; ECC82 cathode {driver.RestDriverCathode:0.00} V, plates "
            + $"{driver.RestPlateA:0.0}/{driver.RestPlateB:0.0} V; output impedance "
            + $"{driver.OutputImpedance.Zaa / 1e3:0.0}k, "
            + $"{driver.OutputImpedance.Zab / 1e3:0.0}k"
        );
        var worst = 0.0;
        foreach (var vd in new[] { -40.0, -20, -8, -3, -1, 0, 1, 3, 8, 20 }) {
            var c = new ReferenceCircuit();
            var (supply, grid, plate, cathode, junction) = (c.Node(), c.Node(), c.Node(), c.Node(), c.Node());
            c.Source(supply, 0, _ => design.Supply);
            c.Source(grid, 0, _ => driver.RestGrid + vd);
            c.Resistor(supply, plate, design.PlateResistor);
            c.Resistor(cathode, junction, design.BiasResistor);
            c.Resistor(junction, 0, design.CathodeResistor);
            c.Device(new Triode(design.Tube), (grid, cathode), (plate, cathode));

            // The coupling caps hold their rest voltages: each leak returns to the output's rest voltage, and each
            // driver grid follows its output's swing.
            var (plateRest, junctionRest, ga, gb, pa, pb, tail, driverSupply) = (c.Node(), c.Node(), c.Node(),
                c.Node(), c.Node(), c.Node(), c.Node(), c.Node());
            c.Source(plateRest, 0, _ => driver.RestSplitter.Plate);
            c.Source(junctionRest, 0, _ => driver.RestSplitter.Junction);
            c.Resistor(plate, plateRest, design.DriverGridLeak);
            c.Resistor(junction, junctionRest, design.DriverGridLeak);
            c.Vcvs(ga, 0, junction, junctionRest, 1);
            c.Vcvs(gb, 0, plate, plateRest, 1);
            c.Source(driverSupply, 0, _ => design.DriverSupply);
            c.Resistor(driverSupply, pa, design.DriverPlate);
            c.Resistor(driverSupply, pb, design.DriverPlate);
            c.Resistor(tail, 0, design.DriverCathode);
            c.Device(new Triode(design.DriverTube), (ga, tail), (pa, tail));
            c.Device(new Triode(design.DriverTube), (gb, tail), (pb, tail));
            var reference = new ReferenceSimulator(c);
            reference.OperatingPoint(40);

            var (ea, eb) = driver.Evaluate(vd, out _, out _);
            var swingA = reference.Voltage(junction) - driver.RestSplitter.Junction;
            var swingB = reference.Voltage(plate) - driver.RestSplitter.Plate;
            var cathodeNow = reference.Voltage(tail);
            output.WriteLine(
                $"  vd {vd,5:0.0}: plates {driver.RestPlateA + ea:0.00}/{driver.RestPlateB + eb:0.00} V, reference "
                + $"{reference.Voltage(pa):0.00}/{reference.Voltage(pb):0.00} V (driver grids "
                + $"{swingA:0.0}/{swingB:0.0} V, "
                + $"cathode {cathodeNow:0.0} V)"
            );

            // The table leaves out grid current: the cathodyne's once its grid meets its cathode, the driver's once a
            // grid rises above the shared cathode.
            if (driver.GridToCathode(vd, out _) < -0.5
                && swingA - cathodeNow < -0.5
                && swingB - cathodeNow < -0.5) {
                worst = Math.Max(worst, Math.Abs(driver.RestPlateA + ea - reference.Voltage(pa)));
                worst = Math.Max(worst, Math.Abs(driver.RestPlateB + eb - reference.Voltage(pb)));
            }
        }

        Assert.True(worst < 0.05, $"{worst:0.000} V from the reference");

        // Push-pull: equal and opposite for small inputs, output A falling as the input rises.
        var (a, b) = driver.Evaluate(0.5, out _, out _);
        Assert.True(a < 0 && b > 0, "the outputs are in antiphase");
        Assert.InRange(-b / a, 0.85, 1.15);
    }

    [Fact]
    public void Differential_driver_table_matches_the_reference_dc_sweep() {
        var design = (DifferentialDriverDesign)MesaBass400.Design.Power.PhaseInverter;
        var driver = (DifferentialDriver)PhaseInverter.For(design);
        output.WriteLine(
            $"Bass 400 driver: V3 cathodes {driver.RestCathode:0.00} V, plates {driver.RestFirstPlate:0.0} V "
            + $"({driver.RestCurrents.First * 1e3:0.00} mA); V4 cathodes {driver.RestDriverCathode:0.00} V, plates "
            + $"{driver.RestPlateA:0.0}/{driver.RestPlateB:0.0} V ({driver.RestCurrents.Second * 1e3:0.00} mA); output "
            + $"impedance {driver.OutputImpedance.Zaa / 1e3:0.0}k, {driver.OutputImpedance.Zab / 1e3:0.0}k"
        );
        var load = design.DividerSeries + design.DividerShunt;
        var ratio = design.DividerShunt / load;
        var worst = 0.0;
        foreach (var vd in new[] { -10.0, -4, -2, -1, -0.3, 0, 0.3, 1, 2, 4, 10 }) {
            var c = new ReferenceCircuit();
            var (supply, ret, grid, pa, pb, tail) = (c.Node(), c.Node(), c.Node(), c.Node(), c.Node(), c.Node());
            c.Source(supply, 0, _ => design.Supply);
            c.Source(ret, 0, _ => design.TailReturn);
            c.Source(grid, 0, _ => vd);
            c.Resistor(supply, pa, design.PlateResistor);
            c.Resistor(supply, pb, design.PlateResistor);
            c.Resistor(tail, ret, design.TailResistor);
            c.Device(new Triode(design.Tube), (grid, tail), (pa, tail));
            c.Device(new Triode(design.Tube), (0, tail), (pb, tail));
            var (rest, ga, gb, qa, qb, joined, driverSupply) = (c.Node(), c.Node(), c.Node(), c.Node(), c.Node(),
                c.Node(), c.Node());
            c.Source(rest, 0, _ => driver.RestFirstPlate);
            c.Resistor(pa, rest, design.CouplingLoad * load / (design.CouplingLoad + load));
            c.Resistor(pb, rest, design.CouplingLoad * load / (design.CouplingLoad + load));
            c.Vcvs(ga, 0, pa, rest, ratio);
            c.Vcvs(gb, 0, pb, rest, ratio);
            c.Source(driverSupply, 0, _ => design.DriverSupply);
            c.Resistor(driverSupply, qa, design.DriverPlate);
            c.Resistor(driverSupply, qb, design.DriverPlate);
            c.Resistor(joined, 0, design.DriverCathode);
            c.Device(new Triode(design.DriverTube), (ga, joined), (qa, joined));
            c.Device(new Triode(design.DriverTube), (gb, joined), (qb, joined));
            var reference = new ReferenceSimulator(c);
            reference.OperatingPoint(40);

            var (ea, eb) = driver.Evaluate(vd, out _, out _);
            var (gridA, gridB, cathodeNow) = (reference.Voltage(ga), reference.Voltage(gb), reference.Voltage(joined));
            output.WriteLine(
                $"  vd {vd,5:0.0}: outputs {driver.RestPlateA + ea:0.00}/{driver.RestPlateB + eb:0.00} V, reference "
                + $"{reference.Voltage(qb):0.00}/{reference.Voltage(qa):0.00} V (V4 grids {gridA:0.00}/{gridB:0.00} V, "
                + $"cathode {cathodeNow:0.00} V)"
            );
            if (driver.GridToCathode(vd, out _) < -0.5 && gridA - cathodeNow < -0.5 && gridB - cathodeNow < -0.5) {
                worst = Math.Max(worst, Math.Abs(driver.RestPlateA + ea - reference.Voltage(qb)));
                worst = Math.Max(worst, Math.Abs(driver.RestPlateB + eb - reference.Voltage(qa)));
            }
        }

        Assert.True(worst < 0.05, $"{worst:0.000} V from the reference");
        var (a, b) = driver.Evaluate(0.02, out _, out _);
        Assert.True(a < 0 && b > 0, "the outputs are in antiphase");
        Assert.InRange(-b / a, 0.85, 1.15);
    }

    /// <summary>
    ///     The power amps whole against the reference, with a stiff supply: the Major's V2A, tone stack, cathodyne,
    ///     ECC82 pair and ultralinear KT88s with the feedback into V2A's cathode (and without it); the Bass 400's two
    ///     differential stages and six 6550.
    /// </summary>
    [Theory]
    [MemberData(nameof(DriverPowerAmps))]
    public void Driver_power_amps_match_the_reference_in_the_time_domain(string name) {
        var stiff = new PowerSupplyDesign(1e-3, 1, 1e-3, 1, 1e3, 1, 0);
        var (design, amplitude) = name switch {
            "Major 1967" => (MarshallMajor.Design.Power with { PowerSupply = stiff }, 0.3),
            "Major 1967 open loop" => (MarshallMajor.Design.Power with { PowerSupply = stiff, HasFeedback = false },
                0.1),
            _ => (MesaBass400.Design.Power with { PowerSupply = stiff }, 0.05)
        };
        var controls = new AmpControls(0.5, 0.7, 0.4, 0.6, 1, false);
        var power = new PowerAmp(design, Rate);
        power.SetSourceResistance(1);
        power.Configure(new(0.5, 0, 0, 0.5, 0.5, 0.5));
        power.ConfigureLoop(controls);
        power.Reset();
        var (circuit, speaker, _) = AmpReferences.PowerAmp(
            design,
            power.Supply,
            power.ScreenSupply,
            power.GridBias,
            power.CathodeResistor,
            0.5,
            0,
            t => Drive(amplitude, t),
            0,
            false,
            1,
            controls
        );
        output.WriteLine(
            $"{name}: B+ {power.Supply:0.0} V, grid bias {power.GridBias:0.00} V, idle "
            + $"{power.PlateCurrents.A * 1e3:0.0} mA "
            + "per tube"
        );
        var errorDb = CompareInTime(name, power.Tick, power.Newton, circuit, speaker, amplitude, 8);
        Assert.True(errorDb < -30, $"{name}: {errorDb:0.0} dB from the reference");
    }
}
