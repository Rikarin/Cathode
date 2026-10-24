using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Cornford MK50H II, from the designer's own drawings (Martin Kidd, 25/01/11, five sheets: pre-amp, effects loop,
///     output stage with phase splitter, power supply, mute; el34world
///     https://el34world.com/charts/Schematics/files/Cornford/Cornford_mk50hii.pdf). The Overdrive channel, or with
///     Switch 1 the Clean channel:
///     <list type="bullet">
///         <item>
///             V1 (both channels): 0.033 µF from the jack (1M to ground), 68k, 100 pF grid to plate; 180k plate load
///             in series with 39k from the rail, 1k5 with 2.2 µF. The Clean channel takes its signal from the 39k / 180k
///             junction into its own stack (330 pF, 68k, 0.1 µF, 0.033 µF to the middle pot's wiper; 250k-L treble,
///             250k-A bass, 25k-L middle) and 0.33 µF into its 1M-A volume with 100 pF. The Overdrive channel: 4.7 nF
///             and 100k from the plate onto 4k7 and the 250k-A Gain as a variable resistor (a 2N5462 shorts the pot on
///             the Clean channel), into V2 (100k, 1k5 with 2.2 µF).
///         </item>
///         <item>
///             Overdrive: 22 nF and 680k with 470 pF onto the 1M-A Overdrive, V3 a cold clipper (100k, 15k
///             unbypassed), 33 nF and 470k with 470 pF over 470k.
///         </item>
///         <item>
///             Relay 1A puts either channel on V4 (220k, 1k5 unbypassed), DC-coupled to the V5 cathode follower
///             (100k), which drives the Overdrive's stack (220 pF in series with 100k to the 250k-L treble's top; 43k;
///             0.1 µF to the treble's foot, which is the top of the 1M-A bass, a variable resistor with 1 nF from its top to
///             ground; 0.033 µF to the 25k-L middle's wiper) with 470k into the 25k-L Volume, and the Clean channel's fixed 0.1 µF, 100k
///             over 15k divider; relay 1B sends one of the two to the loop.
///         </item>
///         <item>
///             The loop's return: V6 (1M grid leak, 220k, 1k5 unbypassed), 33 nF and 220k into the 1M-A Master (one of
///             the two, chosen by relay 3) as a variable resistor to ground; 22 nF into a 12AX7 long-tail pair (1M, 470,
///             10k, 91k/100k, 100 pF, 33 nF out to 220k), two EL34 (10k stoppers, 1k 4 W screens), feedback from the
///             8 Ω tap through 100 µF, the 1M-A Resonance with 4.7 nF across it and 47k into 4k7, with the 25k-L
///             Presence and 0.1 µF.
///         </item>
///     </list>
/// </summary>
/// <remarks>
///     <para>
///         Panel names from Fractal's notes on its model of this amp ("Gain", "Overdrive", "Volume", two Masters,
///         Presence and Resonance): the 250k Gain sets the sensitivity, the 1M Overdrive the distortion. The Volume knob
///         is the playing channel's (the Clean channel's 1M or the Overdrive's 25k); the channels share Bass, Middle and
///         Treble knobs; Master 2 is not modelled (relay 3 picks one of two identical pots).
///     </para>
///     <para>
///         The Resonance is the library's depth shelf (the feedback's low end cut to a tenth at full, against about a
///         twentieth at the pot's 1M), its corner at 400 Hz: the real control moves the shelf's corner too, from about
///         34 Hz to 690 Hz with the pot at full. The effects loop is taken as a wire (send to return).
///     </para>
///     <para>
///         The drawings give no voltages (guesses): 470 V B+, 465 V screens, then 10k droppers to 430 V (V6 and the
///         inverter), 390 V (V4 and V5), 350 V (V2 and V3) and 320 V (V1); the EL34s' 35 mA; the output transformer's
///         primary (3.4k plate to plate, usual for a 50 W EL34 pair). The supply is drawn: a 1N4007 bridge into
///         50 + 50 µF, 100 Ω 7 W to 50 + 50 µF at the screens, 47 µF at each later node.
///     </para>
/// </remarks>
public static class CornfordMk50 {
    const double InverterSupply = 430;
    const double V4Supply = 390;
    const double V2Supply = 350;
    const double V1Supply = 320;

