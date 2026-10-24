using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Super Reverb AB763 (40 W): the Normal or Vibrato channel (<see cref="AmpPanel.Switch1" />), each V1A into
///     the Fender stack (0.022 µF mid cap) and the 1M volume with its 120 pF bright switch, V1B, the AB763 mixing network,
///     a 12AT7 long-tail pair, two 6L6GC in fixed bias at 460 V on a GZ34, the 125A9A transformer (4.2k : 2 Ω), and
///     820 Ω / 100 Ω feedback from the 2 Ω tap (about 41 % less feedback voltage than from a 4 Ω tap). Only the Vibrato
///     channel has a middle pot; the Normal channel has the fixed 6.8k. Sources: see <see cref="Ab763" />; docs/06 §3.1.
/// </summary>
/// <remarks>
///     The first stage is a network stage, so that the channel switch can swap the fixed mid resistor for the mid pot.
///     The mid pot (10k audio) and the Normal channel's mid cap (taken as the Vibrato channel's 0.022 µF) are unverified;
///     the idle current (35 mA) and the supply's source resistance are typical values. The existing "Blackface" type is a
///     Super-style channel without the mixing stage; this is the whole amp.
/// </remarks>
public static class SuperReverb {
    public static AmpDesign Design { get; } = new(
        "Super Reverb",
        [
            FirstStage(),
            Ab763.SecondStage(270),
            Ab763.Mixer(270)
        ],
        new(
            "2 x 6L6GC",
            PentodeModel.SixL6GC,
            1,
            460,
            470,
            0.035,
            false,
            0,
            40,
            Ab763.PhaseInverter(420),
            0.1e-6,
            220e3,
            1.5e3,
            0,
            new(1e6, PotTaper.Linear),
            Ab763.Feedback(0.5, 100),
            true,
            new(4.2e3),
            SpeakerDesign.OpenBack4x10,
            new(100, 40e-6, 100, 20e-6, 10e3, 20e-6, 6e-3, RectifierModel.GZ34),
            InverterInput: Ab763.InverterInput
        ),
        -6
    ) {
        Panel = new() {
            Gain = "Volume",
            Presence = null,
            Depth = null,
            Cut = null,
            Master = null,
            Switch1 = "Vibrato Ch"
        }
    };

    /// <summary>
    ///     V1A (V2A on the Vibrato channel): 34k stopper, 100k plate, 1.5k ‖ 25 µF, the Fender stack on its plate (250 pF,
    ///     100k slope, 0.1 µF bass, 0.022 µF mid; treble and bass 250k audio) and the 1M audio volume on the treble wiper,
    ///     with the 120 pF bright cap across its upper part. The mid leg is the fixed 6.8k on the Normal channel and the
    ///     10k mid pot on the Vibrato channel.
    /// </summary>
    static NetworkStageDesign FirstStage() {
        var net = new StageNetlist();
        var (plate, cathode, rail) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, 270);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 25e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);

        var (top, bottom, slope, middle) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, top, 250e-12, CapacitorRole.Stack);
        net.Resistor(plate, slope, 100e3);
        net.Capacitor(slope, bottom, 100e-9, CapacitorRole.Stack);
        net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
        var treble = net.Pot(top, bottom, new(250e3, PotTaper.Audio), PotRole.Treble);
        net.Rheostat(bottom, middle, new(250e3, PotTaper.Audio), PotRole.Bass);

        // Normal: the fixed 6.8k; Vibrato: the mid pot, switched in by a near-short in series.
        net.SwitchedResistor(middle, StageNetlist.Ground, 6.8e3, 1e12, SwitchRole.Switch1);
        var pot = net.Node();
        net.Rheostat(middle, pot, new(10e3, PotTaper.Audio), PotRole.Middle);
        net.SwitchedResistor(pot, StageNetlist.Ground, 0, 1, SwitchRole.Switch1);

        var wiper = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.SwitchedCapacitor(treble, wiper, 120e-12, SwitchRole.Bright, CapacitorRole.Bright);
        net.Output = wiper;
        return new("V1A", net, 34e3);
    }
}
