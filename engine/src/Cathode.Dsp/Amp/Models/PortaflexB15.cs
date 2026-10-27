using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Ampeg Portaflex B-15-N (1968, about 30 W), channel one: V1 (6SL7: 470k plate, 5.6k unbypassed) with the Normal
///     input's 47k (the Bright input's 100k ‖ 5 nF), its 0.1 µF into 0.01 µF and the 1M volume, the Ultra Lo shunt
///     (0.02 µF and 39k to ground from the plate side, with 1M in series while it is off) and the 500 pF Ultra Hi cap;
///     the second 6SL7 half (220k, 2.2k unbypassed); a passive Baxandall with 1M pots; the 270k mixing resistors; a
///     6SL7 floating paraphase (120k plates, 470k / 510k divider, 1k ‖ 25 µF shared cathode, 220 Ω under the first
///     half where 10k of feedback from the 16 Ω tap comes in); two 6L6GC in fixed bias (−50 V on the drawing) on a
///     5AR4, and the OT-214 (6.5k : 8 / 16 Ω).
/// </summary>
/// <remarks>
///     <para>
///         Sources: Joe Piazza's redraw of the 1968 B-15-N (drtube.com/schematics/ampeg/b15n68-jp.gif, also at
///         schematicheaven.net/ampegamps), read by eye at 4×: every value in the signal path, the −50 V bias node, the
///         5AR4 with 30 µF, 1k 10 W and 40 µF, the 22k / 40 µF preamp filters. The OT-214's 6.5k primary (and 290 Ω
///         DCR) is from Ampeg's 4/69 B-15-N service manual as quoted by Rob Mercure (Music Electronics Forum, "Primary
///         Z early Portaflexes"); plates near 450 V and screens near 445 V from builders on the same forum.
///     </para>
///     <para>
///         Assumptions: the 6SL7 runs on the 5751's curves (μ 70; the library has no 6SL7, whose plate resistance is a
///         little lower); the pot tapers (the volume audio, the Baxandall's linear); the idle current (38 mA a tube,
///         which gives about −50 V of bias at 450 V); the supply's source resistance (60 Ω); the idle channel two,
///         which loads the mix node through its 270k as about 150k more; and the cab: the B-15's closed 1 × 15 runs on
///         the closed 8 × 10 preset, the library's only bass cabinet. The screens have no resistors on the drawing (1 Ω
///         here).
///     </para>
/// </remarks>
public static class PortaflexB15 {
    const double Open = 1e12;
    const double Wire = 1;
    const double Preamp = 420;

    public static AmpDesign Design { get; } = new(
        "Portaflex B-15N",
        [InputStage(), VolumeStage(), ToneStack()],
        new(
            "2 x 6L6GC",
            PentodeModel.SixL6GC,
            1,
            450,
            1,
            0.038,
            false,
            0,
            30,
            new FloatingParaphaseDesign(TriodeModel.FiveSevenFiveOne, 445, 120e3, 220, 1e3, 470e3, 510e3, 470e3),
            0.022e-6,
            270e3,
            1e3,
            0,
            new(1e6, PotTaper.Linear),
            new(Math.Sqrt(2), 10e3, 220, new(1e3, PotTaper.Linear), 1e-15),
            true,
            new(6.5e3, 25, 12e-3, 290, 0.8e-9, 250e3),
            SpeakerDesign.Closed8x10,
            new(60, 30e-6, 1e3, 40e-6, 22e3, 40e-6, 2e-3, RectifierModel.GZ34)
        ),
        -5
    ) {
        Panel = new() {
            Gain = "Volume",
            Presence = null,
            Depth = null,
            Cut = null,
            Master = null,
            Bright = "Bright Input",
            Switch1 = "Ultra Hi",
            Switch2 = "Ultra Lo"
        }
    };

