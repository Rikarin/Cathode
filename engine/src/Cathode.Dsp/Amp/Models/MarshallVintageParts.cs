using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Parts the vintage and reissue Marshalls of the British vintage stream share beyond <see cref="MarshallParts" />:
///     their tone stacks, read from the factory drawings, and first stages with the tremolo circuit's load on a volume.
/// </summary>
public static class MarshallVintageParts {
    const double Closed = 10; // a closed switch or jack contact
    const double Open = 1e12;

    /// <summary>
    ///     The 1959SLP and 1987X reissues (drawings 1959-01-60-02 issue 2 and 1987-01-60-02 issue 4): 470 pF, 33k,
    ///     22 nF, 22 nF; B250K treble, A1M bass, B25K middle.
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
    );

    /// <summary>
    ///     The 1962 Bluesbreaker reissue ("1962 STD", 1989–93): 220 pF, 56k, 22 nF, 22 nF; B220K treble, A1M bass, B22K
    ///     middle.
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
    );

    /// <summary>
    ///     <see cref="MarshallParts.TwoChannelInput" /> (separate cathodes, 100k plates, 34k at each high input, the
    ///     jumper on switch 1 and the normal input on switch 2) with a load on the normal channel's volume wiper: the
    ///     tremolo's shunt network, <paramref name="loadCap" /> in series with <paramref name="loadResistor" /> to
    ///     ground (none where either is 0). The output is the mixing node.
    /// </summary>
    public static NetworkStageDesign TwoChannelInput(
        double supply,
        MarshallParts.ChannelDesign bright,
        MarshallParts.ChannelDesign normal,
        double loadCap,
        double loadResistor
    ) {
        var net = new StageNetlist();
        var (rail, mix) = (net.Node(), net.Node());
        net.Supply(rail, supply);

        var brightJack = net.Node();
        net.SwitchedResistor(net.Input, brightJack, Closed, Open, SwitchRole.Switch2);
        net.SwitchedResistor(net.Input, brightJack, 0, Closed, SwitchRole.Switch1);
        var normalJack = net.Node();
        net.SwitchedResistor(net.Input, normalJack, 0, Closed, SwitchRole.Switch1);
        net.SwitchedResistor(net.Input, normalJack, 0, Closed, SwitchRole.Switch2);
        foreach (var (channel, jack, loaded) in new[] { (bright, brightJack, false), (normal, normalJack, true) }) {
            var (grid, plate, cathode) = (net.Node(), net.Node(), net.Node());
            net.Resistor(jack, StageNetlist.Ground, 1e6);
            net.Resistor(jack, grid, 34e3);
            net.Resistor(rail, plate, 100e3);
            net.Resistor(cathode, StageNetlist.Ground, channel.CathodeResistor);
            net.Capacitor(cathode, StageNetlist.Ground, channel.CathodeBypass);
            net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
            var wiper = Volume(net, plate, channel.CouplingCap, channel.Volume, channel.BrightCap);
            net.Resistor(wiper, mix, channel.MixingResistor);
            if (channel.MixingBypass > 0) {
                net.Capacitor(wiper, mix, channel.MixingBypass);
            }

            if (loaded && loadCap > 0 && loadResistor > 0) {
                var foot = net.Node();
                net.Capacitor(wiper, foot, loadCap);
                net.Resistor(foot, StageNetlist.Ground, loadResistor);
            }
        }

        net.Output = mix;
        return new("V1", net);
    }

    /// <summary>
    ///     The 1962 Bluesbreaker reissue's first tube (drawing "1962 STD"): V1a (Channel II, 150k plate) and V1b
    ///     (Channel I, 100k) on a shared 820 Ω with 330 µF; each channel's two inputs are 68k each (34k at the high
    ///     input, 1M at the jack). Channel I: 22 nF into Volume I (A1M, 100 pF across its upper half), its wiper
    ///     through 470k with 220 pF to the mixing node. Channel II: 22 nF into Volume II (A1M, 100 pF), its wiper
    ///     through 470k to the mixing node and, beside it, through 100k to the tremolo's node and 470k on to the mixing
    ///     node; the tremolo node's load with the tremolo off is 0.22 µF into the 220k Intensity and 100k to ground.
    ///     Switch 1 jumpers the channels, switch 2 plays Channel II alone. The output is the mixing node (V2a's grid).
    /// </summary>
    public static NetworkStageDesign BluesbreakerInput(double supply) {
        var net = new StageNetlist();
        var (rail, cathode, mix) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, supply);
        net.CathodeResistor(cathode, StageNetlist.Ground, 820);
        net.Capacitor(cathode, StageNetlist.Ground, 330e-6);

        var oneJack = net.Node();
        net.SwitchedResistor(net.Input, oneJack, Closed, Open, SwitchRole.Switch2);
        net.SwitchedResistor(net.Input, oneJack, 0, Closed, SwitchRole.Switch1);
        var twoJack = net.Node();
        net.SwitchedResistor(net.Input, twoJack, 0, Closed, SwitchRole.Switch1);
        net.SwitchedResistor(net.Input, twoJack, 0, Closed, SwitchRole.Switch2);

        // Channel I: V1b.
        var oneWiper = Triode(net, rail, cathode, oneJack, 100e3, PotRole.Gain);
        net.Resistor(oneWiper, mix, 470e3);
        net.Capacitor(oneWiper, mix, 220e-12);

        // Channel II: V1a, with the tremolo's branch.
        var twoWiper = Triode(net, rail, cathode, twoJack, 150e3, PotRole.Control1);
        net.Resistor(twoWiper, mix, 470e3);
        var (tremolo, intensity) = (net.Node(), net.Node());
        net.Resistor(twoWiper, tremolo, 100e3);
        net.Resistor(tremolo, mix, 470e3);
        net.Capacitor(tremolo, intensity, 0.22e-6);
        net.Resistor(intensity, StageNetlist.Ground, 320e3);

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
