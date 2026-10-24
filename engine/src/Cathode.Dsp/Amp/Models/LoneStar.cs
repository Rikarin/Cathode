using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Mesa/Boogie Lone Star, both channels. The input stage V1B (100k, 1.5k ‖ 15 µF) drives channel 1's or channel 2's
///     Mark-style stack (250 pF, 100k slope, 0.1 µF, 47 nF; 250k audio treble and bass, 10k linear middle) and its 1M
///     audio gain pot (120 pF and 82k in series across the upper half). Channel 2's Drive switch puts the drive stage in
///     front of its stack: 5 nF, 1M to ground, 220k into the 1M audio drive pot, its wiper on V1A's grid with 220k to
///     ground (100k, 1.5k ‖ 15 µF); its Thick switch adds 250 pF from the stack's input straight to the gain pot. Both
///     channels then share V2B (100k grid stopper, 100k, 1.5k ‖ 15 µF), the reverb mixer (47 nF, 2.2M ‖ 20 pF into
///     V2A's grid, loaded by 680k and the reverb return's 470k), V2A (100k, 1.5k ‖ 100 µF), the master (47 nF, 220k into
///     1M audio) with the passive presence on its wiper (1 nF into a 1M audio rheostat ‖ 120 pF), a long-tail pair
///     (82k/100k, 470 Ω, 15k tail, feedback 820 Ω into 100 Ω from the 8 Ω tap), four 6L6GC at 456 V (2.2k stoppers,
///     470 Ω screens) and the rectifier switch (silicon or a 5U4G).
/// </summary>
/// <remarks>
///     <para>
///         Source: the Mesa/Boogie factory drawings "Lonestar" (John M, Dec 1 2003, board rev LS1A1, six sheets: block
///         diagram, power supplies, preamp, reverb/FX loop, power amp, switching matrix),
///         https://el34world.com/charts/Schematics/files/Mesa_boogie/Boogie_lonestar.pdf. Every value above is read
///         from it, with its node voltages (E 302 V, D 315 V, C 395 V, B 455 V, A 456 V; bias -51 V for 6L6).
///     </para>
///     <para>
///         Guesses and simplifications, marked: the effects loop is in hard bypass and the reverb level at zero (its
///         return's 470k to ground stays); the reverb-mute JFET leaves V2A's grid on the 680k; channel 2's treble wiper
///         is taken as joined to the gain pot's top, as channel 1's is drawn (the scan shows the crossing without a
///         dot); "Thicker" (500 pF) is left out; the 120 pF across the inverter's 100k plate resistor and the zener
///         limiter on the master are left out. Not on the drawing: the idle current (35 mA), the output transformer's
///         primary (2k, a typical value for four 6L6) and the 50 W and 10 W modes (two tubes lifted; this is the 100 W
///         mode).
///     </para>
/// </remarks>
public static class LoneStar {
    public static AmpDesign Design { get; } = new(
        "Lone Star",
        [
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [new NetworkStageDesign("V1B", InputAndStack(false))],
                [
                    new ChannelSwitchStageDesign(
                        "Drive",
                        SwitchRole.Switch2,
                        [new NetworkStageDesign("V1B", InputAndStack(true))],
                        [
                            new NetworkStageDesign("V1B", DriveInput()),
                            new NetworkStageDesign("V1A", DriveAndStack())
                        ]
                    )
                ]
            ),
            new TriodeStageDesign("V2B", TriodeModel.Ecc83, 315, 100e3, 1.5e3, CathodeBypass: 15e-6, InputResistance: 100e3),
            new NetworkStageDesign("V2A", MixerAndMaster())
        ],
        new(
            "4 x 6L6GC",
            PentodeModel.SixL6GC,
            TubesPerSide: 2,
            Supply: 456,
            ScreenResistor: 470,
            IdleCurrent: 0.035,
            CathodeBias: false,
            CathodeBypass: 0,
            RatedPower: 100,
            PhaseInverter: new LongTailPairDesign(TriodeModel.Ecc83, 395, 82e3, 100e3, 15e3, 470),
            CouplingCap: 0.1e-6,
            GridLeak: 220e3,
            GridStopper: 2.2e3,
            CutCap: 0,
            CutPot: new(1e6, PotTaper.Linear),
            Feedback: new(1, 820, 100, new(1e3, PotTaper.Linear), 1e-12),
            HasFeedback: true,
            Transformer: new(2e3),
            Speaker: SpeakerDesign.OpenBack1x12,
            PowerSupply: new(30, 110e-6, 100, 30e-6, 15.9e3, 30e-6, 8e-3, RectifierModel.Silicon),
            InverterInput: new(3e-9, 1.33e6),
            SupplySwitches: [new(SwitchRole.Switch3, RectifierModel.FiveU4)]
        ),
        -6
    ) {
        Panel = AmpPanel.Standard with {
            Presence = null,
            Depth = null,
            Cut = null,
            Bright = "Thick",
            Control1 = "Drive",
            Control2 = "Presence",
            Switch1 = "Channel 2",
            Switch2 = "Drive Mode",
            Switch3 = "Tube Rectifier"
        }
    };

    /// <summary>V1B: 1M grid leak; 100k plate from the 302 V node; 1.5k ‖ 15 µF. Returns the plate.</summary>
    static int InputStage(StageNetlist net) {
        var (supply, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(supply, 302);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 15e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        return plate;
    }

    /// <summary>
    ///     A channel's stack driven from <paramref name="source" /> (250 pF, 100k slope, 0.1 µF, 47 nF; 250k audio
    ///     treble and bass, 10k linear middle) and its 1M audio gain pot on the treble wiper, with 120 pF and 82k in
    ///     series from the pot's top to its wiper; with <paramref name="thick" />, 250 pF from the source to the pot's
    ///     top (channel 2's Thick switch). Returns the gain pot's wiper.
    /// </summary>
    static int StackAndGain(StageNetlist net, int source, bool thick) {
        var (slope, trebleTop, trebleBottom, middle, bright) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(source, trebleTop, 250e-12, CapacitorRole.Stack);
        net.Resistor(source, slope, 100e3);
        var treble = net.Pot(trebleTop, trebleBottom, new(250e3, PotTaper.Audio), PotRole.Treble);
        net.Capacitor(slope, trebleBottom, 0.1e-6, CapacitorRole.Stack);
        net.Rheostat(trebleBottom, middle, new(250e3, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(slope, middle, 47e-9, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, new(10e3, PotTaper.Linear), PotRole.Middle);
        if (thick) {
            net.SwitchedCapacitor(source, treble, 250e-12, SwitchRole.Bright, CapacitorRole.Bright);
        }

        var wiper = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(treble, bright, 120e-12, CapacitorRole.Bright);
        net.Resistor(bright, wiper, 82e3);
        return wiper;
    }

    /// <summary>V1B with a channel's stack and gain pot on its plate (channel 2 with its drive stage bypassed).</summary>
    static StageNetlist InputAndStack(bool thick) {
        var net = new StageNetlist();
        net.Output = StackAndGain(net, InputStage(net), thick);
        return net;
    }

    /// <summary>
    ///     V1B into channel 2's drive network: 5 nF, 1M to ground, 220k into the 1M audio drive pot, its wiper (V1A's
    ///     grid) with 220k to ground.
    /// </summary>
    static StageNetlist DriveInput() {
        var net = new StageNetlist();
        var plate = InputStage(net);
        var (coupled, top) = (net.Node(), net.Node());
        net.Capacitor(plate, coupled, 5e-9);
        net.Resistor(coupled, StageNetlist.Ground, 1e6);
        net.Resistor(coupled, top, 220e3);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Control1);
        net.Resistor(wiper, StageNetlist.Ground, 220e3);
        net.Output = wiper;
        return net;
    }

    /// <summary>V1A (100k from the 302 V node, 1.5k ‖ 15 µF) with channel 2's stack and gain pot on its plate.</summary>
    static StageNetlist DriveAndStack() {
        var net = new StageNetlist();
        var (supply, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Supply(supply, 302);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 15e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Output = StackAndGain(net, plate, true);
        return net;
    }

    /// <summary>
    ///     The reverb mixer, V2A and the master: 47 nF from V2B, 2.2M ‖ 20 pF into V2A's grid with 680k and the reverb
    ///     return's 470k to ground; 100k plate from the 315 V node, 1.5k ‖ 100 µF; 47 nF and 220k into the 1M audio
    ///     master; on its wiper the presence, 1 nF into a 1M audio rheostat ‖ 120 pF.
    /// </summary>
    static StageNetlist MixerAndMaster() {
        var net = new StageNetlist();
        var (coupled, grid, supply, plate, cathode, send, top, presence) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, grid, 2.2e6);
        net.Capacitor(coupled, grid, 20e-12);
        net.Resistor(grid, StageNetlist.Ground, 680e3 * 470e3 / (680e3 + 470e3));
        net.Supply(supply, 315);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 100e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, send, 47e-9);
        net.Resistor(send, top, 220e3);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Master);
        net.Capacitor(wiper, presence, 1e-9);
        net.Rheostat(presence, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Control2);
        net.Capacitor(presence, StageNetlist.Ground, 120e-12);
        net.Output = wiper;
        return net;
    }
}
