using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Bassman 135 (CBS, 1978; 135 W, usually into a 4 × 12 or 2 × 15): the Bass or Normal channel
///     (<see cref="AmpPanel.Switch1" />), each a 7025 stage into a Fender stack with a Middle rheostat and the Volume, the
///     Bass channel's Deep switch (<see cref="AmpPanel.Switch2" />) and the Normal channel's Bright; the second 7025
///     stages
///     into the 220k mix and the 0.022 µF into the Master; a 12AT7 long-tail pair; four 6L6GC in ultralinear fixed bias at
///     500 V on a silicon bridge; 820 Ω of feedback from the 4 Ω tap into 100 Ω. Sources: the CBS drawing 014283 rev 1
///     "Bassman 135 Watts R.M.S. Amp" (schematicheaven.net/fenderamps/bassman_135_schem.pdf), and drawing 010303 rev F
///     "Bassman 100 Watts" (bassman_100_schem.pdf, a sharper scan of the same preamp) to read the small print.
/// </summary>
/// <remarks>
///     <para>
///         Read: the inputs (68k / 68k, 1M); the Bass channel's V1A (100k from 365 V, 1500 Ω ‖ 5 µF, 240 V at the plate)
///         into 500 pF, the 100k slope, 0.1 µF and 0.047 µF, Treble and Bass 250k, Middle 10k, then 0.0047 µF in series
///         into its 1M Volume; Deep: 0.047 µF from the slope's bottom switched to ground (1M to V1A's cathode keeps it
///         charged); the Normal channel's V2A (1500 Ω ‖ 25 µF) into 250 pF, 100k, 0.1 µF, 0.047 µF, the same pots, 0.047
///         µF
///         into its Volume with the 120 pF Bright switch across it; V1B and V2B (100k, one 820 Ω ‖ 5 µF), each through
///         220k
///         into the mix, 0.022 µF into the 1M Master; the inverter (0.01 µF in, 330k grid resistors, 22k 1 W tail, 470 Ω,
///         47k / 47k from 400 V, the second grid on 0.1 µF to the tail's bottom, 100 Ω there to ground, 820 Ω 1 W with
///         0.01 µF across it from the speaker); 0.1 µF into 47k / 68k grid leaks (with the 10k balance pot) and 1.5k
///         stoppers, −58 V; 470 Ω 1 W from each screen to the transformer's screen taps (blue-yellow, brown-yellow:
///         ultralinear); the supply (500 V on 2 × 220 µF in series from a bridge, 2.7k 10 W to 410 V, 1k to 400 V, 4.7k to
///         365 V). The drawing's AC test points (6 mV at the input with every control up: 80 mV at V1B's grid, 4 V at its
///         plate, 1.8 V at the mix) are checked in the tests.
///     </para>
///     <para>
///         The shared cathode of V1B and V2B: the playing triode draws its operating point through twice the resistor, and
///         at audio sees the 820 Ω in parallel with the idle triode's cathode (about 1.6k), as an AC branch here.
///         Unverified:
///         the pot tapers (CBS "J" and "HD" codes: audio taken for every pot but the Middle), the ultralinear tap (40 %
///         assumed), the output transformer 013691's primary (2k assumed, as four 6L6 in the Twin), the 6L6 idle current
///         (30 mA, the −58 V bias through the 6L6GC's curves with the screens near B+), the supply's source resistance,
///         the cabinet (the closed 4 × 12 preset). Left out: the 2000 pF from each pair's grids to ground, the 0.01 µF
///         across the feedback resistor (a corner near 19 kHz).
///     </para>
/// </remarks>
public static class Bassman135 {
    const double Preamp = 365;

