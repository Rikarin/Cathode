using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Vibrolux 6G11 (brownface, 30 W, 2 × 10): the Normal or Bright channel (<see cref="AmpPanel.Switch1" />), each
///     a 7025 stage (100k, 1.5k ‖ 25 µF) driving the 6G11's tone network from its plate, the 1M audio Volume (47 pF across
///     it on the Bright channel), a second 7025 stage and 220k into the mix with the idle channel's; a 12AX7 long-tail
///     pair; two 6L6GC in fixed bias at 365 V on a GZ34; 56k of feedback from the speaker into 1.5k. Panel: Volume, Treble,
///     Bass, the channel. Source: the Fender drawing "Vibrolux 6G11" (I-FA, P/B;
///     schematicheaven.net/fenderamps/vibrolux_6g11_schem.pdf).
/// </summary>
/// <remarks>
///     <para>
///         Read: the inputs (68k / 68k, 1M); V1 (100k from 275 V, 180 V plates, 1500 Ω ‖ 25 µF each, 1.3 V), the 0.003 µF
///         across the Normal channel's plate resistor; the tone network: 250 pF from the plate to the Treble's top, 100k
///         from the plate to a node with 0.1 µF to the Treble's 70k tap and 0.05 µF to the Bass rheostat's far end, the
///         250k audio Bass between them, 6.8k from that end to ground, the Treble's bottom on 0.005 µF to ground; the Volume
///         from the Treble's wiper; V2 (100k, both on one 820 Ω ‖ 25 µF, "B"), 220k mixing resistors; the inverter
///         (0.005 µF in, 1M grids, 6.8k tail, 820 Ω, 82k / 100k from 310 V, 0.1 µF out, 220k, −45 V); 470 Ω 1 W screens;
///         the supply (GZ34 into 2 × 16 µF, the 125C3A choke to 364 V, 10k to 310 V, 10k to 275 V); the 56k feedback into
///         1.5k.
///     </para>
///     <para>
///         Simplified: the Treble pot's tap. Its wiper is taken over the track from the tap to the top only (the 180k
///         there, linear), so the darkest quarter of its travel, between the tap and the 0.005 µF end, is not reached.
///         Unverified: the 6L6 idle current (35 mA, as the Vibrolux Reverb AA964; the 6L6GC's Koren curves give 13 mA at
///         the drawing's −45 V with 364 V screens, colder than the 6L6s of an amp of this kind run), the pot tapers
///         (Treble linear, Bass and Volume audio as on the drawing's "A"), the output transformer 45217 (4k assumed, as the Vibrolux Reverb's 125A6A), the feedback tap (the two 8 Ω
///         10s in parallel, 4 Ω), the supply's source resistance, the 2 × 10's impedance curve (the 4 × 10 preset).
///         Not modelled: the tremolo (a bias tremolo on the 6L6 grids through the Intensity pot: at 0), the 100 pF between
///         the inverter's plates.
///     </para>
///     <para>
///         The catalogue had this amp down for the harmonic vibrato of the 6G12; the 6G11 drawing shows a bias tremolo (an
///         oscillator and a follower into the Intensity pot on the bias line), so it has none.
///     </para>
/// </remarks>
public static class Vibrolux6G11 {
    const double Preamp = 275;

    public static AmpDesign Design { get; } = new(
        "Vibrolux 6G11",
        [
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [FirstStage("V1A", normal: true), SecondStage("V2A")],
                [FirstStage("V1B", normal: false), SecondStage("V2B")]
            )
        ],
        new(
            "2 x 6L6GC",
            PentodeModel.SixL6GC,
            TubesPerSide: 1,
            Supply: 365,
            ScreenResistor: 470,
            IdleCurrent: 0.035,
            CathodeBias: false,
            CathodeBypass: 0,
            RatedPower: 30,
            PhaseInverter: new LongTailPairDesign(TriodeModel.Ecc83, 310, 82e3, 100e3, 6.8e3, 820),
            CouplingCap: 0.1e-6,
            GridLeak: 220e3,
            GridStopper: 1,
            CutCap: 0,
            CutPot: new(1e6, PotTaper.Linear),
            Feedback: new(1 / Math.Sqrt(2), 56e3, 1.5e3, new(1e3, PotTaper.Linear), 1e-12),
            HasFeedback: true,
            Transformer: new(4e3),
            Speaker: SpeakerDesign.OpenBack4x10,
            PowerSupply: new(100, 32e-6, 100, 16e-6, 10e3, 16e-6, 5.8e-3, RectifierModel.GZ34),
            InverterInput: new(0.005e-6, 1e6)
        ),
        -5
    ) {
        Panel = new() {
            Gain = "Volume",
            Middle = null,
            Presence = null,
            Depth = null,
            Cut = null,
            Master = null,
            Bright = null,
            Switch1 = "Bright Ch"
        }
    };

    /// <summary>A channel's V1 half, the tone network on its plate and the Volume; the output is the Volume's wiper.</summary>
    static NetworkStageDesign FirstStage(string name, bool normal) {
        var net = new StageNetlist();
        var (plate, cathode, rail) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 100e3);
        if (normal) {
            net.Capacitor(rail, plate, 0.003e-6);
        }

        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 25e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);

        var (top, slope, tap, far, bottom) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, top, 250e-12, CapacitorRole.Stack);
        net.Resistor(plate, slope, 100e3);
        net.Capacitor(slope, tap, 0.1e-6, CapacitorRole.Stack);
        net.Capacitor(slope, far, 0.05e-6, CapacitorRole.Stack);
        net.Rheostat(tap, far, new(250e3, PotTaper.Audio), PotRole.Bass);
        net.Resistor(far, StageNetlist.Ground, 6.8e3);
        var treble = net.Pot(top, tap, new(180e3, PotTaper.Linear), PotRole.Treble);
        net.Resistor(tap, bottom, 70e3);
        net.Capacitor(bottom, StageNetlist.Ground, 0.005e-6, CapacitorRole.Stack);

        var volume = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        if (!normal) {
            net.Capacitor(treble, volume, 47e-12, CapacitorRole.Bright);
        }

        net.Output = volume;
        return new(name, net, 34e3);
    }

    /// <summary>
    ///     V2 (100k; the two halves' shared 820 Ω ‖ 25 µF, twice the resistor each), its 220k into the mix, and the idle
    ///     channel's second stage (its plate, 100k ‖ r_p, about 38k at its 180 V) through its own 220k: the output.
    /// </summary>
    static NetworkStageDesign SecondStage(string name) {
        var net = new StageNetlist();
        var (plate, cathode, rail, mix, idle) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2 * 820);
        net.Capacitor(cathode, StageNetlist.Ground, 25e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);
        net.Resistor(plate, mix, 220e3);
        net.Supply(idle, 180);
        net.Resistor(idle, mix, 220e3 + 38e3);
        net.Output = net.DcBlock(mix);
        return new(name, net);
    }
}
