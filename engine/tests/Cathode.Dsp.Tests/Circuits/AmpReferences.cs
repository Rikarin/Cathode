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
    ///     A cathodyne with an ideal source on its grid; returns the plate and the junction (the cathode output).
    /// </summary>
    public static (ReferenceCircuit Circuit, int Plate, int Junction) Cathodyne(CathodyneDesign d, double grid) {
        var c = new ReferenceCircuit();
        var (supply, plate, cathode, junction, g) = (c.Node(), c.Node(), c.Node(), c.Node(), c.Node());
        c.Source(supply, 0, _ => d.Supply);
        c.Source(g, 0, _ => grid);
        c.Resistor(supply, plate, d.PlateResistor);
        c.Resistor(cathode, junction, d.BiasResistor);
        c.Resistor(junction, 0, d.CathodeResistor);
        c.Device(new Wdf.Triode(d.Tube), (g, cathode), (plate, cathode));
        return (c, plate, junction);
    }

    /// <summary>
    ///     A <see cref="NetworkStageDesign" />'s netlist as a reference circuit, at the given knobs and settings, driven
    ///     through <paramref name="sourceResistance" /> (plus the design's input resistance). The netlist's nodes keep
    ///     their numbers; returns the circuit and the output node.
    /// </summary>
    public static (ReferenceCircuit Circuit, int Output) Network(
        NetworkStageDesign d,
        AmpControls controls,
        PreampSettings settings,
        double sourceResistance,
        Func<double, double> input
    ) {
        var net = d.Netlist;
        var c = new ReferenceCircuit();
        for (var n = 0; n < net.Nodes; n++) {
            c.Node();
        }

        var source = c.Node();
        c.Source(source, 0, input);
        c.Resistor(source, net.Input, Math.Max(sourceResistance + d.InputResistance, 1));
        Elements(c, d, controls, settings, static n => n);
        return (c, net.Output);
    }

    /// <summary>
    ///     Appends <paramref name="d" />'s netlist to <paramref name="c" />, driven from node <paramref name="source" />
    ///     through the design's input resistance (directly where it has none): the whole circuit of a chain of stages,
    ///     with no Thévenin cut between them. Returns the stage's output node in <paramref name="c" />.
    /// </summary>
    public static int Append(
        ReferenceCircuit c,
        NetworkStageDesign d,
        AmpControls controls,
        PreampSettings settings,
        int source
    ) {
        var net = d.Netlist;
        var first = c.Nodes + 1;
        for (var n = 0; n < net.Nodes; n++) {
            c.Node();
        }

        int Map(int n) => n == 0 ? 0 : first + n - 1;
        c.Resistor(source, Map(net.Input), Math.Max(d.InputResistance, 1)); // 1 Ω for a direct join: well conditioned
        Elements(c, d, controls, settings, Map);
        return Map(net.Output);
    }

    /// <summary>The netlist's elements into <paramref name="c" />, its node n at <paramref name="map" />(n).</summary>
    static void Elements(
        ReferenceCircuit c,
        NetworkStageDesign d,
        AmpControls controls,
        PreampSettings settings,
        Func<int, int> map
    ) {
        foreach (var raw in d.Netlist.Elements) {
            var e = raw with { A = map(raw.A), B = map(raw.B), C = map(raw.C), D = map(raw.D) };
            var on = e.Switch is not { } role || controls.IsOn(role);
            switch (e.Kind) {
                case ElementKind.Resistor: {
                    var ohms = on ? e.Value : e.OffValue > 0 ? e.OffValue : 1e12;
                    if (e.Part != PotPart.None) {
                        var fraction = e.Pot.Fraction(controls.Rotation(e.Role));
                        ohms = (e.Part == PotPart.Upper ? 1 - fraction : fraction) * e.Pot.Ohms;
                    }

                    c.Resistor(e.A, e.B, Math.Max(e.Cathode ? ohms * settings.CathodeResistor : ohms, Floor));
                    break;
                }
                case ElementKind.Capacitor: {
                    var farads = e.ValueAt(controls);
                    if (farads > 0) {
                        c.Capacitor(e.A, e.B, e.CapacitorRole switch {
                            CapacitorRole.Bright => farads * settings.BrightCap,
                            CapacitorRole.Stack => farads / settings.StackFrequency,
                            _ => farads
                        });
                    }

                    break;
                }
                case ElementKind.Inductor:
                    c.Inductor(e.A, e.B, e.Value);
                    break;
                case ElementKind.Supply:
                    c.Source(e.A, 0, _ => e.Value);
                    break;
                case ElementKind.Triode:
                    c.Device(new Wdf.Triode(settings.TubeFor(e.Triode)), (e.B, e.C), (e.A, e.C));
                    break;
                case ElementKind.Pentode:
                    c.Device(new Pentode(e.Pentode), (e.C, e.D), (e.B, e.D), (e.A, e.D));
                    break;
                case ElementKind.Diodes:
                    c.Device(new DiodeClipper(e.Diode, e.Forward, e.Diode, e.Reverse), (e.A, e.B));
                    break;
                case ElementKind.OpAmp:
                    // Linear and ideal but for a finite gain: valid below clipping, which the comparisons stay under.
                    c.Vcvs(e.C, 0, e.A, e.B, 1e5);
                    break;
            }
        }
    }

    /// <summary>
    ///     A complete power amp as the circuit it models: the phase inverter (a real long-tail pair, two exact triodes
    ///     with the grid leaks returning to the tail and the second grid AC-coupled to the feedback node; or a
    ///     cathodyne; or, single-ended, none), its input network (with an <see cref="InverterInputDesign" />: the
    ///     source behind <paramref name="sourceResistance" /> and the coupling cap, else a large cap), coupling caps,
    ///     grid leaks to the bias supply, grid stoppers, exact pentodes (with Dempwolf grid current) with screen
    ///     resistors from the screen supply, or on ultralinear taps of the primary, or strapped as triodes (the design's
    ///     triode-strapped tubes besides), an ideal transformer (centre-tapped, or a single primary from B+) with the
    ///     magnetising inductance, primary capacitance and core loss across the primary and the leakage inductance and
    ///     copper on the secondary, the speaker, and the feedback network from a tap (a VCVS of the speaker voltage)
    ///     with the presence control, and the cut control across the inverter's outputs. Supplies are stiff at the
    ///     given voltages; a cathode-biased amp's cathode resistor (all tubes') is <paramref name="cathodeResistor" />,
    ///     per side twice that with separate cathodes. Returns the circuit and the speaker node.
    /// </summary>
    public static (ReferenceCircuit Circuit, int Speaker, int Grid) PowerAmp(
        PowerAmpDesign d,
        double plateSupply,
        double screenSupply,
        double gridBias,
        double cathodeResistor,
        double presence,
        double cut,
        Func<double, double> input,
        double sourceResistance = 0,
        bool triode = false,
        double master = 1
    ) {
        var c = new ReferenceCircuit();
        var singleEnded = d.Topology == PowerTopology.SingleEnded;
        var (source, piSupply, feedback) = (c.Node(), c.Node(), c.Node());
        c.Source(source, 0, input);
        c.Source(piSupply, 0, _ => d.PhaseInverter.Supply);

        // The input network: the inverter's own (with grid current) or a large cap.
        int ga;
        var driven = c.Node();
        var (inputCap, inputLeak) = d.InverterInput is { } network ? (network.CouplingCap, network.GridLeak) : (10e-6, 1e6);
        c.Resistor(source, driven, Math.Max(sourceResistance, 1e-3));
        ga = c.Node();
        c.Capacitor(driven, ga, inputCap);

        // Phase inverter: outputs a and b.
        int a, b;
        if (singleEnded) {
            (a, b) = (ga, 0);
            c.Resistor(ga, 0, 1e9);
        } else if (d.PhaseInverter is CathodyneDesign cathodyne) {
            var (plate, cathode, junction) = (c.Node(), c.Node(), c.Node());
            c.Resistor(piSupply, plate, cathodyne.PlateResistor);
            c.Resistor(cathode, junction, cathodyne.BiasResistor);
            c.Resistor(junction, 0, cathodyne.CathodeResistor);
            c.Resistor(ga, junction, inputLeak);
            c.Device(new Wdf.Triode(cathodyne.Tube), (ga, cathode), (plate, cathode));
            (a, b) = (plate, junction);
        } else {
            var pi = (LongTailPairDesign)d.PhaseInverter;
            var (piA, piB, cathode, junction, gb) = (c.Node(), c.Node(), c.Node(), c.Node(), c.Node());
            c.Resistor(piSupply, piA, pi.PlateResistorA);
            c.Resistor(piSupply, piB, pi.PlateResistorB);
            c.Resistor(cathode, junction, pi.BiasResistor);
            c.Resistor(junction, 0, pi.TailResistor);
            c.Resistor(ga, junction, inputLeak);
            c.Resistor(gb, junction, 1e6);
            c.Capacitor(gb, feedback, 10e-6);
            c.Device(new Wdf.Triode(pi.Tube), (ga, cathode), (piA, cathode));
            c.Device(new Wdf.Triode(pi.Tube), (gb, cathode), (piB, cathode));
            (a, b) = (piA, piB);
        }

        // The cut control: a pot and a cap between the outputs.
        if (d.CutCap > 0 && !singleEnded) {
            var middle = c.Node();
            c.Resistor(a, middle, Math.Max(d.CutPot.Ohms * (1 - d.CutPot.Fraction(cut)), Floor));
            c.Capacitor(middle, b, d.CutCap);
        }

        // A master wired as a rheostat across the outputs.
        if (d.ShuntMaster is { } pot && !singleEnded) {
            c.Resistor(a, b, Math.Max(pot.Ohms * pot.Fraction(master), 1));
        }

        // The transformer's primary: centre-tapped (with ultralinear taps), or single from B+ to the plate.
        var ot = d.Transformer;
        var n = ot.TurnsRatio;
        var n2 = n * n;
        var (bias, screen, centre) = (c.Node(), c.Node(), c.Node());
        c.Source(bias, 0, _ => gridBias);
        c.Source(screen, 0, _ => screenSupply);
        c.Source(centre, 0, _ => plateSupply);
        var tap = triode && d.TriodeSwitch is not null ? 1 : d.UltralinearTap;
        var sides = singleEnded ? 1 : 2;
        var plates = new int[sides];
        var taps = new int[sides];
        for (var side = 0; side < sides; side++) {
            plates[side] = c.Node();
            taps[side] = tap is > 0 and < 1 ? c.Node() : tap >= 1 ? plates[side] : screen;
        }

        var (secondary, winding, speaker, coil, motional) = (c.Node(), c.Node(), c.Node(), c.Node(), c.Node());
        if (singleEnded) {
            c.Transformer((plates[0], centre, n), (secondary, 0, 1));
            c.Inductor(plates[0], centre, ot.PrimaryInductance);
            c.Capacitor(plates[0], centre, ot.PrimaryCapacitance);
            c.Resistor(plates[0], centre, ot.CoreLoss);
        } else {
            if (tap is > 0 and < 1) {
                c.Transformer(
                    (plates[0], taps[0], n / 2 * (1 - tap)),
                    (taps[0], centre, n / 2 * tap),
                    (centre, taps[1], n / 2 * tap),
                    (taps[1], plates[1], n / 2 * (1 - tap)),
                    (secondary, 0, 1)
                );
            } else {
                c.Transformer((plates[0], centre, n / 2), (centre, plates[1], n / 2), (secondary, 0, 1));
            }

            c.Inductor(plates[0], plates[1], ot.PrimaryInductance);
            c.Capacitor(plates[0], plates[1], ot.PrimaryCapacitance);
            c.Resistor(plates[0], plates[1], ot.CoreLoss);
        }

        // Grid networks and output tubes.
        var outputs = new[] { a, b };
        var shared = 0;
        if (d.CathodeBias && !d.SeparateCathodes) {
            shared = c.Node();
            c.Resistor(shared, 0, cathodeResistor);
            c.Capacitor(shared, 0, d.CathodeBypass);
        }

        var count = d.TubesPerSide + (d.Strapped is not null ? d.StrappedPerSide : 0);
        for (var side = 0; side < sides; side++) {
            var tubeCathode = shared;
            if (d.CathodeBias && d.SeparateCathodes) {
                tubeCathode = c.Node();
                c.Resistor(tubeCathode, 0, 2 * cathodeResistor);
                c.Capacitor(tubeCathode, 0, d.CathodeBypass);
            }

            var (leak, grid) = (c.Node(), c.Node());
            c.Capacitor(outputs[side], leak, d.CouplingCap);
            c.Resistor(leak, bias, d.GridLeak);
            c.Resistor(leak, grid, d.GridStopper / count); // paralleled tubes, each with its own stopper
            for (var t = 0; t < d.TubesPerSide; t++) {
                var screenPin = c.Node();
                c.Resistor(taps[side], screenPin, tap > 0 ? 1 : d.ScreenResistor);
                c.Device(new Pentode(d.Tube), (grid, tubeCathode), (screenPin, tubeCathode), (plates[side], tubeCathode));
            }

            if (d.Strapped is { } strapped) {
                for (var t = 0; t < d.StrappedPerSide; t++) {
                    var screenPin = c.Node();
                    c.Resistor(plates[side], screenPin, 1);
                    c.Device(new Pentode(strapped), (grid, tubeCathode), (screenPin, tubeCathode), (plates[side], tubeCathode));
                }
            }
        }

        // Transformer secondary and speaker.
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
        if (d.HasFeedback && !singleEnded && d.PhaseInverter is LongTailPairDesign) {
            var f = d.Feedback;
            var (tapNode, presenceNode) = (c.Node(), c.Node());
            c.Vcvs(tapNode, 0, speaker, 0, f.TapGain);
            c.Resistor(tapNode, feedback, f.FeedbackResistor);
            c.Resistor(feedback, 0, f.ShuntResistor);
            c.Resistor(feedback, presenceNode, Math.Max(f.Presence.Ohms * (1 - f.Presence.Fraction(presence)), Floor));
            c.Capacitor(presenceNode, 0, f.PresenceCap);
        } else {
            c.Resistor(feedback, 0, 1);
        }

        return (c, speaker, ga);
    }

}
