using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     The circuits the JCM2000 main boards share (DSL100: JCM2-60-02 issue 9; TSL100: TL10-60-02 issue 7): V1B's
///     input stage, the V1A stage of the high-gain path, V2B, V2A and V3B, the Tone Shift stack of the front panels,
///     and the power amp. The values that differ between the two boards are parameters; each model's file says where
///     its own come from.
/// </summary>
public static class Jcm2000Parts {
    const double Closed = 10;

    /// <summary>
    ///     V1B from <paramref name="grid" />: <paramref name="plateResistor" />, 1k8 ‖ 4.7 µF, 100 pF plate to
    ///     cathode, 22 nF and 1M to ground: the line to the front panel, which is returned.
    /// </summary>
    public static int InputStage(StageNetlist net, int grid, double supply, double plateResistor) {
        var (rail, plate, cathode, line) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, supply);
        net.Resistor(rail, plate, plateResistor);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.8e3);
        net.Capacitor(cathode, StageNetlist.Ground, 4.7e-6);
        net.Capacitor(plate, cathode, 100e-12);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, line, 22e-9);
        net.Resistor(line, StageNetlist.Ground, 1e6);
        return line;
    }

    /// <summary>
    ///     V1A from its grid: 100k plate, 470 pF from the plate to the rail, 1k8 with 1 µF + 10k to ground (the 10k
    ///     shorted while switch 2 is on); <paramref name="couplingCap" /> (1M to ground), 10k, 1k
    ///     (<paramref name="filterCap" /> to ground), 470k into V2B's grid (2M2 to ground), the idle channel's 470k
    ///     to ground there. Out at V2B's grid.
    /// </summary>
    public static StageNetlist LeadStage(double supply, double couplingCap, double filterCap) {
        var net = new StageNetlist();
        var (rail, plate, cathode, bypass, coupled, ldr, filtered, grid) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, supply);
        net.Resistor(rail, plate, 100e3);
        net.Capacitor(plate, rail, 470e-12, CapacitorRole.Plate);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.8e3);
        net.Capacitor(cathode, bypass, 1e-6);
        net.Resistor(bypass, StageNetlist.Ground, 10e3);
        net.SwitchedResistor(bypass, StageNetlist.Ground, 0, Closed, SwitchRole.Switch2);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, couplingCap);
        net.Resistor(coupled, StageNetlist.Ground, 1e6);
        net.Resistor(coupled, ldr, 10e3);
        net.Resistor(ldr, filtered, 1e3);
        net.Capacitor(filtered, StageNetlist.Ground, filterCap);
        net.Resistor(filtered, grid, 470e3);
        net.Resistor(grid, StageNetlist.Ground, 2.2e6);
        net.Resistor(grid, StageNetlist.Ground, 470e3);
        net.Output = grid;
        return net;
    }

    /// <summary>
    ///     V2B from its grid: 100k, 4k7 unbypassed; 22 nF and 470k into V2A's grid (470k leak). Out at that grid.
    /// </summary>
    public static StageNetlist SecondStage(double supply) {
        var net = new StageNetlist();
        var (plate, _) = Stage(net, supply, 4.7e3);
        var (coupled, grid) = (net.Node(), net.Node());
        net.Capacitor(plate, coupled, 22e-9);
        net.Resistor(coupled, grid, 470e3);
        net.Resistor(grid, StageNetlist.Ground, 470e3);
        net.Output = grid;
        return net;
    }

    /// <summary>
    ///     V2A from its grid: 100k, 2k2 unbypassed; 22 nF, 220k to the reverb send's node, 100k into V3B's grid,
    ///     which 47k and the reverb return's 47k + 2k2 hold to ground. Out at that grid. Returns the netlist with the
    ///     cathode and the send node, for what a board adds there.
    /// </summary>
    public static (StageNetlist Net, int Cathode, int Send) ThirdStage(double supply) {
        var net = new StageNetlist();
        var (plate, cathode) = Stage(net, supply, 2.2e3);
        var (coupled, send, grid) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, coupled, 22e-9);
        net.Resistor(coupled, send, 220e3);
        net.Resistor(send, grid, 100e3);
        net.Resistor(grid, StageNetlist.Ground, 47e3);
        net.Resistor(grid, StageNetlist.Ground, 49.2e3);
        net.Output = grid;
        return (net, cathode, send);
    }

    /// <summary>V3B from its grid: 100k, 1k unbypassed. Out at the plate, which drives V3A's grid directly.</summary>
    public static StageNetlist MixerStage(double supply) {
        var net = new StageNetlist();
        net.Output = Stage(net, supply, 1e3).Plate;
        return net;
    }

    /// <summary>
    ///     A triode from the netlist's input: 100k from a rail at <paramref name="supply" />,
    ///     <paramref name="cathodeResistor" /> unbypassed. Returns its plate and cathode.
    /// </summary>
    static (int Plate, int Cathode) Stage(StageNetlist net, double supply, double cathodeResistor) {
        var (rail, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, supply);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, cathodeResistor);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        return (plate, cathode);
    }

    /// <summary>
    ///     The high-gain channel's Gain network from V1B's <paramref name="line" />: 4.7 nF to
    ///     <paramref name="feed" />, then 470k ‖ (2.2 nF, through <paramref name="arm" />, + 47k) onto
    ///     <paramref name="top" /> of the 1M audio Gain, 470 pF from its top to its wiper, which is returned.
    /// </summary>
    public static int LeadGain(StageNetlist net, int line, int feed, int arm, int top) {
        net.Capacitor(line, feed, 4.7e-9);
        net.Resistor(feed, top, 470e3);
        net.Capacitor(feed, arm, 2.2e-9);
        net.Resistor(arm, top, 47e3);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, wiper, 470e-12, CapacitorRole.Bright);
        return wiper;
    }

    /// <summary>The inner nodes of a <see cref="ToneShiftStack" />, in the order the netlist numbers them.</summary>
    public readonly record struct StackNodes(
        int Upper,
        int TrebleTop,
        int Slope,
        int SlopeFoot,
        int BassTop,
        int MiddleTop,
        int MiddleCap,
        int MiddleArm
    ) {
        public static StackNodes Of(StageNetlist net) =>
            new(net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
    }

    /// <summary>
    ///     The JCM2000 tone stack from <paramref name="line" />: 470 pF, 33k, 22 nF, 22 nF; 200k linear treble, 1M
    ///     audio bass, 22k linear middle with the middle cap on its wiper. Tone Shift (switch 3) opens the contacts
    ///     across another 470 pF in series with the treble cap, 68k in series with the slope resistor and 22 nF in
    ///     series with the middle cap; 10M holds each cap junction. Returns the treble wiper.
    /// </summary>
    public static int ToneShiftStack(StageNetlist net, int line, StackNodes n) {
        // Treble cap: 470 pF, with Tone Shift another 470 pF in series.
        net.Capacitor(line, n.Upper, 470e-12, CapacitorRole.Stack);
        net.Capacitor(n.Upper, n.TrebleTop, 470e-12, CapacitorRole.Stack);
        net.SwitchedResistor(n.Upper, n.TrebleTop, Closed, 0, SwitchRole.Switch3);
        net.Resistor(n.Upper, StageNetlist.Ground, 10e6);

        // Slope: 33k, with Tone Shift 68k more.
        net.Resistor(line, n.Slope, 33e3);
        net.Resistor(n.Slope, n.SlopeFoot, 68e3);
        net.SwitchedResistor(n.Slope, n.SlopeFoot, Closed, 0, SwitchRole.Switch3);
        net.Capacitor(n.SlopeFoot, n.BassTop, 22e-9, CapacitorRole.Stack);
        var treble = net.Pot(n.TrebleTop, n.BassTop, new(200e3, PotTaper.Linear), PotRole.Treble);
        net.Rheostat(n.BassTop, n.MiddleTop, new(1e6, PotTaper.Audio), PotRole.Bass);
        var middle = net.Pot(n.MiddleTop, StageNetlist.Ground, new(22e3, PotTaper.Linear), PotRole.Middle);

        // Middle cap: 22 nF, with Tone Shift another 22 nF in series.
        net.Capacitor(n.SlopeFoot, n.MiddleCap, 22e-9, CapacitorRole.Stack);
        net.Resistor(n.MiddleCap, StageNetlist.Ground, 10e6);
        net.Capacitor(n.MiddleCap, n.MiddleArm, 22e-9, CapacitorRole.Stack);
        net.SwitchedResistor(n.MiddleCap, n.MiddleArm, Closed, 0, SwitchRole.Switch3);
        net.Resistor(n.MiddleArm, middle, Closed);
        return treble;
    }

    /// <summary>
    ///     A JCM2000 model: its <paramref name="channels" /> into V2B, V2A (<paramref name="thirdStage" />), V3B
    ///     and the model's <paramref name="output" /> network around the V3A follower, then <see cref="Power" />.
    ///     The feedback is 47k from the output transformer (<paramref name="tapGain" />: the tap's voltage against
    ///     the 8 Ω one) onto the tail's 4k7, with the 5k linear Presence and <paramref name="presenceCap" /> and the
    ///     Deep network on the block's Bright switch: 10k, which the switch shorts, and 10 µF into an RC4558
    ///     simulated inductor (270 Ω to a junction with 330 Ω to ground, bootstrapped through 10 µF by a follower).
    ///     The panel has no master, Depth or Cut; <paramref name="switches" /> names switches 1 to 3.
    /// </summary>
    public static AmpDesign Design(
        string name,
        ChannelSwitchStageDesign channels,
        (double V2B, double V3, double Inverter) rails,
        StageNetlist thirdStage,
        StageNetlist output,
        double tapGain,
        double presenceCap,
        (string First, string Second, string Third) switches
    ) =>
        new(
            name,
            [
                channels,
                new NetworkStageDesign("V2B", SecondStage(rails.V2B)),
                new NetworkStageDesign("V2A", thirdStage),
                new NetworkStageDesign("V3B", MixerStage(rails.V3)),
                new NetworkStageDesign("V3A", output)
            ],
            Power(
                rails.Inverter,
                new(tapGain, 47e3, 4.7e3, new(5e3, PotTaper.Linear), presenceCap) {
                    Deep = DeepSwitchDesign.Gyrator(SwitchRole.Bright, 10e-6, 270, 330, 10e-6, 10e3)
                }
            ),
            -7
        ) {
            Panel = AmpPanel.Standard with {
                Master = "Volume",
                Depth = null,
                Cut = null,
                Bright = "Deep",
                Switch1 = switches.First,
                Switch2 = switches.Second,
                Switch3 = switches.Third
            }
        };

    /// <summary>
    ///     The 100 W power amp of both boards: 22 nF and 1M into a long-tail pair (82k / 100k with 47 pF across, 470 Ω
    ///     and 10k tail over 4k7) on <paramref name="inverterSupply" />, 22 nF out, 220k leaks, 5k6 stoppers, four
    ///     EL34 with 1k screens; the supply's 330 µF pairs, 270 Ω to the screens' 100 µF pairs and 4k7 to the
    ///     inverter's 22 µF. Neither drawing prints a voltage or the transformers' data: the 470 V B+, 35 mA a tube
    ///     and 1.7k primary are guesses.
    /// </summary>
    static PowerAmpDesign Power(double inverterSupply, FeedbackDesign feedback) =>
        new(
            "4 x EL34",
            PentodeModel.EL34,
            2,
            470,
            1e3,
            0.035,
            false,
            0,
            100,
            new LongTailPairDesign(TriodeModel.Ecc83, inverterSupply, 82e3, 100e3, 10e3, 470),
            22e-9,
            220e3,
            5.6e3,
            0,
            new(1e6, PotTaper.Linear),
            feedback,
            true,
            new(1.7e3),
            SpeakerDesign.Closed4x12,
            new(25, 165e-6, 270, 50e-6, 4.7e3, 22e-6),
            InverterInput: new(22e-9, 1e6)
        );
}
