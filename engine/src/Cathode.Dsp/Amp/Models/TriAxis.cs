using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Mesa/Boogie TriAxis preamp (1992, rev. 1.08), as three types: the Rhythm modes (Green, or Yellow on Switch 1), Lead
///     1 and Lead 2 (Green, Yellow on Switch 1, Red on Switch 2), each into a generic power amp (the Dual Rectifier
///     model's
///     four 6L6GC). The modes are the drawing's light-dependent
///     resistors (LDRs) as switches, set per mode as its LDR table says.
/// </summary>
/// <remarks>
///     <para>
///         Source: the Mesa/Boogie factory drawings "TriAxis Audio" (sheet 1 of 2, rev. 1.08, 18-Feb-1992: V1A, V2A,
///         the stack, V2B, V3A, the Lead 2 overdrive V4A/V4B; sheet 2: the effects-return stage V5), the Dynamic Voice
///         sheet, and "TriAxis Diode/LDR Switching Matrix" (29-Mar-1993) with its LDR-to-mode table, in
///         https://el34world.com/charts/Schematics/files/Mesa_boogie/Boogie_triaxis.pdf (fourteen pages; read by eye).
///         The same PDF has a copy of sheet 1 with handwritten changes (C120B 120 → 180 pF, C801 1 nF → 750 pF, 250 pF
///         added from V4B's grid to its cathode) and a dealer's TX4 modification sheet; the printed values are used.
///     </para>
///     <para>
///         The modes: Rhythm takes the input to V2A (LDR17) with the plate's tone pad (82k and 0.15 µF, LDR13) and a
///         100k load after V2B (LDRK1); Green fully bypasses V2A's and V3A's cathodes (LDR12, LDR11). Lead 1 adds V1A
///         (LDR16, LDR15) through 4.7 nF and 270k onto the Lead 1 drive (LDR20, 250k), keeps both bypasses, and pads the
///         output (LDR8 4.7k, LDRK2 16k and 4.7k). Lead 2 runs V2B's output through the Lead 2 overdrive (LDR10, LDR6:
///         1M into the Lead 2 drive with 470k and 120 pF from grid to cathode at V4A, 82k, 1.5k ‖ 2.2 µF; 22 nF, 270k
///         into 68k ‖ 1 nF at V4B, 270k, 3.3k ‖ 0.22 µF; 47 nF, 220k ‖ 250 pF into 100k ‖ 500 pF), into V3A, whose
///         15 µF bypass Green shorts in (LDR9). Yellow and Red add the treble shift (750 pF, LDR14) and the gain's bright
///         cap (LDR18), and in Lead 1 the bass shift (82k beside the 100k slope, LDR34); Red adds the "shred" 120 pF
///         across V3A's output network (LDR7). This Lead 2 overdrive is the Mark IIC+ / IV lead circuit with the same
///         values (82k / 120 pF / 270k / 68k ‖ 1 nF / 3.3k ‖ 0.22 µF / 220k ‖ 250 pF / 500 pF).
///     </para>
///     <para>
///         The Dynamic Voice is the Mesa graphic equaliser with its sliders fixed: an op-amp (TL072) with 10k in, 10k
///         from the non-inverting to the inverting input and 10k of feedback; four series LCR branches (68 mH, 47 µF,
///         1.6k; 68 mH, 3.3 µF, 2.2k; 33 mH, 33 nF, 7.5k; 68 mH, 0.15 µF, 7.5k: 89 / 335 / 4,820 / 1,570 Hz) from the
///         inverting input and one (220 mH, 0.22 µF, 220 Ω: 720 Hz) from the non-inverting input to a common node, which
///         the Dynamic Voice LDR takes to ground: at zero the response is flat, turned up the lows and highs rise and
///         720 Hz is cut, the "V".
///     </para>
///     <para>
///         Guesses, marked: the LDRs' ranges, which the drawing does not print (the controls are a microprocessor's
///         DACs driving LEDs on the LDRs), take the Mark IIC+'s pot values: treble 250k linear, bass 250k audio, middle
///         10k linear, gain 1M audio, master 250k audio as a shunt, Lead 2 drive 1M audio as a shunt; Lead 1 drive is
///         printed 250k; the Dynamic Voice a 10k reverse-audio rheostat. The presence LDR (1 nF to ground behind 47k)
///         is fixed at 47k (no free control: the panel's Presence is the power amp's). The bright cap printed "C75P"
///         is taken as 75 pF. Left out: V3A's 3.3M feed from V2B in Lead 2 (a 3.3M ‖ 20 pF load here), the effects loop
///         (bypassed: LDR1, 4.7k into V5A) and V5B, the record outputs; the output divider (100k into the 25k output
///         level) at full level. The power amp is not the TriAxis's (it has none): the Dual Rectifier model's, documented
///         there.
///     </para>
/// </remarks>
public static class TriAxis {
    /// <summary>The front supply (B+, V1A and V2A/B).</summary>
    const double FrontSupply = 416;

