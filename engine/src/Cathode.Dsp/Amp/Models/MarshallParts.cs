using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Parts shared by the Marshall-family models (docs/06 §3.2): their tone stacks, read from the factory drawings,
///     and the circuits several of them use.
/// </summary>
public static class MarshallParts {
    const double Closed = 10; // a closed switch or jack contact
    const double Open = 1e12;

    /// <summary>
    ///     JCM800 2203/2204 (drawings 2203 STD and 2204 STD, issue 4): 470 pF, 33k, 22 nF, 22 nF; 220k linear treble,
    ///     1M log bass, 22k linear middle.
    /// </summary>
    public static ToneStackDesign JCM800Stack { get; } = new(
        "JCM800",
        470e-12,
        33e3,
        22e-9,
        22e-9,
        new(220e3, PotTaper.Linear),
        new(1e6, PotTaper.Audio),
        new(22e3, PotTaper.Linear)
    );

    /// <summary>
    ///     1959 Super Lead (Unicord drawing 70-6-11): 500 pF, 33k, 22 nF, 22 nF; 250k treble (linear assumed), 1M log
    ///     bass, 25k linear middle.
    /// </summary>
    public static ToneStackDesign SuperLeadStack { get; } = new(
        "Super Lead",
        500e-12,
        33e3,
        22e-9,
        22e-9,
        new(250e3, PotTaper.Linear),
        new(1e6, PotTaper.Audio),
        new(25e3, PotTaper.Linear)
    );

    /// <summary>
    ///     JTM45 (drawing JTM45.DGM issue 7): 270 pF, 56k, 20 nF, 20 nF; 250k-B treble, 1M-A bass, 25k-B middle.
    /// </summary>
    public static ToneStackDesign JTM45Stack { get; } = new(
        "JTM45",
        270e-12,
        56e3,
        20e-9,
        20e-9,
        new(250e3, PotTaper.Linear),
        new(1e6, PotTaper.Audio),
        new(25e3, PotTaper.Linear)
    );

    /// <summary>
    ///     The panel of a two-channel Marshall without a master: the jumper and the normal input on switches 1 and 2.
    /// </summary>
    public static AmpPanel TwoChannelPanel { get; } = AmpPanel.Standard with {
        Gain = "Treble Vol",
        Control1 = "Normal Vol",
        Master = null,
        Cut = null,
        Depth = null,
        Bright = null,
        Switch1 = "Jumper",
        Switch2 = "Normal Input"
    };

    /// <summary>
    ///     The JCM800's power-amp feedback (2203/2204 STD output stage): 100k from the 4 Ω tap into 4k7, presence 22k
    ///     linear with 0.1 µF.
    /// </summary>
    public static FeedbackDesign JCM800Feedback { get; } =
        new(Math.Sqrt(0.5), 100e3, 4.7e3, new(22e3, PotTaper.Linear), 0.1e-6, 80);

    /// <summary>
    ///     The panel of a JCM800-style master-volume amp: no Cut, Depth or Bright; the Low input on switch 1.
    /// </summary>
    public static AmpPanel MasterVolumePanel { get; } = AmpPanel.Standard with {
        Gain = "Preamp",
        Cut = null,
        Depth = null,
        Bright = null,
        Switch1 = "Low Input"
    };

    /// <summary>
    ///     A cathode follower DC-coupled to the previous plate, driving a TMB stack whose treble wiper is the output:
    ///     an amp without a master volume, whose stack feeds the phase inverter. As a <see cref="ToneStackStage" />
    ///     with a fixed <paramref name="load" /> (the inverter's grid leak) in place of the load pot, or none (0) where
    ///     the power amp models the inverter's input network (<see cref="InverterInputDesign" />).
    /// </summary>
    public static NetworkStageDesign FollowerStack(
        string name,
        double supply,
        double cathodeResistor,
        ToneStackDesign stack,
        double load
    ) {
        var net = new StageNetlist();
        var (rail, cathode, top, slope, bottom, middle) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(),
            net.Node());
        net.Supply(rail, supply);
        net.Triode(rail, net.Input, cathode, TriodeModel.Ecc83);
        net.Resistor(cathode, StageNetlist.Ground, cathodeResistor);
        net.Capacitor(cathode, top, stack.TrebleCap, CapacitorRole.Stack);
        net.Resistor(cathode, slope, stack.SlopeResistor);
        net.Capacitor(slope, bottom, stack.BassCap, CapacitorRole.Stack);
        net.Capacitor(slope, middle, stack.MiddleCap, CapacitorRole.Stack);
        var wiper = net.Pot(top, bottom, stack.Treble, PotRole.Treble);
        net.Rheostat(bottom, middle, stack.Bass, PotRole.Bass);
        net.Rheostat(middle, StageNetlist.Ground, stack.Middle, PotRole.Middle);
        if (load > 0) {
            net.Resistor(wiper, StageNetlist.Ground, load);
        }

