using System.Numerics;
using System.Text.Json;

namespace Cathode.Dsp.Capture;

/// <summary>
///     An AIDA-X model (the RTNeural Keras JSON format): one or more LSTM or GRU layers and a dense head with one
///     output, evaluated in managed code.
/// </summary>
/// <remarks>
///     The layout follows Keras, as RTNeural and NeuralAudio read it: a recurrent layer's <c>weights</c> are the kernel
///     <c>[in][G·H]</c>, the recurrent kernel <c>[H][G·H]</c> and the bias, with gates in the order i, f, c, o (LSTM)
///     or z, r, h (GRU, <c>reset_after</c>, so its bias is <c>[2][3·H]</c>). <c>in_skip</c> adds the input to the
///     output,
///     <c>in_gain</c> is a gain in dB the model applies to its input (as the AIDA-X plugin does, before the layers and
///     the skip) and <c>out_gain</c> its output gain in dB (loudness = -18 - out_gain).
///     Weights live in native memory, so loading even a large model leaves nothing on the large object heap.
/// </remarks>
public sealed class AidaModel : ICaptureModel {
    const int PrewarmSamples = 4_096;

    readonly RecurrentLayer[] layers;
    readonly NativeBuffer head;
    readonly NativeBuffer input = new(1);
    readonly float headBias;
    readonly bool skip;
    readonly float inputGain;

    public string Format => "AIDA-X";

    public string Architecture { get; }

    public double ExpectedSampleRate { get; }

    public double? Loudness { get; }

    /// <summary>The gain the model applies to its input (<c>in_gain</c>), in dB, when the file records it.</summary>
    public double? InputGainDb { get; }

    /// <summary>AIDA-X files record no analog input level.</summary>
    public double? InputLevelDbu => null;

    AidaModel(
        RecurrentLayer[] layers,
        NativeBuffer head,
        float headBias,
        bool skip,
        double sampleRate,
        double? loudness,
        double? inputGainDb
    ) {
        this.layers = layers;
        this.head = head;
        this.headBias = headBias;
        this.skip = skip;
        ExpectedSampleRate = sampleRate;
        Loudness = loudness;
        InputGainDb = inputGainDb;
        inputGain = inputGainDb is { } db ? (float)Decibels.ToGain(db) : 1f;
        var type = layers[0] is LstmLayer ? "LSTM" : "GRU";
        Architecture = layers.Length == 1
            ? $"{type}-{layers[0].HiddenSize}"
            : $"{type}-{layers[0].HiddenSize}x{layers.Length}";
    }

    /// <summary>True for file names AIDA-X uses for its models (<c>.aidax</c>, <c>.json</c>).</summary>
    public static bool IsAidaFile(string path) => Path.GetExtension(path).ToLowerInvariant() is ".aidax" or ".json";

    public static AidaModel Load(string path) {
        try {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            return Parse(document.RootElement);
        } catch (Exception e) when (e is JsonException
                                        or KeyNotFoundException
                                        or InvalidOperationException
                                        or FormatException) {
            throw new InvalidDataException($"Could not load AIDA-X model '{path}': {e.Message}", e);
        }
    }

    public static AidaModel Parse(JsonElement root) {
        var inputSize = root.GetProperty("in_shape").EnumerateArray().Last().GetInt32();
        if (inputSize != 1) {
            throw new InvalidDataException(
                $"Only single-input models are supported; this one has {inputSize} inputs (a conditioned model)."
            );
        }

        var json = root.GetProperty("layers").EnumerateArray().ToArray();
        if (json.Length < 2 || json[^1].GetProperty("type").GetString() != "dense") {
            throw new InvalidDataException("Expected recurrent layers followed by a dense output layer.");
        }

        var layers = new RecurrentLayer[json.Length - 1];
        try {
            var size = 1;
            for (var i = 0; i < layers.Length; i++) {
                var hidden = json[i].GetProperty("shape").EnumerateArray().Last().GetInt32();
                var weights = json[i].GetProperty("weights");
                layers[i] = json[i].GetProperty("type").GetString() switch {
                    "lstm" => new LstmLayer(size, hidden, weights),
                    "gru" => new GruLayer(size, hidden, weights),
                    var other => throw new InvalidDataException($"Unsupported layer type '{other}'.")
                };
                size = hidden;
            }

            var dense = json[^1].GetProperty("weights");
            var head = Matrix(dense[0], size, 1);
            var bias = dense.GetArrayLength() > 1 ? dense[1][0].GetSingle() : 0f;
            return new(
                layers,
                head,
                bias,
                root.TryGetProperty("in_skip", out var skip)
                && skip.ValueKind == JsonValueKind.Number
                && skip.GetDouble() != 0,
                Number(root, "samplerate") ?? 48_000,
                Number(root, "out_gain") is { } outGain ? -18 - outGain : null,
                Number(root, "in_gain")
            );
        } catch {
            foreach (var layer in layers) {
                layer?.Dispose();
            }

            throw;
        }
    }

