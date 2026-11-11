using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Vox AC15C1 (Custom), from the factory service manual's drawings "AC15C1 Preamp and Reverb" and "AC15C1 Power
///     Stage" (Vox R&amp;D, ISS3, 24/11/2009; service manual AC15C1/C2/CH ver. 2.0,
///     https://www.manualslib.com/manual/1520645/Vox-Ac15c1.html): the Top Boost input, or (Switch 1) the Normal input.
///     Each input has its own half of V1, a 12AX7 (R2/R3 10k at the jack, R8/R21 1M, R9/R20 33k to the grid, R6/R5
///     100k plates, the cathodes sharing R14 1.5k with C7 22 µF). The Top Boost half (C16 120 pF from its plate to the
///     cathode) couples through C6 470 pF into VR2, the 500k log TB Volume (C8 120 pF across its upper part, C67 10 pF
///     from its top to ground), whose wiper is V2's first grid: R34 100k, R19 1.5k with C14 22 µF, DC-coupled to the
///     second half as a cathode follower (R15 56k, C68 1 nF) into the treble/bass network (C13 56 pF to VR3, the 1M
///     log Treble; R17 100k to the slope node; C20 22 nF from the Treble's far end and C24 22 nF from the Bass wiper to
///     that node; VR4, the 1M log Bass, as a rheostat from the Treble's far end to its wiper; R32 10k from the wiper to
///     ground). The Normal half couples through C5 22 nF and R1 330k into VR1, the 500k log Normal Volume, with C4 120
///     pF from the cap to the wiper. Two NJM2147 op-amps on ± 27 V join the channels: U1A takes the Treble's wiper (C19
///     220 nF, R23 330k over R25 120k) on its non-inverting input and the Normal wiper through R22 470k on its
///     inverting input (R24 820k feedback); its output goes through R57 56k, beside the reverb's R60 56k and R54 22k to
///     ground, into U5A (R55 3.9k over R58 1k), which drives the inverter through C32 100 nF. V3 is a 12AX7 long-tail
///     pair (R40/R49 1M, R44 1.2k, R42 47k, R51/R56 100k), C41/C43 100 nF, the Tone Cut (VR8 220k linear in series with
///     C58 4.7 nF) and the Master Volume (VR9 500k log as a rheostat) across the two phases, two EL84 (R65/R77 3.3k,
///     R89/R86 220k, R76/R83 470 Ω screens) on a shared R79 120 Ω with C54 220 µF, no feedback, a silicon bridge (R87/R88
///     180 Ω in its legs) into C62 47 µF, R80 1k and C53 22 µF.
/// </summary>
/// <remarks>
///     <para>
///         Not on the drawings, which carry no voltages, so assumptions: B+ (340 V here; owners on TDPRI report 334
///         to 348 V at the plates, threads "Vox AC15C1 voltages &amp; power" and "Redplating AC15C1", seen only as
///         search excerpts) and the output transformer's primary (the
///         parts list's "TRANS AUDIO 173.2V/10.4-14.6V", read as volts across half the primary for the 8 and 16 Ω
///         taps: 9k plate to plate). The preamp's rails follow from the drawing's droppers at the stages' own
///         currents: R75 22k from the screen node to the inverter's node (C26), R18 10k from there to V2's (C22) and
///         R7 22k to V1's (C15). The NJM2147 is from its datasheet (New Japan Radio, 2006: 88 dB, 0.5 V/µs, 0.3 µs
///         rise time, output to within 1 V of the rails); its input and output resistances are not given and are
///         taken as the JRC4558's.
///     </para>
///     <para>
///         Approximations. The idle input's half of V1 is not run: the playing half sees twice the shared cathode
///         resistor and half its cap. The idle channel's volume and the Reverb are taken at zero, so R22 and R60 go to
///         ground. R81/R82 10k, which stand between the Tone Cut and the Master Volume in each phase, are added to
///         the EL84s' grid stoppers: the master here shunts the inverter's outputs directly, where the amp's leaves
///         20k across them at zero. Left out: the spring reverb (U1B to U4B, Q1) and the bias tremolo (MF1, Q2, on the
///         EL84 grid leaks' return through R85 1M), C38 47 pF between the inverter's plates, C69 10 pF at U5A's input,
///         the jacks' 10 pF caps and the ferrite beads.
///     </para>
/// </remarks>
public static class AC15C1 {
    /// <summary>The inverter's node, C26, below R75 22k.</summary>
    public const double InverterSupply = 219;

