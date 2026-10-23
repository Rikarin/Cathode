using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Bassman 5F6-A (tweed, about 45 W): the Bright and Normal channels' V1 halves (12AY7, 100k plates, a shared
///     820 Ω ‖ 250 µF cathode), their 0.02 µF caps and 1M volumes (the Bright channel's with a fixed 100 pF), the 270k
///     mixing resistors into V2A (12AX7, 100k, 820 Ω unbypassed), the V2B cathode follower DC-coupled to it (100k) driving
///     the Bassman stack (Yeh and Smith's values: 250 pF, 56k slope, 0.02 µF bass and mid; treble 250k, bass 1M, mid 25k)
///     into the inverter's 1M grid resistor, a 12AX7 long-tail pair (82k/100k, 470 Ω, 10k tail over the 5k presence pot),
///     two 5881 (6L6 curves) in fixed bias at 430 V on a GZ34, a 4k : 2 Ω transformer and 27k feedback from the 2 Ω tap into
///     the presence pot. Panel: Bright and Normal volumes, Bass, Middle, Treble, Presence; <b>Normal Input</b> plays the
///     Normal channel instead of the Bright one, <b>Jumper</b> drives both (the classic jumpered input).
/// </summary>
/// <remarks>
///     <para>
///         Sources: AmpBooks "Fender Bassman 5F6-A" (12AY7 at 325 V with 820 Ω ‖ 250 µF and 34k stoppers; 0.02 µF; 1M
///         volume; V2A 100k / 820 Ω unbypassed; the inverter's 470 Ω, 10k + 5k tail, 82k plates, 0.1 µF out; 470 Ω screens;
///         GZ34); Rob Robinette's 5F6-A page (100 pF bright cap, 270k mixers, 100k cathode follower, 2 × 5881 at −48 V,
///         220k grid leaks, B+ 442 V unloaded and 411 V loaded, 4.7k to the preamp, 27k feedback from the 2 Ω tap, a 5k
///         presence pot); Yeh and Smith, DAFx-06, for the stack (the schematic's 56k slope and 0.02 µF bass cap; Robinette
///         says production used 100k and 0.1 µF: this model takes the schematic, which is also the published reference).
///     </para>
///     <para>
///         Unverified: the pot tapers (treble and mid linear, bass and volumes audio, as on the JTM45 drawing that copied
///         this circuit), the presence cap (0.1 µF), the output transformer (about 4k), the 5881 idle current (35 mA), no
///         grid stoppers, and the supply's source resistance and reservoir. Jumpered, each grid sees about 68k instead of
///         34k (the two input jacks' resistors in series); here the played channel keeps its 34k too.
///     </para>
/// </remarks>
public static class Bassman {
    /// <summary>A resistor switched out.</summary>
    const double Open = 1e12;

    /// <summary>The Bassman 5F6-A stack (Yeh and Smith, DAFx-06).</summary>
    public static ToneStackDesign Stack { get; } = new(
        "Bassman 5F6-A",
        250e-12,
        56e3,
        20e-9,
        20e-9,
        new(250e3, PotTaper.Linear),
        new(1e6, PotTaper.Audio),
        new(25e3, PotTaper.Linear)
    );

    public static AmpDesign Design { get; } = new(
        "Bassman 5F6-A",
        [
            InputStage(),
            new TriodeStageDesign("V2A", TriodeModel.Ecc83, 325, 100e3, 820),
            new ToneStackStageDesign(
                "V2B",
                TriodeModel.Ecc83,
                StackDriver.CathodeFollower,
                Supply: 325,
                CathodeResistor: 100e3,
                Stack: Stack,
                Load: new(1e6, PotTaper.Linear),
                LoadRole: PotRole.Master
            )
        ],
        new(
            "2 x 5881",
            PentodeModel.SixL6GC,
            TubesPerSide: 1,
            Supply: 430,
            ScreenResistor: 470,
            IdleCurrent: 0.035,
            CathodeBias: false,
            CathodeBypass: 0,
            RatedPower: 45,
            PhaseInverter: new LongTailPairDesign(TriodeModel.Ecc83, 330, 82e3, 100e3, 10e3, 470),
            CouplingCap: 0.1e-6,
            GridLeak: 220e3,
            GridStopper: 1,
            CutCap: 0,
            CutPot: new(1e6, PotTaper.Linear),
            Feedback: new(0.5, 27e3, 5e3, new(5e3, PotTaper.Linear), 0.1e-6),
            HasFeedback: true,
            Transformer: new(4e3),
            Speaker: SpeakerDesign.OpenBack4x10,
            PowerSupply: new(100, 32e-6, 100, 16e-6, 4.7e3, 16e-6, 6e-3, RectifierModel.GZ34)
        ),
        -11
    ) {
        Panel = new() {
            Gain = "Bright Vol",
            Control1 = "Normal Vol",
            Depth = null,
            Cut = null,
            Master = null,
            Bright = null,
            Switch1 = "Normal Input",
            Switch2 = "Jumper"
        }
    };

    /// <summary>
    ///     Both V1 halves with their shared cathode, coupling caps, volumes and the 270k mixing resistors; the output is
    ///     V2A's grid (the mixing node). The guitar reaches the Bright grid unless Normal Input is on, the Normal grid when
    ///     it is, and both with the jumper.
    /// </summary>
    static NetworkStageDesign InputStage() {
        var net = new StageNetlist();
        var (normalGrid, brightGrid) = (net.Node(), net.Node());
        net.SwitchedResistor(net.Input, brightGrid, 34e3, Open, SwitchRole.Switch1);
        net.SwitchedResistor(net.Input, brightGrid, 0, 68e3, SwitchRole.Switch2);
        net.SwitchedResistor(net.Input, normalGrid, 0, 34e3, SwitchRole.Switch1);
        net.SwitchedResistor(net.Input, normalGrid, 0, 68e3, SwitchRole.Switch2);
        net.Resistor(brightGrid, StageNetlist.Ground, 1e6);
        net.Resistor(normalGrid, StageNetlist.Ground, 1e6);

        var (normalPlate, brightPlate, cathode, rail) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, 325);
        net.Resistor(rail, normalPlate, 100e3);
        net.Resistor(rail, brightPlate, 100e3);
        net.Resistor(cathode, StageNetlist.Ground, 820);
        net.Capacitor(cathode, StageNetlist.Ground, 250e-6);
        net.Triode(brightPlate, brightGrid, cathode, TriodeModel.TwelveAY7);
        net.Triode(normalPlate, normalGrid, cathode, TriodeModel.TwelveAY7);

        var (brightTop, normalTop, mix) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(brightPlate, brightTop, 20e-9);
        var bright = net.Pot(brightTop, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(brightTop, bright, 100e-12, CapacitorRole.Bright);
        net.Resistor(bright, mix, 270e3);
        net.Capacitor(normalPlate, normalTop, 20e-9);
        var normal = net.Pot(normalTop, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Control1);
        net.Resistor(normal, mix, 270e3);
        net.Output = mix;
        return new("V1", net);
    }
}
