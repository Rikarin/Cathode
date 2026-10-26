using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Testing;
using System.Security.Cryptography;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The guard of the circuit solver's speed work: every Circuit Amp model at gains 2, 5 and 8, and at 8 with every
///     switch and Bright on, and the AFD100 and the Quick Rod at every combination of their switches, render
///     bit-identical to the hashes in <c>tests/assets/defaults/circuit-solver.sha256</c>, taken before it (on a6bc94f).
///     Each render plays noise, a sine, loud noise and silence through uneven host blocks. A change meant to alter a model
///     regenerates the file with <c>CATHODE_UPDATE_SOLVER=1 dotnet test</c>, and is reviewed. The hashes are macOS ARM64
///     renders, so the test runs there only (<see cref="BaselinePlatform" />).
/// </summary>
public class CircuitAmpBitIdentityTests {
    static readonly string Path =
        System.IO.Path.Combine(EngineRoot(), "tests", "assets", "defaults", "circuit-solver.sha256");

    static readonly float[] Input = Signals.Concat(
        Signals.Noise(2_000, 3, 0.3f),
        Signals.Sine(110, 48_000, 4_000, 0.5f),
        Signals.Noise(2_000, 5),
        new float[1_000]
    );

    [Fact]
    public void Every_model_renders_bit_identical_to_the_solver_baseline() {
        BaselinePlatform.Require();
        var renders = Render();
        if (Environment.GetEnvironmentVariable("CATHODE_UPDATE_SOLVER") == "1") {
            File.WriteAllLines(Path, renders);
            return;
        }

        var reference = File.ReadAllLines(Path);
        Assert.Equal(reference.Length, renders.Count);
        var mismatched = reference.Except(renders).ToArray();
        Assert.True(mismatched.Length == 0, $"Not bit-identical: {string.Join("; ", mismatched.Take(10))}");
    }

    /// <summary>One line per model and case: "type case SHA-256".</summary>
    static List<string> Render() {
        var lines = new List<string>();
        var names = AmpDesign.All.Select(static a => a.Name).ToList();
        int[] switched = [names.IndexOf("AFD100"), names.IndexOf("Quick Rod")];
        for (var type = 0; type < names.Count; type++) {
            List<(string Name, (int Param, float Value)[] Settings)> cases = [
                ("g2", [(CircuitAmpBlock.Gain, 2f)]),
                ("g5", [(CircuitAmpBlock.Gain, 5f)]),
                ("g8", [(CircuitAmpBlock.Gain, 8f)]),
                ("all", [
                    (CircuitAmpBlock.Gain, 8f), (CircuitAmpBlock.Switch1, 1f), (CircuitAmpBlock.Switch2, 1f),
                    (CircuitAmpBlock.Switch3, 1f), (CircuitAmpBlock.Bright, 1f)
                ])
            ];
            if (switched.Contains(type)) {
                for (var combination = 0; combination < 16; combination++) {
                    var c = combination;
                    cases.Add(
                        ($"s{c}", [
                            (CircuitAmpBlock.Gain, 7f), (CircuitAmpBlock.Switch1, c & 1),
                            (CircuitAmpBlock.Switch2, c >> 1 & 1),
                            (CircuitAmpBlock.Switch3, c >> 2 & 1), (CircuitAmpBlock.Bright, c >> 3 & 1)
                        ])
                    );
                }
            }

            foreach (var (name, settings) in cases) {
                using var block = new CircuitAmpBlock();
                block.Params.SetTarget(CircuitAmpBlock.Type, type);
                block.Params.SetTarget(CircuitAmpBlock.Master, 10); // the default when the baseline was made
                foreach (var (param, value) in settings) {
                    block.Params.SetTarget(param, value);
                }

                block.Prepare(new(48_000, 64));
                var (left, _) = BlockRender.Stereo(block, Input, Input, static i => 17 + i % 47);
                var bytes = new byte[left.Length * 4];
                Buffer.BlockCopy(left, 0, bytes, 0, bytes.Length);
                lines.Add($"{type} {name} {Convert.ToHexString(SHA256.HashData(bytes))}");
            }
        }

        return lines;
    }

    static string EngineRoot() {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "Cathode.slnx"))) {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Cathode.slnx not found above the test binaries.");
    }
}
