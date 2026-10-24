using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Hiwatt DR103 (Custom 100), from Mark Huss's redraws on hiwatt.org: "Mid-70s Four-Input Preamp" rev 1.4
///     (https://hiwatt.org/Schematics/DR_Pre4Input_v2.pdf) and "100 Watt Output Stage" v1.1
///     (https://hiwatt.org/Schematics/DR_100wOutput.pdf): V1's two triodes (220k plates, a shared cathode of two 2k2
///     with 47 nF) into the Brilliant (1 nF) and Normal (22 nF) 470k-A volumes and their 470k mixing resistors; V2A
///     (100k, 1k with 47 nF) driving the Hiwatt tone network from its plate (no follower), 22k into the 220k-A master;
///     V2B (100k, 1k5); 22 nF into a 12AT7 long-tail pair (82k/91k) biased from a cathode follower; four EL34 (22k
///     stoppers, 100k leaks, 100 Ω screens) on a Partridge transformer; 10k of feedback from the 16 Ω tap into 2k2 and
///     the 100k presence with 0.1 µF.
/// </summary>
/// <remarks>
///     <para>
///         The tone network, from V2A's plate: 1 nF to a node A, which reaches the 220k Treble's top through 220 pF and
///         its foot through 220k; the Treble's foot through 22k to the 100k Middle (a rheostat with 1 nF across it) and
///         on to the 470k-A Bass (a rheostat to ground); the plate also feeds 100k to a node with 47 nF to ground and
///         47 nF to the Bass's top. The Treble's wiper goes through 22k to the Master. The middle's taper is not given
///         (linear here).
///     </para>
///     <para>
///         The inverter: its first grid has 100k and its second 1M to a cathode follower (V3: a 1M/220k divider from the
///         preamp rail, 220k cathode) at about 50 V; the cathodes go through 22k to the feedback node, which has 2k2 to
///         ground and 0.1 µF to the second grid. Here it is the library's long-tail pair with the same rest point: a
///         22.6k tail under a 1.6k bias resistor (about 3.5 V of grid bias at 2.2 mA), with the 22 nF and 100k taken
///         into V2B's stage as its load (the follower is stiff, so the 100k returns to AC ground, not bootstrapped). The
///         presence pot sits between 1 nF from V2B's plate and 0.1 µF to the feedback node, its wiper through 100 Ω to
///         ground: here it is the library's presence (pot and cap across the shunt), which is its feedback half; the
///         treble it takes off V2B's plate when turned down, and the fixed 470 Ω with 10 nF across the shunt, are left
///         out.
///     </para>
///     <para>
///         Not on the drawings (guesses): the rails (B+ 496 V at the plates, as AmpBooks gives; 450 V at the inverter
///         and, through the 22k and 10k droppers, 320 V at V2A and 285 V at V1, V2B and V3), the EL34s' 30 mA, the
///         output transformer's 1.7k plate to plate, the supply's reservoir and choke. The supply's preamp node is the
///         drawing's far one: both droppers (32k) and its 16 µF. The 47 nF across V2B's 1k5 is drawn dashed (fitted on
///         some) and left out.
///     </para>
/// </remarks>
public static class HiwattDR103 {
    const double Closed = 10;
    const double Open = 1e12;
    const double V1Supply = 285;
    const double V2ASupply = 320;

