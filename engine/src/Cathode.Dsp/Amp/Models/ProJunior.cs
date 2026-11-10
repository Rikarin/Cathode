using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Pro Junior (15 W, 1 × 10): V1B (10k and 1M at the jack; 100k, 1.5k unbypassed), 0.01 µF and 470k (22 pF
///     across it) into the 250k audio volume with 56k beside it; the volume's top is the 250k linear tone pot's wiper,
///     one end of the tone pot through 0.01 µF to the volume's wiper, the other through 0.0033 µF to ground; 100 Ω
///     into V1A (100k, 1.5k unbypassed), 0.01 µF into a 12AX7 long-tail pair (91k + 9.1k / 100k, 820 Ω, a 33k tail,
///     1M grid leaks), two EL84 in fixed bias at 319 V with 100 Ω screen resistors, 100k feedback from the 8 Ω speaker
///     into 1k ‖ 0.1 µF, which the second grid's 0.1 µF stands on. Panel: Volume and Tone (on the Treble knob).
/// </summary>
/// <remarks>
///     <para>
///         Source: Fender drawing 047420 rev C, "Schematic diagram, Pro Junior" (7 October 1994;
///         schematicheaven.net/fenderamps/pro_jr.pdf), read zoomed: every value above, the supply (319 V on 2 × 22
///         µF, 2.7k to 294 V for the screens, 22k to 232 V for the inverter and the preamp, 22 µF each), the −10.4 V
///         bias, the 1.5k grid stoppers and 220k grid leaks, 0.022 µF coupling caps, the DC test points (152 V on both
///         V1 plates, 1.2 V on their cathodes, 172 V on the inverter's plates, 41 V on its cathodes) and the AC ones
///         at 1 kHz with 20 mV in and the knobs at noon (545 mV at V1B's plate, 8 mV at V1A's grid, 238 mV at V1A's
///         plate), which <c>FenderUsAmpTests</c> checks the model against. The drawing's currents agree with its
///         values: 0.8 mA in each V1 plate resistor and 1.2 mA in the tail are the 2.8 mA in the 22k.
///     </para>
///     <para>
///         The tone control is the Princeton 5F2-A's (<see cref="Princeton5F2" />) with other values: turned up, the
///         0.01 µF bridges the volume pot; turned down, the 0.0033 µF shunts the volume's top. The feedback enters at
///         the inverter's second grid, not under the tail: the tail is the 33k alone, and the divider is 100k into 1k.
///         The 0.1 µF straight across the 1k is a fixed presence circuit without a pot (the feedback falls above 1.6
///         kHz); the panel has no Presence. The 2200 pF with 270 Ω in series between the EL84s' plates is added to the
///         transformer's primary capacitance, the 270 Ω left out. Simplified as on the Blues Junior: the driven
///         inverter triode's 91k + 9.1k load with the output between them is a 91k plate resistor, and the 47 pF
///         across the inverter's plates is left out.
///     </para>
///     <para>
///         Unverified: the output transformer (Fender 041781; no figure found, taken as the Blues Junior's 7k : 8 Ω,
///         the same output stage); the EL84s' idle current (the bias is fixed; 28 mA is an estimate from the drawing's
///         screen current, (319 − 294) V / 2.7k less the 2.8 mA of the 22k, 3.2 mA a tube, at the EL84 data sheet's
///         plate-to-screen ratio of about 9); the supply's source resistance; the speaker's curve (a typical 10").
///         The drawing's output test is 10.23 V RMS into 8 Ω at 5 % THD, 13 W; 15 W is Fender's rating.
///     </para>
/// </remarks>
public static class ProJunior {
    /// <summary>The drawing's node Z: the preamp's and the inverter's supply.</summary>
    const double Preamp = 232;

    public static AmpDesign Design { get; } = new(
        "Pro Junior",
        [
            InputStage(),
            SecondStage()
        ],
        new(
            "2 x EL84",
            PentodeModel.EL84,
            1,
            319,
            100,
            0.028,
            false,
            0,
            15,
            new LongTailPairDesign(TriodeModel.Ecc83, Preamp, 91e3, 100e3, 33e3, 820),
            22e-9,
            220e3,
            1.5e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 100e3, 1e3, new(0, PotTaper.Linear), 0.1e-6),
            true,
            new(7e3, PrimaryCapacitance: 1e-9 + 2.2e-9),
            SpeakerDesign.OpenBack1x10,
            new(40, 44e-6, 2.7e3, 22e-6, 22e3, 22e-6, 2.8e-3, RectifierModel.Silicon),
            InverterInput: new(10e-9, 1e6)
        ),
        -6
    ) {
        Panel = new() {
            Gain = "Volume",
            Bass = null,
            Middle = null,
            Treble = "Tone",
            Presence = null,
            Depth = null,
            Cut = null,
            Master = null,
            Bright = null
        }
    };

    /// <summary>
    ///     V1B (1M grid leak behind the jack's 10k), 0.01 µF and 470k ‖ 22 pF into the volume's top (56k to ground
    ///     beside the pot), which is the tone pot's wiper; the output is the volume's wiper.
    /// </summary>
    static NetworkStageDesign InputStage() {
        var net = new StageNetlist();
        var (plate, coupled, bright, dark) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        Triode(net, plate);
        net.Capacitor(plate, coupled, 10e-9);
        var top = net.Pot(bright, dark, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Resistor(coupled, top, 470e3);
        net.Capacitor(coupled, top, 22e-12);
        net.Resistor(top, StageNetlist.Ground, 56e3);
        net.Capacitor(dark, StageNetlist.Ground, 3.3e-9, CapacitorRole.Stack);
        var volume = net.Pot(top, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(bright, volume, 10e-9, CapacitorRole.Stack);
        net.Output = volume;
        return new("V1B", net, 10e3);
    }

    /// <summary>V1A behind its 100 Ω; the output is the plate, into the inverter's 0.01 µF.</summary>
    static NetworkStageDesign SecondStage() {
        var net = new StageNetlist();
        var plate = net.Node();
        Triode(net, plate);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);
        net.Output = plate;
        return new("V1A", net, 100);
    }

    /// <summary>Half of V1 on the netlist's input: 100k to the 232 V node, 1.5k unbypassed.</summary>
    static void Triode(StageNetlist net, int plate) {
        var (cathode, rail) = (net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
    }
}
