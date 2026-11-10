using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Parts the vintage and reissue Marshalls of the British vintage stream share beyond <see cref="MarshallParts" />:
///     their tone stacks, read from the factory drawings, and the Bluesbreaker's first stage and tremolo.
/// </summary>
public static class MarshallVintageParts {
    /// <summary>The 1962 reissue's Intensity pot, VR7: B220K.</summary>
    static readonly Pot BluesbreakerIntensity = new(220e3, PotTaper.Linear);

    /// <summary>R5, under the Intensity pot.</summary>
    const double BluesbreakerIntensityFoot = 100e3;

    /// <summary>
    ///     The 1962 reissue's tremolo oscillator, V6c ("1962 STD" issue 4): R39 150k from node B (the inverter's
    ///     supply) to the plate, 4.7 nF from plate to cathode, the cathode on R36 560 Ω with no bypass (R30 2k2 under
    ///     it is what the footswitch shorts to start it); C19 22 nF with C18 10 nF across it to R9 68k and the Speed
    ///     pot VR8 (two 1M sections in series, as rheostats), C17 22 nF to R38 470k plus R7 33k, C16 22 nF to the grid
    ///     with R37 1M plus R6 470k. Not on the drawing (guesses): node B's voltage (330 V, as the model's inverter),
    ///     the Speed pot's taper (linear) and which way it turns (up is faster). The 4.7 nF is taken to ground.
    /// </summary>
    public static PhaseShiftOscillatorDesign BluesbreakerOscillator { get; } = new(
        TriodeModel.Ecc83,
        330,
        150e3,
        560,
        32e-9,
        22e-9,
        22e-9,
        68e3,
        503e3,
        1.47e6,
        new(2e6, PotTaper.Linear),
        PlateShunt: 4.7e-9
    ) { CathodeBypassed = false };

    /// <summary>
    ///     The 1962 reissue's tremolo switch ("1962 STD" issue 4): FET1, a J174, from the Intensity pot's wiper to
    ///     ground. V6b, a cathode follower straight on V6c's plate, drives R40 68k into the RV2 22k preset, whose
    ///     wiper feeds the gate through R8 470k with D1 1N4007 across it (they only slow the gate by microseconds
    ///     against its few picofarads, and are left out). The J174's cut-off voltage is 5 to 10 V at 10 nA (7.5 V
    ///     here) and its r_DS(on) 85 Ω at most (Fairchild's J174 to J177 data sheet, rev. A, 1997). RV2's setting is
    ///     not on the drawing: see <see cref="JfetChopperDesign" />. The follower is taken as following the plate
    ///     exactly.
    /// </summary>
    public static JfetChopperDesign BluesbreakerSwitch { get; } = new(7.5, 85, 22.0 / (68 + 22), 10e-9);

    /// <summary>
    ///     The 1959SLP and 1987X reissues (drawings 1959-01-60-02 issue 2 and 1987-01-60-02 issue 4): 470 pF, 33k,
    ///     22 nF, 22 nF; B250K treble, A1M bass, B25K middle with the middle cap on its wiper.
    /// </summary>
    public static ToneStackDesign ReissueStack { get; } = new(
        "Plexi reissue",
        470e-12,
        33e3,
        22e-9,
        22e-9,
        new(250e3, PotTaper.Linear),
        new(1e6, PotTaper.Audio),
        new(25e3, PotTaper.Linear)
    ) { MiddleOnWiper = true };

    /// <summary>
    ///     The 1962 Bluesbreaker reissue ("1962 STD", 1989–93): 220 pF, 56k, 22 nF, 22 nF; B220K treble, A1M bass, B22K
    ///     middle (VR4) with C9 22 nF on its wiper.
    /// </summary>
    public static ToneStackDesign BluesbreakerStack { get; } = new(
        "Bluesbreaker",
        220e-12,
        56e3,
        22e-9,
        22e-9,
        new(220e3, PotTaper.Linear),
        new(1e6, PotTaper.Audio),
        new(22e3, PotTaper.Linear)
    ) { MiddleOnWiper = true };

