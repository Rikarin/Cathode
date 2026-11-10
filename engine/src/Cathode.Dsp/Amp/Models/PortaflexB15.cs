using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Ampeg Portaflex B-15-N (1968, about 30 W), one of its two channels, which are the same circuit (channel one's
///     part names here): V1 (6SL7: 470k plate, 5.6k unbypassed) with the Normal
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
///         which loads the mix node through its 270k as about 150k more; and the speaker load: the closed 8 × 10
///         preset, the library's only bass cabinet. A 1 × 15 load of its own is not built (#310): the 1968 cabinet is
///         a ported one (ggjaguar.com/b-15n.htm: "a Thiele design cabinet, introduced in 1968", 24 × 21 × 14 inches,
///         against the "other port designs" before it), not a sealed box, and no impedance curve or Thiele-Small
///         figures for its 15-inch speaker of the time were found (Jensen's current C15N sheet, Fs 73 Hz, is a guitar
///         speaker's). The screens have no resistors on the drawing (1 Ω here).
///     </para>
///     <para>
///         Channel two (V2, another 6SL7) is channel one again on the drawing, part for part: the same input network,
///         Ultra Lo and Ultra Hi, volume, second half and Baxandall into its own 270k. So this type is either channel,
///         and a channel switch would change nothing. Both channels played at once is <see cref="Jumped" />.
///     </para>
///     <para>
///         The jacks, as drawn: each channel has a Bright jack (100k ‖ 5 nF to the grid) and a Normal jack (47k to the
///         grid). <see cref="Jumped" /> has the guitar in channel one's Normal jack and a cable from channel one's
///         Bright jack to channel two's Normal jack, so channel two's grid hangs on channel one's through 100k ‖ 5 nF
///         and 47k. Both channels then run in one netlist, every part of each, into the two 270k mixing resistors:
///         nothing of channel two is a stand-in there. Its panel gives channel two its own controls on the block's
///         free ones: Volume 2 (Control 1), Bass 2 (the Middle knob, which the amp lacks), Treble 2 (Control 2), Ultra
///         Hi 2 (the Bright switch: the jumper takes the place of the input choice) and Ultra Lo 2 (Switch 3).
///     </para>
/// </remarks>
public static class PortaflexB15 {
    const double Open = 1e12;
    const double Wire = 1;
    const double Preamp = 420;

    /// <summary>
    ///     The jumped type's output trim: two channels at the same settings put about 4 dB more on the mix node than
    ///     one beside its idle neighbour (each 270k then works against the other's instead of against 420k).
    /// </summary>
    const double JumpedTrim = -9;

    static readonly ChannelControls ChannelOne =
        new(PotRole.Gain, PotRole.Bass, PotRole.Treble, SwitchRole.Switch1, SwitchRole.Switch2);

    static readonly ChannelControls ChannelTwo =
        new(PotRole.Control1, PotRole.Middle, PotRole.Control2, SwitchRole.Bright, SwitchRole.Switch3);

    public static AmpDesign Design { get; } = new(
        "Portaflex B-15N",
        [InputStage(), VolumeStage(), ToneStack()],
        Power(),
        -5
    ) {
        Panel = new() {
            Gain = "Volume",
            Middle = null,
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
    ///     The B-15-N with its channels jumpered: the guitar in channel one, a cable on to channel two, each channel
    ///     with its own volume, tone controls and Ultra switches (see the remarks).
    /// </summary>
    public static AmpDesign Jumped { get; } = new(
        "Portaflex B-15N Jumped",
        [JumpedStage()],
        Power(),
        JumpedTrim
    ) {
        Panel = new() {
            Gain = "Volume 1",
            Bass = "Bass 1",
            Middle = "Bass 2",
            Treble = "Treble 1",
            Presence = null,
            Depth = null,
            Cut = null,
            Master = null,
            Bright = "Ultra Hi 2",
            Switch1 = "Ultra Hi 1",
            Switch2 = "Ultra Lo 1",
            Switch3 = "Ultra Lo 2",
            Control1 = "Volume 2",
            Control2 = "Treble 2"
        }
    };

    /// <summary>The power amp, the same behind one channel or both.</summary>
    static PowerAmpDesign Power() =>
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
        );

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
        FirstHalf(net, rail, grid, plate, cathode);
        net.Output = plate;
        return new("V1A", net);
    }

    /// <summary>
    ///     The volume network on V1A's plate (a Thévenin cut: the network is linear, the plate's resistance carries its
    ///     load) and V1B (<see cref="SecondHalf" />); out at its plate.
    /// </summary>
    static NetworkStageDesign VolumeStage() {
        var net = new StageNetlist();
        var rail = net.Node();
        net.Supply(rail, Preamp);
        net.Output = SecondHalf(net, net.Input, rail, ChannelOne);
        return new("V1B", net);
    }

    /// <summary>
    ///     The Baxandall (<see cref="Baxandall" />) and the 270k mixing resistor, the idle channel's beside it (270k
    ///     into about 150k). Out at the mix node (the inverter's grid).
    /// </summary>
    static NetworkStageDesign ToneStack() {
        var net = new StageNetlist();
        var stack = AmpegParts.StackNodes.In(net);
        var (mix, idle) = (net.Node(), net.Node());
        var treble = Baxandall(net, net.Input, stack, ChannelOne);
        net.Resistor(treble, mix, 270e3);
        net.Resistor(mix, idle, 270e3);
        net.Resistor(idle, StageNetlist.Ground, 150e3);
        net.Output = mix;
        return new("Baxandall", net);
    }

    /// <summary>
    ///     Both channels in one netlist (<see cref="Jumped" />): the guitar through channel one's Normal 47k to V1A's
    ///     grid, on through channel one's Bright 100k ‖ 5 nF, the cable and channel two's Normal 47k to V2A's grid;
    ///     each channel's two 6SL7 halves, volume network and Baxandall with its own knobs and switches; the two 270k
    ///     into the mix node. Out at the mix node (the inverter's grid).
    /// </summary>
    static NetworkStageDesign JumpedStage() {
        var net = new StageNetlist();
        var (rail, mix, link) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        var (grid1, plate1, cathode1) = (net.Node(), net.Node(), net.Node());
        var (grid2, plate2, cathode2) = (net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, grid1, 47e3);
        net.Resistor(grid1, link, 100e3);
        net.Capacitor(grid1, link, 5e-9);
        net.Resistor(link, grid2, 47e3);
        FirstHalf(net, rail, grid1, plate1, cathode1);
        FirstHalf(net, rail, grid2, plate2, cathode2);
        foreach (var (plate, channel) in new[] { (plate1, ChannelOne), (plate2, ChannelTwo) }) {
            var driven = SecondHalf(net, plate, rail, channel);
            net.Resistor(Baxandall(net, driven, AmpegParts.StackNodes.In(net), channel), mix, 270e3);
        }

        net.Output = mix;
        return new("V1-V2", net) { MaxIterations = 64 };
    }

    /// <summary>
    ///     A channel's first 6SL7 half: its 5.6M grid leak, 470k plate resistor and 5.6k unbypassed cathode resistor.
    /// </summary>
    static void FirstHalf(StageNetlist net, int rail, int grid, int plate, int cathode) {
        net.Resistor(grid, StageNetlist.Ground, 5.6e6);
        net.Resistor(rail, plate, 470e3);
        net.Resistor(cathode, StageNetlist.Ground, 5.6e3);
        net.Triode(plate, grid, cathode, TriodeModel.FiveSevenFiveOne);
    }

    /// <summary>
    ///     A channel's volume network and second 6SL7 half, from the first half's plate at <paramref name="from" />:
    ///     0.1 µF into 0.01 µF and the 1M volume, the Ultra Lo shunt (0.02 µF and 39k to ground, with 1M in series
    ///     while it is off), the 500 pF Ultra Hi cap from the volume's top to its wiper, and the second half (220k,
    ///     2.2k unbypassed). Returns its plate.
    /// </summary>
    static int SecondHalf(StageNetlist net, int from, int rail, in ChannelControls channel) {
        var (coupled, top, shunt, low) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(from, coupled, 0.1e-6);
        net.Capacitor(coupled, top, 0.01e-6);
        net.Capacitor(coupled, shunt, 0.02e-6);
        net.Resistor(shunt, low, 39e3);
        net.Resistor(low, StageNetlist.Ground, 1e6);
        net.SwitchedResistor(low, StageNetlist.Ground, Open, Wire, channel.UltraLo);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), channel.Volume);
        net.SwitchedCapacitor(top, wiper, 500e-12, channel.UltraHi, CapacitorRole.Bright);

        var (plate2, cathode2) = (net.Node(), net.Node());
        net.Resistor(rail, plate2, 220e3);
        net.CathodeResistor(cathode2, StageNetlist.Ground, 2.2e3);
        net.Triode(plate2, wiper, cathode2, TriodeModel.FiveSevenFiveOne);
        net.Capacitor(wiper, plate2, Ab763.GridPlate);
        return plate2;
    }

    /// <summary>
    ///     A channel's passive Baxandall from <paramref name="from" /> (<see cref="AmpegParts.Baxandall" /> with 470
    ///     pF to the treble pot's top, 4.7 nF to its bottom and 270k from the bass wiper to ground). Returns the
    ///     treble wiper.
    /// </summary>
    static int Baxandall(StageNetlist net, int from, in AmpegParts.StackNodes nodes, in ChannelControls channel) =>
        AmpegParts.Baxandall(net, from, nodes, channel.Bass, channel.Treble, 470e-12, 4.7e-9, 270e3);

    /// <summary>The knobs and switches a channel's circuit answers to.</summary>
    readonly record struct ChannelControls(
        PotRole Volume,
        PotRole Bass,
        PotRole Treble,
        SwitchRole UltraHi,
        SwitchRole UltraLo
    );
}
