using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall JCM800 2210, the 100 W split-channel head, and its 50 W sister the 2205, both channels (factory
///     drawings "2210 Preamp" issue 2, 10-5-88, file 2210PRE.DGM, PCB JMB3, and "2210 STD Output Stage &amp; PSU" issue
///     2, 4-5-88; "2205 Preamp" and "2205 STD Output Stage &amp; PSU" of the same issues; DrTube's 2210prem.gif,
///     2210pwrm.gif, 2205prem.gif and 2205pwrm.gif, the same sheets as schematicheaven's PDFs). V1A (0.047 µF in, 1M;
///     100k, 2k7 with 0.47 µF) drives both channels from its plate. Normal channel: a two-knob tone network (22 nF
///     onto the 1M Bass pot with 100k under it, 5 nF onto its wiper; 100k to the 1M Treble's wiper and 330k on to the
///     1M Volume's top; 0.01 µF from there to the Treble's top, 2 nF under its foot), 100 pF across the Volume's upper
///     part, V2a (150k, 10k unbypassed), 0.022 µF and 470k into V2b (100k, 1k5 unbypassed), 0.022 µF and 150k onto
///     100k with 470 pF, and 470k to the Master's top. Boost channel: 2 nF and 100k from V1A's plate onto the front
///     gang of the 1M Gain pot, 0.047 µF and 220k into V1B (470k plate, 10k cathode with a 1N4007 across it), 0.022 µF
///     onto the back gang, 27k with 2 nF across it onto a W005 bridge with a 1N4007 across its DC terminals (three
///     junctions each way to ground) and the 1M Volume, whose wiper drives V3A (100k with 2 nF to the rail, 1k5 with
///     22 µF; 220 pF grid to cathode, 47 pF on the 2205); 10k from its plate into the tone stack (470 pF, 56k, 0.022
///     µF, 22 nF; 220k linear Treble, 100k Middle and 4k7 + 1M Bass as rheostats, 2 nF under the Treble's foot) and
///     100k from the Treble's wiper to the Master's top. Both: the 1M Master, 470k with 100 pF onto 10k (the effects
///     send), 0.22 µF and 1M into V4A (100k, 1k5 unbypassed), 0.022 µF into a 12AX7 long-tail pair (82k and 100k with
///     100 pF between the plates, 470, 22k tail on the 4k7 shunt, 330k leak), 0.022 µF out to 220k and 5k6, four EL34
///     (two on the 2205) with 1k screens, 100k of feedback from the 4 Ω tap with the 22k Presence and 0.1 µF.
/// </summary>
/// <remarks>
///     <para>
///         Channel switching is a CA3046 transistor array whose transistors short three points to ground through 0.22
///         µF: V2a's grid (the Normal channel muted), V1B's grid and V3A's grid (the Boost channel muted). Both
///         channels' outputs stay joined at the Master's top through their mixing resistors (470k and 100k), so the
///         idle channel loads the playing one: the Normal channel's 470k works into 100k and the Boost stack behind
///         it, which makes it the quieter channel. Each channel's netlists therefore hold the idle
///         channel's passive parts, its pots at half rotation and its muted tube's plate as that plate's small-signal
///         resistance to ground.
///     </para>
///     <para>
///         V1B's cathode diode conducts at rest (on the 10k alone the cathode would sit at 2.7 V), so it holds
///         the cathode near 0.5 V like an LED bias: V1B runs at high gain and close to grid conduction, on a 470k
///         load. The back gang of the Gain pot sets how hard the bridge is driven, the front gang how hard V1B is.
///     </para>
///     <para>
///         Not on the drawings, so guesses: every DC voltage (the sheets have none). B+ is taken as the 2203's 470 V
///         (2205: the 2204's 460 V) with its idle current, output transformer (1.7k plate to plate; 3.2k on the 2205)
///         and choke resistance. The whole preamp, V4 and the inverter hang from one node (the drawing's X and A, one
///         50 µF) through one 10k 1 W from the screens' node; at the idle currents of the model's tubes (9.7 mA: the
///         seven triodes on it, 7.0 mA, and the inverter, 2.7 mA) that node sits at 372 V from 469 V at the screens
///         (a computed figure, not a measured one; 364 V from 459 V on the 2205). The
///         direction of the Bass and Middle rheostats (resistance rising with the knob) follows from their labels.
///         The pots are log but the 220k Treble ("All pots LOG unless stated otherwise").
///     </para>
///     <para>
///         Left out: the reverb (V3B, the tank and V4B; its drive's 220k, 0.01 µF and 1M load the Master's wiper, and
///         its 56k return resistor and 100k Reverb pot load the send node, as the Reverb at 0), the effects loop
///         (send and return linked), the DI output, the 100 pF between the inverter's plates, and the inverter's
///         second grid leak (120k where the driven grid has 330k: the model's long-tail pair has one leak). Cuts:
///         the Boost channel between its Volume's wiper and V3A's grid, and both channels at the Master's wiper.
///         V1A is in each channel's first netlist: its partly bypassed cathode makes its plate a poor Thévenin
///         source.
///     </para>
///     <para>
///         Panel: Switch 1 selects the Normal channel (off is Boost, the amp's lead sound). Gain is the Boost
///         channel's Gain and the Normal channel's Volume; Control 1 the Boost channel's Volume; Bass and Treble are
///         the playing channel's; Middle is the Boost channel's only. The Boost Volume defaults to 10 on this model
///         (<see cref="AmpPanel.Defaults" />): it stands between the clipper and the power amp like a second master,
///         and with four log pots in a row at the block's 5 (Gain's two gangs, Boost Volume, Master) and the send's
///         470k / 10k pad the amp plays 36 dB under a 2203 at the same settings. Even full up, the Boost channel
///         drives the power amp less hard than a 2203's preamp does: the clipper holds what reaches V3A to 1.5 V.
///     </para>
/// </remarks>
public static class JCM2210 {
    /// <summary>The preamp node (the drawing's X and A), through 10k from the screens' node.</summary>
    const double Rail = 372;

    /// <summary>The 2205's, from its 460 V.</summary>
    const double Rail50 = 364;

    /// <summary>What the preamp, V4 and the inverter draw through the 10k at <see cref="Rail" />.</summary>
    const double PreampDraw = 9.7e-3;

    /// <summary>The 2205's, at <see cref="Rail50" />.</summary>
    const double PreampDraw50 = 9.5e-3;

    static readonly Pot Log1M = new(1e6, PotTaper.Audio);

    /// <summary>The 2210: four EL34.</summary>
    public static AmpDesign Design { get; } = new(
        "JCM800 2210",
        Preamp(Rail, 220e-12),
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
            Inverter(Rail),
            22e-9,
            220e3,
            5.6e3,
            0,
            new(1e6, PotTaper.Linear),
            MarshallParts.JCM800Feedback with { Presence = new(22e3, PotTaper.Audio) },
            true,
            new(1.7e3),
            SpeakerDesign.Closed4x12,
            new(25, 100e-6, 100, 100e-6, 10e3, 50e-6, PreampDraw),
            InverterInput: new(22e-9, 330e3)
        ),
        -5
    ) { Panel = Panel, PreampSupply = new([]) };

    /// <summary>
    ///     The 2205: two EL34, single 50 µF cans at the reservoir and the screens, 47 pF on V3A's grid.
    /// </summary>
    public static AmpDesign Head50 { get; } = new(
        "JCM800 2205",
        Preamp(Rail50, 47e-12),
        Design.Power with {
            Name = "2 x EL34",
            TubesPerSide = 1,
            Supply = 460,
            IdleCurrent = 0.040,
            RatedPower = 50,
            PhaseInverter = Inverter(Rail50),
            Transformer = new(3.2e3),
            PowerSupply = new(40, 50e-6, 100, 50e-6, 10e3, 50e-6, PreampDraw50)
        },
        -4
    ) { Panel = Panel, PreampSupply = new([]) };

    static AmpPanel Panel =>
        AmpPanel.Standard with {
            Depth = null,
            Cut = null,
            Bright = null,
            Switch1 = "Normal",
            Control1 = "Boost Volume",
            Defaults = new Dictionary<string, float> { ["control1"] = 10 }
        };

    /// <summary>The long-tail pair: 82k and 100k, 470, and the 22k tail standing on the 4k7 feedback shunt.</summary>
    static LongTailPairDesign Inverter(double rail) => new(TriodeModel.Ecc83, rail, 82e3, 100e3, 22e3 + 4.7e3, 470);

    static AmpStageDesign[] Preamp(double rail, double gridCap) => [
        new ChannelSwitchStageDesign(
            "Channel",
            SwitchRole.Switch1,
            [
                new NetworkStageDesign("V1", BoostGain(rail)),
                new NetworkStageDesign("V3A", BoostStack(rail, gridCap))
            ],
            [new NetworkStageDesign("V1A + V2", Normal(rail))]
        ),
        new NetworkStageDesign("V4A", Recovery(rail))
    ];

    /// <summary>
    ///     V1A on <paramref name="rail" />: 0.047 µF from the jack, 1M; 100k, 2k7 with 0.47 µF. Returns the plate,
    ///     which both channels' networks load.
    /// </summary>
    static int Input(StageNetlist net, int rail) {
        var (grid, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, grid, 47e-9);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        net.Resistor(rail, plate, 100e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.7e3);
        net.Capacitor(cathode, StageNetlist.Ground, 0.47e-6);
        return plate;
    }

    /// <summary>
    ///     The Normal channel from the jack to the Master's wiper: V1A, the tone network and Volume, V2a, V2b and the
    ///     mixing resistor, with the muted Boost channel's parts on V1A's plate and on the Master's top.
    /// </summary>
    static StageNetlist Normal(double supply) {
        var net = new StageNetlist();
        var (rail, plate1, cathode1, grid2, plate2, cathode2) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, supply);
        var first = Input(net, rail);
        var volume = NormalNetwork(net, first, false);
        net.Resistor(rail, plate1, 150e3);
        net.Triode(plate1, volume, cathode1, TriodeModel.Ecc83);
        net.CathodeResistor(cathode1, StageNetlist.Ground, 10e3);
        net.Capacitor(plate1, grid2, 22e-9);
        net.Resistor(grid2, StageNetlist.Ground, 470e3);
        net.Resistor(rail, plate2, 100e3);
        net.Triode(plate2, grid2, cathode2, TriodeModel.Ecc83);
        net.CathodeResistor(cathode2, StageNetlist.Ground, 1.5e3);

        // The Boost channel, muted: V1B's grid at ground through 0.22 µF, V3A's plate at rest behind the stack.
        net.Capacitor(GainBranch(net, first, true), StageNetlist.Ground, 0.22e-6);
        var top = net.Node();
        NormalMix(net, plate2, top);
        var idle = IdlePlate(net, supply, 100e3, 1.5e3, true);
        net.Capacitor(idle, StageNetlist.Ground, 2e-9);
        BoostMix(net, idle, top, true);
        net.Output = Master(net, top);
        return net;
    }

    /// <summary>
    ///     The Boost channel from the jack to its Volume's wiper: V1A, the Gain pot's front gang, V1B with its cathode
    ///     diode, the back gang and the bridge clipper; the muted Normal channel's tone network loads V1A's plate, its
    ///     Volume's wiper at ground through 0.22 µF.
    /// </summary>
    static StageNetlist BoostGain(double supply) {
        var net = new StageNetlist();
        var (rail, plate, cathode, top, clip) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, supply);
        var first = Input(net, rail);
        net.Capacitor(NormalNetwork(net, first, true), StageNetlist.Ground, 0.22e-6);
        var grid = GainBranch(net, first, false);
        net.Resistor(rail, plate, 470e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.CathodeResistor(cathode, StageNetlist.Ground, 10e3);
        net.Diodes(cathode, StageNetlist.Ground, DiodeModel.Rectifier, 1, 0);
        net.Capacitor(plate, top, 22e-9);
        var wiper = net.Pot(top, StageNetlist.Ground, Log1M, PotRole.Gain);
        net.Resistor(wiper, clip, 27e3);
        net.Capacitor(wiper, clip, 2e-9);
        net.Diodes(clip, StageNetlist.Ground, DiodeModel.Rectifier, 3, 3);
        net.Output = net.Pot(clip, StageNetlist.Ground, Log1M, PotRole.Control1);
        return net;
    }

    /// <summary>
    ///     The Boost channel from its Volume's wiper to the Master's wiper: V3A (<paramref name="gridCap" /> from its
    ///     grid to its cathode), its tone stack and mixing resistor, with the muted Normal channel's output network on
    ///     the Master's top.
    /// </summary>
    static StageNetlist BoostStack(double supply, double gridCap) {
        var net = new StageNetlist();
        var (rail, plate, cathode, top) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, supply);
        net.Resistor(rail, plate, 100e3);
        net.Capacitor(rail, plate, 2e-9, CapacitorRole.Plate);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Capacitor(net.Input, cathode, gridCap);
        BoostMix(net, plate, top, false);
        NormalMix(net, IdlePlate(net, supply, 100e3, 1.5e3, false), top);
        net.Output = Master(net, top);
        return net;
    }

    /// <summary>
    ///     From the Master's wiper to V4A's plate: 470k with 100 pF onto the send node (10k to ground; 56k and the
    ///     100k Reverb pot's track, the reverb off), 0.22 µF and 1M into V4A (100k, 1k5 unbypassed). The inverter's
    ///     own 0.022 µF and 330k are the power amp's input network.
    /// </summary>
    static StageNetlist Recovery(double supply) {
        var net = new StageNetlist();
        var (send, grid, rail, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, send, 470e3);
        net.Capacitor(net.Input, send, 100e-12);
        net.Resistor(send, StageNetlist.Ground, 10e3);
        net.Resistor(send, StageNetlist.Ground, 56e3 + 100e3);
        net.Capacitor(send, grid, 0.22e-6);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        net.Supply(rail, supply);
        net.Resistor(rail, plate, 100e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Output = net.DcBlock(plate);
        return net;
    }

    /// <summary>
    ///     The Normal channel's tone network and Volume on V1A's <paramref name="plate" />; returns the Volume's
    ///     wiper. <paramref name="idle" />: with the pots at half rotation (the Boost channel plays).
    /// </summary>
    static int NormalNetwork(StageNetlist net, int plate, bool idle) {
        var (bassTop, bassFoot, trebleTop, trebleFoot, top) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, bassTop, 22e-9, CapacitorRole.Stack);
        var bass = Turned(net, bassTop, bassFoot, Log1M, idle ? null : PotRole.Bass);
        net.Resistor(bassFoot, StageNetlist.Ground, 100e3);
        net.Capacitor(plate, bass, 5e-9, CapacitorRole.Stack);
        var treble = Turned(net, trebleTop, trebleFoot, Log1M, idle ? null : PotRole.Treble);
        net.Resistor(bass, treble, 100e3);
        net.Resistor(treble, top, 330e3);
        net.Capacitor(top, trebleTop, 10e-9, CapacitorRole.Stack);
        net.Capacitor(trebleFoot, StageNetlist.Ground, 2e-9, CapacitorRole.Stack);
        var volume = Turned(net, top, StageNetlist.Ground, Log1M, idle ? null : PotRole.Gain);
        net.Capacitor(top, volume, 100e-12, CapacitorRole.Bright);
        return volume;
    }

    /// <summary>
    ///     The Boost channel's input on V1A's <paramref name="plate" />: 2 nF and 100k onto the Gain pot's front gang,
    ///     0.047 µF from its wiper to V1B's grid with its 220k leak; returns the grid.
    /// </summary>
    static int GainBranch(StageNetlist net, int plate, bool idle) {
        var (coupled, top, grid) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, coupled, 2e-9);
        net.Resistor(coupled, top, 100e3);
        var wiper = Turned(net, top, StageNetlist.Ground, Log1M, idle ? null : PotRole.Gain);
        net.Capacitor(wiper, grid, 47e-9);
        net.Resistor(grid, StageNetlist.Ground, 220e3);
        return grid;
    }

    /// <summary>
    ///     The Normal channel's output network from V2b's <paramref name="plate" />: 0.022 µF and 150k onto 100k with
    ///     470 pF, and 470k from there to the Master's <paramref name="top" />.
    /// </summary>
    static void NormalMix(StageNetlist net, int plate, int top) {
        var (coupled, tap) = (net.Node(), net.Node());
        net.Capacitor(plate, coupled, 22e-9);
        net.Resistor(coupled, tap, 150e3);
        net.Resistor(tap, StageNetlist.Ground, 100e3);
        net.Capacitor(tap, StageNetlist.Ground, 470e-12);
        net.Resistor(tap, top, 470e3);
    }

    /// <summary>
    ///     The Boost channel's tone stack from V3A's <paramref name="plate" />: 10k, then 470 pF onto the 220k linear
    ///     Treble's top and 56k to the slope node; 0.022 µF from there to the Treble's foot, which has 2 nF and 4k7 +
    ///     the 1M Bass to ground; 22 nF from the slope node to the 100k Middle, a rheostat to ground; 100k from the
    ///     Treble's wiper to the Master's <paramref name="top" />.
    /// </summary>
    static void BoostMix(StageNetlist net, int plate, int top, bool idle) {
        var (source, trebleTop, slope, foot, bass, middle) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        var (bassPot, middlePot) = (Log1M, new Pot(100e3, PotTaper.Audio));
        net.Resistor(plate, source, 10e3);
        net.Capacitor(source, trebleTop, 470e-12, CapacitorRole.Stack);
        net.Resistor(source, slope, 56e3);
        net.Capacitor(slope, foot, 22e-9, CapacitorRole.Stack);
        net.Capacitor(foot, StageNetlist.Ground, 2e-9, CapacitorRole.Stack);
        net.Resistor(foot, bass, 4.7e3);
        net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
        if (idle) {
            net.Resistor(bass, StageNetlist.Ground, bassPot.Fraction(0.5) * bassPot.Ohms);
            net.Resistor(middle, StageNetlist.Ground, middlePot.Fraction(0.5) * middlePot.Ohms);
        } else {
            net.Rheostat(bass, StageNetlist.Ground, bassPot, PotRole.Bass);
            net.Rheostat(middle, StageNetlist.Ground, middlePot, PotRole.Middle);
        }

        var treble = Turned(net, trebleTop, foot, new(220e3, PotTaper.Linear), idle ? null : PotRole.Treble);
        net.Resistor(treble, top, 100e3);
    }

    /// <summary>
    ///     The 1M Master from the mixing node <paramref name="top" />, its wiper (returned) loaded by the reverb
    ///     drive's 220k, 0.01 µF and 1M.
    /// </summary>
    static int Master(StageNetlist net, int top) {
        var (drive, driveGrid) = (net.Node(), net.Node());
        var wiper = net.Pot(top, StageNetlist.Ground, Log1M, PotRole.Master);
        net.Resistor(wiper, drive, 220e3);
        net.Capacitor(drive, driveGrid, 10e-9);
        net.Resistor(driveGrid, StageNetlist.Ground, 1e6);
        return wiper;
    }

    /// <summary>A pot turned by <paramref name="role" />, or at half rotation where no knob turns it (null).</summary>
    static int Turned(StageNetlist net, int top, int bottom, Pot pot, PotRole? role) =>
        role is { } knob ? net.Pot(top, bottom, pot, knob) : QuickRod.Idle(net, top, bottom, pot);

    /// <summary>
    ///     A muted stage's plate as the playing channel sees it: a node with the plate's small-signal resistance to
    ///     ground, the plate resistor in parallel with r_p (plus (μ + 1) times the cathode resistor where it is not
    ///     <paramref name="bypassed" />), at the stage's rest point.
    /// </summary>
    static int IdlePlate(StageNetlist net, double supply, double plateResistor, double cathodeResistor, bool bypassed) {
        var (_, plate, cathode) =
            OperatingPoint.CommonCathode(TriodeModel.Ecc83, 0, supply, plateResistor, cathodeResistor);
        var (rp, mu) = OperatingPoint.SmallSignal(TriodeModel.Ecc83, -cathode, plate - cathode);
        var tube = bypassed ? rp : rp + (mu + 1) * cathodeResistor;
        var node = net.Node();
        net.Resistor(node, StageNetlist.Ground, plateResistor * tube / (plateResistor + tube));
        return node;
    }
}