    /// <summary>
    ///     V1A: the Normal input's 47k (the Bright input's 100k ‖ 5 nF) and 5.6M leak, the first 6SL7 half (470k, 5.6k
    ///     unbypassed); out at its plate.
    /// </summary>
    static NetworkStageDesign InputStage() {
        var net = new StageNetlist();
        var (grid, plate, cathode, rail) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.SwitchedResistor(net.Input, grid, 47e3, 100e3, SwitchRole.Bright);
        net.SwitchedCapacitor(net.Input, grid, 5e-9, SwitchRole.Bright, CapacitorRole.Bright);
        net.Resistor(grid, StageNetlist.Ground, 5.6e6);
        net.Resistor(rail, plate, 470e3);
        net.Resistor(cathode, StageNetlist.Ground, 5.6e3);
        net.Triode(plate, grid, cathode, TriodeModel.FiveSevenFiveOne);
        net.Output = plate;
        return new("V1A", net);
    }

    /// <summary>
    ///     The volume network on V1A's plate (a Thévenin cut: the network is linear, the plate's resistance carries its
    ///     load) and V1B (220k, 2.2k unbypassed); out at its plate.
    /// </summary>
    static NetworkStageDesign VolumeStage() {
        var net = new StageNetlist();
        var rail = net.Node();
        net.Supply(rail, Preamp);
        var (coupled, top, shunt, low) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 0.1e-6);
        net.Capacitor(coupled, top, 0.01e-6);
        net.Capacitor(coupled, shunt, 0.02e-6);
        net.Resistor(shunt, low, 39e3);
        net.Resistor(low, StageNetlist.Ground, 1e6);
        net.SwitchedResistor(low, StageNetlist.Ground, Open, Wire, SwitchRole.Switch2);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.SwitchedCapacitor(top, wiper, 500e-12, SwitchRole.Switch1, CapacitorRole.Bright);

        var (plate2, cathode2) = (net.Node(), net.Node());
        net.Resistor(rail, plate2, 220e3);
        net.CathodeResistor(cathode2, StageNetlist.Ground, 2.2e3);
        net.Triode(plate2, wiper, cathode2, TriodeModel.FiveSevenFiveOne);
        net.Capacitor(wiper, plate2, Ab763.GridPlate);
        net.Output = plate2;
        return new("V1B", net);
    }

    /// <summary>
    ///     0.1 µF in, 220k to the 1M bass pot's top and 470 pF to the 1M treble pot's, 1 nF and 10 nF from the bass
    ///     wiper (270k from it to ground), 22k to ground, 4.7 nF to the treble pot's bottom, 120k between the wipers;
    ///     the 270k mixing resistor, channel two's beside it. Out at the mix node (the inverter's grid).
    /// </summary>
    static NetworkStageDesign ToneStack() {
        var net = new StageNetlist();
        var (input, bassTop, bassBottom, trebleTop, trebleBottom, mix, idle) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, input, 0.1e-6);
        net.Resistor(input, bassTop, 220e3);
        net.Capacitor(input, trebleTop, 470e-12, CapacitorRole.Stack);
        var bass = net.Pot(bassTop, bassBottom, new(1e6, PotTaper.Linear), PotRole.Bass);
        net.Capacitor(bassTop, bass, 1e-9, CapacitorRole.Stack);
        net.Capacitor(bass, bassBottom, 10e-9, CapacitorRole.Stack);
        net.Resistor(bass, StageNetlist.Ground, 270e3);
        net.Resistor(bassBottom, StageNetlist.Ground, 22e3);
        net.Capacitor(StageNetlist.Ground, trebleBottom, 4.7e-9, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, trebleBottom, new(1e6, PotTaper.Linear), PotRole.Treble);
        net.Resistor(bass, treble, 120e3);
        net.Resistor(treble, mix, 270e3);
        net.Resistor(mix, idle, 270e3);
        net.Resistor(idle, StageNetlist.Ground, 150e3);
        net.Output = mix;
        return new("Baxandall", net);
    }
}