    /// <summary>The A+ node (V3A, V4, V5).</summary>
    const double BackSupply = 420;

    /// <summary>A contact closed in a mode (an LDR lit).</summary>
    const double Lit = 1;

    public static AmpDesign Rhythm { get; } = Create(
        "TriAxis Rhythm",
        -7,
        [
            new NetworkStageDesign("V2A", StackStage(Mode.Rhythm)),
            new NetworkStageDesign("V2B", SecondGain()),
            new NetworkStageDesign("V3A", Recovery(Mode.Rhythm)),
            new BufferStageDesign("IC3A"),
            new NetworkStageDesign("Dynamic Voice", DynamicVoice()),
            new NetworkStageDesign("V5A", ReturnStage(Mode.Rhythm))
        ],
        AmpPanel.Standard with {
            Depth = null,
            Cut = null,
            Bright = null,
            Switch1 = "Yellow",
            Control2 = "Dynamic Voice"
        }
    );

    public static AmpDesign Lead1 { get; } = Create(
        "TriAxis Lead 1",
        -4,
        [
            new NetworkStageDesign("V1A", LeadOneInput()),
            new NetworkStageDesign("V2A", StackStage(Mode.LeadOne)),
            new NetworkStageDesign("V2B", SecondGain()),
            new NetworkStageDesign("V3A", Recovery(Mode.LeadOne)),
            new BufferStageDesign("IC3A"),
            new NetworkStageDesign("Dynamic Voice", DynamicVoice()),
            new NetworkStageDesign("V5A", ReturnStage(Mode.LeadOne))
        ],
        LeadPanel("Lead 1 Drive")
    );

    public static AmpDesign Lead2 { get; } = Create(
        "TriAxis Lead 2",
        -4,
        [
            new NetworkStageDesign("V2A", StackStage(Mode.LeadTwo)),
            new NetworkStageDesign("V2B", SecondGain()),
            new NetworkStageDesign("V4A", LeadTwoDrive()),
            new NetworkStageDesign("V4B", LeadTwoStage()),
            new NetworkStageDesign("V3A", Recovery(Mode.LeadTwo)),
            new BufferStageDesign("IC3A"),
            new NetworkStageDesign("Dynamic Voice", DynamicVoice()),
            new NetworkStageDesign("V5A", ReturnStage(Mode.LeadTwo))
        ],
        LeadPanel("Lead 2 Drive")
    );

    /// <summary>
    ///     The generic power amp the TriAxis feeds here: the Dual Rectifier model's (four 6L6GC at 460 V, a long-tail pair,
    ///     the factory drawing's values) with its feedback on and without its panel switches, as a Mesa 2:Ninety-style
    ///     stage would be (Mesa's own TriAxis power amps are Simul-Class; the plain pentode quad costs less). A getter, not
    ///     an initialiser: the designs above are initialised first, whatever order the members are in.
    /// </summary>
    static PowerAmpDesign PowerAmp => DualRectifier.Design.Power with {
        Name = "4 x 6L6GC (Dual Rectifier power amp)", SupplySwitches = null, FeedbackOffSwitch = null
    };

    static AmpPanel LeadPanel(string drive) =>
        AmpPanel.Standard with {
            Depth = null,
            Cut = null,
            Bright = null,
            Switch1 = "Yellow",
            Switch2 = "Red",
            Control1 = drive,
            Control2 = "Dynamic Voice"
        };

    static AmpDesign Create(string name, double trim, IReadOnlyList<AmpStageDesign> preamp, AmpPanel panel) =>
        new(name, preamp, PowerAmp, trim) { Panel = panel };