    public void Reset(double sampleRate, int maxFrames) {
        foreach (var layer in layers) {
            layer.Clear();
        }

        // Settle the recurrent state on silence, so the first played note does not start with a thump.
        var silence = new float[256];
        for (var i = 0; i < PrewarmSamples; i += silence.Length) {
            Process(silence, silence);
            silence.AsSpan().Clear();
        }
    }

    public void Process(ReadOnlySpan<float> input, Span<float> output) {
        var x = this.input.Span;
        var weights = head.Span;
        for (var n = 0; n < input.Length; n++) {
            var sample = input[n] * inputGain;
            x[0] = sample;
            ReadOnlySpan<float> state = x;
            foreach (var layer in layers) {
                state = layer.Step(state);
            }

            var y = headBias + Dot(weights, state);
            output[n] = skip ? y + sample : y;
        }
    }

    public void Dispose() {
        foreach (var layer in layers) {
            layer.Dispose();
        }

        head.Dispose();
        input.Dispose();
    }

    static double? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    /// <summary>Reads a <paramref name="rows" /> x <paramref name="columns" /> JSON array row-major.</summary>
    static NativeBuffer Matrix(JsonElement json, int rows, int columns) {
        if (json.GetArrayLength() != rows) {
            throw new InvalidDataException($"Expected {rows} weight rows, found {json.GetArrayLength()}.");
        }

        var buffer = new NativeBuffer(rows * columns);
        var r = 0;
        foreach (var row in json.EnumerateArray()) {
            if (row.GetArrayLength() != columns) {
                buffer.Dispose();
                throw new InvalidDataException($"Expected {columns} weights per row, found {row.GetArrayLength()}.");
            }

            var c = 0;
            foreach (var value in row.EnumerateArray()) {
                buffer[r * columns + c++] = value.GetSingle();
            }

            r++;
        }

        return buffer;
    }

    static NativeBuffer Row(JsonElement json, int length) {
        if (json.GetArrayLength() != length) {
            throw new InvalidDataException($"Expected {length} biases, found {json.GetArrayLength()}.");
        }

        var buffer = new NativeBuffer(length);
        var i = 0;
        foreach (var value in json.EnumerateArray()) {
            buffer[i++] = value.GetSingle();
        }

        return buffer;
    }

