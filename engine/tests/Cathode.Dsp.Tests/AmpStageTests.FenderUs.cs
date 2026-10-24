using Cathode.Dsp.Amp;
using Cathode.Dsp.Tests.Circuits;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Fender and US vintage stream's new stage combinations against the reference MNA simulator: the Hot Rod
///     Deluxe's relay-switched input networks, its stack with the 220k, V2A's switched cathode bypasses and V2B with the
///     master and the zener clamp; the tweed Twin's follower with the mid cap on the wiper; the 5F2-A's tone network; the
///     Vibro-King's input follower, Fat stage and stack; the Blues Junior's stages; the non-reverb AB763 mixer.
/// </summary>
public partial class AmpStageTests {
    public static TheoryData<string, string, double> FenderUsNetworks => new() {
        // Model, stage and its switches ("v": Switch1, "m": Switch2, "b": bright), amplitude in volts.
        { "Hot Rod Deluxe", "V1A", 0.1 },
        { "Hot Rod Deluxe", "V1A v", 0.1 },
        { "Hot Rod Deluxe", "V1B", 0.2 },
        { "Hot Rod Deluxe", "V2A m", 0.5 },
        { "Hot Rod Deluxe", "V2A vb", 0.5 },
        { "Hot Rod Deluxe", "V2B m", 2 },
        { "Hot Rod Deluxe", "V2B", 8 },
        { "Hot Rod Deluxe", "Normal v", 5 },
        { "Champ 5F1", "V1A", 0.3 },
        { "Princeton 5F2-A", "V1A", 0.3 },
        { "Tweed Twin 5F8-A", "V2B", 5 },
        { "Bandmaster AB763", "Mixer v", 5 },
        { "Princeton AA964", "V1B", 1 },
        { "Vibro-King", "V1A", 1 },
        { "Vibro-King", "V4A v", 0.3 },
        { "Vibro-King", "V4B", 1 },
        { "Blues Junior", "V1B", 0.1 },
        { "Blues Junior", "V1A v", 0.3 },
        { "Blues Junior", "V2B", 1 }
    };

    [Theory]
    [MemberData(nameof(FenderUsNetworks))]
    public void Fender_and_us_stages_match_the_reference_in_the_time_domain(string model, string stage, double amplitude) {
        var parts = stage.Split(' ');
        var flags = parts.Length > 1 ? parts[1] : string.Empty;
        var controls = new AmpControls(
            0.6,
            0.4,
            0.7,
            0.6,
            0.8,
            flags.Contains('b', StringComparison.Ordinal),
            0.3,
            Switch1: flags.Contains('v', StringComparison.Ordinal),
            Switch2: flags.Contains('m', StringComparison.Ordinal)
        );

        // The playing chain, with the channel switch's chosen path in place of the switch.
        var chain = AmpDesign.All.Single(a => a.Name == model).Preamp
            .SelectMany(s => s is ChannelSwitchStageDesign c ? controls.IsOn(c.Switch) ? c.On : c.Off : [s])
            .ToList();
        var index = chain.FindIndex(s => s.Name == parts[0]);
        var design = (NetworkStageDesign)chain[index];

        // The stage's source rests where the chain before it does.
        var rest = 0.0;
        var sourceResistance = 1e3;
        for (var i = 0; i < index; i++) {
            var previous = chain[i].Create(Rate);
            previous.SetSourceResistance(sourceResistance);
            previous.Update(controls);
            previous.Reset(rest);
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
            $"{model} {stage}: rest output {wdf.RestOutput:0.000} V (reference {reference.Voltage(node):0.000} V), " +
            $"output resistance {wdf.OutputResistance / 1e3:0.0}k"
        );
        Assert.Equal(reference.Voltage(node), wdf.RestOutput, 0.002 * Math.Abs(reference.Voltage(node)) + 1e-6);

        var errorDb = CompareInTime($"{model} {stage}", x => wdf.Tick(rest + x), wdf.Newton, circuit, node, amplitude);
        Assert.True(errorDb < -35, $"{model} {stage}: {errorDb:0.0} dB from the reference");
    }
}
