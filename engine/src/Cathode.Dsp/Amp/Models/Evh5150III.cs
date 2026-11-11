using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     EVH 5150III (100 W), red channel, drawn from the 100S (Stealth): V1A (220k, 1.8k ‖ 1 µF, 10k + 39 pF and 68k at
///     the input, a 68k + 830 pF treble shunt on the plate), 470 pF into the gain pot (1M audio, 1 nF bright cap), 470k
///     and 10k into V1B (100k, 1.8k ‖ 1 µF), 1 nF and a 470k/820k divider into the V2A cold clipper (5.1k unbypassed,
///     100k plate with 1 nF across it), 330k and 220k into V2B (220k, 1.8k ‖ 1 µF), 1M into V3A (220k, 2.2k unbypassed,
///     100k leak), 1M into V3B (220k, 2.2k unbypassed, 22 nF + 1M from plate to grid), 560k into a cathode follower
///     (its grid held by 220k and 330k ‖ 220 nF), 43k into a 33k + 10 nF shunt and the stack (47k slope, 470 pF, 47 nF
///     bass, 22 nF middle; 250k linear treble, 1M audio bass, 25k linear middle), the channel volume; a long-tail pair,
///     four 6L6GC.
/// </summary>
/// <remarks>
///     <para>
///         Source: the MARKTHERENCE LiveSPICE redraws "EVH 5150 III 100S Stealth - Channel 3" and its power amp, which
///         are hobbyist redraws of unclear provenance (docs/06 §3.4: "redraw only"). Every value here is unverified
///         against a factory drawing. The 100S is EVH's revoiced 5150III; no drawing of the standard 100 W head was
///         found, so the type keeps the family's name and the Stealth's channel 3 stands in for the red channel. How
///         far the two red channels differ is not known here.
///     </para>
///     <para>
///         The presence is the redraw's: a 10k audio pot in series with two 68 nF in parallel (136 nF) across the
///         4.7k shunt. Further guesses: its resonance network (120k ‖ 6.8 nF with a 1M rheostat in the feedback) is
///         the depth control's generic shelf here; the feedback tap is taken as 8 Ω, the output transformer as 1.9k,
///         the idle current as 35 mA; the stack's middle pot is the usual rheostat (the redraw has the cap on its
///         wiper).
///     </para>
///     <para>
///         <see cref="CleanCrunch" /> is channels one (green, clean) and two (blue, crunch) of the standard 100 W
///         head, read from Fender's "Service Diagram, Combined (schematic), EVH 5150-III Amplifier, Main PCA",
///         drawing 0070922000 rev A of 4 August 2006, sheet 1 of 3 (as distributed by thetubestore,
///         Fender-EVH-5150-III-Schematic.pdf). The two channels share every tube. V1B (R34 100k, R51 1.5k ‖ C29 1 µF,
///         R50 10k from the jack) feeds both gain networks: One Gain through C6 4.7 nF, R24 330k ‖ (C11 220 pF + R22
///         220k) with R23 33k across the pot; Two Gain through C13 750 pF and R25 180k with R26 150k across the pot
///         (both pots 250k, 30 % audio). K2A picks a wiper; R52 1M, C23 0.1 µF, R53 1M into V2A (R83 100k, R36 1.5k
///         ‖ C18 22 µF); C35 10 nF and R80 390k into V2B (R77 100k ‖ C24 270 pF, R54 1.5k unbypassed) with R56 180k
///         at its grid, beside which K2B grounds R55 68k on channel one, or R35 330k ahead of the 390k on channel
///         two; C25 3.3 nF, R84 390k and R57 180k into V3A (R85 100k, R37 1.5k unbypassed); R59 430k into the follower
///         V3B (R45 100k), its grid held by R58 150k into R60 430k ‖ C30 0.22 µF. The follower drives both stacks;
///         K3 picks the volume. One: C7 250 pF, R16 100k, C12 0.1 µF, C1 22 nF. Two: R38 43k with R17 43k + C10
///         10 nF to ground, then C8 470 pF, R18 47k, C14 0.1 µF, C2 22 nF. Both: 250k linear treble, 250k (15 %
///         audio) bass, 25k linear middle with the cap on its wiper, 1M (30 % audio) volume. Relay states are read
///         from the contacts' NC and NO marks: at rest K2 and K3 select channel one.
///     </para>
///     <para>
///         The same sheet draws the inverter and output stage this file's power amp already has (82k and 100k, 10k
///         tail over 4.7k, 470 Ω, 47 nF into 220k, 1.5k stoppers, 100 Ω screens, 485 V at the plates, 402 V on the
///         inverter), so <see cref="CleanCrunch" /> shares it, with two things from the sheet: the bias (its note:
///         20 mA per tube), and the preamp's rails, Y 402 V (C39 47 µF) through R109 ‖ R96 (two 10k: 5k) to X 341 V
///         (C40 47 µF: V3A, V3B) and R95 10k to W 297 V (C36 22 µF: V1B, V2A, V2B). Each node draws what its drawn
///         voltage drops across its resistor (every channel's tubes hang on them): 12.2 mA through the 5k, of
///         which 4.4 mA through the 10k. The dropper from the screens to Y is on the power-supply sheet, which was
///         not found: it is taken as what rests Y at 402 V with those 12.2 mA and the inverter's 3.2 mA (47 V at its
///         tail, TP30, over 14.7k), which is 5.3k. The presence and resonance controls are not on sheet 1 either and
///         stay the red type's.
///     </para>
///     <para>
///         The sheet prints the signal at test points along both channels (1 kHz at the jack, controls at half
///         rotation, Two Gain at 20 %). From V2B on, each stage's gain here is within 4 % of the sheet's on both
///         channels. V1B's is 50 where the sheet has 41, and channel two's gain pot at 20 % of its rotation passes
///         2.2 times what the sheet shows (the audio law leaves 9 % of the track there; the sheet's figure is what
///         3.4 % gives), so Gain on channel two is hotter low on the knob than the sheet's amp.
///     </para>
///     <para>
///         The sheet also draws channel three. It was found after the red type was built and the red type is not
///         changed here; where the two differ (the sheet's cold clipper has 39k on its cathode where the Stealth
///         redraw has 5.1k, its bass cap is 22 nF for 47 nF, the stage ahead of the follower has 100k on its plate
///         for 220k) the red type is the Stealth's.
///     </para>
///     <para>
///         Simplified on <see cref="CleanCrunch" />: the idle stack and volume as a load on the follower, the
///         effects loop between the volume and the inverter, and the red channel's 10k, 39 pF and 68k hanging on
///         the jack are left out; the jack's 1M is at V1B's grid.
///     </para>
/// </remarks>
public static class Evh5150III {
    /// <summary>The sheet's W rail (V1B, V2A, V2B) and X rail (V3A, V3B).</summary>
    const double RailW = 297, RailX = 341;

    public static AmpDesign Design { get; } = new(
        "5150III",
        [
            new NetworkStageDesign("V1A", InputStage(), 10e3),
            new NetworkStageDesign("V1B", GainStage()),
            new TriodeStageDesign(
                "V2A",
                TriodeModel.Ecc83,
                297,
                100e3,
                5.1e3,
                CouplingCap: 1e-9,
                SeriesResistor: 470e3,
                GridLeak: 820e3,
                PlateBypass: 1e-9
            ),
            new NetworkStageDesign("V2B", FourthStage()),
            new TriodeStageDesign(
                "V3A",
                TriodeModel.Ecc83,
                341,
                220e3,
                2.2e3,
                CouplingCap: 22e-9,
                SeriesResistor: 1e6,
                GridLeak: 100e3
            ),
            new NetworkStageDesign("V3B", FeedbackStage()),
            new NetworkStageDesign("V4A", FollowerAndStack())
        ],
        new(
            "4 x 6L6GC",
            PentodeModel.SixL6GC,
            2,
            485,
            100,
            0.035,
            false,
            0,
            100,
            // The 10k tail stands on the 4.7k feedback shunt: 14.7k from the bias junction to ground.
            new LongTailPairDesign(TriodeModel.Ecc83, 402, 82e3, 100e3, 14.7e3, 470),
            47e-9,
            220e3,
            1.5e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 39e3, 4.7e3, new(10e3, PotTaper.Audio), 136e-9),
            true,
            new(1.9e3),
            SpeakerDesign.Closed4x12,
            new(25, 100e-6, 100, 47e-6, 10e3, 22e-6, 6e-3)
        ),
        -7
    ) { Panel = AmpPanel.Standard with { Master = "Volume", Depth = "Resonance", Cut = null, Bright = null } };

    /// <summary>
    ///     Channel one (green, clean), or on Switch 1 channel two (blue, crunch), of the standard 100 W head, from
    ///     Fender's service diagram (see the remarks). Gain and Volume are the playing channel's.
    /// </summary>
    public static AmpDesign CleanCrunch { get; } = new(
        "5150III Clean/Crunch",
        [
            new TriodeStageDesign(
                "V1B",
                TriodeModel.Ecc83,
                RailW,
                100e3,
                1.5e3,
                1e-6,
                10e3,
                GridLeak: 1e6
            ) { SupplyNode = 2 },
            new ChannelSwitchStageDesign("Channel", SwitchRole.Switch1, Channel(false), Channel(true)) {
                SupplyNode = 2
            }
        ],
        Design.Power with {
            IdleCurrent = 0.020,
            // The sheet's Y node: C39 47 µF, 15.4 mA through a dropper sized to rest it at the drawn 402 V.
            PowerSupply = new(25, 100e-6, 100, 47e-6, 5.3e3, 47e-6, 15.4e-3)
        },
        -7
    ) {
        Panel = AmpPanel.Standard with {
            Master = "Volume",
            Depth = "Resonance",
            Cut = null,
            Bright = null,
            Switch1 = "Crunch"
        },
        // Y to X through R109 ‖ R96 (5k) into C40 47 µF, X to W through R95 10k into C36 22 µF; X's own draw is
        // what is left of the 5k's 12.2 mA after W's 4.4 mA.
        PreampSupply = new(
            [
                new(5e3, 47e-6, (402 - RailX) / 5e3 - (RailX - RailW) / 10e3),
                new(10e3, 22e-6, (RailX - RailW) / 10e3, 1)
            ]
        )
    };

    /// <summary>
    ///     Channel one's stages after V1B, or channel <paramref name="two" />'s: V2A behind its gain network, V2B,
    ///     V3A (C25 3.3 nF, R84 390k, R57 180k; R85 100k, R37 1.5k unbypassed) and the follower with its stack.
    /// </summary>
    static AmpStageDesign[] Channel(bool two) {
        var prefix = two ? "Two " : string.Empty;
        return [
            new NetworkStageDesign(prefix + "V2A", GainNetwork(two)) { SupplyNode = 2 },
            new NetworkStageDesign(prefix + "V2B", ThirdStage(two)) { SupplyNode = 2 },
            new TriodeStageDesign(
                prefix + "V3A",
                TriodeModel.Ecc83,
                RailX,
                100e3,
                1.5e3,
                CouplingCap: 3.3e-9,
                SeriesResistor: 390e3,
                GridLeak: 180e3
            ) { SupplyNode = 1 },
            new NetworkStageDesign(prefix + "V3B", FollowerAndChannelStack(two)) { SupplyNode = 1 }
        ];
    }

    /// <summary>
    ///     V2A behind the gain networks, both of which hang on V1B's plate: One Gain's (4.7 nF; 330k ‖ (220 pF +
    ///     220k); 33k across the pot) and Two Gain's (750 pF, 180k; 150k across the pot). The playing channel's
    ///     pot (250k, 30 % audio) is the Gain knob, the other one a fixed 250k. From the wiper 1M to ground, 0.1
    ///     µF, 1M grid leak; 100k plate, 1.5k ‖ 22 µF.
    /// </summary>
    static StageNetlist GainNetwork(bool two) {
        var net = new StageNetlist();
        var (oneTop, twoTop) = (OneGainTop(net), TwoGainTop(net));
        net.Resistor(two ? oneTop : twoTop, StageNetlist.Ground, 250e3);
        var wiper = net.Pot(
            two ? twoTop : oneTop,
            StageNetlist.Ground,
            new(250e3, PotTaper.Audio, 0.3),
            PotRole.Gain
        );
        var grid = net.Node();
        net.Resistor(wiper, StageNetlist.Ground, 1e6);
        net.Capacitor(wiper, grid, 0.1e-6);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        return PlateStage(net, grid, 22e-6, 0);
    }

    /// <summary>
    ///     One Gain's network from V1B's plate: 4.7 nF, then 330k ‖ (220 pF + 220k) to the top of the pot, with 33k
    ///     from there to ground. Returns the pot's top.
    /// </summary>
    static int OneGainTop(StageNetlist net) {
        var (coupled, lift, top) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 4.7e-9);
        net.Resistor(coupled, top, 330e3);
        net.Capacitor(coupled, lift, 220e-12);
        net.Resistor(lift, top, 220e3);
        net.Resistor(top, StageNetlist.Ground, 33e3);
        return top;
    }

    /// <summary>
    ///     Two Gain's network from V1B's plate: 750 pF and 180k to the top of the pot, with 150k from there to
    ///     ground. Returns the pot's top.
    /// </summary>
    static int TwoGainTop(StageNetlist net) {
        var (coupled, top) = (net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 750e-12);
        net.Resistor(coupled, top, 180e3);
        net.Resistor(top, StageNetlist.Ground, 150e3);
        return top;
    }

    /// <summary>
    ///     A gain stage of channels one and two on <paramref name="grid" />, on the W rail: 100k plate, 1.5k cathode
    ///     with <paramref name="cathodeBypass" /> across it and <paramref name="plateBypass" /> across the plate
    ///     resistor (0: none). Its plate is the netlist's output.
    /// </summary>
    static StageNetlist PlateStage(StageNetlist net, int grid, double cathodeBypass, double plateBypass) {
        var (supply, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Supply(supply, RailW);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        if (cathodeBypass > 0) {
            net.Capacitor(cathode, StageNetlist.Ground, cathodeBypass);
        }

        if (plateBypass > 0) {
            net.Capacitor(plate, supply, plateBypass, CapacitorRole.Plate);
        }

        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     V2B: 10 nF, 390k to the grid and 180k from the grid to ground; K2B grounds 68k from the grid on channel
    ///     one, or 330k from ahead of the 390k on channel <paramref name="two" />; 100k plate with 270 pF across
    ///     it, 1.5k unbypassed.
    /// </summary>
    static StageNetlist ThirdStage(bool two) {
        var net = new StageNetlist();
        var (coupled, grid) = (net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 10e-9);
        net.Resistor(coupled, grid, 390e3);
        net.Resistor(grid, StageNetlist.Ground, 180e3);
        if (two) {
            net.Resistor(coupled, StageNetlist.Ground, 330e3);
        } else {
            net.Resistor(grid, StageNetlist.Ground, 68e3);
        }

        return PlateStage(net, grid, 0, 270e-12);
    }

    /// <summary>
    ///     The follower V3B, DC-coupled to V3A's plate through 430k, its grid held by 150k into 430k ‖ 0.22 µF; 100k
    ///     cathode load. Channel one's stack hangs on the cathode (250 pF, 100k slope); channel
    ///     <paramref name="two" />'s behind 43k with 43k + 10 nF to ground (470 pF, 47k slope). Both: 0.1 µF bass and
    ///     22 nF middle caps, 250k linear treble, 250k (15 % audio) bass, the 25k linear middle pot with the middle
    ///     cap on its wiper, the 1M (30 % audio) volume on the treble wiper, and the 10M after the relays (two of
    ///     them on channel one).
    /// </summary>
    static StageNetlist FollowerAndChannelStack(bool two) {
        var net = new StageNetlist();
        var (grid, hold, supply, follower) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(supply, RailX);
        net.Triode(supply, grid, follower, TriodeModel.Ecc83);
        net.Resistor(follower, StageNetlist.Ground, 100e3);
        net.Resistor(net.Input, grid, 430e3);
        net.Resistor(grid, hold, 150e3);
        net.Resistor(hold, StageNetlist.Ground, 430e3);
        net.Capacitor(hold, StageNetlist.Ground, 0.22e-6);
        var (slope, trebleTop, trebleBottom, middleTop) = (net.Node(), net.Node(), net.Node(), net.Node());
        var drive = follower;
        if (two) {
            var shelf = net.Node();
            drive = net.Node();
            net.Resistor(follower, drive, 43e3);
            net.Resistor(drive, shelf, 43e3);
            net.Capacitor(shelf, StageNetlist.Ground, 10e-9);
        }

        net.Capacitor(drive, trebleTop, two ? 470e-12 : 250e-12, CapacitorRole.Stack);
        net.Resistor(drive, slope, two ? 47e3 : 100e3);
        var treble = net.Pot(trebleTop, trebleBottom, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(slope, trebleBottom, 0.1e-6, CapacitorRole.Stack);
        net.Rheostat(trebleBottom, middleTop, new(250e3, PotTaper.Audio, 0.15), PotRole.Bass);
        var middle = net.Pot(middleTop, StageNetlist.Ground, new(25e3, PotTaper.Linear), PotRole.Middle);
        net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
        var volume = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio, 0.3), PotRole.Master);
        net.Resistor(volume, StageNetlist.Ground, two ? 10e6 : 5e6);
        net.Output = volume;
        return net;
    }

    /// <summary>
    ///     V1A: 39 pF and 1M at the jack, 68k stopper; 220k plate, 1.8k ‖ 1 µF; 68k + 830 pF from the plate to B+.
    /// </summary>
    static StageNetlist InputStage() {
        var net = new StageNetlist();
        var (grid, supply, plate, cathode, shunt) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, StageNetlist.Ground, 39e-12);
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Resistor(net.Input, grid, 68e3);
        net.Supply(supply, 297);
        net.Resistor(supply, plate, 220e3);
        net.Resistor(plate, shunt, 68e3);
        net.Capacitor(shunt, supply, 830e-12);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.8e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The gain pot into V1B: 470 pF, 1M to ground, the 1M audio gain with 1 nF across its upper half and 1M from
    ///     its wiper to ground, 470k and 10k to the grid; 100k plate, 1.8k ‖ 1 µF.
    /// </summary>
    static StageNetlist GainStage() {
        var net = new StageNetlist();
        var (top, grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, top, 470e-12);
        net.Resistor(top, StageNetlist.Ground, 1e6);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, wiper, 1e-9, CapacitorRole.Bright);
        net.Resistor(wiper, StageNetlist.Ground, 1e6);
        net.Resistor(wiper, grid, 480e3);
        net.Supply(supply, 297);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.8e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>V2B: 22 nF into 330k to ground, 220k to the grid; 220k plate, 1.8k ‖ 1 µF.</summary>
    static StageNetlist FourthStage() {
        var net = new StageNetlist();
        var (coupled, grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 22e-9);
        net.Resistor(coupled, StageNetlist.Ground, 330e3);
        net.Resistor(coupled, grid, 220e3);
        net.Supply(supply, 341);
        net.Resistor(supply, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.8e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     V3B: 22 nF and 1M in, 1M grid leak; 220k plate, 2.2k unbypassed; 22 nF + 1M from the plate to the grid.
    /// </summary>
    static StageNetlist FeedbackStage() {
        var net = new StageNetlist();
        var (coupled, grid, supply, plate, cathode, feedback) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 22e-9);
        net.Resistor(coupled, grid, 1e6);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        net.Supply(supply, 341);
        net.Resistor(supply, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.2e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, feedback, 22e-9);
        net.Resistor(feedback, grid, 1e6);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The follower and the stack: 560k from V3B's plate to the grid, 220k from the grid to 330k ‖ 220 nF; 100k
    ///     cathode load; 43k into 33k + 10 nF to ground; the stack and the 1M audio volume on the treble wiper.
    /// </summary>
    static StageNetlist FollowerAndStack() {
        var net = new StageNetlist();
        var (grid, hold, supply, follower, drive, shelf, slope, trebleTop, trebleBottom, middle) = (
            net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(),
            net.Node()
        );
        net.Resistor(net.Input, grid, 560e3);
        net.Resistor(grid, hold, 220e3);
        net.Resistor(hold, StageNetlist.Ground, 330e3);
        net.Capacitor(hold, StageNetlist.Ground, 220e-9);
        net.Supply(supply, 341);
        net.Resistor(follower, StageNetlist.Ground, 100e3);
        net.Triode(supply, grid, follower, TriodeModel.Ecc83);
        net.Resistor(follower, drive, 43e3);
        net.Resistor(drive, shelf, 33e3);
        net.Capacitor(shelf, StageNetlist.Ground, 10e-9);
        net.Capacitor(drive, trebleTop, 470e-12, CapacitorRole.Stack);
        net.Resistor(drive, slope, 47e3);
        var treble = net.Pot(trebleTop, trebleBottom, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(slope, trebleBottom, 47e-9, CapacitorRole.Stack);
        net.Rheostat(trebleBottom, middle, new(1e6, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, new(25e3, PotTaper.Linear), PotRole.Middle);
        net.Output = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Master);
        return net;
    }
}
