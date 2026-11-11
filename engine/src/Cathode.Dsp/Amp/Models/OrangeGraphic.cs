using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Orange Graphic Mk II of the early 1970s (the "Pics Only" amp): the OR120 (120 W, four EL34) and the OR80 (80 W,
///     two EL34), from the factory drawing "Orange Amplifier Graphic MK II, 120 Watt", drawn 1972
///     (https://schematicheaven.net/newamps/orange_mkii1972.pdf) and B. Hickmott's 2004 redraw of it
///     (https://schematicheaven.net/newamps/orange_graphic_mkii_or120.pdf, three sheets). 68k from the jack (1M to
///     ground) into V1A (ECC83, 220k, 2k2 with 50 µF); 0.068 µF onto a Baxandall network: 100k, the 1M log Bass with
///     2200 pF above its wiper and 0.022 µF below it, 22k to ground; 1500 pF, the 1M log Treble, 0.01 µF to ground;
///     100k from the Bass's wiper to the Treble's, which feeds the 1M log Volume with 330 pF from its top to its wiper;
///     V1B (220k, 2k2 with 50 µF); 0.068 µF and the six-way FAC switch, which adds 4700 pF, 4700 pF, 2200 pF, 1000 pF
///     and 330 pF in series one after another; 100k into V2A's grid (220k to ground). V2A (ECC83, 100k with 1000 pF
///     across it, 1k5 unbypassed) takes the feedback at its cathode, 15k from the 16 Ω tap, and has the Boost (HF
///     Drive) across the 1k5: a 1k linear pot, a 2 mH choke and 0.47 µF in series. 0.068 µF into V2B, a cathodyne
///     (100k plate, 1k5 over 100k, 1M grid leak to their junction); 0.068 µF to each side's 220k bias feed, 2k4
///     stoppers, 1k screen resistors, EL34s in fixed bias; a silicon bridge on two 100 µF in series, a choke to the
///     screens (two 32 µF in series), 33k (16 µF) to V2 and 33k (16 µF) to V1.
/// </summary>
/// <remarks>
///     <para>
///         The later sheets differ at V2A: the 1973 parts list
///         (https://schematicheaven.net/newamps/orange_mkii1973.pdf, R16) and the 1974 Musonic sheet
///         (https://schematicheaven.net/newamps/orange_mkii1974.pdf) give it a 390k plate resistor where the 1972
///         drawing has 100k, 2k2 stoppers for 2k4, and the 1974 sheet draws the Boost without the choke. The 1973 list
///         confirms 1k5 (R19) under V2A with 15k (R18) to the feedback, which the 1972 hand reads either way. This is
///         the 1972 amp; which sheet a given "Pics Only" amp follows is not known. The 1973 sheet's note is the OR80:
///         "80 watt version identical but Tx2 different and V3, V6, R24, R27, R35, R38 omitted".
///     </para>
///     <para>
///         V2A and its 1000 pF are inside the feedback loop (<see cref="PowerAmpDesign.Loop" />), the feedback at its
///         cathode: the unbypassed resistance there is the 1k5 with the 15k beside it. The Boost is the library's
///         presence branch across the 1k5 (the 1k pot in series with 0.47 µF, on the Presence knob, named HF Drive),
///         without the 2 mH choke: its 65 Ω at the 5.2 kHz series resonance and 250 Ω at 20 kHz are small beside the
///         pot and the 1k5, so in the audio band the branch is the pot and the cap. It acts on the feedback only: V2A's
///         own degeneration stays that of the whole 1k5 (a loop stage's table), where the amp's falls with the branch,
///         so the model's HF Drive lifts the treble less than the amp's (the comparison with the reference circuit in
///         <c>Driver_power_amps_match_the_reference_in_the_time_domain</c> runs with the branch out). V2B's coupling
///         cap and bootstrapped leak are an open load (well under 1 Hz) and left out. V2A draws no grid current (a loop
///         stage's table), so its 100k grid resistor only divides the signal. The echo send and return (100k each, at
///         the FAC's output and V2A's grid) are open, as with nothing plugged in. One input.
///     </para>
///     <para>
///         The 1972 drawing prints no DC voltages. The rails are the 1974 sheet's boxed readings, whose supply chain is
///         the same (choke, 33k, 33k): 470 V at the screens (B), 370 V at V2, 330 V at V1 (it also gives V1A 170 V at
///         the plate and 1.4 V at the cathode, V2B 275 V at the plate, and −37 V of bias). Guessed: 480 V at the
///         plates (the sheet's reading at A is illegible; B plus a choke's drop), 35 mA an EL34 at idle, the choke's
///         200 Ω, the supply's source resistance, the output transformers (1.7k plate to plate for the four EL34, 3.4k
///         for the pair: no sheet gives them), the pots' direction, and the FAC's: taken as full range at 0, each step
///         clockwise cutting more bass.
///     </para>
/// </remarks>
public static class OrangeGraphic {
    const double FirstSupply = 330; // V1 (1974 sheet)
    const double SecondSupply = 370; // V2 (1974 sheet)

    /// <summary>
    ///     The FAC's coupling capacitance per position: the 0.068 µF alone, then with 4700 pF, 4700 pF, 2200 pF, 1000
    ///     pF and 330 pF added in series one by one (4.4 nF, 2.3 nF, 1.1 nF, 528 pF, 203 pF).
    /// </summary>
    internal static double[] FacSteps { get; } = Series(0.068e-6, 4700e-12, 4700e-12, 2200e-12, 1000e-12, 330e-12);

    /// <summary>The OR120: four EL34.</summary>
    public static AmpDesign Design { get; } = Amp("OR120", "4 x EL34", 2, 120, 1.7e3, -3);

    /// <summary>The OR80: the same amp with two EL34 and its own output transformer.</summary>
    public static AmpDesign Or80 { get; } = Amp("OR80", "2 x EL34", 1, 80, 3.4e3, -3);

    static AmpDesign Amp(string name, string tubes, int perSide, double watts, double primary, double trim) =>
        new(
            name,
            [
                new TriodeStageDesign(
                    "V1A",
                    TriodeModel.Ecc83,
                    FirstSupply,
                    220e3,
                    2.2e3,
                    50e-6,
                    68e3,
                    GridLeak: 1e6
                ) { SupplyNode = 1 },
                V1B() with { SupplyNode = 1 }
            ],
            new(
                tubes,
                PentodeModel.EL34,
                perSide,
                480,
                1e3,
                0.035,
                false,
                0,
                watts,
                new CathodyneDesign(TriodeModel.Ecc83, SecondSupply, 100e3, 100e3, 1.5e3),
                0.068e-6,
                220e3,
                2.4e3,
                0,
                new(1e6, PotTaper.Linear),
                new(Math.Sqrt(2), 15e3, 1.5e3, new(1e3, PotTaper.Linear), 0.47e-6),
                true,
                new(primary),
                SpeakerDesign.Closed4x12,
                new(40, 50e-6, 200, 16e-6, 33e3, 16e-6, 3e-3, RectifierModel.Silicon),
                Loop: new(TriodeModel.Ecc83, SecondSupply, 100e3, 0, 1.5e3 * 15e3 / (1.5e3 + 15e3), PlateCap())
            ),
            trim
        ) {
            Panel = AmpPanel.Standard with {
                Middle = null,
                Presence = "HF Drive",
                Depth = null,
                Cut = null,
                Master = null,
                Bright = null,
                Control1 = "FAC",
                Defaults = new Dictionary<string, float> { ["control1"] = 0 }
            },
            PreampSupply = new([new(33e3, 16e-6, (SecondSupply - FirstSupply) / 33e3)])
        };

    /// <summary>
    ///     0.068 µF from V1A's plate onto the Bass and Treble network and the Volume (330 pF from its top to its
    ///     wiper), V1B (220k, 2k2 with 50 µF), the FAC's coupling capacitance (Control 1) and 100k into V2A's grid with
    ///     its 220k to ground (the output).
    /// </summary>
    static NetworkStageDesign V1B() {
        var net = new StageNetlist();
        var (coupled, bassTop, bassFoot, trebleTop, trebleFoot) = (net.Node(), net.Node(), net.Node(), net.Node(),
            net.Node());
        var (rail, plate, cathode, fac, grid) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 0.068e-6);

        net.Resistor(coupled, bassTop, 100e3);
        var bass = net.Pot(bassTop, bassFoot, new(1e6, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(bassTop, bass, 2200e-12, CapacitorRole.Stack);
        net.Capacitor(bass, bassFoot, 0.022e-6, CapacitorRole.Stack);
        net.Resistor(bassFoot, StageNetlist.Ground, 22e3);

        net.Capacitor(coupled, trebleTop, 1500e-12, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, trebleFoot, new(1e6, PotTaper.Audio), PotRole.Treble);
        net.Capacitor(trebleFoot, StageNetlist.Ground, 0.01e-6, CapacitorRole.Stack);
        net.Resistor(bass, treble, 100e3);

        var volume = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(treble, volume, 330e-12, CapacitorRole.Bright);

        net.Supply(rail, FirstSupply);
        net.Resistor(rail, plate, 220e3);
        net.Triode(plate, volume, cathode, TriodeModel.Ecc83);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.2e3);
        net.Capacitor(cathode, StageNetlist.Ground, 50e-6);

        net.SelectedCapacitor(plate, fac, FacSteps, PotRole.Control1);
        net.Resistor(fac, grid, 100e3);
        net.Resistor(grid, StageNetlist.Ground, 220e3);
        net.Output = grid;
        return new("V1B", net);
    }

    /// <summary>
    ///     V2A's plate inside the loop: the 1000 pF across its 100k (to the rail, ground for the signal); the plate is
    ///     the output, V2B's grid.
    /// </summary>
    static StageNetlist PlateCap() {
        var net = new StageNetlist();
        net.Capacitor(net.Input, StageNetlist.Ground, 1000e-12);
        net.Output = net.Input;
        return net;
    }

    /// <summary>
    ///     <paramref name="first" /> alone, then in series with one more of <paramref name="added" /> at each step.
    /// </summary>
    static double[] Series(double first, params double[] added) {
        var steps = new double[added.Length + 1];
        var elastance = 1 / first;
        steps[0] = first;
        for (var i = 0; i < added.Length; i++) {
            elastance += 1 / added[i];
            steps[i + 1] = 1 / elastance;
        }

        return steps;
    }
}
