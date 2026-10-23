using System.Numerics;
using System.Text.Json;

namespace Cathode.Testing.References;

/// <summary>
///     A GuitarML Proteus/NeuralPi model (the PyTorch <c>SimpleRNN</c> of Automated-GuitarAmpModelling, saved as JSON):
///     stacked LSTM layers and a linear head, optionally with the input added to the output (<c>skip</c>) and extra
///     inputs that condition the model on knob positions (0–1). Offline, for matching; not real-time safe.
/// </summary>
/// <remarks>
///     PyTorch layout: <c>rec.weight_ih_l{k}</c> is <c>[4H][in]</c>, <c>rec.weight_hh_l{k}</c> <c>[4H][H]</c>, two bias
///     vectors <c>[4H]</c> that add, gates in the order i, f, g, o; <c>lin.weight</c> <c>[1][H]</c>. The output is
///     <c>lin(h) + x[0]</c> when <c>skip</c> is set. Computed in single precision, as the models run in the plugins.
/// </remarks>
public sealed class GuitarMlLstmModel : IReferenceModel {
    readonly Layer[] layers;
    readonly float[] head;
    readonly float headBias;
    readonly bool skip;
    readonly float[] conditioning;

    public int InputSize { get; }

    public int HiddenSize => layers[0].Hidden;

    public string Architecture => $"LSTM-{HiddenSize}" + (layers.Length > 1 ? $"x{layers.Length}" : string.Empty);

    public double SampleRate { get; }

    GuitarMlLstmModel(Layer[] layers, float[] head, float headBias, bool skip, int inputSize, double sampleRate) {
        this.layers = layers;
        this.head = head;
        this.headBias = headBias;
        this.skip = skip;
        InputSize = inputSize;
        SampleRate = sampleRate;
        conditioning = new float[Math.Max(0, inputSize - 1)];
    }

    public static GuitarMlLstmModel Load(string path, double sampleRate = 44_100) {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        return Parse(document.RootElement, sampleRate);
    }

    public static GuitarMlLstmModel Parse(JsonElement root, double sampleRate = 44_100) {
        var info = root.GetProperty("model_data");
        var unit = info.GetProperty("unit_type").GetString();
        if (!string.Equals(unit, "LSTM", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidDataException($"Only LSTM models are supported, not '{unit}'.");
        }

        var inputSize = info.GetProperty("input_size").GetInt32();
        var hidden = info.GetProperty("hidden_size").GetInt32();
        var count = info.TryGetProperty("num_layers", out var n) ? n.GetInt32() : 1;
        var skip = info.TryGetProperty("skip", out var s) && s.GetInt32() != 0;
        var weights = root.GetProperty("state_dict");

        var layers = new Layer[count];
        for (var k = 0; k < count; k++) {
            var input = k == 0 ? inputSize : hidden;
            var bias = Vector(weights.GetProperty($"rec.bias_ih_l{k}"), 4 * hidden);
            var recurrentBias = Vector(weights.GetProperty($"rec.bias_hh_l{k}"), 4 * hidden);
            for (var i = 0; i < bias.Length; i++) {
                bias[i] += recurrentBias[i];
            }

            layers[k] = new(
                hidden,
                Matrix(weights.GetProperty($"rec.weight_ih_l{k}"), 4 * hidden, input),
                Matrix(weights.GetProperty($"rec.weight_hh_l{k}"), 4 * hidden, hidden),
                bias
            );
        }

        var head = Matrix(weights.GetProperty("lin.weight"), 1, hidden);
        var headBias = weights.TryGetProperty("lin.bias", out var b) ? Vector(b, 1)[0] : 0f;
        return new(layers, head, headBias, skip, inputSize, sampleRate);
    }

    /// <summary>Sets the conditioning inputs (knob positions 0–1), one per extra input.</summary>
    public void SetConditioning(IReadOnlyList<double>? values) {
        if ((values?.Count ?? 0) != conditioning.Length) {
            throw new ArgumentException(
                $"This model takes {conditioning.Length} conditioning value(s), got {values?.Count ?? 0}.",
                nameof(values)
            );
        }

        for (var i = 0; i < conditioning.Length; i++) {
            conditioning[i] = (float)values![i];
        }
    }

    public void Reset() {
        foreach (var layer in layers) {
            layer.Clear();
        }
    }

    public float[] Render(ReadOnlySpan<float> input) {
        Reset();
        var output = new float[input.Length];
        var x = new float[InputSize];
        conditioning.CopyTo(x, 1);
        for (var n = 0; n < input.Length; n++) {
            x[0] = input[n];
            ReadOnlySpan<float> state = x;
            foreach (var layer in layers) {
                state = layer.Step(state);
            }

            var y = headBias + Dot(head, state);
            output[n] = skip ? y + input[n] : y;
        }

        return output;
    }

    public void Dispose() { }

    static float[] Vector(JsonElement json, int length) {
        if (json.GetArrayLength() != length) {
            throw new InvalidDataException($"Expected {length} values, found {json.GetArrayLength()}.");
        }

        var values = new float[length];
        var i = 0;
        foreach (var value in json.EnumerateArray()) {
            values[i++] = value.GetSingle();
        }

        return values;
    }

    /// <summary>A <paramref name="rows" /> x <paramref name="columns" /> JSON matrix, row-major.</summary>
    static float[] Matrix(JsonElement json, int rows, int columns) {
        if (json.GetArrayLength() != rows) {
            throw new InvalidDataException($"Expected {rows} rows, found {json.GetArrayLength()}.");
        }

        var values = new float[rows * columns];
        var r = 0;
        foreach (var row in json.EnumerateArray()) {
            Vector(row, columns).CopyTo(values, r++ * columns);
        }

        return values;
    }

    static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b) {
        var sum = 0f;
        var i = 0;
        if (System.Numerics.Vector.IsHardwareAccelerated) {
            var acc = Vector<float>.Zero;
            for (; i <= a.Length - Vector<float>.Count; i += Vector<float>.Count) {
                acc += new Vector<float>(a[i..]) * new Vector<float>(b[i..]);
            }

            sum = System.Numerics.Vector.Sum(acc);
        }

        for (; i < a.Length; i++) {
            sum += a[i] * b[i];
        }

        return sum;
    }

    static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));

    sealed class Layer(int hidden, float[] inputWeights, float[] recurrentWeights, float[] bias) {
        readonly float[] h = new float[hidden];
        readonly float[] c = new float[hidden];
        readonly float[] gates = new float[4 * hidden];

        public int Hidden { get; } = hidden;

        public void Clear() {
            Array.Clear(h);
            Array.Clear(c);
        }

        public ReadOnlySpan<float> Step(ReadOnlySpan<float> x) {
            var inputs = x.Length;
            for (var g = 0; g < gates.Length; g++) {
                gates[g] = bias[g]
                    + Dot(inputWeights.AsSpan(g * inputs, inputs), x)
                    + Dot(recurrentWeights.AsSpan(g * Hidden, Hidden), h);
            }

            for (var i = 0; i < Hidden; i++) {
                var input = Sigmoid(gates[i]);
                var forget = Sigmoid(gates[Hidden + i]);
                var candidate = MathF.Tanh(gates[2 * Hidden + i]);
                var output = Sigmoid(gates[3 * Hidden + i]);
                c[i] = forget * c[i] + input * candidate;
                h[i] = output * MathF.Tanh(c[i]);
            }

            return h;
        }
    }
}