    static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b) {
        var sum = 0f;
        var i = 0;
        if (Vector.IsHardwareAccelerated) {
            var acc = Vector<float>.Zero;
            for (; i <= a.Length - Vector<float>.Count; i += Vector<float>.Count) {
                acc += new Vector<float>(a[i..]) * new Vector<float>(b[i..]);
            }

            sum = Vector.Sum(acc);
        }

        for (; i < a.Length; i++) {
            sum += a[i] * b[i];
        }

        return sum;
    }

    /// <summary><paramref name="y" /> += <paramref name="a" /> · <paramref name="x" />.</summary>
    static void Accumulate(Span<float> y, float a, ReadOnlySpan<float> x) {
        var i = 0;
        if (Vector.IsHardwareAccelerated) {
            var scale = new Vector<float>(a);
            for (; i <= y.Length - Vector<float>.Count; i += Vector<float>.Count) {
                (new Vector<float>(y[i..]) + scale * new Vector<float>(x[i..])).CopyTo(y[i..]);
            }
        }

        for (; i < y.Length; i++) {
            y[i] += a * x[i];
        }
    }

    static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));

    abstract class RecurrentLayer(int inputSize, int hiddenSize, int gates, JsonElement weights) : IDisposable {
        protected readonly NativeBuffer Kernel = Matrix(weights[0], inputSize, gates * hiddenSize);
        protected readonly NativeBuffer Recurrent = Matrix(weights[1], hiddenSize, gates * hiddenSize);
        protected readonly NativeBuffer State = new(hiddenSize);

        public int HiddenSize { get; } = hiddenSize;

        /// <summary>Advances one sample and returns the new hidden state (valid until the next step).</summary>
        public abstract ReadOnlySpan<float> Step(ReadOnlySpan<float> x);

        public virtual void Clear() => State.Clear();

        public virtual void Dispose() {
            Kernel.Dispose();
            Recurrent.Dispose();
            State.Dispose();
        }

        /// <summary><paramref name="gates" /> += kernel · <paramref name="x" />, one weight row per input.</summary>
        protected static void Multiply(Span<float> gates, NativeBuffer kernel, ReadOnlySpan<float> x) {
            var width = gates.Length;
            var rows = kernel.Span;
            for (var j = 0; j < x.Length; j++) {
                Accumulate(gates, x[j], rows.Slice(j * width, width));
            }
        }
    }

    sealed class LstmLayer : RecurrentLayer {
        readonly NativeBuffer bias;
        readonly NativeBuffer cell;
        readonly NativeBuffer gates;

        public LstmLayer(int inputSize, int hiddenSize, JsonElement weights)
            : base(inputSize, hiddenSize, 4, weights) {
            bias = Row(weights[2], 4 * hiddenSize);
            cell = new(hiddenSize);
            gates = new(4 * hiddenSize);
        }

        public override ReadOnlySpan<float> Step(ReadOnlySpan<float> x) {
            var h = State.Span;
            var c = cell.Span;
            var g = gates.Span;
            bias.Span.CopyTo(g);
            Multiply(g, Kernel, x);
            Multiply(g, Recurrent, h);

            var size = HiddenSize;
            for (var i = 0; i < size; i++) {
                var input = Sigmoid(g[i]);
                var forget = Sigmoid(g[size + i]);
                var candidate = MathF.Tanh(g[2 * size + i]);
                var output = Sigmoid(g[3 * size + i]);
                c[i] = forget * c[i] + input * candidate;
                h[i] = output * MathF.Tanh(c[i]);
            }

            return h;
        }

        public override void Clear() {
            base.Clear();
            cell.Clear();
        }

        public override void Dispose() {
            base.Dispose();
            bias.Dispose();
            cell.Dispose();
            gates.Dispose();
        }
    }

    sealed class GruLayer : RecurrentLayer {
        readonly NativeBuffer inputBias;
        readonly NativeBuffer recurrentBias;
        readonly NativeBuffer fromInput;
        readonly NativeBuffer fromState;

        public GruLayer(int inputSize, int hiddenSize, JsonElement weights)
            : base(inputSize, hiddenSize, 3, weights) {
            var biases = Matrix(weights[2], 2, 3 * hiddenSize);
            inputBias = new(3 * hiddenSize);
            recurrentBias = new(3 * hiddenSize);
            biases.Span[..(3 * hiddenSize)].CopyTo(inputBias.Span);
            biases.Span[(3 * hiddenSize)..].CopyTo(recurrentBias.Span);
            biases.Dispose();
            fromInput = new(3 * hiddenSize);
            fromState = new(3 * hiddenSize);
        }

        public override ReadOnlySpan<float> Step(ReadOnlySpan<float> x) {
            var h = State.Span;
            var gx = fromInput.Span;
            var gh = fromState.Span;
            inputBias.Span.CopyTo(gx);
            recurrentBias.Span.CopyTo(gh);
            Multiply(gx, Kernel, x);
            Multiply(gh, Recurrent, h);

            var size = HiddenSize;
            for (var i = 0; i < size; i++) {
                var update = Sigmoid(gx[i] + gh[i]);
                var reset = Sigmoid(gx[size + i] + gh[size + i]);
                var candidate = MathF.Tanh(gx[2 * size + i] + reset * gh[2 * size + i]);
                h[i] = update * h[i] + (1f - update) * candidate;
            }

            return h;
        }

        public override void Dispose() {
            base.Dispose();
            inputBias.Dispose();
            recurrentBias.Dispose();
            fromInput.Dispose();
            fromState.Dispose();
        }
    }
}
