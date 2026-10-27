using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Ampeg SVT (1970, 6550 version, about 300 W), channel one: the 12DW7 input stage (its 12AX7 half, 220k plate,
///     3.3k unbypassed) DC-coupled to its 12AU7 half as a cathode follower; the Bass Cut / Ultra Lo network (a bridged
///     pair of 2 nF
///     caps for the cut, a 150k / 2 nF / 68k twin-T notch near 500 Hz for Ultra Lo) into the 1M volume with its 500 pF
///     Ultra Hi cap; V3A (12AU7 half, 47k, 1.5k unbypassed); a passive Baxandall (1M bass and treble pots); the
///     midrange loop: V3B (12AX7 half) into V4A (12AX7 half, 470k) DC-coupled to the V4B follower (12AU7 half), whose
///     output returns to V3B's cathode through 56k, with the Mid-Range pot between the two ends of the loop and a
///     series resonant circuit at its wiper; the 6C4 cathode follower and the power amp's input clamp. The power amp: a
///     12DW7 input stage taking the feedback at its cathode, a cathodyne, 12BH7 voltage amplifiers and 12BH7 cathode
///     followers DC-coupled through 47k to six 6550 (three a side) in fixed bias, on a silicon bridge.
/// </summary>
/// <remarks>
///     <para>
///         Sources: Joe Piazza's redraws of the 1972 preamp and the 1970 6550 power amp (drtube.com/schematics/ampeg,
///         also at schematicheaven.net/ampegamps), read by eye at 6×: every resistor and capacitor value in the signal
///         path comes from them. The midrange frequencies (220 Hz, 800 Hz, 3 kHz) and the Ultra Hi / Ultra Lo figures
///         are from Ampeg's SVT owner's manuals. Plates near 680-700 V, screens at 350 V and 24 mA a tube at idle are
///         from Music Electronics Forum threads quoting Ampeg's 1970 service information. 12DW7: pins 6-8 are the 12AX7
///         section, pins 1-3 the 12AU7 one (JJ's and Electro-Harmonix's ECC832 data).
///     </para>
///     <para>
///         Read with care: the redraw's V3B cathode line (to the midrange network) crosses the 560 / 7.5k junction
///         (where the 1M grid leak and the 56k feedback return) without a joint; read as a joint it would short the
///         560. The midrange select switch puts 0.15 µF or 0.033 µF on taps of an inductor (Ampeg 320821-1) whose taps
///         and inductance are not published: the model has one inductor of 3.5 H (0.15 µF resonating at 220 Hz) with a
///         selected capacitance for each of the manual's three frequencies, and 300 Ω of winding resistance (guessed).
///     </para>
///     <para>
///         Guessed (no figure found): the preamp node (300 V), the power amp's small-signal nodes (input stage 330 V,
///         cathodyne 300 V with its balance pot at the balanced 4k, voltage amplifiers on a 600 V "H" node, followers'
///         plates at 350 V, their 47k loads returning to −120 V with their grids at −55 V), the 12BH7 (the 12AU7's
///         curves: the library has no 12BH7), the 6C4 (one 12AU7 half), the output transformer (2k plate to plate, from
///         300 W at 680 V), the feedback tap (the 4 Ω one), the supply's source resistance and the reservoir (100 µF
///         and 70 µF stacked: 41 µF). The separate screen winding is a stand-in: a dropping resistor from B+ into a
///         very large cap, which holds 350 V through a note as the real winding would. Channel two (12AX7 stages, its
///         own stack, mixed after the midrange loop) loads the mix node as 100k into 150k and is not modelled; nor is
///         the input jacks' switching beyond Bright.
///     </para>
/// </remarks>
public static class Svt {
    const double Open = 1e12;
    const double Wire = 1;
    const double Preamp = 300;

    /// <summary>
    ///     Newton's step tolerance in every preamp stage (the default is 1e-6): ten times looser costs a fraction of an
    ///     iteration a sample and leaves the output within -100 dB of the default's.
    /// </summary>
    const double NewtonTolerance = 1e-5;

    /// <summary>The midrange inductor (see the remarks).</summary>
    const double MidInductor = 3.5;

    /// <summary>The midrange select's capacitances: 220 Hz, 800 Hz and 3 kHz with <see cref="MidInductor" />.</summary>
    static readonly double[] MidCaps = [Resonant(220), Resonant(800), Resonant(3_000)];

    public static AmpDesign Design { get; } = new(
        "SVT",
        [InputStage(), FollowerStage(), VolumeStage(), ToneStack(), MidrangeLoop(), OutputStage(), ClampStage()],
        new(
            "6 x 6550",
            PentodeModel.SixtyFiveFifty,
            3,
            680,
            22,
            0.024,
            false,
            0,
            300,
            new DriverChainDesign(
                TriodeModel.Ecc83,
                330,
                220e3,
                2.2e3 + 220,
                new(TriodeModel.Ecc82, 300, 15e3, 10e3 + 4e3, 1e3),
                TriodeModel.Ecc82,
                600,
                47e3,
                1.8e3,
                350,
                47e3,
                -120,
                -55
            ),
            1, // DC-coupled from the followers
            10e6, // none: the followers' cathodes hold the grids
            47e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1 / Math.Sqrt(2), 47e3, 220, new(1e3, PotTaper.Linear), 1e-15),
            true,
            new(2e3, 15, 4e-3, 60, 2e-9, 100e3),
            SpeakerDesign.Closed8x10,
            new(40, 41e-6, 50e3, 0.5, 8.2e3, 40e-6, 6e-3, RectifierModel.Silicon),
            ScreenSupply: 350
        ),
        -5
    ) {
        Panel = new() {
            Gain = "Volume",
            Presence = null,
            Depth = null,
            Cut = null,
            Master = null,
            Bright = "Bright Input",
            Switch1 = "Ultra Hi",
            Switch2 = "Ultra Lo",
            Switch3 = "Bass Cut",
            Control1 = "Mid Freq"
        }
    };

    /// <summary>
    ///     V1A: the Normal input's 47k (the Bright input's 100k ‖ 470 pF with <see cref="SwitchRole.Bright" />) and
    ///     5.6M leak, the 12AX7 half (220k, 3.3k unbypassed); out at its plate.
    /// </summary>
    static NetworkStageDesign InputStage() {
        var net = new StageNetlist();
        var (grid, plate, cathode, rail) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.SwitchedResistor(net.Input, grid, 47e3, 100e3, SwitchRole.Bright);
        net.SwitchedCapacitor(net.Input, grid, 470e-12, SwitchRole.Bright, CapacitorRole.Bright);
        net.Resistor(grid, StageNetlist.Ground, 5.6e6);
        net.Resistor(rail, plate, 220e3);
        net.Resistor(cathode, StageNetlist.Ground, 3.3e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return new("V1A", net) { Tolerance = NewtonTolerance };
    }

    /// <summary>
    ///     V1B: the 12AU7 half as a follower (47k), its grid DC-coupled to V1A's plate (a Thévenin cut: the grid draws
    ///     no current while the cathode follows it); out at its cathode.
    /// </summary>
    static NetworkStageDesign FollowerStage() {
        var net = new StageNetlist();
        var (follower, rail) = (net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(follower, StageNetlist.Ground, 47e3);
        net.Triode(rail, net.Input, follower, TriodeModel.Ecc82);
        net.Output = follower;
        return new("V1B", net) { Tolerance = NewtonTolerance };
    }

    /// <summary>
    ///     The follower's 0.1 µF and 100k; Bass Cut (<see cref="SwitchRole.Switch3" />: the 2 nF - 2 nF bridge with
    ///     820k + 68k from its middle, a high-pass) and Ultra Lo (<see cref="SwitchRole.Switch2" />: the 150k - 150k
    ///     arm with 2 nF to ground beside the bridge, its 820k shorted, a twin-T notch near 500 Hz); otherwise a wire.
    ///     The 1M linear volume with the 500 pF Ultra Hi cap (<see cref="SwitchRole.Switch1" />), and V3A (12AU7 half,
    ///     47k, 1.5k unbypassed); out at its plate.
    /// </summary>
    static NetworkStageDesign VolumeStage() {
        var net = new StageNetlist();
        var (x, direct, y, z, w, arm, top) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(),
            net.Node());
        net.Capacitor(net.Input, x, 0.1e-6);
        net.Resistor(x, StageNetlist.Ground, 100e3);
        net.SwitchedResistor(x, direct, Wire, Open, SwitchRole.Switch2);
        net.SwitchedResistor(direct, top, Wire, Open, SwitchRole.Switch3);
        net.Capacitor(x, y, 2e-9);
        net.Capacitor(y, top, 2e-9);
        net.Resistor(y, z, 820e3);
        net.SwitchedResistor(y, z, Open, Wire, SwitchRole.Switch2);
        net.Resistor(z, StageNetlist.Ground, 68e3);
        net.Resistor(x, w, 150e3);
        net.Capacitor(w, StageNetlist.Ground, 2e-9);
        net.Resistor(w, arm, 150e3);
        net.SwitchedResistor(arm, top, Open, Wire, SwitchRole.Switch2);

        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Linear), PotRole.Gain);
        net.SwitchedCapacitor(top, wiper, 500e-12, SwitchRole.Switch1, CapacitorRole.Bright);
        var (plate, cathode, rail) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 47e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Triode(plate, wiper, cathode, TriodeModel.Ecc82);
        net.Output = plate;
        return new("V3A", net) { Tolerance = NewtonTolerance };
    }

    /// <summary>
    ///     The passive Baxandall: 0.1 µF in, 220k to the bass pot's top and 47 pF to the treble pot's, the 1M bass pot
    ///     with 1 nF and 10 nF from its wiper, 22k to ground, 470 pF to the 1M treble pot's bottom, 120k between the
    ///     wipers; out at the treble wiper. The tapers are not marked (taken as linear, as a Baxandall's usually are).
    /// </summary>
    static NetworkStageDesign ToneStack() {
        var net = new StageNetlist();
        var (input, bassTop, bassBottom, trebleTop, trebleBottom) = (net.Node(), net.Node(), net.Node(), net.Node(),
            net.Node());
        net.Capacitor(net.Input, input, 0.1e-6);
        net.Resistor(input, bassTop, 220e3);
        net.Capacitor(input, trebleTop, 47e-12, CapacitorRole.Stack);
        var bass = net.Pot(bassTop, bassBottom, new(1e6, PotTaper.Linear), PotRole.Bass);
        net.Capacitor(bassTop, bass, 1e-9, CapacitorRole.Stack);
        net.Capacitor(bass, bassBottom, 10e-9, CapacitorRole.Stack);
        net.Resistor(bassBottom, StageNetlist.Ground, 22e3);
        net.Capacitor(StageNetlist.Ground, trebleBottom, 470e-12, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, trebleBottom, new(1e6, PotTaper.Linear), PotRole.Treble);
        net.Resistor(bass, treble, 120e3);
        net.Output = treble;
        return new("Baxandall", net);
    }

    /// <summary>
    ///     The midrange loop: 0.01 µF into V3B (12AX7 half: 220k, 560 then 7.5k to ground, its 1M leak returning above
    ///     the 7.5k), 0.01 µF into V4A (12AX7 half: 470k, 3.3k, 1M leak) DC-coupled to V4B (12AU7 half) as a follower
    ///     (47k then 6.8k to ground), 56k from the follower back to the 560 / 7.5k junction; the Mid-Range pot (50k
    ///     linear) from V3B's cathode (0.68 µF + 470) to the follower's 6.8k tap (0.68 µF + 620, 220k to ground), its
    ///     wiper to ground through the series resonant circuit the Mid Freq selector (<see cref="PotRole.Control1" />)
    ///     tunes. Out at the 6.8k tap.
    /// </summary>
    static NetworkStageDesign MidrangeLoop() {
        var net = new StageNetlist();
        var (grid3, cathode3, junction, plate3, rail) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Capacitor(net.Input, grid3, 0.01e-6);
        net.Resistor(grid3, junction, 1e6);
        net.Resistor(rail, plate3, 220e3);
        net.Resistor(cathode3, junction, 560);
        net.Resistor(junction, StageNetlist.Ground, 7.5e3);
        net.Triode(plate3, grid3, cathode3, TriodeModel.Ecc83);

        var (grid4, plate4, cathode4, follower, tap) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(plate3, grid4, 0.01e-6);
        net.Resistor(grid4, StageNetlist.Ground, 1e6);
        net.Resistor(rail, plate4, 470e3);
        net.CathodeResistor(cathode4, StageNetlist.Ground, 3.3e3);
        net.Triode(plate4, grid4, cathode4, TriodeModel.Ecc83);
        net.Resistor(follower, tap, 47e3);
        net.Resistor(tap, StageNetlist.Ground, 6.8e3);
        net.Triode(rail, plate4, follower, TriodeModel.Ecc82);
        net.Resistor(follower, junction, 56e3);

        var (left, leftEnd, right, rightEnd, coil) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(cathode3, left, 0.68e-6);
        net.Resistor(left, leftEnd, 470);
        net.Capacitor(tap, right, 0.68e-6);
        net.Resistor(right, rightEnd, 620);
        net.Resistor(rightEnd, StageNetlist.Ground, 220e3);
        // Up (10) puts the resonant circuit across V3B's cathode (boost), down across the output tap (cut).
        var wiper = net.Pot(leftEnd, rightEnd, new(50e3, PotTaper.Linear), PotRole.Middle);
        var winding = net.Node();
        net.Inductor(wiper, winding, MidInductor);
        net.Resistor(winding, coil, 300);
        net.SelectedCapacitor(coil, StageNetlist.Ground, MidCaps, PotRole.Control1);
        net.Output = tap;
        return new("V3B-V4", net) { MaxIterations = 128, ReuseFactors = true, Tolerance = NewtonTolerance };
    }

    /// <summary>
    ///     The mix and output: 0.1 µF and 100k into the mix node (channel two's 100k into about 150k beside it), 0.02
    ///     µF into the 6C4 follower (1k then 47k, its 1M leak above the 1k); out at its cathode.
    /// </summary>
    static NetworkStageDesign OutputStage() {
        var net = new StageNetlist();
        var (coupled, mix, idle, grid, cathode, bias, rail) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Capacitor(net.Input, coupled, 0.1e-6);
        net.Resistor(coupled, mix, 100e3);
        net.Resistor(mix, idle, 100e3);
        net.Resistor(idle, StageNetlist.Ground, 150e3);
        net.Capacitor(mix, grid, 0.02e-6);
        net.Resistor(grid, bias, 1e6);
        net.Resistor(cathode, bias, 1e3);
        net.Resistor(bias, StageNetlist.Ground, 47e3);
        net.Triode(rail, grid, cathode, TriodeModel.Ecc82);
        net.Output = cathode;
        return new("V5", net) { MaxIterations = 64, Tolerance = NewtonTolerance };
    }

    /// <summary>
    ///     The follower's 0.1 µF out with 100k, and the power amp's input: 1k into antiparallel silicon diodes to
    ///     ground and its 470k leak. Out at the power amp's first grid.
    /// </summary>
    static NetworkStageDesign ClampStage() {
        var net = new StageNetlist();
        var (output, clamp) = (net.Node(), net.Node());
        net.Capacitor(net.Input, output, 0.1e-6);
        net.Resistor(output, StageNetlist.Ground, 100e3);
        net.Resistor(output, clamp, 1e3);
        net.Diodes(clamp, StageNetlist.Ground, DiodeModel.Silicon, 1, 1);
        net.Resistor(clamp, StageNetlist.Ground, 470e3);
        net.Output = clamp;
        return new("Clamp", net) { Tolerance = NewtonTolerance };
    }

    /// <summary>The capacitance that resonates with <see cref="MidInductor" /> at <paramref name="hertz" />.</summary>
    static double Resonant(double hertz) => 1 / (Math.Pow(2 * Math.PI * hertz, 2) * MidInductor);
}