    /// <summary>
    ///     An LDR contact between <paramref name="a" /> and <paramref name="b" />: lit in every voicing of the mode when
    ///     <paramref name="always" />, else in the Yellow and Red voicings (Switch 1 or Switch 2; Red implies Yellow's
    ///     LDRs).
    /// </summary>
    static void Contact(StageNetlist net, int a, int b, bool always) {
        if (always) {
            net.Resistor(a, b, Lit);
            return;
        }

        net.SwitchedResistor(a, b, 0, Lit, SwitchRole.Switch1);
        net.SwitchedResistor(a, b, 0, Lit, SwitchRole.Switch2);
    }

    /// <summary>
    ///     A capacitor from <paramref name="a" /> to <paramref name="b" /> through an LDR contact (<see cref="Contact" />),
    ///     with the drawing's 10M bleeder from the capacitor's other side to <paramref name="b" /> (a plain capacitor when
    ///     always lit).
    /// </summary>
    static void SwitchedCap(StageNetlist net, int a, int b, double farads, bool always, CapacitorRole role) {
        if (always) {
            net.Capacitor(a, b, farads, role);
            return;
        }

        var k = net.Node();
        net.Capacitor(a, k, farads, role);
        net.Resistor(k, b, 10e6);
        Contact(net, k, b, always);
    }

