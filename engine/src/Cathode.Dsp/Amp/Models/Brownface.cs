using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     What the Fender brownface amps with the harmonic vibrato share (the 6G12 Concert and 6G4 Super, read from their
///     factory drawings; the 6G5 Pro and 6G7 Bandmaster are the same circuit family): two channels of two 7025 stages,
///     the brownface tone network, and, on the Vibrato channel, the harmonic vibrato
///     (<see cref="HarmonicVibratoStageDesign" />)
///     whose mixer pair the dry signal also runs through; the Normal channel joins at the inverter's grid.
/// </summary>
/// <remarks>
///     <para>
///         Read from the drawings (schematicheaven.net/fenderamps/concert_6g12_schem.pdf and super_6g4_schem.pdf, both
///         "A-FJ", each with its layout): the inputs (68k / 68k, 1M); each channel's first triode (100k, the two first
///         triodes on one 820 Ω ‖ 25 µF cathode, "C"), 0.05 µF into the tone network (250 pF to the 250k linear Treble,
///         100k to its far end, which has the 250k audio Bass to ground with its wiper on 0.01 µF from that end and 10k
///         to ground) and the 500k linear Volume from the treble wiper (47 pF across it on the Vibrato channel only);
///         the second triodes on one 820 Ω ‖ 25 µF ("D"): the Normal channel's with 220k, 0.05 µF and 1M to the
///         inverter's grid; the Vibrato channel's with a split load, 100k over 6.8k, the signal taken at the junction
///         (Fender's "split plate load": it gives up most of the stage's gain, which the vibrato's mixer pair makes
///         up); then the crossover and the mixer pair (values in <see cref="HarmonicVibratoStageDesign" />, whose
///         defaults are these drawings').
///     </para>
///     <para>
///         The voltages printed on the drawings set the nodes: the preamp node "A" about 245 V (from 170 V and 120 V on
///         the plates and 1.4 V and 1.1 V on the shared cathodes), the mixer pair's 355 V (its 320 V plates with
///         3.3 V / 4.7k between two triodes through 100k). Inferred: the Bass pot's sense (the drawing does not mark
///         its lugs; wired so that it adds bass turned up). The idle channel's loading: the idle mixer pair's plates
///         through their two 470k (about 258k) while the Normal channel plays, the idle Normal second stage's 1M and
///         its plate (1.05M) while the Vibrato channel plays.
///     </para>
/// </remarks>
static class Brownface {
    /// <summary>The preamp node "A" (see the remarks).</summary>
    public const double Preamp = 245;

    /// <summary>The mixer pair's node.</summary>
    public const double Mixer = 355;

    /// <summary>
    ///     The idle mixer pair as the Normal channel's load: two 470k into the plates' 100k ‖ r_p (about 46k).
    /// </summary>
    const double IdleMixer = 258e3;

    /// <summary>The 6G inputs: 68k ‖ 68k into the grid from input 1.</summary>
    const double InputResistance = 34e3;

    /// <summary>
    ///     The brownface long-tail pair: a 7025 with 82k and 100k plates from the 430 V node, a 6.8k tail and 820 Ω
    ///     bias; its second grid on 0.1 µF to the tail's bottom, 1M grid resistors, 0.001 µF in (the 47 pF between its
    ///     plates is left out).
    /// </summary>
    public static LongTailPairDesign PhaseInverter { get; } = new(TriodeModel.Ecc83, 430, 82e3, 100e3, 6.8e3, 820);

    /// <summary>
    ///     The panel: Volume, Treble, Bass and Presence, the channel, the vibrato pedal, Speed and Intensity.
    /// </summary>
    public static AmpPanel Panel { get; } = new() {
        Gain = "Volume",
        Middle = null,
        Depth = null,
        Cut = null,
        Master = null,
        Bright = null,
        Switch1 = "Vibrato Ch",
        Switch2 = "Vibrato",
        TremoloRate = "Speed",
        TremoloDepth = "Intensity"
    };

    /// <summary>
    ///     A channel's first triode with the tone network and the Volume: 100k from the preamp node, the two first
    ///     triodes' shared 820 Ω ‖ 25 µF (twice the resistor each), 0.05 µF into the network; the output is the
    ///     Volume's wiper.
    ///     <paramref name="brightCap" /> sits across the Volume (47 pF on the Vibrato channel, none on the Normal).
    /// </summary>
    public static NetworkStageDesign FirstStage(string name, double brightCap) {
        var net = new StageNetlist();
        var (plate, cathode, rail) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2 * 820);
        net.Capacitor(cathode, StageNetlist.Ground, 25e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);

        var (coupled, top, bottom) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, coupled, 0.05e-6);
        net.Capacitor(coupled, top, 250e-12, CapacitorRole.Stack);
        net.Resistor(coupled, bottom, 100e3);
        var treble = net.Pot(top, bottom, new(250e3, PotTaper.Linear), PotRole.Treble);

        // The Bass pot from the network's bottom to ground, its wiper on the 0.01 µF from the bottom and on 10k to
        // ground: turned up, the wiper goes to ground (the cap then shunts the bottom) - see the remarks.
        var bass = net.Pot(StageNetlist.Ground, bottom, new(250e3, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(bottom, bass, 0.01e-6, CapacitorRole.Stack);
        net.Resistor(bass, StageNetlist.Ground, 10e3);

        var volume = net.Pot(treble, StageNetlist.Ground, new(500e3, PotTaper.Linear), PotRole.Gain);
        if (brightCap > 0) {
            net.Capacitor(treble, volume, brightCap, CapacitorRole.Bright);
        }

        net.Output = volume;
        return new(name, net, InputResistance);
    }

    /// <summary>
    ///     The Normal channel's second triode: 220k, the shared 820 Ω ‖ 25 µF, 0.05 µF and 1M into the inverter's grid,
    ///     which the idle mixer pair loads (the output).
    /// </summary>
    public static NetworkStageDesign NormalSecondStage() {
        var net = new StageNetlist();
        var (plate, cathode, rail, coupled, output) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2 * 820);
        net.Capacitor(cathode, StageNetlist.Ground, 25e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);
        net.Capacitor(plate, coupled, 0.05e-6);
        net.Resistor(coupled, output, 1e6);
        net.Resistor(output, StageNetlist.Ground, IdleMixer);
        net.Output = output;
        return new("V2B", net);
    }

    /// <summary>
    ///     The Vibrato channel's second triode with its split load: 100k over 6.8k to the preamp node, the output at
    ///     their junction (about a sixteenth of the plate's swing).
    /// </summary>
    public static NetworkStageDesign VibratoSecondStage() {
        var net = new StageNetlist();
        var (plate, tap, cathode, rail) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, tap, 6.8e3);
        net.Resistor(tap, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2 * 820);
        net.Capacitor(cathode, StageNetlist.Ground, 25e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);
        net.Output = tap;
        return new("V2A", net);
    }

    /// <summary>
    ///     The channels: Normal, or (<see cref="SwitchRole.Switch1" />) Vibrato with the harmonic vibrato.
    /// </summary>
    public static ChannelSwitchStageDesign Channels() =>
        new(
            "Channel",
            SwitchRole.Switch1,
            [FirstStage("V1B", 0), NormalSecondStage()],
            [FirstStage("V1A", 47e-12), VibratoSecondStage(), new HarmonicVibratoStageDesign("Vibrato", Mixer)]
        );
}