    /// <summary>V2's node, C22, below R18 10k.</summary>
    public const double SecondSupply = 192;

    /// <summary>V1's node, C15, below R7 22k.</summary>
    public const double FirstSupply = 198;

    /// <summary>The op-amps' rails (D2/D5, 27 V zeners).</summary>
    const double Rails = 27;

    static readonly OpAmpModel Njm2147 = new(25e3, 1.2e6, 0.5e6, 5e6, 75, 1);

    public static AmpDesign Design { get; } = new(
        "AC15C1",
        [
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [
                    FirstStage("V1 Top Boost", 120e-12) with { SupplyNode = 2 },
                    TopBoost() with { SupplyNode = 1 },
                    Mixer("U1A U5A", false)
                ],
                [FirstStage("V1 Normal", 0) with { SupplyNode = 2 }, Mixer("Normal volume U1A U5A", true)]
            )
        ],
        VoxParts.PowerAmp(
            "2 x EL84",
            1,
            340,
            0.045,
            120,
            220e-6,
            15,
            InverterSupply,
            100e-9,
            100e-9,
            4.7e-9,
            9e3,
            SpeakerDesign.OpenBack1x12,
            // Two of the bridge's 180 Ω in the path, and the winding; R80 1k to the screens; R75 22k to the preamp.
            new(400, 47e-6, 1e3, 22e-6, 22e3, 10e-6, 4.8e-3, RectifierModel.Silicon),
            new(220e3, PotTaper.Linear),
            470,
            13.3e3, // R65/R77 3.3k behind R81/R82 10k
            new Pot(500e3, PotTaper.Audio)
        ),
        -6
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Volume",
            Middle = null,
            Presence = null,
            Depth = null,
            Bright = null,
            Switch1 = "Normal Ch"
        },
        // R18 10k and C22 to V2 (2.8 mA), R7 22k and C15 to V1 (both halves, 1 mA).
        PreampSupply = new([new(10e3, 10e-6, 2.8e-3), new(22e3, 10e-6, 1e-3)])
    };

    /// <summary>
    ///     One half of V1: 10k at the jack, 1M to ground, 33k to the grid, a 100k plate; the shared 1.5k with 22 µF as
    ///     this half's 3k with 11 µF (the other half idles beside it); <paramref name="plateToCathode" /> is the Top
    ///     Boost half's C16.
    /// </summary>
    static NetworkStageDesign FirstStage(string name, double plateToCathode) {
        var net = new StageNetlist();
        var grid = net.Node();
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Resistor(net.Input, grid, 33e3);
        var (_, plate, cathode) = VoxParts.GainStage(net, grid, FirstSupply, 3e3, 11e-6);
        if (plateToCathode > 0) {
            net.Capacitor(plate, cathode, plateToCathode);
        }

        net.Output = plate;
        return new(name, net, 10e3);
    }

    /// <summary>
    ///     The TB Volume and V2. C6 470 pF into VR2 (the Gain knob) with C67 10 pF from its top to ground and C8 120
    ///     pF across its upper part, the wiper to the first grid; R34 100k, R19 1.5k with C14 22 µF. The second half is
    ///     a cathode follower (R15 56k, C68 1 nF), its grid on the first half's plate: at these rails it idles in grid
    ///     current, so the two halves are one circuit here. Then the treble/bass network (see the type) and the
    ///     Treble's wiper through C19 220 nF into R23 330k over R25 120k. The output is U1A's non-inverting input, the
    ///     divider's tap.
    /// </summary>
    static NetworkStageDesign TopBoost() {
        var net = new StageNetlist();
        var top = net.Node();
        net.Capacitor(net.Input, top, 470e-12);
        net.Capacitor(top, StageNetlist.Ground, 10e-12);
        var wiper = net.Pot(top, StageNetlist.Ground, new(500e3, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, wiper, 120e-12, CapacitorRole.Bright);
        var (rail, plate, _) = VoxParts.GainStage(net, wiper, SecondSupply, 1.5e3, 22e-6);

        var (follower, trebleTop, trebleFoot) = (net.Node(), net.Node(), net.Node());
        var (slope, bass, coupled, tap) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(follower, StageNetlist.Ground, 56e3);
        net.Capacitor(follower, StageNetlist.Ground, 1e-9);
        net.Triode(rail, plate, follower, TriodeModel.Ecc83);
        net.Capacitor(follower, trebleTop, 56e-12, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, trebleFoot, new(1e6, PotTaper.Audio), PotRole.Treble);
        net.Resistor(follower, slope, 100e3);
        net.Capacitor(trebleFoot, slope, 22e-9, CapacitorRole.Stack);
        // The Bass as a rheostat, its wiper tied to its far end: the whole track at 10.
        net.Rheostat(trebleFoot, bass, new(1e6, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(slope, bass, 22e-9, CapacitorRole.Stack);
        net.Resistor(bass, StageNetlist.Ground, 10e3);
        net.Capacitor(treble, coupled, 220e-9);
        net.Resistor(coupled, tap, 330e3);
        net.Resistor(tap, StageNetlist.Ground, 120e3);
        net.Output = tap;
        return new("V2", net);
    }

    /// <summary>
    ///     The two op-amps between the channels and the inverter. U1A: R24 820k from its output to its inverting input,
    ///     R22 470k from there to the Normal Volume's wiper. U5A: R57 56k from U1A into its non-inverting input with
    ///     R60 56k (the Reverb at zero) and R54 22k to ground, R55 3.9k over R58 1k. Top Boost: the source is U1A's
    ///     non-inverting input and R22 goes to ground (the Normal Volume at zero). <paramref name="normal" />: the
    ///     stage starts at V1's plate with C5 22 nF, R1 330k, VR1 (the Gain knob) and C4 120 pF from the cap to the
    ///     wiper; U1A's non-inverting input rests at ground on R25.
    /// </summary>
    static NetworkStageDesign Mixer(string name, bool normal) {
        var net = new StageNetlist();
        var (minus, sum, mix, minus2, output) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        var plus = net.Input;
        var foot = StageNetlist.Ground;
        if (normal) {
            var (coupled, top) = (net.Node(), net.Node());
            net.Capacitor(net.Input, coupled, 22e-9);
            net.Resistor(coupled, top, 330e3);
            foot = net.Pot(top, StageNetlist.Ground, new(500e3, PotTaper.Audio), PotRole.Gain);
            net.Capacitor(coupled, foot, 120e-12, CapacitorRole.Bright);
            plus = StageNetlist.Ground;
        }

        net.Resistor(minus, foot, 470e3);
        net.Resistor(minus, sum, 820e3);
        net.OpAmp(plus, minus, sum, Njm2147, Rails);
        net.Resistor(sum, mix, 56e3);
        net.Resistor(mix, StageNetlist.Ground, 56e3);
        net.Resistor(mix, StageNetlist.Ground, 22e3);
        net.Resistor(minus2, StageNetlist.Ground, 1e3);
        net.Resistor(minus2, output, 3.9e3);
        net.OpAmp(mix, minus2, output, Njm2147, Rails);
        net.Output = output;
        return new(name, net);
    }
}