    /// <summary>
    ///     Lead 1's V1A: 1M grid leak; 100k plate, 1.5k ‖ 15 µF; 4.7 nF and 270k onto the Lead 1 drive (250k, Control
    ///     1, as a rheostat to ground); the output is its top (LDR15 on to V2A's grid).
    /// </summary>
    static StageNetlist LeadOneInput() {
        var net = new StageNetlist();
        var (rail, plate, cathode, coupled, drive) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(rail, FrontSupply);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 15e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 4.7e-9);
        net.Resistor(coupled, drive, 270e3);
        net.Rheostat(drive, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Control1);
        net.Output = drive;
        return net;
    }

    /// <summary>
    ///     V2A and the stack: 1M grid leak; 150k plate, 1.5k ‖ 0.47 µF and 15 µF through 22k (shorted by LDR12 in
    ///     Rhythm Green and Lead 1); the Mark stack on the plate (250 pF, 750 pF treble shift; 100k slope; 0.1 µF, 47 nF),
    ///     82k from the plate to a node X, X to the slope node in Lead 1's Yellow and Red (bass shift, LDR34) or through
    ///     0.15 µF to ground in Rhythm (tone pad, LDR13); the gain on the treble wiper with 120 pF across its upper part
    ///     and 75 pF more in Yellow and Red (LDR18; Lead 2 always).
    /// </summary>
    static StageNetlist StackStage(Mode mode) {
        var net = new StageNetlist();
        var (rail, plate, cathode, boost, x) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(rail, FrontSupply);
        net.Resistor(rail, plate, 150e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 0.47e-6);
        net.Capacitor(cathode, boost, 15e-6);
        switch (mode) {
            case Mode.Rhythm:
                // LDR12 lit in Rhythm Green (Yellow, Switch 1, leaves the 22k in).
                net.SwitchedResistor(boost, StageNetlist.Ground, Lit, 22e3, SwitchRole.Switch1);
                break;
            case Mode.LeadOne:
                net.Resistor(boost, StageNetlist.Ground, Lit);
                break;
            default:
                net.Resistor(boost, StageNetlist.Ground, 22e3);
                break;
        }

        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);

        var (wiper, slope, trebleTop) = MesaParts.Stack(
            net,
            plate,
            250e-12,
            0,
            null,
            100e3,
            0.1e-6,
            47e-9,
            new(250e3, PotTaper.Linear),
            new(250e3, PotTaper.Audio),
            new(10e3, PotTaper.Linear)
        );
        // LDR14 (treble shift): Lead 1 Yellow and Red, Lead 2 always.
        if (mode != Mode.Rhythm) {
            SwitchedCap(net, plate, trebleTop, 750e-12, mode == Mode.LeadTwo, CapacitorRole.Stack);
        }

        net.Resistor(plate, x, 82e3);
        switch (mode) {
            case Mode.Rhythm:
                // LDR13 (tone pad), lit in both Rhythm modes.
                net.Capacitor(x, StageNetlist.Ground, 0.15e-6);
                break;
            case Mode.LeadOne:
                // LDR34 (bass shift): 82k beside the slope.
                Contact(net, x, slope, false);
                break;
        }

        var gain = net.Pot(wiper, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(wiper, gain, 120e-12, CapacitorRole.Bright);
        // LDR18 (bright): Lead 1 Yellow and Red, Lead 2 always.
        if (mode != Mode.Rhythm) {
            SwitchedCap(net, wiper, gain, 75e-12, mode == Mode.LeadTwo, CapacitorRole.Bright);
        }

        net.Output = gain;
        return net;
    }

    /// <summary>V2B: the gain's wiper on the grid; 100k plate, 1.5k ‖ 15 µF.</summary>
    static StageNetlist SecondGain() {
        var net = new StageNetlist();
        var (rail, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, FrontSupply);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 15e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     Lead 2's V4A: 47 nF from V2B with 100k to ground (LDRK1), 1M into the Lead 2 drive (1M audio rheostat to
    ///     ground, Control 1) beside 470k, 120 pF from grid to cathode; 82k plate, 1.5k ‖ 2.2 µF.
    /// </summary>
    static StageNetlist LeadTwoDrive() {
        var net = new StageNetlist();
        var (coupled, grid, rail, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 100e3);
        net.Resistor(coupled, grid, 1e6);
        net.Rheostat(grid, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Control1);
        net.Resistor(grid, StageNetlist.Ground, 470e3);
        net.Capacitor(grid, cathode, 120e-12);
        net.Supply(rail, BackSupply);
        net.Resistor(rail, plate, 82e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 2.2e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>V4B: 22 nF, 270k into 68k ‖ 1 nF; 270k plate, 3.3k ‖ 0.22 µF.</summary>
    static StageNetlist LeadTwoStage() {
        var net = new StageNetlist();
        var (coupled, grid, rail, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 22e-9);
        net.Resistor(coupled, grid, 270e3);
        net.Resistor(grid, StageNetlist.Ground, 68e3);
        net.Capacitor(grid, StageNetlist.Ground, 1e-9);
        net.Supply(rail, BackSupply);
        net.Resistor(rail, plate, 270e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 3.3e3);
        net.Capacitor(cathode, StageNetlist.Ground, 0.22e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     V3A and its output network. In: Rhythm and Lead 1, 47 nF from V2B (100k to ground in Rhythm), 3.3M ‖ 20 pF;
    ///     Lead 2, 47 nF from V4B, 220k ‖ 250 pF into 100k ‖ 500 pF (and the 3.3M ‖ 20 pF to V2B's side as a load); 680k
    ///     at the grid. V3A: 100k plate, 1.5k with 2.2 µF through 22k (shorted in Rhythm Green and Lead 1, LDR11) and
    ///     15 µF through 22k (shorted in Lead 2 Green, LDR9). Out: 47 nF, 47k into the presence (1 nF and the LDR, 47k),
    ///     150k (120 pF across both in Red, LDR7), 4.7k to ground in the leads (LDR8), 47k into the master (250k audio
    ///     shunt), 10k into the Dynamic Voice's input (16k to ground in the leads, LDRK2A; 1M).
    /// </summary>
    static StageNetlist Recovery(Mode mode) {
        var net = new StageNetlist();
        var (coupled, grid, rail, plate, cathode, bass, mid) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        if (mode == Mode.LeadTwo) {
            var load = net.Node();
            net.Resistor(coupled, grid, 220e3);
            net.Capacitor(coupled, grid, 250e-12);
            net.Resistor(grid, StageNetlist.Ground, 100e3);
            net.Capacitor(grid, StageNetlist.Ground, 500e-12);
            net.Resistor(grid, load, 3.3e6);
            net.Capacitor(grid, load, 20e-12);
            net.Resistor(load, StageNetlist.Ground, 100e3);
        } else {
            if (mode == Mode.Rhythm) {
                net.Resistor(coupled, StageNetlist.Ground, 100e3);
            }

            net.Resistor(coupled, grid, 3.3e6);
            net.Capacitor(coupled, grid, 20e-12);
        }

        net.Resistor(grid, StageNetlist.Ground, 680e3);
        net.Supply(rail, BackSupply);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, bass, 2.2e-6);
        net.Capacitor(cathode, mid, 15e-6);
        switch (mode) {
            case Mode.Rhythm:
                net.SwitchedResistor(bass, StageNetlist.Ground, Lit, 22e3, SwitchRole.Switch1);
                net.Resistor(mid, StageNetlist.Ground, 22e3);
                break;
            case Mode.LeadOne:
                net.Resistor(bass, StageNetlist.Ground, Lit);
                net.Resistor(mid, StageNetlist.Ground, 22e3);
                break;
            default: {
                net.Resistor(bass, StageNetlist.Ground, 22e3);
                // LDR9, lit in Lead 2 Green only: two contacts in series, each open in its voicing.
                var between = net.Node();
                net.Resistor(mid, StageNetlist.Ground, 22e3);
                net.SwitchedResistor(mid, between, Lit, 0, SwitchRole.Switch1);
                net.SwitchedResistor(between, StageNetlist.Ground, Lit, 0, SwitchRole.Switch2);
                net.Resistor(between, StageNetlist.Ground, 10e6);
                break;
            }
        }

        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);

        var (p, q, presence, o, shred, m, input) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, p, 47e-9);
        net.Resistor(p, q, 47e3);
        net.Capacitor(q, presence, 1e-9);
        net.Resistor(presence, StageNetlist.Ground, 47e3);
        net.Resistor(q, o, 150e3);
        if (mode != Mode.Rhythm) {
            net.SwitchedResistor(p, shred, 0, Lit, SwitchRole.Switch2);
            net.Capacitor(shred, o, 120e-12);
            net.Resistor(shred, StageNetlist.Ground, 10e6);
            net.Resistor(o, StageNetlist.Ground, 4.7e3);
        }

        net.Resistor(o, m, 47e3);
        net.Rheostat(m, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Master);
        net.Resistor(m, input, 10e3);
        net.Resistor(input, StageNetlist.Ground, mode == Mode.Rhythm ? 1e6 : 16e3 * 1e6 / (16e3 + 1e6));
        net.Output = input;
        return net;
    }

    /// <summary>
    ///     The Dynamic Voice (IC3B, TL072 on ±16 V): 10k from the buffer to the non-inverting input P, 10k from P to the
    ///     inverting input N, 10k from the output to N; the LCR branches from N (and the 720 Hz one from P) to a common
    ///     node, the Dynamic Voice LDR (Control 2, a 10k reverse-audio rheostat that takes resistance out as it turns up)
    ///     beside 10 MΩ from it to ground.
    /// </summary>
    static StageNetlist DynamicVoice() {
        var net = new StageNetlist();
        var (p, n, output, common) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, p, 10e3);
        net.Resistor(p, n, 10e3);
        net.Resistor(output, n, 10e3);
        Branch(net, n, common, 68e-3, 47e-6, 1.6e3);
        Branch(net, n, common, 68e-3, 3.3e-6, 2.2e3);
        Branch(net, n, common, 33e-3, 33e-9, 7.5e3);
        Branch(net, n, common, 68e-3, 0.15e-6, 7.5e3);
        Branch(net, p, common, 220e-3, 0.22e-6, 220);
        net.Rheostat(common, StageNetlist.Ground, new(10e3, PotTaper.ReverseAudio), PotRole.Control2, true);
        net.Resistor(common, StageNetlist.Ground, 10e6);
        net.OpAmp(p, n, output, OpAmpModel.TL072, 16);
        net.Output = output;
        return net;

        static void Branch(StageNetlist net, int from, int to, double henries, double farads, double ohms) {
            var (a, b) = (net.Node(), net.Node());
            net.Inductor(from, a, henries);
            net.Resistor(a, b, ohms);
            net.Capacitor(b, to, farads);
        }
    }

    /// <summary>
    ///     The effects-return stage V5A: 1 µF, 10k (4.7k to ground in the leads, LDRK2B), 4.7k into the grid with 47k to
    ///     ground; 120k plate, 1k ‖ 15 µF; 47 nF, 100k into the 25k output level (at full).
    /// </summary>
    static StageNetlist ReturnStage(Mode mode) {
        var net = new StageNetlist();
        var (coupled, pad, grid, rail, plate, cathode, output) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 1e-6);
        net.Resistor(coupled, pad, 10e3);
        if (mode != Mode.Rhythm) {
            net.Resistor(pad, StageNetlist.Ground, 4.7e3);
        }

        net.Resistor(pad, grid, 4.7e3);
        net.Resistor(grid, StageNetlist.Ground, 47e3);
        net.Supply(rail, BackSupply);
        net.Resistor(rail, plate, 120e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.Capacitor(cathode, StageNetlist.Ground, 15e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        var series = net.Node();
        net.Capacitor(plate, series, 47e-9);
        net.Resistor(series, output, 100e3);
        net.Resistor(output, StageNetlist.Ground, 25e3);
        net.Output = output;
        return net;
    }

    enum Mode {
        Rhythm,
        LeadOne,
        LeadTwo
    }
}
