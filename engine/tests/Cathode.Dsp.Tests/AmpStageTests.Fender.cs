using Cathode.Dsp.Amp;
using Cathode.Dsp.Tests.Circuits;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Fender family's new stage combinations (docs/06 §3.1) against the reference MNA simulator: the AB763 mixing
///     network with V4B on either channel, the Super's switched-mid first stage, the Princeton's third stage over its
///     feedback resistor, and the tweed input networks (one 12AY7 half routed to either channel with the other's plate
///     standing idle, the volumes, the 5E3's "backwards" volumes and tone control; both halves on one cathode when
///     jumpered).
/// </summary>
public partial class AmpStageTests {
    public static TheoryData<string, string, double> FenderNetworks =>
        new() {
            // Model, stage and its switches ("v": Switch1, "b": bright), amplitude in volts.
            { "Twin Reverb", "Mixer", 0.3 },
            { "Twin Reverb", "Mixer v", 0.3 },
            { "Twin Reverb", "Mixer v", 8 },
            { "Super Reverb", "V1A", 0.05 },
            { "Super Reverb", "V1A vb", 0.05 },
            { "Princeton Reverb", "V3B", 2 },
            { "Bassman 5F6-A", "V1", 0.1 },
            { "Bassman 5F6-A", "V1 v", 1 },
            { "Bassman 5F6-A Jumped", "V1", 1 },
            { "Bassman 5F6-A", "V2A", 1 },
            { "Tweed Deluxe 5E3", "V1", 0.1 },
            { "Tweed Deluxe 5E3", "V1 v", 1 },
            { "Tweed Deluxe 5E3 Jumped", "V1", 1 },
            { "Tweed Deluxe 5E3", "V2A", 0.5 }
        };

    [Theory]
    [MemberData(nameof(FenderNetworks))]
    public void Fender_stages_match_the_reference_in_the_time_domain(string model, string stage, double amplitude) {
        var parts = stage.Split(' ');
        var flags = parts.Length > 1 ? parts[1] : string.Empty;
        var amp = AmpDesign.All.Single(a => a.Name == model);
        var design = (NetworkStageDesign)amp.Preamp.Single(s => s.Name == parts[0]);
        var controls = new AmpControls(
            0.6,
            0.4,
            0.7,
            0.6,
            1,
            flags.Contains('b', StringComparison.Ordinal),
            0.3,
            Switch1: flags.Contains('v', StringComparison.Ordinal)
        );

        // The stage's source rests where the stage before it does: run that first to find it.
        var index = amp.Preamp.ToList().FindIndex(s => s.Name == parts[0]);
        var rest = 0.0;
        var sourceResistance = 34e3;
        if (index > 0) {
            var previous = amp.Preamp[index - 1].Create(Rate);
            previous.Update(controls);
            previous.Reset(0);
            rest = previous.RestOutput;
            sourceResistance = previous.OutputResistance;
        }

        var wdf = design.Create(Rate);
        wdf.SetSourceResistance(sourceResistance);
        wdf.Update(controls);
        wdf.Reset(rest);
        var (circuit, node) = AmpReferences.Network(
            design,
            controls,
            PreampSettings.Stock,
            sourceResistance,
            t => rest + Drive(amplitude, t)
        );
        var reference = new ReferenceSimulator(circuit);
        reference.OperatingPoint(40);
        output.WriteLine(
            $"{model} {stage}: rest output {wdf.RestOutput:0.000} V (reference {reference.Voltage(node):0.000} V), "
            + $"output resistance {wdf.OutputResistance / 1e3:0.0}k"
        );
        Assert.Equal(reference.Voltage(node), wdf.RestOutput, 0.002 * Math.Abs(reference.Voltage(node)) + 1e-6);

        var errorDb = CompareInTime($"{model} {stage}", x => wdf.Tick(rest + x), wdf.Newton, circuit, node, amplitude);
        Assert.True(errorDb < -35, $"{model} {stage}: {errorDb:0.0} dB from the reference");
    }
}