    public static AmpDesign Design { get; } = new(
        "Bassman 135",
        [
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [BassStage(), SecondStage("V1B")],
                [NormalStage(), SecondStage("V2B")]
            )
        ],
        new(
            "4 x 6L6GC",
            PentodeModel.SixL6GC,
            2,
            500,
            470,
            0.03,
            false,
            0,
            135,
            new LongTailPairDesign(TriodeModel.Ecc81, 400, 47e3, 47e3, 22e3, 470),
            0.1e-6,
            57.5e3,
            1.5e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1 / Math.Sqrt(2), 820, 100, new(1e3, PotTaper.Linear), 1e-12),
            true,
            new(2e3),
            SpeakerDesign.Closed4x12,
            new(20, 110e-6, 2.7e3, 20e-6, 1e3, 20e-6, 12e-3, RectifierModel.Silicon),
            UltralinearTap: 0.4,
            InverterInput: new(0.01e-6, 330e3)
        ),
        -3
    ) {
        Panel = new() {
            Gain = "Volume",
            Presence = null,
            Depth = null,
            Cut = null,
            Switch1 = "Normal Ch",
            Switch2 = "Deep"
        }
    };

    /// <summary>The first stage: 100k from the preamp node, 1500 Ω with <paramref name="bypass" />.</summary>
    static (StageNetlist Net, int Plate, int Cathode) Triode(double bypass) {
        var net = new StageNetlist();
        var (plate, cathode, rail) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, bypass);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);
        return (net, plate, cathode);
    }

    /// <summary>
    ///     A Fender stack on the plate (no coupling cap: the stack's caps block the plate's DC) with the Middle rheostat;
    ///     returns the treble wiper and the slope's bottom.
    /// </summary>
    static (int Treble, int Slope) Stack(StageNetlist net, int plate, double trebleCap) {
        var (top, slope, bottom, middle) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, top, trebleCap, CapacitorRole.Stack);
        net.Resistor(plate, slope, 100e3);
        net.Capacitor(slope, bottom, 0.1e-6, CapacitorRole.Stack);
        net.Capacitor(slope, middle, 0.047e-6, CapacitorRole.Stack);
        var treble = net.Pot(top, bottom, new(250e3, PotTaper.Audio), PotRole.Treble);
        net.Rheostat(bottom, middle, new(250e3, PotTaper.Audio), PotRole.Bass);
        net.Rheostat(middle, StageNetlist.Ground, new(10e3, PotTaper.Linear), PotRole.Middle);
        return (treble, slope);
    }

    /// <summary>V1A, the Bass channel's stack (500 pF), Deep, and 0.0047 µF into its Volume: the output is the wiper.</summary>
    static NetworkStageDesign BassStage() {
        var (net, plate, cathode) = Triode(5e-6);
        var (treble, slope) = Stack(net, plate, 500e-12);
        var (deep, top) = (net.Node(), net.Node());
        net.Capacitor(slope, deep, 0.047e-6, CapacitorRole.Stack);
        net.Resistor(deep, cathode, 1e6);
        net.SwitchedResistor(deep, StageNetlist.Ground, 0, 10, SwitchRole.Switch2);
        net.Capacitor(treble, top, 0.0047e-6);
        net.Output = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        return new("V1A", net, 34e3);
    }

    /// <summary>V2A, the Normal channel's stack (250 pF) and 0.047 µF into its Volume with the Bright switch's 120 pF.</summary>
    static NetworkStageDesign NormalStage() {
        var (net, plate, _) = Triode(25e-6);
        var (treble, _) = Stack(net, plate, 250e-12);
        var top = net.Node();
        net.Capacitor(treble, top, 0.047e-6);
        var volume = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.SwitchedCapacitor(top, volume, 120e-12, SwitchRole.Bright, CapacitorRole.Bright);
        net.Output = volume;
        return new("V2A", net, 34e3);
    }

    /// <summary>
    ///     V1B or V2B (100k; the shared 820 Ω ‖ 5 µF: twice the resistor for the operating point, and at audio the idle
    ///     triode's cathode beside it), 220k into the mix with the idle stage's 220k (its plate, 100k ‖ r_p, about 35k at
    ///     240 V), 0.022 µF into the Master: the output is the Master's wiper.
    /// </summary>
    static NetworkStageDesign SecondStage(string name) {
        var net = new StageNetlist();
        var (plate, cathode, rail, idleCathode) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2 * 820);
        net.Capacitor(cathode, StageNetlist.Ground, 5e-6);

        // At audio 820 Ω ‖ 1.6k: 1640 Ω beside 810 Ω behind a blocking cap.
        net.Capacitor(cathode, idleCathode, 1e-3);
        net.Resistor(idleCathode, StageNetlist.Ground, 810);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);

        var (mix, idle, top) = (net.Node(), net.Node(), net.Node());
        net.Resistor(plate, mix, 220e3);
        net.Supply(idle, 240);
        net.Resistor(idle, mix, 220e3 + 35e3);
        net.Capacitor(mix, top, 0.022e-6);
        net.Output = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Master);
        return new(name, net);
    }
}