        net.Output = wiper;
        return new(name, net);
    }

    /// <summary>
    ///     The JCM800's first stage and its input jacks (2203 STD / 2204 STD preamp): High input through 68k (1M at the
    ///     jack) to V1A (100k plate, 2k7 with 0.68 µF, 100 pF plate to cathode), 22 nF out; the Low input (switch 1)
    ///     takes the guitar past V1A straight into the 470k/470 pF attenuator, as its jack's switching contact does;
    ///     then the 1M log preamp volume with its 1 nF bright cap. The output is the volume's wiper, which feeds V1B's
    ///     grid.
    /// </summary>
    public static NetworkStageDesign JCM800Input(double supply) {
        var net = new StageNetlist();
        var (grid, plate, cathode, rail, coupled, attenuator, top) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Resistor(net.Input, grid, 68e3);
        net.Supply(rail, supply);
        net.Resistor(rail, plate, 100e3);
        net.Resistor(cathode, StageNetlist.Ground, 2.7e3);
        net.Capacitor(cathode, StageNetlist.Ground, 0.68e-6);
        net.Capacitor(plate, cathode, 100e-12);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 22e-9);
        Jack(net, coupled, attenuator, SwitchRole.Switch1);
        net.Resistor(attenuator, top, 470e3);
        net.Capacitor(attenuator, top, 470e-12);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, wiper, 1e-9, CapacitorRole.Bright);
        net.Output = wiper;
        return new("V1A", net);
    }

    /// <summary>
    ///     A Low input jack's switching contact: <paramref name="normal" /> reaches <paramref name="node" /> while
    ///     <paramref name="role" /> is off, the guitar (the netlist's input) while it is on.
    /// </summary>
    public static void Jack(StageNetlist net, int normal, int node, SwitchRole role) {
        net.SwitchedResistor(normal, node, Closed, Open, role);
        net.SwitchedResistor(net.Input, node, 0, Closed, role);
    }

    /// <summary>
    ///     The JCM800 preamp after the first stage: V1B cold clipper (100k, 10k unbypassed), 22 nF into 470k with 470
    ///     pF over 470k, V2A (100k, 820 unbypassed), the V2B cathode follower (100k) on the JCM800 stack and the 1M log
    ///     master.
    /// </summary>
    public static AmpStageDesign[] JCM800Preamp(double firstSupply, double supply) => [
        JCM800Input(firstSupply),
        new TriodeStageDesign("V1B", TriodeModel.Ecc83, firstSupply, 100e3, 10e3),
        new TriodeStageDesign(
            "V2A",
            TriodeModel.Ecc83,
            supply,
            100e3,
            820,
            CouplingCap: 22e-9,
            SeriesResistor: 470e3,
            SeriesBypass: 470e-12,
            GridLeak: 470e3
        ),
        new ToneStackStageDesign(
            "V2B",
            TriodeModel.Ecc83,
            StackDriver.CathodeFollower,
            supply,
            100e3,
            JCM800Stack,
            new(1e6, PotTaper.Audio),
            PotRole.Master
        )
    ];

    /// <summary>
    ///     The first tube of a two-channel Marshall with separate cathodes (1959): both triodes with 100k plates, the
    ///     two channels' volumes mixed into the next grid through their mixing resistors; the output is that grid (the
    ///     mixing node). The guitar plays into the bright channel's high input (34k: both 68k stoppers in parallel, 1M
    ///     at the jack); switch 1 jumpers it into the normal channel as well, switch 2 moves it to the normal channel
    ///     alone. An idle channel's grid rests on its jack's 1M, and its volume still loads the mixing node, as in the
    ///     amp.
    /// </summary>
    public static NetworkStageDesign TwoChannelInput(double supply, ChannelDesign bright, ChannelDesign normal) {
        var net = new StageNetlist();
        var (rail, mix) = (net.Node(), net.Node());
        net.Supply(rail, supply);

        // The bright channel's input is closed unless switch 2 alone is on; the normal channel's when either is.
        var brightJack = net.Node();
        net.SwitchedResistor(net.Input, brightJack, Closed, Open, SwitchRole.Switch2);
        net.SwitchedResistor(net.Input, brightJack, 0, Closed, SwitchRole.Switch1);
        var normalJack = net.Node();
        net.SwitchedResistor(net.Input, normalJack, 0, Closed, SwitchRole.Switch1);
        net.SwitchedResistor(net.Input, normalJack, 0, Closed, SwitchRole.Switch2);
        foreach (var (channel, jack) in new[] { (bright, brightJack), (normal, normalJack) }) {
            var (grid, plate, cathode) = (net.Node(), net.Node(), net.Node());
            net.Resistor(jack, StageNetlist.Ground, 1e6);
            net.Resistor(jack, grid, 34e3);
            net.Resistor(rail, plate, 100e3);
            net.Resistor(cathode, StageNetlist.Ground, channel.CathodeResistor);
            net.Capacitor(cathode, StageNetlist.Ground, channel.CathodeBypass);
            net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
            Volume(net, plate, mix, channel);
        }

        net.Output = mix;
        return new("V1", net);
    }

    /// <summary>
    ///     The first tube of a two-channel Marshall whose triodes share a bypassed cathode (JTM45), as one triode: with
    ///     the shared cathode at AC ground and both grids fed the same, the two plates swing alike, so one triode (on
    ///     twice the cathode resistor, as each sees both currents) drives the active channels' volumes. An idle
    ///     channel's coupling cap hangs on the idle triode's plate: its rest voltage behind 100k in parallel with r_p.
    ///     Switches as
    ///     <see cref="TwoChannelInput" />. Jumpered, the one plate drives both volumes' 1M loads instead of each its
    ///     own, a fraction of a decibel at 100k.
    /// </summary>
    public static NetworkStageDesign SharedCathodeInput(
        double supply,
        double cathodeResistor,
        double cathodeBypass,
        ChannelDesign bright,
        ChannelDesign normal
    ) {
        var (_, restPlate, restCathode) =
            OperatingPoint.CommonCathode(TriodeModel.Ecc83, 0, supply, 100e3, 2 * cathodeResistor);
        var (rp, _) = OperatingPoint.SmallSignal(TriodeModel.Ecc83, -restCathode, restPlate - restCathode);
        var idle = 100e3 * rp / (100e3 + rp);
        var net = new StageNetlist();
        var (rail, idleRail, jack, grid, plate, cathode, mix) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, supply);
        net.Supply(idleRail, restPlate);
        net.Resistor(net.Input, jack, Closed);
        net.Resistor(jack, StageNetlist.Ground, 1e6);
        net.Resistor(jack, grid, 34e3);
        net.Resistor(rail, plate, 100e3);
        net.Resistor(cathode, StageNetlist.Ground, 2 * cathodeResistor);
        net.Capacitor(cathode, StageNetlist.Ground, cathodeBypass / 2);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);

        // Bright: driven unless switch 2 alone is on, then idle.
        var brightPlate = net.Node();
        net.SwitchedResistor(plate, brightPlate, Closed, Open, SwitchRole.Switch2);
        net.SwitchedResistor(plate, brightPlate, 0, Closed, SwitchRole.Switch1);
        var brightIdle = net.Node();
        net.SwitchedResistor(brightPlate, brightIdle, 0, Closed, SwitchRole.Switch2);
        net.SwitchedResistor(brightIdle, idleRail, idle, Open, SwitchRole.Switch1);
        Volume(net, brightPlate, mix, bright);

        // Normal: driven while either switch is on, else idle.
        var normalPlate = net.Node();
        net.SwitchedResistor(plate, normalPlate, 0, Closed, SwitchRole.Switch1);
        net.SwitchedResistor(plate, normalPlate, 0, Closed, SwitchRole.Switch2);
        var normalIdle = net.Node();
        net.SwitchedResistor(normalPlate, normalIdle, Closed, Open, SwitchRole.Switch1);
        net.SwitchedResistor(normalIdle, idleRail, idle, Open, SwitchRole.Switch2);
        Volume(net, normalPlate, mix, normal);

        net.Output = mix;
        return new("V1", net);
    }

    /// <summary>
    ///     A channel's coupling cap, volume (with its bright cap) and mixing resistor (with its bypass), plate to mix.
    /// </summary>
    static void Volume(StageNetlist net, int plate, int mix, ChannelDesign channel) {
        var top = net.Node();
        net.Capacitor(plate, top, channel.CouplingCap);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), channel.Volume);
        if (channel.BrightCap > 0) {
            net.Capacitor(top, wiper, channel.BrightCap, CapacitorRole.Bright);
        }

        net.Resistor(wiper, mix, channel.MixingResistor);
        if (channel.MixingBypass > 0) {
            net.Capacitor(wiper, mix, channel.MixingBypass);
        }
    }

    /// <summary>
    ///     One channel of a two-channel Marshall's first tube, as <see cref="TwoChannelInput" /> wires it: its cathode
    ///     network (unused where the channels share one), coupling cap, volume pot with an optional bright cap across
    ///     its upper half, mixing resistor with an optional bypass cap.
    /// </summary>
    public sealed record ChannelDesign(
        double CathodeResistor,
        double CathodeBypass,
        double CouplingCap,
        PotRole Volume,
        double BrightCap,
        double MixingResistor,
        double MixingBypass
    );
}
