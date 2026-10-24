using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cathode.Dsp.Capture;
using Cathode.Testing;
using Cathode.Testing.References;

namespace Cathode.References.Tests;

public class GuitarMlLstmModelTests {
    [Fact]
    public void Matches_the_AIDA_runner_on_the_same_weights_in_Keras_layout() {
        // Two independent runners: PyTorch layout (W_ih [4H][in], two biases) here, Keras layout (kernel [in][4H], one
        // bias) in AidaModel. Same gate order (i, f, g, o), so transposing the weights must give the same output.
        const int Hidden = 8;
        var random = new Random(3);

        float[][] Matrix(int rows, int columns) => [
            ..Enumerable.Range(0, rows)
                .Select(_ => Enumerable.Range(0, columns).Select(_ => (float)(random.NextDouble() - 0.5)).ToArray())
        ];

        var weightIh = Matrix(4 * Hidden, 1);
        var weightHh = Matrix(4 * Hidden, Hidden);
        var biasIh = Matrix(1, 4 * Hidden)[0];
        var biasHh = Matrix(1, 4 * Hidden)[0];
        var linear = Matrix(1, Hidden);
        var linearBias = 0.01f;

        var proteus = new JsonObject {
            ["model_data"] = new JsonObject {
                ["model"] = "SimpleRNN",
                ["input_size"] = 1,
                ["skip"] = 1,
                ["output_size"] = 1,
                ["unit_type"] = "LSTM",
                ["num_layers"] = 1,
                ["hidden_size"] = Hidden,
                ["bias_fl"] = true
            },
            ["state_dict"] = new JsonObject {
                ["rec.weight_ih_l0"] = Json(weightIh),
                ["rec.weight_hh_l0"] = Json(weightHh),
                ["rec.bias_ih_l0"] = Json(biasIh),
                ["rec.bias_hh_l0"] = Json(biasHh),
                ["lin.weight"] = Json(linear),
                ["lin.bias"] = Json([linearBias])
            }
        };
        var keras = new JsonObject {
            ["in_shape"] = new JsonArray(null, null, 1),
            ["in_skip"] = 1,
            ["layers"] = new JsonArray(
                new JsonObject {
                    ["type"] = "lstm",
                    ["shape"] = new JsonArray(null, null, Hidden),
                    ["weights"] = new JsonArray(
                        Json(Transpose(weightIh)),
                        Json(Transpose(weightHh)),
                        Json(biasIh.Zip(biasHh, static (a, b) => a + b).ToArray())
                    )
                },
                new JsonObject {
                    ["type"] = "dense",
                    ["shape"] = new JsonArray(null, null, 1),
                    ["weights"] = new JsonArray(Json(Transpose(linear)), Json([linearBias]))
                }
            )
        };

        var input = Signals.Concat(Signals.Sine(220, 44_100, 2_000, 0.5f), Signals.Noise(2_000, 9, 0.8f));
        using var model = GuitarMlLstmModel.Parse(JsonDocument.Parse(proteus.ToJsonString()).RootElement);
        using var aida = AidaModel.Parse(JsonDocument.Parse(keras.ToJsonString()).RootElement);
        var expected = new float[input.Length];
        aida.Process(input, expected); // no Reset: it would pre-warm, and the PyTorch runner starts from zero state

        var actual = model.Render(input);

        Assert.Equal("LSTM-8", model.Architecture);
        for (var i = 0; i < input.Length; i++) {
            Assert.True(Math.Abs(actual[i] - expected[i]) < 1e-5, $"sample {i}: {actual[i]} vs {expected[i]}");
        }
    }

    [Theory]
    [InlineData("tl-princeton-clean")]
    [InlineData("tl-mesa-iicplus-drive-knob")]
    public void Matches_an_independent_numpy_render_of_the_real_model(string id) {
        // The golden files are numpy renders (tests/assets/references/tools/guitarml_golden.py) of the cached model.
        var entry = ReferenceManifest.Load(ReferenceManifest.Locate()).Find(id);
        var path = ReferenceCache.Default.TryLocate(entry);
        Assert.SkipWhen(path is null, $"{id} is not in the reference cache (run cathode-references fetch)");
        var golden = File
            .ReadAllLines(Path.Combine(Path.GetDirectoryName(ReferenceManifest.Locate())!, $"{id}.golden.txt"))
            .Select(static line => float.Parse(line, CultureInfo.InvariantCulture))
            .ToArray();
        var input = new float[4_000];
        for (var i = 0; i < input.Length; i++) {
            input[i] = (float)(0.5 * Math.Min(1.0, i / 1000.0) * Math.Sin(2 * Math.PI * 220 * i / 44_100.0));
        }

        using var model = (GuitarMlLstmModel)ReferenceModels.Open(entry, path!);
        var output = model.Render(input);

        var worst = golden.Select((g, i) => Math.Abs(g - output[i * 10])).Max();
        Assert.True(worst < 1e-4, $"{id}: worst difference {worst} from the numpy render");
    }

    [Fact]
    public void A_conditioned_model_insists_on_its_knob_values() {
        var json = """
                   {"model_data": {"unit_type": "LSTM", "input_size": 2, "hidden_size": 1, "num_layers": 1, "skip": 0},
                    "state_dict": {"rec.weight_ih_l0": [[0, 0], [0, 0], [0, 0], [0, 0]], "rec.weight_hh_l0": [[0], [0], [0], [0]],
                                   "rec.bias_ih_l0": [0, 0, 0, 0], "rec.bias_hh_l0": [0, 0, 0, 0], "lin.weight": [[1]], "lin.bias": [0]}}
                   """;
        using var model = GuitarMlLstmModel.Parse(JsonDocument.Parse(json).RootElement);

        Assert.Throws<ArgumentException>(() => model.SetConditioning([]));
        model.SetConditioning([0.5]);
        Assert.Equal(2, model.InputSize);
    }

    static JsonArray Json(float[] values) => new([..values.Select(static v => JsonValue.Create(v))]);

    static JsonArray Json(float[][] rows) => new([..rows.Select(static r => (JsonNode)Json(r))]);

    static float[][] Transpose(float[][] matrix) =>
        [..Enumerable.Range(0, matrix[0].Length).Select(c => matrix.Select(r => r[c]).ToArray())];
}