    public static AmpDesign Design { get; } = new(
        "Cornford MK50H II",
        [
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [
                    V1(false),
                    new TriodeStageDesign("V2", TriodeModel.Ecc83, V2Supply, 100e3, 1.5e3, CathodeBypass: 2.2e-6),
                    V3(),
                    V4(),
                    Follower(false)
                ],
                [V1(true), V4(), Follower(true)]
            ),
            Return()
        ],
        new(
            "2 x EL34",
            PentodeModel.EL34,
            TubesPerSide: 1,
            Supply: 470,
            ScreenResistor: 1e3,
            IdleCurrent: 0.035,
            CathodeBias: false,
            CathodeBypass: 0,
            RatedPower: 50,
            PhaseInverter: new LongTailPairDesign(TriodeModel.Ecc83, InverterSupply, 91e3, 100e3, 10e3, 470),
            CouplingCap: 33e-9,
            GridLeak: 220e3,
            GridStopper: 10e3,
            CutCap: 0,
            CutPot: new(1e6, PotTaper.Linear),
            Feedback: new(1, 47e3, 4.7e3, new(25e3, PotTaper.Linear), 0.1e-6, 400),
            HasFeedback: true,
            Transformer: new(3.4e3),
            Speaker: SpeakerDesign.Closed4x12,
            PowerSupply: new(25, 25e-6, 100, 25e-6, 30e3, 47e-6, 6e-3),
            InverterInput: new(22e-9, 1e6)
        ),
        -6
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Gain",
            Control1 = "Overdrive",
            Control2 = "Volume",
            Depth = "Resonance",
            Cut = null,
            Bright = null,
            Switch1 = "Clean Ch"
        }
    };

    /// <summary>
    ///     V1 with the Overdrive's input divider; the output is the Overdrive's Gain node (V2's grid) or, for the Clean
    ///     channel (<paramref name="clean" />), the Clean volume's wiper, with the 2N5462 shorting the Gain pot. The Clean
    ///     channel's stack hangs on the 39k / 180k junction while the Overdrive plays too, but in series with 180k it
    ///     moves V1's plate load by about 6 % (half a decibel): the Overdrive's network leaves it out, for the CPU budget.
    /// </summary>
    static NetworkStageDesign V1(bool clean) {
        var net = new StageNetlist();
        var (jack, grid, rail, tap, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, jack, 0.033e-6);
        net.Resistor(jack, StageNetlist.Ground, 1e6);
        net.Resistor(jack, grid, 68e3);
        net.Capacitor(grid, plate, 100e-12);
        net.Supply(rail, V1Supply);
        net.Resistor(rail, tap, 39e3);
        net.Resistor(tap, plate, 180e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 2.2e-6);

        // The Overdrive channel's input divider, onto V2's grid; on the Clean channel the 2N5462 shorts the Gain pot.
        var (coupled, gainNode, gainFoot) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, coupled, 4.7e-9);
        net.Resistor(coupled, gainNode, 100e3);
        net.Resistor(gainNode, gainFoot, 4.7e3);
        if (!clean) {
            net.Rheostat(gainFoot, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Gain);
            net.Output = gainNode;
            return new("V1", net);
        }

        net.Resistor(gainFoot, StageNetlist.Ground, 100);

        // The Clean channel's stack and volume, on the 39k / 180k junction.
        var (trebleTop, slope, bassTop, middleTop, volumeTop) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(tap, trebleTop, 330e-12, CapacitorRole.Stack);
        net.Resistor(tap, slope, 68e3);
        net.Capacitor(slope, bassTop, 0.1e-6, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, bassTop, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Rheostat(bassTop, middleTop, new(250e3, PotTaper.Audio), PotRole.Bass);
        var middle = net.Pot(middleTop, StageNetlist.Ground, new(25e3, PotTaper.Linear), PotRole.Middle);
        net.Capacitor(slope, middle, 0.033e-6, CapacitorRole.Stack);
        net.Capacitor(treble, volumeTop, 0.33e-6);
        var volume = net.Pot(volumeTop, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Control2);
        net.Capacitor(volumeTop, volume, 100e-12, CapacitorRole.Bright);
        net.Output = volume;
        return new("V1", net);
    }

    /// <summary>
    ///     22 nF and 680k with 470 pF onto the 1M-A Overdrive, V3 (100k, 15k unbypassed), 33 nF and 470k with 470 pF
    ///     onto 470k (the output, V4's grid through relay 1A).
    /// </summary>
    static NetworkStageDesign V3() {
        var net = new StageNetlist();
        var (coupled, top, rail, plate, cathode, series, output) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 22e-9);
        net.Resistor(coupled, top, 680e3);
        net.Capacitor(coupled, top, 470e-12);
        var overdrive = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Control1);
        net.Supply(rail, V2Supply);
        net.Resistor(rail, plate, 100e3);
        net.Triode(plate, overdrive, cathode, TriodeModel.Ecc83);
        net.CathodeResistor(cathode, StageNetlist.Ground, 15e3);
        net.Capacitor(plate, series, 33e-9);
        net.Resistor(series, output, 470e3);
        net.Capacitor(series, output, 470e-12);
        net.Resistor(output, StageNetlist.Ground, 470e3);
        net.Output = output;
        return new("V3", net);
    }

    /// <summary>V4 (220k, 1k5 unbypassed), its grid on whichever channel relay 1A selects; the output is the plate.</summary>
    static TriodeStageDesign V4() => new("V4", TriodeModel.Ecc83, V4Supply, 220e3, 1.5e3);

    /// <summary>
    ///     The V5 follower (100k), DC-coupled to V4's plate, with the Overdrive's stack and Volume (the output) or, for
    ///     the Clean channel (<paramref name="clean" />), the fixed 0.1 µF, 100k over 15k divider (the output). Each
    ///     channel's network carries only its own path: the other one, some 50k or 115k on the follower's few hundred
    ///     ohms, changes its output by well under a tenth of a decibel.
    /// </summary>
    static NetworkStageDesign Follower(bool clean) {
        var net = new StageNetlist();
        var (rail, follower) = (net.Node(), net.Node());
        net.Supply(rail, V4Supply);
        net.Triode(rail, net.Input, follower, TriodeModel.Ecc83);
        net.CathodeResistor(follower, StageNetlist.Ground, 100e3);
        if (clean) {
            var (pad, divider) = (net.Node(), net.Node());
            net.Capacitor(follower, pad, 0.1e-6);
            net.Resistor(pad, divider, 100e3);
            net.Resistor(divider, StageNetlist.Ground, 15e3);
            net.Output = divider;
            return new("V5", net);
        }

        var (series, trebleTop, slope, bassTop, middleTop, volumeTop) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(follower, series, 220e-12, CapacitorRole.Stack);
        net.Resistor(series, trebleTop, 100e3);
        net.Resistor(follower, slope, 43e3);
        net.Capacitor(slope, bassTop, 0.1e-6, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, bassTop, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Rheostat(bassTop, middleTop, new(1e6, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(bassTop, StageNetlist.Ground, 1e-9, CapacitorRole.Stack);
        var middle = net.Pot(middleTop, StageNetlist.Ground, new(25e3, PotTaper.Linear), PotRole.Middle);
        net.Capacitor(slope, middle, 0.033e-6, CapacitorRole.Stack);
        net.Resistor(treble, volumeTop, 470e3);
        net.Output = net.Pot(volumeTop, StageNetlist.Ground, new(25e3, PotTaper.Linear), PotRole.Control2);
        return new("V5", net);
    }

    /// <summary>
    ///     The loop's return: V6 (1M grid leak, 220k, 1k5 unbypassed), 33 nF and 220k onto the 1M-A Master as a variable
    ///     resistor to ground (the output).
    /// </summary>
    static NetworkStageDesign Return() {
        var net = new StageNetlist();
        var (rail, plate, cathode, series, master) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(rail, InverterSupply);
        net.Resistor(rail, plate, 220e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(plate, series, 33e-9);
        net.Resistor(series, master, 220e3);
        net.Rheostat(master, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Master);
        net.Output = master;
        return new("V6", net);
    }
}