    public static AmpDesign Design { get; } = new(
        "Hiwatt DR103",
        [Input(), V2A(), V2B()],
        new(
            "4 x EL34",
            PentodeModel.EL34,
            TubesPerSide: 2,
            Supply: 496,
            ScreenResistor: 100,
            IdleCurrent: 0.030,
            CathodeBias: false,
            CathodeBypass: 0,
            RatedPower: 100,
            PhaseInverter: new LongTailPairDesign(TriodeModel.Ecc81, 450, 82e3, 91e3, 22.6e3, 1.6e3),
            CouplingCap: 47e-9,
            GridLeak: 100e3,
            GridStopper: 22e3,
            CutCap: 0,
            CutPot: new(1e6, PotTaper.Linear),
            Feedback: new(Math.Sqrt(2), 10e3, 2.2e3, new(100e3, PotTaper.Linear), 0.1e-6),
            HasFeedback: true,
            Transformer: new(1.7e3),
            Speaker: SpeakerDesign.Closed4x12,
            PowerSupply: new(25, 100e-6, 100, 50e-6, 32e3, 16e-6, 6e-3),
            InverterInput: null
        ),
        -6
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Brilliant Vol",
            Control1 = "Normal Vol",
            Master = "Master Vol",
            Cut = null,
            Depth = null,
            Bright = null,
            Switch1 = "Jumper",
            Switch2 = "Normal Input"
        }
    };

    /// <summary>
    ///     V1: the guitar into the Brilliant channel's high input (34k: both 68k in parallel, 1M at the jack), into both
    ///     with the Jumper (switch 1), into the Normal channel alone with switch 2; two triodes on 220k plates and a
    ///     shared 1k1 with 47 nF; each plate through its coupling cap (Brilliant 1 nF, Normal 22 nF) to its 470k-A
    ///     volume and through 470k from the wiper to the mixing node, V2A's grid (the output). An idle channel's grid
    ///     rests on its jack's 1M.
    /// </summary>
    static NetworkStageDesign Input() {
        var net = new StageNetlist();
        var (rail, cathode, mix) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, V1Supply);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.1e3);
        net.Capacitor(cathode, StageNetlist.Ground, 47e-9);

        var brilliantJack = net.Node();
        net.SwitchedResistor(net.Input, brilliantJack, Closed, Open, SwitchRole.Switch2);
        net.SwitchedResistor(net.Input, brilliantJack, 0, Closed, SwitchRole.Switch1);
        var normalJack = net.Node();
        net.SwitchedResistor(net.Input, normalJack, 0, Closed, SwitchRole.Switch1);
        net.SwitchedResistor(net.Input, normalJack, 0, Closed, SwitchRole.Switch2);
        foreach (var (jack, coupling, volume) in new[] { (brilliantJack, 1e-9, PotRole.Gain), (normalJack, 22e-9, PotRole.Control1) }) {
            var (grid, plate, top) = (net.Node(), net.Node(), net.Node());
            net.Resistor(jack, StageNetlist.Ground, 1e6);
            net.Resistor(jack, grid, 34e3);
            net.Resistor(rail, plate, 220e3);
            net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
            net.Capacitor(plate, top, coupling);
            var wiper = net.Pot(top, StageNetlist.Ground, new(470e3, PotTaper.Audio), volume);
            net.Resistor(wiper, mix, 470e3);
        }

        net.Output = mix;
        return new("V1", net);
    }

    /// <summary>
    ///     V2A from the mixing node (100k, 1k with 47 nF, its grid-plate capacitance behind the mixers) on the tone
    ///     network (see the remarks); the Treble's wiper through 22k to the 220k-A master, whose wiper is the output.
    /// </summary>
    static NetworkStageDesign V2A() {
        var net = new StageNetlist();
        var (rail, plate, cathode, a, trebleTop, trebleFoot) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        var (middleTop, bassTop, shunt, master) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, V2ASupply);
        net.Resistor(rail, plate, 100e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.Capacitor(cathode, StageNetlist.Ground, 47e-9);

        net.Capacitor(plate, a, 1e-9, CapacitorRole.Stack);
        net.Capacitor(a, trebleTop, 220e-12, CapacitorRole.Stack);
        net.Resistor(a, trebleFoot, 220e3);
        var treble = net.Pot(trebleTop, trebleFoot, new(220e3, PotTaper.Linear), PotRole.Treble);
        net.Resistor(trebleFoot, middleTop, 22e3);
        net.Rheostat(middleTop, bassTop, new(100e3, PotTaper.Linear), PotRole.Middle);
        net.Capacitor(middleTop, bassTop, 1e-9, CapacitorRole.Stack);
        net.Rheostat(bassTop, StageNetlist.Ground, new(470e3, PotTaper.Audio), PotRole.Bass);
        net.Resistor(plate, shunt, 100e3);
        net.Capacitor(shunt, StageNetlist.Ground, 47e-9, CapacitorRole.Stack);
        net.Capacitor(shunt, bassTop, 47e-9, CapacitorRole.Stack);

        net.Resistor(treble, master, 22e3);
        var wiper = net.Pot(master, StageNetlist.Ground, new(220e3, PotTaper.Audio), PotRole.Master);
        net.Output = wiper;
        return new("V2A", net);
    }

    /// <summary>V2B (100k, 1k5 unbypassed), 22 nF into the inverter's 100k grid resistor (the output).</summary>
    static NetworkStageDesign V2B() {
        var net = new StageNetlist();
        var (rail, plate, cathode, grid) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, V1Supply);
        net.Resistor(rail, plate, 100e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(plate, grid, 22e-9);
        net.Resistor(grid, StageNetlist.Ground, 100e3);
        net.Output = grid;
        return new("V2B", net);
    }
}
