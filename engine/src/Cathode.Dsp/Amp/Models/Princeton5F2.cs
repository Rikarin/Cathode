namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Princeton 5F2-A (tweed, about 5 W, 1 × 10): the Champ 5F1's circuit (<see cref="Champ" />) with V1A's
///     cathode bypassed (25 µF), linear 1M volume and tone pots, and a tone control: the volume's top on the tone pot's
///     wiper, one end of the tone pot through 0.005 µF to ground, the other through 500 pF to the volume's wiper (V1B's
///     grid). Treble up turns the wiper toward the 500 pF end. Panel: Volume and Tone (on the Treble knob).
/// </summary>
/// <remarks>
///     Source: the Fender drawing "Princeton Amp 5F2-A" with its layout
///     (schematicheaven.net/fenderamps/princeton_5f2a_schem.pdf,
///     code K-EG): every passive value, both pots linear, the tone network's wiring, the supply chain (2 × 16 µF, 10k,
///     8 µF, 22k, 8 µF), 470 Ω ‖ 25 µF, 22k feedback into V1B's 1.5k. The drawing prints no voltages, so B+ and the nodes
///     are taken as the 5F1's (the same supply chain); the output transformer (Fender 265) is taken as the Champ's
///     8k : 4 Ω, and the speaker's curve is a typical 10".
/// </remarks>
public static class Princeton5F2 {
    /// <summary>An open-back 1 × 10 (typical values).</summary>
    static readonly SpeakerDesign OpenBack1x10 = new(6.2, 0.7e-3, 30, 110, 38, 2.8);

    public static AmpDesign Design { get; } = Champ.Build("Princeton 5F2-A", FirstStage(), OpenBack1x10, -16, "Tone");

    /// <summary>V1A (bypassed), the volume and the tone network; the output is the volume's wiper.</summary>
    static NetworkStageDesign FirstStage() {
        var net = new StageNetlist();
        var (bright, dark) = (net.Node(), net.Node());
        var top = net.Pot(bright, dark, new(1e6, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(dark, StageNetlist.Ground, 5e-9, CapacitorRole.Stack);
        Champ.FirstTriode(net, true, top);
        var volume = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Linear), PotRole.Gain);
        net.Capacitor(bright, volume, 500e-12, CapacitorRole.Stack);
        net.Output = volume;
        return new("V1A", net, 34e3);
    }
}
