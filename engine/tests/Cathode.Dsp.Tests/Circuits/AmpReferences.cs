using Cathode.Dsp.Amp;
using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Tests.Circuits;

/// <summary>
///     The amp stages of <see cref="Cathode.Dsp.Amp" /> rebuilt as <see cref="ReferenceCircuit" />s from their design
///     records, with the exact Koren devices (the stages use tabulated ones). Each returns the circuit and the node to
///     compare; source 0 is the input.
/// </summary>
static class AmpReferences {
    const double Floor = 1e-6; // RTypeScattering's smallest resistance: a pot at its end stop

    /// <summary>A <see cref="TriodeStage" /> driven through <paramref name="sourceResistance" /> (plus its own input resistance).</summary>
    public static (ReferenceCircuit Circuit, int Plate, int Grid, int Cathode) Triode(
        TriodeStageDesign d,
        double rotation,
        bool bright,
        double sourceResistance,
        Func<double, double> input
    ) {
        var c = new ReferenceCircuit();
        var source = c.Node();
        var node = c.Node();
        c.Source(source, 0, input);
        c.Resistor(source, node, Math.Max(sourceResistance + d.InputResistance, 1));
        var top = node;
        if (d.CouplingCap > 0) {
            top = c.Node();
            c.Capacitor(node, top, d.CouplingCap);
        }

        var wiper = top;
        if (d.Volume is { } pot) {
            wiper = c.Node();
            var fraction = pot.Fraction(rotation);
            c.Resistor(top, wiper, Math.Max((1 - fraction) * pot.Ohms, Floor));
            c.Resistor(wiper, 0, Math.Max(fraction * pot.Ohms, Floor));
            if (bright && d.BrightCap > 0) {
                c.Capacitor(top, wiper, d.BrightCap);
            }
        }

        var grid = wiper;
        if (d.SeriesResistor > 0) {
            grid = c.Node();
            c.Resistor(wiper, grid, d.SeriesResistor);
            if (d.SeriesBypass > 0) {
                c.Capacitor(wiper, grid, d.SeriesBypass);
            }
        }

        if (d.GridLeak > 0) {
            c.Resistor(grid, 0, d.GridLeak);
        }

        var (plate, cathode, supply) = (c.Node(), c.Node(), c.Node());
        c.Resistor(supply, plate, d.PlateResistor);
        c.Resistor(cathode, 0, d.CathodeResistor);
        if (d.CathodeBypass > 0) {
            c.Capacitor(cathode, 0, d.CathodeBypass);
        }

        c.Source(supply, 0, _ => d.Supply);
        c.Device(new Wdf.Triode(d.Tube), (grid, cathode), (plate, cathode));
        return (c, plate, grid, cathode);
    }

    /// <summary>A <see cref="ToneStackStage" />; returns the circuit, the output (load wiper) and the driven node.</summary>
    public static (ReferenceCircuit Circuit, int Output, int Driven) ToneStackStage(
        ToneStackStageDesign d,
        AmpControls controls,
        double sourceResistance,
        Func<double, double> input
    ) {
        var c = new ReferenceCircuit();
        var source = c.Node();
        var grid = c.Node();
        c.Source(source, 0, input);
        c.Resistor(source, grid, Math.Max(sourceResistance + d.InputResistance, 1));
        if (d.GridLeak > 0) {
            c.Resistor(grid, 0, d.GridLeak);
        }

        var (cathode, supply) = (c.Node(), c.Node());
        c.Resistor(cathode, 0, d.CathodeResistor);
        int driven;
        if (d.Driver == StackDriver.CathodeFollower) {
            driven = cathode;
            c.Device(new Wdf.Triode(d.Tube), (grid, cathode), (supply, cathode));
        } else {
            driven = c.Node();
            c.Resistor(supply, driven, d.PlateResistor);
            if (d.CathodeBypass > 0) {
                c.Capacitor(cathode, 0, d.CathodeBypass);
            }

            c.Device(new Wdf.Triode(d.Tube), (grid, cathode), (driven, cathode));
        }

        var output = Stack(c, driven, d.Stack, controls.Bass, controls.Middle, controls.Treble);
        var wiper = c.Node();
        var fraction = d.Load.Fraction(controls.Rotation(d.LoadRole));
        c.Resistor(output, wiper, Math.Max((1 - fraction) * d.Load.Ohms, Floor));
        c.Resistor(wiper, 0, Math.Max(fraction * d.Load.Ohms, Floor));
        if (controls.Bright && d.BrightCap > 0) {
            c.Capacitor(output, wiper, d.BrightCap);
        }

        c.Source(supply, 0, _ => d.Supply);
        return (c, wiper, driven);
    }