    /// <summary>
    ///     The 1962 Bluesbreaker reissue's first tube (drawing "1962 STD"): V1a (Channel II, 150k plate) and V1b
    ///     (Channel I, 100k) on a shared 820 Ω with 330 µF; each channel's two inputs are 68k each (34k at the high
    ///     input, 1M at the jack). Channel I: 22 nF into Volume I (A1M, 100 pF across its upper half), its wiper
    ///     through 470k with 220 pF to the mixing node. Channel II: 22 nF into Volume II (A1M, 100 pF), its wiper
    ///     through 470k to the mixing node and, beside it, through 100k to the tremolo's node and 470k on to the mixing
    ///     node; the tremolo node's load with the tremolo off is 0.22 µF into the 220k Intensity and 100k to ground.
    ///     Switch 1 jumpers the channels, switch 2 plays Channel II alone. The output is the mixing node (V2a's grid).
    ///     The stage carries the amp's tremolo (<see cref="ChopperTremoloDesign" />): the J174 on the Intensity pot's
    ///     wiper, chopped by <see cref="BluesbreakerOscillator" />. The node it shunts sees about 80k (Channel II's
    ///     100k beside the 470k to the mixing node, against the pot's chain).
    /// </summary>
    public static NetworkStageDesign BluesbreakerInput(double supply) =>
        BluesbreakerStage(supply, false) with {
            Tremolo = new ChopperTremoloDesign(
                BluesbreakerStage(supply, true),
                BluesbreakerOscillator,
                BluesbreakerSwitch,
                80e3,
                BluesbreakerSwitch.OnResistance
            )
        };

    /// <summary>
    ///     The stage's netlist with the tremolo's FET open (the Intensity pot and R5 as one resistor) or
    ///     (<paramref name="closed" />) closed: the pot's wiper, turned by <see cref="PotRole.TremoloDepth" /> up
    ///     towards the 0.22 µF, on ground through the FET's r_DS(on).
    /// </summary>
    static NetworkStageDesign BluesbreakerStage(double supply, bool closed) {
        var net = new StageNetlist();
        var (rail, cathode, mix) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, supply);
        net.CathodeResistor(cathode, StageNetlist.Ground, 820);
        net.Capacitor(cathode, StageNetlist.Ground, 330e-6);

        var (oneJack, twoJack) = MarshallParts.ChannelJacks(net);

        // Channel I: V1b.
        var oneWiper = Triode(net, rail, cathode, oneJack, 100e3, PotRole.Gain);
        net.Resistor(oneWiper, mix, 470e3);
        net.Capacitor(oneWiper, mix, 220e-12);

        // Channel II: V1a, with the tremolo's branch.
        var twoWiper = Triode(net, rail, cathode, twoJack, 150e3, PotRole.Control1);
        net.Resistor(twoWiper, mix, 470e3);
        var tremolo = net.Node();
        net.Resistor(twoWiper, tremolo, 100e3);
        net.Resistor(tremolo, mix, 470e3);
        var intensity = net.Node();
        net.Capacitor(tremolo, intensity, 0.22e-6);
        if (closed) {
            var foot = net.Node();
            var wiper = net.Pot(intensity, foot, BluesbreakerIntensity, PotRole.TremoloDepth);
            net.Resistor(foot, StageNetlist.Ground, BluesbreakerIntensityFoot);
            net.Resistor(wiper, StageNetlist.Ground, BluesbreakerSwitch.OnResistance);
        } else {
            net.Resistor(intensity, StageNetlist.Ground, BluesbreakerIntensity.Ohms + BluesbreakerIntensityFoot);
        }

        net.Output = mix;
        return new("V1", net);
    }

    /// <summary>
    ///     One Bluesbreaker channel's triode, 22 nF and volume (100 pF across its upper half); returns the wiper.
    /// </summary>
    static int Triode(StageNetlist net, int rail, int cathode, int jack, double plateResistor, PotRole volume) {
        var (grid, plate) = (net.Node(), net.Node());
        net.Resistor(jack, StageNetlist.Ground, 1e6);
        net.Resistor(jack, grid, 34e3);
        net.Resistor(rail, plate, plateResistor);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        return Volume(net, plate, 22e-9, volume, 100e-12);
    }

    /// <summary>
    ///     A coupling cap into a 1M-A volume with an optional bright cap across its upper half; returns the wiper.
    /// </summary>
    static int Volume(StageNetlist net, int plate, double couplingCap, PotRole role, double brightCap) {
        var top = net.Node();
        net.Capacitor(plate, top, couplingCap);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), role);
        if (brightCap > 0) {
            net.Capacitor(top, wiper, brightCap, CapacitorRole.Bright);
        }

        return wiper;
    }
}
