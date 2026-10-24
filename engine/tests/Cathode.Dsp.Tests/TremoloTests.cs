using System.Diagnostics;
using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Tests;

/// <summary>
///     Stream T: the amps' own tremolos. The phase-shift oscillator against the textbook ladder; each model's rate range
///     from its drawing's parts; the depth at full intensity; intensity 0 leaving every model exactly as it is without
///     its tremolo; a smooth onset; no allocation; the cost.
/// </summary>
public class TremoloTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    /// <summary>The models with a tremolo of their own, and what kind.</summary>
    public static TheoryData<string> Models =>
        new() {
            "Deluxe 6G3", "Vibrolux 6G11", "Scout GA-17RVT", "Princeton AA964", "Princeton Reverb", "Deluxe Reverb",
            "Twin Reverb", "Super Reverb", "Vibroverb AB763", "Vibrolux Reverb AA964", "Bandmaster AB763",
            "Tremolux AA763", "Vibro-King", "18 W 1974X"
        };

    [Fact]
    public void Diagnostics() {
        foreach (var name in Models.Select(static m => (string)m[0])) {
            var design = Design(name);
            foreach (var oscillator in Oscillators(design)) {
                var (slowest, fastest) = oscillator.RateRange();
                output.WriteLine(
                    $"{name}: {slowest:0.00}..{fastest:0.00} Hz, grid {oscillator.GridSwing(0):0.0}/{oscillator.GridSwing(1):0.0} V, "
                    + $"plate {oscillator.PlateSwing(0):0}/{oscillator.PlateSwing(1):0} V, h2 {oscillator.Harmonic(2, 0.5):0.00} "
                    + $"h3 {oscillator.Harmonic(3, 0.5):0.00}, bias {oscillator.Bias:0.00}"
                );
            }

            foreach (var optical in Opticals(design)) {
                output.WriteLine(
                    $"   driver bias {optical.DriverBias(0):0.0}/{optical.DriverBias(1):0.0} V, lamp peak "
                    + $"{optical.PeakLampCurrent(0) * 1e3:0.00}/{optical.PeakLampCurrent(1) * 1e3:0.00} mA, cell "
                    + $"{optical.Cell.Resistance(optical.PeakLampCurrent(0.5)):0} ohm"
                );
            }
        }
    }

    internal static AmpDesign Design(string name) => AmpDesign.All.Single(a => a.Name == name);

    static IEnumerable<NetworkStageDesign> Networks(IEnumerable<AmpStageDesign> stages) =>
        stages.SelectMany(static s => s switch {
                ChannelSwitchStageDesign c => Networks(c.Off).Concat(Networks(c.On)),
                NetworkStageDesign n => [n],
                _ => []
            }
        );

    internal static IEnumerable<OpticalTremoloDesign> Opticals(AmpDesign design) =>
        Networks(design.Preamp).Select(static n => n.Tremolo).OfType<OpticalTremoloDesign>();

    internal static IEnumerable<PhaseShiftOscillatorDesign> Oscillators(AmpDesign design) {
        if (design.Power.BiasTremolo is { } bias) {
            yield return bias.Oscillator;
        }

        foreach (var tremolo in Networks(design.Preamp).Select(static n => n.Tremolo)) {
            switch (tremolo) {
                case OpticalTremoloDesign optical:
                    yield return optical.Oscillator;
                    break;
                case GridDriveTremoloDesign drive:
                    yield return drive.Oscillator;
                    break;
            }
        }
    }
}