    /// <summary>A tone stack fed from <paramref name="input" />; returns the treble wiper.</summary>
    public static int Stack(ReferenceCircuit c, int input, ToneStackDesign d, double bass, double middle, double treble) {
        var (top, bottom, slope, mid, output) = (c.Node(), c.Node(), c.Node(), c.Node(), c.Node());
        var t = d.Treble.Fraction(treble);
        c.Capacitor(input, top, d.TrebleCap);
        c.Resistor(input, slope, d.SlopeResistor);
        c.Capacitor(slope, bottom, d.BassCap);
        c.Capacitor(slope, mid, d.MiddleCap);
        c.Resistor(top, output, Math.Max((1 - t) * d.Treble.Ohms, Floor));
        c.Resistor(output, bottom, Math.Max(t * d.Treble.Ohms, Floor));
        c.Resistor(bottom, mid, Math.Max(d.Bass.Fraction(bass) * d.Bass.Ohms, Floor));
        c.Resistor(mid, 0, Math.Max(d.Middle.Fraction(middle) * d.Middle.Ohms, Floor));
        return output;
    }

    /// <summary>
    ///     A long-tail pair with ideal sources on both grids (<paramref name="gridA" />, <paramref name="gridB" />);
    ///     returns the plates.
    /// </summary>
    public static (ReferenceCircuit Circuit, int PlateA, int PlateB) LongTailPair(
        LongTailPairDesign d,
        double gridA,
        double gridB
    ) {
        var c = new ReferenceCircuit();
        var (supply, a, b, cathode, junction) = (c.Node(), c.Node(), c.Node(), c.Node(), c.Node());
        var (ga, gb) = (c.Node(), c.Node());
        c.Source(supply, 0, _ => d.Supply);
        c.Source(ga, 0, _ => gridA);
        c.Source(gb, 0, _ => gridB);
        c.Resistor(supply, a, d.PlateResistorA);
        c.Resistor(supply, b, d.PlateResistorB);
        c.Resistor(cathode, junction, d.BiasResistor);
        c.Resistor(junction, 0, d.TailResistor);
        c.Device(new Wdf.Triode(d.Tube), (ga, cathode), (a, cathode));
        c.Device(new Wdf.Triode(d.Tube), (gb, cathode), (b, cathode));
        return (c, a, b);
    }

