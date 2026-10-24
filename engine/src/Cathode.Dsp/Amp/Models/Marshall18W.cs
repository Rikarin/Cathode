using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall 18 W combo (1958/1974, the 1974X reissue's circuit), from PedalMonkey's "Marshall 18 Watt Combo
///     Schematic" revision 5 (2001, drawn from authentic unmodified amps, with its node voltages:
///     https://el34world.com/charts/Schematics/files/Marshall/Marshall_18watt_schem.pdf): the Normal channel's V1A and
///     V1B in parallel (1M, 100k, a shared 820 with 50 µF) into its tone network (0.01 µF to the 500k-A Tone, 0.005 µF to
///     the 500k-A Volume, 100k between them) or (switch 1) the Tremolo channel (V3A: 68k, 1M; 220k from a 100k shared
///     with V3B's plate; 0.005 µF, 470k, 0.005 µF into its 470 pF / 0.005 µF tone and volume); the V2 long-tail pair
///     (0.01 µF into 470k, 100k/100k, 820, 47k) whose two grids are the two channels' inputs; two EL84 on a shared 125 Ω,
///     no feedback, an EZ81.
/// </summary>
/// <remarks>
///     <para>
///         The inverter is the mixer: the Normal volume drives V2A's grid through 0.01 µF, the Tremolo volume V2B's
///         through 0.022 µF. Each channel here drives the inverter's input; the idle channel's grid is taken as AC
///         ground (in the amp it sits on the idle volume's wiper). With the tremolo stopped the footswitch grounds V3B's grid,
///         so V3B idles on the shared cathode (820 plus the 1k Intensity rheostat, at half here) and its plate current
///         through the shared 100k sets V3A's supply; the phase-shift network's 0.05 µF decouples that node, the rest of
///         it is left out. Running, V3B oscillates and its current moves V3A's supply and cathode:
///         <see cref="Oscillator" /> drives V3B's grid in a copy of the channel's netlist (<see cref="GridDriveTremoloDesign" />),
///         on the Circuit Amp's tremolo parameters (Speed, and Depth for the oscillation's swing). The Normal Tone grounds its wiper: turned up, it grounds the 100k's foot (thinner, brighter);
///         down, the 0.01 µF's foot (darker); that direction is assumed.
///     </para>
///     <para>
///         Nodes from the drawing: 325 V (EL84 plates), 300 V (inverter), 280 V (preamp), from 1.5k and 8.2k droppers.
///         Not on it (guesses): the output transformer (8k plate to plate, as an AC15), the EL84 cathode bypass (the
///         drawing gives 50-500 µF: 250 µF here), the screen feed (the 300 V node, 100 Ω per tube) and the low input.
///     </para>
/// </remarks>
public static class Marshall18W {
    const double PreampSupply = 280;

    /// <summary>
    ///     The tremolo's oscillator, V3B, from the drawing: R11 100k from the 280 V node to its plate (shared with V3A's
    ///     220k), C7 0.05 µF from the plate to ground, C10 0.01 µF to R24 470k and the 2M audio Speed rheostat R33 to
    ///     ground, C9 0.01 µF to R26 1M, C8 0.01 µF to the grid with R10 2.2M to the cathode's foot (between R14 820 and
    ///     the 1k Intensity rheostat R32; the footswitch grounds the grid, stopping it). Approximations: the shared
    ///     cathode (820 with 50 µF, the Intensity at half) as V3B's own 1.32k, bypassed; V3A's path (220k and its plate)
    ///     as a 280k load on the plate; which way the Speed rheostat turns (up is faster).
    /// </summary>
    static PhaseShiftOscillatorDesign Oscillator { get; } = new(
        TriodeModel.Ecc83,
        PreampSupply,
        100e3,
        1.32e3,
        0.01e-6,
        0.01e-6,
        0.01e-6,
        470e3,
        1e6,
        2.2e6,
        new(2e6, PotTaper.Audio),
        PlateShunt: 0.05e-6,
        PlateLoad: 280e3
    );

    public static AmpDesign Design { get; } = new(
        "18 W 1974X",
        [
            new ChannelSwitchStageDesign("Channel", SwitchRole.Switch1, [Normal()], [Tremolo()])
        ],
        new(
            "2 x EL84",
            PentodeModel.EL84,
            1,
            325,
            100,
            0.034,
            true,
            250e-6,
            18,
            new LongTailPairDesign(TriodeModel.Ecc83, 300, 100e3, 100e3, 47e3, 820),
            0.01e-6,
            470e3,
            8.2e3,
            0,
            new(250e3, PotTaper.Linear),
            new(1, 47e3, 4.7e3, new(5e3, PotTaper.Linear), 0.1e-6),
            false,
            new(8e3),
            SpeakerDesign.OpenBack2x12,
            new(80, 32e-6, 1.5e3, 16e-6, 8.2e3, 32e-6, 3e-3, RectifierModel.EZ81),
            InverterInput: new(0.01e-6, 470e3),
            CathodeResistor: 125
        ),
        -6
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Volume",
            Treble = "Tone",
            Bass = null,
            Middle = null,
            Presence = null,
            Depth = null,
            Cut = null,
            Master = null,
            Bright = null,
            Switch1 = "Tremolo Ch",
            TremoloRate = "Speed",
            TremoloDepth = "Depth"
        }
    };

    /// <summary>
    ///     The Normal channel: V1A and V1B in parallel as one triode of twice the current (1M at the input, 100k, 820
    ///     with 50 µF), its tone network and the Volume's wiper (the output).
    /// </summary>
    static NetworkStageDesign Normal() {
        var net = new StageNetlist();
        var (rail, plate, cathode, toneTop, toneFoot, top) = (net.Node(), net.Node(), net.Node(), net.Node(),
            net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(rail, PreampSupply);
        net.Resistor(rail, plate, 100e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83.Paralleled(2));
        net.CathodeResistor(cathode, StageNetlist.Ground, 820);
        net.Capacitor(cathode, StageNetlist.Ground, 50e-6);
        net.Capacitor(plate, toneTop, 0.01e-6, CapacitorRole.Stack);
        net.Capacitor(plate, top, 0.005e-6, CapacitorRole.Stack);
        net.Resistor(top, toneFoot, 100e3);
        var tone = net.Pot(toneFoot, toneTop, new(500e3, PotTaper.Audio), PotRole.Treble);
        net.Resistor(tone, StageNetlist.Ground, 1);
        var volume = net.Pot(top, StageNetlist.Ground, new(500e3, PotTaper.Audio), PotRole.Gain);
        net.Output = volume;
        return new("V1", net);
    }

    /// <summary>
    ///     The Tremolo channel (its tremolo on <see cref="GridDriveTremoloDesign" />), stopped: V3A (68k, 1M; 220k from the node V3B's plate shares through 100k;
    ///     the shared cathode, 820 plus half the 1k Intensity, with 50 µF), V3B's grid grounded; 0.005 µF, 470k to
    ///     ground, 0.005 µF to the Volume's top and through 470 pF to the Tone, whose foot has 0.005 µF to ground; the
    ///     Tone's and the Volume's wipers are joined (the output).
    /// </summary>
    static NetworkStageDesign Tremolo() =>
        TremoloChannel(false) with {
            Tremolo = new GridDriveTremoloDesign(TremoloChannel(true), 0, Oscillator)
        };

    /// <summary>
    ///     The Tremolo channel's netlist (see <see cref="Tremolo" />); <paramref name="oscillating" /> puts V3B's grid on
    ///     a source (the netlist's first element) for the tremolo to swing.
    /// </summary>
    static NetworkStageDesign TremoloChannel(bool oscillating) {
        var net = new StageNetlist();
        var drive = StageNetlist.Ground;
        if (oscillating) {
            drive = net.Node();
            net.Supply(drive, 0);
        }

        var (grid, rail, shared, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        var (coupled, top, toneTop, toneFoot) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Resistor(net.Input, grid, 68e3);
        net.Supply(rail, PreampSupply);
        net.Resistor(rail, shared, 100e3);
        net.Capacitor(shared, StageNetlist.Ground, 0.05e-6);
        net.Resistor(shared, plate, 220e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Triode(shared, drive, cathode, TriodeModel.Ecc83);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.32e3);
        net.Capacitor(cathode, StageNetlist.Ground, 50e-6);
        net.Capacitor(plate, coupled, 0.005e-6);
        net.Resistor(coupled, StageNetlist.Ground, 470e3);
        net.Capacitor(coupled, top, 0.005e-6);
        net.Capacitor(top, toneTop, 470e-12, CapacitorRole.Stack);
        net.Capacitor(toneFoot, StageNetlist.Ground, 0.005e-6, CapacitorRole.Stack);
        var tone = net.Pot(toneTop, toneFoot, new(500e3, PotTaper.Audio), PotRole.Treble);
        var volume = net.Pot(top, StageNetlist.Ground, new(500e3, PotTaper.Audio), PotRole.Gain);
        net.Resistor(tone, volume, 1);
        net.Output = volume;
        return new("V3", net);
    }
}
