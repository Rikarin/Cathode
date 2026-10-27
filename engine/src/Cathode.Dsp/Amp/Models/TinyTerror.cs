using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Orange Tiny Terror (15 W head), from a vector redraw (not a factory sheet; el34world
///     https://el34world.com/charts/Schematics/files/Orange/Orange_tiny_terror.pdf): 0.22 µF from the jack (1M to
///     ground) and 68k into V1A (100k, 1.5k with 22 µF); 0.001 µF and 68k onto 470k and the first half of the dual
///     500k-A Gain, 100 pF from its top to its wiper, into V1B (100k with 470 pF across it, 1.5k with 22 µF); 68k and
///     0.047 µF onto 220k and the Gain's second half, whose wiper drives a 12AX7 long-tail pair (0.047 µF, 1M grid
///     leaks, 1.2k bias, 47k tail, 100k plates, the second grid on 0.047 µF to ground); 0.1 µF out to the dual 500k-A
///     Volume, whose wipers feed the EL84s' grids (1.5k stoppers, 220k leaks) with the Tone (2.2 nF and the 500k-L pot)
///     across them; two EL84 on a shared 120 Ω with 220 µF, 470 Ω screen resistors, no feedback; a silicon bridge with
///     100 µF, 150 Ω 5 W to the screens (47 µF), 10k (47 µF) to the inverter and 15k (22 µF) to the preamp.
/// </summary>
/// <remarks>
///     <para>
///         Two simplifications in the power amp: the Volume is a dual pot between the inverter's coupling caps and the
///         grids (a post-inverter master); here it is the library's master across the inverter's outputs
///         (<see cref="PowerAmpDesign.ShuntMaster" />, the dual's two 500k tracks in series), which also runs from
///         silence to nearly full level but with a different law. The Tone sits across the grids after the Volume; here
///         it is the library's cut across the inverter's plates, which turns the other way: the panel's Cut knob turned
///         up takes the pot's resistance out (darker), where the amp's Tone turned up puts it in (brighter).
///     </para>
///     <para>
///         Not on the drawing (guesses): every DC voltage (330 V at the reservoir, 320 V screens, 300 V inverter, 270 V
///         preamp), the EL84s' idle (set by the drawn 120 Ω), the output transformer's primary (8k plate to plate,
///         usual for an EL84 pair at 15 W), the transformer's source resistance. The 7 W / 15 W switch (drawn as the
///         A/B links at the output transformer's centre tap) is not modelled: 15 W.
///     </para>
/// </remarks>
public static class TinyTerror {
    const double PreampSupply = 270;

    public static AmpDesign Design { get; } = new(
        "Tiny Terror",
        [V1A(), V1B()],
        new(
            "2 x EL84",
            PentodeModel.EL84,
            1,
            330,
            470,
            0.04,
            true,
            220e-6,
            15,
            new LongTailPairDesign(TriodeModel.Ecc83, 300, 100e3, 100e3, 47e3, 1.2e3),
            0.1e-6,
            220e3,
            1.5e3,
            2.2e-9,
            new(500e3, PotTaper.Linear),
            new(1, 47e3, 4.7e3, new(5e3, PotTaper.Linear), 0.1e-6),
            false,
            new(8e3),
            SpeakerDesign.Closed4x12,
            new(30, 100e-6, 150, 47e-6, 25e3, 22e-6, 2.5e-3),
            InverterInput: new(0.047e-6, 1e6),
            ShuntMaster: new(1e6, PotTaper.Audio),
            CathodeResistor: 120
        ),
        -4
    ) {
        Panel = AmpPanel.Standard with {
            Master = "Volume",
            Bass = null,
            Middle = null,
            Treble = null,
            Presence = null,
            Depth = null,
            Bright = null
        }
    };

    /// <summary>
    ///     The jack's 0.22 µF with 1M to ground, 68k into V1A (100k, 1.5k with 22 µF); the output is the plate.
    /// </summary>
    static NetworkStageDesign V1A() {
        var net = new StageNetlist();
        var (jack, grid, rail, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, jack, 0.22e-6);
        net.Resistor(jack, StageNetlist.Ground, 1e6);
        net.Resistor(jack, grid, 68e3);
        net.Supply(rail, PreampSupply);
        net.Resistor(rail, plate, 100e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Output = plate;
        return new("V1A", net);
    }

    /// <summary>
    ///     0.001 µF and 68k onto 470k and the Gain's first half (100 pF from its top to its wiper), V1B (100k with 470
    ///     pF, 1.5k with 22 µF), 68k and 0.047 µF onto 220k and the Gain's second half, whose wiper is the output.
    /// </summary>
    static NetworkStageDesign V1B() {
        var net = new StageNetlist();
        var (coupled, top, rail, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        var (series, second) = (net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 0.001e-6);
        net.Resistor(coupled, top, 68e3);
        net.Resistor(top, StageNetlist.Ground, 470e3);
        var gain = net.Pot(top, StageNetlist.Ground, new(500e3, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, gain, 100e-12, CapacitorRole.Bright);
        net.Supply(rail, PreampSupply);
        net.Resistor(rail, plate, 100e3);
        net.Capacitor(rail, plate, 470e-12);
        net.Triode(plate, gain, cathode, TriodeModel.Ecc83);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Resistor(plate, series, 68e3);
        net.Capacitor(series, second, 0.047e-6);
        net.Resistor(second, StageNetlist.Ground, 220e3);
        var wiper = net.Pot(second, StageNetlist.Ground, new(500e3, PotTaper.Audio), PotRole.Gain);
        net.Output = wiper;
        return new("V1B", net);
    }
}