    /// <summary>
    ///     A complete push-pull power amp as the circuit it models: a real long-tail pair (two exact triodes, the grid
    ///     leaks returning to the tail, the second grid AC-coupled to the feedback node), coupling caps, grid leaks to the
    ///     bias supply, grid stoppers, two exact pentodes (with Dempwolf grid current) with screen resistors, an ideal
    ///     centre-tapped transformer with the magnetising inductance, primary capacitance and core loss across the
    ///     primary and the leakage inductance and copper on the secondary, the speaker, and the feedback network from a
    ///     tap (a VCVS of the speaker voltage) with the presence control, and the cut control across the inverter's
    ///     plates. Supplies are stiff at the given voltages; a
    ///     cathode-biased amp's shared cathode resistor is <paramref name="cathodeResistor" />.
    ///     Returns the circuit and the speaker node.
    /// </summary>
    public static (ReferenceCircuit Circuit, int Speaker) PowerAmp(
        PowerAmpDesign d,
        double plateSupply,
        double screenSupply,
        double gridBias,
        double cathodeResistor,
        double presence,
        double cut,
        Func<double, double> input
    ) {
        var c = new ReferenceCircuit();
        var pi = d.PhaseInverter;

        // Phase inverter.
        var (source, piSupply, piA, piB, cathode, junction, ga, gb, feedback) =
            (c.Node(), c.Node(), c.Node(), c.Node(), c.Node(), c.Node(), c.Node(), c.Node(), c.Node());
        c.Source(source, 0, input);
        c.Source(piSupply, 0, _ => pi.Supply);
        c.Resistor(piSupply, piA, pi.PlateResistorA);
        c.Resistor(piSupply, piB, pi.PlateResistorB);
        c.Resistor(cathode, junction, pi.BiasResistor);
        c.Resistor(junction, 0, pi.TailResistor);
        c.Capacitor(source, ga, 10e-6);
        c.Resistor(ga, junction, 1e6);
        c.Resistor(gb, junction, 1e6);
        c.Capacitor(gb, feedback, 10e-6);
        c.Device(new Wdf.Triode(pi.Tube), (ga, cathode), (piA, cathode));
        c.Device(new Wdf.Triode(pi.Tube), (gb, cathode), (piB, cathode));

        // The cut control: a pot and a cap between the plates.
        if (d.CutCap > 0) {
            var middle = c.Node();
            c.Resistor(piA, middle, Math.Max(d.CutPot.Ohms * (1 - d.CutPot.Fraction(cut)), Floor));
            c.Capacitor(middle, piB, d.CutCap);
        }

        // Grid networks and output tubes (cathode bias: a shared, bypassed cathode resistor).
        var (bias, screen, centre) = (c.Node(), c.Node(), c.Node());
        c.Source(bias, 0, _ => gridBias);
        var tubeCathode = 0;
        if (d.CathodeBias) {
            tubeCathode = c.Node();
            c.Resistor(tubeCathode, 0, cathodeResistor);
            c.Capacitor(tubeCathode, 0, d.CathodeBypass);
        }

        c.Source(screen, 0, _ => screenSupply);
        c.Source(centre, 0, _ => plateSupply);
        var plates = new int[2];
        foreach (var (side, piPlate) in new[] { (0, piA), (1, piB) }) {
            var (leak, grid, plate) = (c.Node(), c.Node(), c.Node());
            c.Capacitor(piPlate, leak, d.CouplingCap);
            c.Resistor(leak, bias, d.GridLeak);
            c.Resistor(leak, grid, d.GridStopper / d.TubesPerSide); // paralleled tubes, each with its own stopper
            for (var t = 0; t < d.TubesPerSide; t++) {
                var screenPin = c.Node();
                c.Resistor(screen, screenPin, d.ScreenResistor);
                c.Device(new Pentode(d.Tube), (grid, tubeCathode), (screenPin, tubeCathode), (plate, tubeCathode));
            }

            plates[side] = plate;
        }

        // Transformer and speaker.
        var ot = d.Transformer;
        var n = ot.TurnsRatio;
        var n2 = n * n;
        var (secondary, winding, speaker, coil, motional) = (c.Node(), c.Node(), c.Node(), c.Node(), c.Node());
        c.Transformer((plates[0], centre, n / 2), (centre, plates[1], n / 2), (secondary, 0, 1));
        c.Inductor(plates[0], plates[1], ot.PrimaryInductance);
        c.Capacitor(plates[0], plates[1], ot.PrimaryCapacitance);
        c.Resistor(plates[0], plates[1], ot.CoreLoss);
        c.Inductor(secondary, winding, ot.LeakageInductance / n2);
        c.Resistor(winding, speaker, ot.WindingResistance / n2);
        var s = d.Speaker;
        var (rm, lm, cm) = s.Motional;
        c.Resistor(speaker, coil, s.Resistance);
        c.Inductor(coil, motional, s.Inductance);
        c.Resistor(coil, motional, s.EddyResistance);
        c.Inductor(motional, 0, lm);
        c.Capacitor(motional, 0, cm);
        c.Resistor(motional, 0, rm);

        // Feedback from the tap into the second grid's AC ground.
        if (d.HasFeedback) {
            var f = d.Feedback;
            var (tap, presenceNode) = (c.Node(), c.Node());
            c.Vcvs(tap, 0, speaker, 0, f.TapGain);
            c.Resistor(tap, feedback, f.FeedbackResistor);
            c.Resistor(feedback, 0, f.ShuntResistor);
            c.Resistor(feedback, presenceNode, Math.Max(f.Presence.Ohms * (1 - f.Presence.Fraction(presence)), Floor));
            c.Capacitor(presenceNode, 0, f.PresenceCap);
        } else {
            c.Resistor(feedback, 0, 1);
        }

        return (c, speaker);
    }
}
