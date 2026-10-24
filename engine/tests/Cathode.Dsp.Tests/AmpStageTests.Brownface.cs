using Cathode.Dsp.Amp;
using Cathode.Dsp.Tests.Circuits;

namespace Cathode.Dsp.Tests;

/// <summary>
///     Stream C's new stage combinations against the reference MNA simulator: the brownface tone network and split
///     plate loads, the harmonic vibrato's crossover and mixer pair (at rest: the dry path), the 6G3's tone and mix, the
///     6G11's tapped treble network, the Bassman 135's stacks with Deep and its shared-cathode mix, the Scout's 6EU7
///     stages with the bridged T. Each preamp is also run as a whole chain, joined in one circuit with no Thévenin cut.
/// </summary>
public partial class AmpStageTests {
    public static TheoryData<string, string, double> StreamCNetworks =>
        new() {
            // Model, stage and its switches ("v": Switch1, "m": Switch2, "b": bright), amplitude in volts.
            { "Concert 6G12", "V1B", 0.3 },
            { "Concert 6G12", "V2B", 1 },
            { "Concert 6G12", "V1A v", 0.3 },
            { "Concert 6G12", "V2A v", 1 },
            { "Concert 6G12", "Vibrato v", 0.3 },
            { "Concert 6G12", "Vibrato v", 3 },
            { "Deluxe 6G3", "V1A", 0.3 },
            { "Deluxe 6G3", "V1B v", 0.3 },
            { "Deluxe 6G3", "V2A", 1 },
            { "Vibrolux 6G11", "V1A", 0.3 },
            { "Vibrolux 6G11", "V2B v", 1 },
            { "Bassman 135", "V1A", 0.3 },
            { "Bassman 135", "V1A m", 0.3 },
            { "Bassman 135", "V2A vb", 0.3 },
            { "Bassman 135", "V1B", 1 },
            { "Scout GA-17RVT", "V2A", 0.1 },
            { "Scout GA-17RVT", "V2B", 1 }
        };

    public static TheoryData<string, bool, double, double> StreamCChains =>
        new() {
            // Model, the second channel (Switch1), amplitude, limit in dB.
            { "Concert 6G12", false, 0.01, -35 },
            { "Concert 6G12", true, 0.01, -35 },
            { "Concert 6G12", true, 0.1, -30 },
            { "Deluxe 6G3", false, 0.01, -35 },
            { "Vibrolux 6G11", true, 0.01, -35 },
            { "Bassman 135", false, 0.01, -35 },
            { "Bassman 135", true, 0.01, -35 },
            { "Scout GA-17RVT", false, 0.01, -35 }
        };

    [Theory]
    [MemberData(nameof(StreamCNetworks))]
    public void Stream_c_stages_match_the_reference_in_the_time_domain(string model, string stage, double amplitude) {
        var parts = stage.Split(' ');
        var controls = StreamCControls(parts.Length > 1 ? parts[1] : string.Empty);
        var chain = PlayingChain(model, controls);
        var index = chain.FindIndex(s => s.Name == parts[0]);

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

        // The vibrato stage runs as built (its oscillator stopped: the pedal is off), the reference as its network.
        var wdf = chain[index].Create(Rate);
        wdf.SetSourceResistance(sourceResistance);
        wdf.Update(controls);
        wdf.Reset(rest);
        var design = AsNetwork(chain[index]);
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

        // The vibrato runs as two bands (see HarmonicVibratoStage), the reference as the whole circuit: driven hard, the
        // grid lines' grid current and the shared cathode are where they part.
        var limit = parts[0] == "Vibrato" && amplitude > 1 ? -28 : -35;
        var errorDb = CompareInTime($"{model} {stage}", x => wdf.Tick(rest + x), wdf.Newton, circuit, node, amplitude);
        Assert.True(errorDb < limit, $"{model} {stage}: {errorDb:0.0} dB from the reference");
    }

    [Theory]
    [MemberData(nameof(StreamCChains))]
    public void Stream_c_chains_match_the_reference(string model, bool second, double amplitude, double limitDb) {
        var controls = new AmpControls(0.5, 0.5, 0.5, 0.5, 1, false, Switch1: second);
        var designs = PlayingChain(model, controls);
        var stages = designs.Select(static d => d.Create(Rate)).ToArray();
        var (rest, resistance) = (0.0, 0.0);
        foreach (var stage in stages) {
            stage.Update(controls);
            stage.SetSourceResistance(resistance);
            stage.Reset(rest);
            (rest, resistance) = (stage.RestOutput, stage.OutputResistance);
        }

        var circuit = new ReferenceCircuit();
        var node = circuit.Node();
        circuit.Source(node, 0, t => Drive(amplitude, t));
        foreach (var d in designs) {
            node = AmpReferences.Append(circuit, AsNetwork(d), controls, PreampSettings.Stock, node);
        }

        var errorDb = CompareInTime(
            $"{model} {(second ? "second" : "first")} channel {string.Join(" > ", designs.Select(static d => d.Name))}",
            x => {
                foreach (var stage in stages) {
                    x = stage.Tick(x);
                }

                return x;
            },
            stages[^1].Newton,
            circuit,
            node,
            amplitude
        );
        Assert.True(errorDb < limitDb, $"{model}: {errorDb:0.0} dB from the reference");
    }

    static AmpControls StreamCControls(string flags) =>
        new(
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

    /// <summary>The playing chain, with the channel switch's chosen path in place of the switch.</summary>
    static List<AmpStageDesign> PlayingChain(string model, in AmpControls controls) {
        var on = controls.Switch1;
        return AmpDesign.All.Single(a => a.Name == model)
            .Preamp
            .SelectMany(s => s is ChannelSwitchStageDesign c ? on ? c.On : c.Off : [s])
            .ToList();
    }

    /// <summary>A stage as the netlist the reference simulator runs (the vibrato's network with its sources at rest).</summary>
    static NetworkStageDesign AsNetwork(AmpStageDesign stage) =>
        stage switch {
            NetworkStageDesign network => network,
            HarmonicVibratoStageDesign vibrato => vibrato.Network,
            TriodeStageDesign triode => triode.ToNetwork(),
            _ => throw new InvalidOperationException(stage.Name)
        };
}
