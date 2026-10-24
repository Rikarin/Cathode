using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Deluxe 6G3 (brownface, 20 W, 1 × 12): the Normal or Bright channel (<see cref="AmpPanel.Switch1" />), each
///     half of a 7025 (220k, the two on one 1.5k cathode) into 0.02 µF, the 1M audio Volume and the Tone pot, then 220k
///     into the mix with the idle channel's 220k; a 12AX7 stage with a split load (100k over 15k, the signal from the
///     junction); a 12AX7 long-tail pair; two 6V6GT in fixed bias at 375 V on a GZ34; the 125A1A transformer (6.6k : 8 Ω);
///     and 56k of feedback from the speaker into 1.5k. Panel: Volume, Tone (the Treble knob), the channel. Source: the
///     Fender drawing and layout "Deluxe 6G3" (I-FA; schematicheaven.net/fenderamps/deluxe_6g3_schem.pdf).
/// </summary>
/// <remarks>
///     <para>
///         Read: the inputs (68k / 68k, 1M); V1's 220k plates (165 V) from the 270 V node and its shared 1500 Ω with
///         25 + 25 µF (1.5 V), the 0.003 µF across the Normal channel's plate resistor; the tone circuit on each channel
///         (the 1M audio Tone pot's wiper on the Volume's top, one end through 0.01 µF (Normal) or 0.02 µF (Bright) to
///         ground, the other through 500 pF to the Volume's wiper); the 220k mixing resistors into V2A's grid; V2A
///         (100k + 15k, 1500 Ω with 25 + 25 µF, 165 V at the plate) and 0.01 µF into the inverter (82k / 100k, 6.8k tail,
///         820 Ω, 1M grids, the second grid on 0.1 µF to the tail's bottom, 1.5k there to ground); 0.1 µF and 220k into
///         the
///         6V6s, −26 V; the supply (GZ34, 16 µF, 1k 2 W to the 365 V screens, 10k to the inverter's 325 V, 27k to 270 V);
///         the 125A1A, whose 6.6k : 8 Ω docs/06 §3.1 sources for the AB763 Deluxe.
///     </para>
///     <para>
///         Unverified: the 6V6 idle current (30 mA, about 11 W; the 6V6GT's Koren curves give about 43 mA, 16 W, at
///         the drawing's −26 V with 365 V screens, past the tube's 14 W rating), the supply's source resistance. Inferred:
///         which end of the Tone pot is "up" (the 500 pF end: brighter turned up). The idle channel loads the mix through
///         its 220k and its Volume (at the same setting), its coupling cap and its plate (220k ‖ r_p, about 48k). Not
///         modelled: the tremolo (a bias tremolo on the 6V6 grids: intensity at 0), the 100 pF between the inverter's
///         plates.
///     </para>
/// </remarks>
public static class Deluxe6G3 {
    const double Preamp = 270;

    /// <summary>
    ///     The bias tremolo, from the drawing: the 12AX7's other half (820k from the 375 V node, 2700 Ω with 25 µF) with
    ///     0.02 µF from its plate to the Speed rheostat (3.5M reverse audio, 100k in series), 0.01 µF to a node with 1M to
    ///     the cathode (the pedal's jack on it), 0.01 µF to the grid with 1M to ground; the plate through 220k and 0.1 µF
    ///     to the 250k linear Intensity pot, whose far end is the −26 V bias supply (25 µF and 22k), its wiper on the
    ///     6V6 grid leaks' return with 0.05 µF to ground. The plate's load from that network at the rate is taken as
    ///     600k (220k, the 0.1 µF's reactance and the pot).
    /// </summary>
    internal static BiasTremoloDesign Tremolo { get; } = new(
        new(
            TriodeModel.Ecc83,
            375,
            820e3,
            2.7e3,
            0.02e-6,
            0.01e-6,
            0.01e-6,
            100e3,
            1e6,
            1e6,
            new(3.5e6, PotTaper.ReverseAudio),
            PlateLoad: 600e3
        ),
        220e3,
        0.1e-6,
        new Pot(250e3, PotTaper.Linear),
        WiperCap: 0.05e-6
    );

    public static AmpDesign Design { get; } = new(
        "Deluxe 6G3",
        [
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [FirstStage("V1A", true)],
                [FirstStage("V1B", false)]
            ),
            SecondStage()
        ],
        new(
            "2 x 6V6GT",
            PentodeModel.SixV6GT,
            1,
            375,
            1,
            0.03,
            false,
            0,
            20,
            new LongTailPairDesign(TriodeModel.Ecc83, 325, 82e3, 100e3, 6.8e3, 820),
            0.1e-6,
            220e3,
            1,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 56e3, 1.5e3, new(1e3, PotTaper.Linear), 1e-12),
            true,
            new(6.6e3),
            SpeakerDesign.OpenBack1x12,
            new(100, 16e-6, 1e3, 16e-6, 10e3, 16e-6, 4.4e-3, RectifierModel.GZ34),
            InverterInput: new(0.01e-6, 1e6)
        ) { BiasTremolo = Tremolo },
        -6
    ) {
        Panel = new() {
            TremoloRate = "Speed",
            TremoloDepth = "Intensity",
            Gain = "Volume",
            Bass = null,
            Middle = null,
            Treble = "Tone",
            Presence = null,
            Depth = null,
            Cut = null,
            Master = null,
            Bright = null,
            Switch1 = "Bright Ch"
        }
    };

    /// <summary>
    ///     One channel's V1 half (220k; the two halves' shared 1.5k ‖ 50 µF, twice the resistor each), its tone circuit
    ///     and Volume, and the mix: its 220k and the idle channel's 220k from that channel's Volume wiper. The output is
    ///     the mix (V2A's grid).
    /// </summary>
    static NetworkStageDesign FirstStage(string name, bool normal) {
        var net = new StageNetlist();
        var (plate, cathode, rail) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 220e3);
        if (normal) {
            net.Capacitor(rail, plate, 0.003e-6);
        }

        net.CathodeResistor(cathode, StageNetlist.Ground, 2 * 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 50e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);

        // The Tone pot: its wiper is the Volume's top; one end through the shunt cap to ground, the other through 500 pF
        // to the Volume's wiper; turned up, the wiper goes to the 500 pF end.
        var (shunt, bright, mix) = (net.Node(), net.Node(), net.Node());
        var top = net.Pot(bright, shunt, new(1e6, PotTaper.Audio), PotRole.Treble);
        net.Capacitor(plate, top, 0.02e-6);
        net.Capacitor(shunt, StageNetlist.Ground, normal ? 0.01e-6 : 0.02e-6, CapacitorRole.Stack);
        var volume = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(bright, volume, 500e-12, CapacitorRole.Stack);
        net.Resistor(volume, mix, 220e3);

        // The idle channel: its plate (220k ‖ r_p) through its 0.02 µF into its Volume, whose wiper loads the mix.
        var (idlePlate, idleTop) = (net.Node(), net.Node());
        net.Resistor(idlePlate, StageNetlist.Ground, 48e3);
        net.Capacitor(idlePlate, idleTop, 0.02e-6);
        var idle = net.Pot(idleTop, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Resistor(idle, mix, 220e3);
        net.Output = mix;
        return new(name, net, 34e3);
    }

    /// <summary>V2A: 100k over 15k from the preamp node, the output at their junction; 1.5k ‖ 50 µF.</summary>
    static NetworkStageDesign SecondStage() {
        var net = new StageNetlist();
        var (plate, tap, cathode, rail) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, tap, 15e3);
        net.Resistor(tap, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 50e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);
        net.Output = net.DcBlock(tap);
        return new("V2A", net);
    }
}
