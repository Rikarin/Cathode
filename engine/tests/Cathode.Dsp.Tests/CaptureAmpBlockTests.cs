using Cathode.Dsp.Blocks;
using Cathode.Dsp.Capture;
using Cathode.Testing;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Cathode.Dsp.Tests;

public class CaptureAmpBlockTests : IDisposable {
    readonly string folder = Directory.CreateTempSubdirectory("cathode-aida-").FullName;

    public void Dispose() {
        Directory.Delete(folder, true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("lstm", 1, false)]
    [InlineData("lstm", 2, true)]
    [InlineData("gru", 1, true)]
    [InlineData("gru", 2, false)]
    public void Aida_model_matches_a_reference_evaluation_of_the_keras_layout(string type, int depth, bool skip) {
        var spec = SyntheticModel.Create(type, 8, depth, skip, 7);
        using var model = AidaModel.Parse(JsonDocument.Parse(spec.Json).RootElement);
        var input = Signals.Noise(2_000, 3, 0.5f);

        var actual = new float[input.Length];
        model.Process(input, actual);
        var expected = spec.Reference(input);

        for (var i = 0; i < input.Length; i++) {
            Assert.True(Math.Abs(actual[i] - expected[i]) < 1e-4, $"sample {i}: {actual[i]} vs {expected[i]}");
        }

        Assert.Equal($"{type.ToUpperInvariant()}-8{(depth > 1 ? "x2" : string.Empty)}", model.Architecture);
    }

    [Fact]
    public void Aida_metadata_sets_loudness_and_input_gain() {
        var json = SyntheticModel.Create("lstm", 4, 1, false, 1).Json.TrimEnd('}')
            + ""","in_gain":-6.5,"out_gain":4,"samplerate":44100}""";
        using var model = AidaModel.Parse(JsonDocument.Parse(json).RootElement);

        Assert.Equal(-22, model.Loudness);
        Assert.Equal(-6.5, model.InputGainDb);
        Assert.Null(model.InputLevelDbu);
        Assert.Equal(44_100, model.ExpectedSampleRate);
        Assert.Equal("AIDA-X", model.Format);
    }

    [Theory]
    [InlineData("""{"in_shape":[null,null,2],"layers":[]}""", "conditioned")]
    [InlineData(
        """{"in_shape":[null,null,1],"layers":[{"type":"dense","shape":[null,null,1],"weights":[[[1]],[0]]}]}""",
        "dense output"
    )]
    [InlineData(
        """{"in_shape":[null,null,1],"layers":[{"type":"conv1d","shape":[null,null,4],"weights":[]},"""
        + """{"type":"dense","shape":[null,null,1],"weights":[[[1]],[0]]}]}""",
        "conv1d"
    )]
    [InlineData(
        """{"in_shape":[null,null,1],"layers":[{"type":"lstm","shape":[null,null,2],"weights":"""
        + """[[[1,2,3]],[[0],[0]],[0]]},"""
        + """{"type":"dense","shape":[null,null,1],"weights":[[[1],[1]],[0]]}]}""",
        "weights per row"
    )]
    public void Malformed_models_are_rejected_with_a_reason(string json, string reason) {
        var path = Path.Combine(folder, "bad.aidax");
        File.WriteAllText(path, json);

        var error = Assert.Throws<InvalidDataException>(() => AidaModel.Load(path));
        Assert.Contains(reason, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Processing_does_not_allocate() {
        using var model = AidaModel.Parse(
            JsonDocument.Parse(SyntheticModel.Create("lstm", 16, 1, true, 2).Json).RootElement
        );
        var buffer = Signals.Noise(64, 1);

        var bytes = AllocationProbe.Measure(() => model.Process(buffer, buffer), () => model.Process(buffer, buffer));

        Assert.Equal(0, bytes);
    }

    [Fact]
    public void Block_loads_an_aida_model_normalises_it_and_copies_mono_to_both_channels() {
        var spec = SyntheticModel.Create("gru", 8, 1, false, 5);
        var path = Path.Combine(folder, "amp.aidax");
        File.WriteAllText(path, spec.Json.TrimEnd('}') + ""","out_gain":6}""");
        using var block = new CaptureAmpBlock();
        block.LoadAsset(CaptureAmpBlock.ModelSlot, path);
        block.Prepare(new(48_000, 64));
        var input = Signals.Noise(4_800, 9, 0.3f);

        var (left, right) = BlockRender.Stereo(block, input, input, static _ => 64);

        // Loudness = -18 - 6 = -24 dB, so normalisation adds +6 dB on top of the model's own output.
        using var reference = AidaModel.Load(path);
        reference.Reset(48_000, 64);
        var raw = new float[input.Length];
        reference.Process(input, raw);
        Assert.Equal(left, right);
        Assert.InRange(Analysis.ToDb(Analysis.Rms(left) / Analysis.Rms(raw)), 5.9, 6.1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Aida_in_gain_scales_the_input_before_the_layers_and_the_skip(bool skip) {
        var spec = SyntheticModel.Create("lstm", 8, 1, skip, 11);
        using var plain = AidaModel.Parse(JsonDocument.Parse(spec.Json).RootElement);
        using var gained = AidaModel.Parse(
            JsonDocument.Parse(spec.Json.TrimEnd('}') + ""","in_gain":-6}""").RootElement
        );
        var input = Signals.Noise(2_000, 4, 0.5f);
        var scaled = input.Select(static x => x * (float)Decibels.ToGain(-6.0)).ToArray();

        var expected = new float[input.Length];
        plain.Process(scaled, expected);
        var actual = new float[input.Length];
        gained.Process(input, actual);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0f, -12.7918)] // 1 V at full scale (-0.79 dBu) into a capture made at +12 dBu
    [InlineData(6f, -6.7918)]
    public void A_capture_that_records_its_input_level_is_driven_at_input_1s_calibration(
        float calibrationDb,
        double db
    ) {
        using var block = new CaptureAmpBlock(static _ => new IdentityModel(12));
        block.LoadAsset(CaptureAmpBlock.ModelSlot, "capture.nam");
        block.Prepare(new(48_000, 64));
        var input = Signals.Noise(4_800, 6, 0.3f);

        var output = BlockRender.Stereo(block, input, input, static _ => 64, inputCalibrationDb: calibrationDb).Left;

        Assert.Equal(db, block.CalibrationGainDb(calibrationDb), 3);
        // After the first callback's ramp from the reset's gain, the output is the input at that gain.
        var steady = Analysis.ToDb(
            Analysis.Rms(output.AsSpan(256).ToArray()) / Analysis.Rms(input.AsSpan(256).ToArray())
        );
        Assert.InRange(steady, db - 0.05, db + 0.05);
    }

    [Fact]
    public void Calibrated_input_off_or_no_recorded_level_leaves_the_input_alone() {
        using var off = new CaptureAmpBlock(static _ => new IdentityModel(12));
        off.LoadAsset(CaptureAmpBlock.ModelSlot, "capture.nam");
        off.Params.SetTarget(CaptureAmpBlock.Calibrate, 0f);
        off.Prepare(new(48_000, 64));
        using var unrecorded = new CaptureAmpBlock(static _ => new IdentityModel(null));
        unrecorded.LoadAsset(CaptureAmpBlock.ModelSlot, "capture.nam");
        unrecorded.Prepare(new(48_000, 64));

        Assert.Equal(0, off.CalibrationGainDb(6));
        Assert.Equal(0, unrecorded.CalibrationGainDb(6));
    }

    [Theory]
    [InlineData(CaptureAmpBlock.Bass, 80, 1_000)]
    [InlineData(CaptureAmpBlock.Middle, 650, 5_000)]
    [InlineData(CaptureAmpBlock.Treble, 8_000, 200)]
    [InlineData(CaptureAmpBlock.Presence, 5_000, 300)]
    public void Each_tone_control_lifts_its_band_and_leaves_others_alone(int control, double inBand, double outOfBand) {
        using var block = new CaptureAmpBlock();
        block.Params.SetTarget(control, 12f);
        block.Prepare(new(48_000, 64));

        var response = BlockRender.Mono(block, Signals.Impulse(16_384), 64);

        Assert.True(Analysis.ToDb(Analysis.MagnitudeAt(response, inBand, 48_000)) > 8, "boosted band");
        Assert.InRange(Analysis.ToDb(Analysis.MagnitudeAt(response, outOfBand, 48_000)), -1.5, 3);
    }

    [Fact]
    public void Flat_tone_without_a_model_is_bit_transparent() {
        using var block = new CaptureAmpBlock();
        block.Prepare(new(48_000, 64));
        var input = Signals.Noise(4_800, 2);

        Assert.Equal(input, BlockRender.Mono(block, input, 64));
    }

    /// <summary>
    ///     A random AIDA-X model in Keras layout, plus a deliberately naive double-precision evaluation of it.
    /// </summary>
    internal sealed record SyntheticModel(string Json, Func<float[], float[]> Reference) {
        public static SyntheticModel Create(string type, int hidden, int depth, bool skip, int seed) {
            var random = new Random(seed);
            var gates = type == "lstm" ? 4 : 3;
            var layers = new List<(double[,] Kernel, double[,] Recurrent, double[,] Bias)>();
            var json = new StringBuilder("""{"in_shape":[null,null,1],""");
            json.Append(CultureInfo.InvariantCulture, $"\"in_skip\":{(skip ? 1 : 0)},\"layers\":[");
            var size = 1;
            for (var l = 0; l < depth; l++) {
                var kernel = Random(random, size, gates * hidden, 0.6);
                var recurrent = Random(random, hidden, gates * hidden, 0.4);
                var bias = Random(random, type == "lstm" ? 1 : 2, gates * hidden, 0.2);
                layers.Add((kernel, recurrent, bias));
                json.Append(CultureInfo.InvariantCulture, $"{{\"type\":\"{type}\",\"activation\":\"\",");
                json.Append(CultureInfo.InvariantCulture, $"\"shape\":[null,null,{hidden}],\"weights\":[");
                json.Append(Array2D(kernel)).Append(',').Append(Array2D(recurrent)).Append(',');
                json.Append(type == "lstm" ? Array1D(bias, 0) : Array2D(bias)).Append("]},");
                size = hidden;
            }

            var head = Random(random, hidden, 1, 0.5);
            var headBias = random.NextDouble() * 0.1;
            json.Append("""{"type":"dense","activation":"","shape":[null,null,1],"weights":[""");
            json.Append(Array2D(head)).Append(CultureInfo.InvariantCulture, $",[{headBias:R}]]}}]}}");

            return new(json.ToString(), input => Evaluate(input, type, hidden, layers, head, headBias, skip));
        }

        static float[] Evaluate(
            float[] input,
            string type,
            int hidden,
            List<(double[,] Kernel, double[,] Recurrent, double[,] Bias)> layers,
            double[,] head,
            double headBias,
            bool skip
        ) {
            var h = layers.Select(_ => new double[hidden]).ToArray();
            var c = layers.Select(_ => new double[hidden]).ToArray();
            var output = new float[input.Length];
            for (var n = 0; n < input.Length; n++) {
                double[] x = [input[n]];
                for (var l = 0; l < layers.Count; l++) {
                    var (w, u, b) = layers[l];

                    double In(int g, int i) {
                        var sum = 0.0;
                        for (var j = 0; j < x.Length; j++) {
                            sum += w[j, g * hidden + i] * x[j];
                        }

                        return sum;
                    }

                    double Rec(int g, int i) {
                        var sum = 0.0;
                        for (var j = 0; j < hidden; j++) {
                            sum += u[j, g * hidden + i] * h[l][j];
                        }

                        return sum;
                    }

                    var next = new double[hidden];
                    for (var i = 0; i < hidden; i++) {
                        if (type == "lstm") {
                            double Gate(int g) => In(g, i) + Rec(g, i) + b[0, g * hidden + i];
                            var ig = Sigmoid(Gate(0));
                            var fg = Sigmoid(Gate(1));
                            var cg = Math.Tanh(Gate(2));
                            var og = Sigmoid(Gate(3));
                            c[l][i] = fg * c[l][i] + ig * cg;
                            next[i] = og * Math.Tanh(c[l][i]);
                        } else {
                            var z = Sigmoid(In(0, i) + b[0, i] + Rec(0, i) + b[1, i]);
                            var r = Sigmoid(In(1, i) + b[0, hidden + i] + Rec(1, i) + b[1, hidden + i]);
                            var candidate = Math.Tanh(
                                In(2, i) + b[0, 2 * hidden + i] + r * (Rec(2, i) + b[1, 2 * hidden + i])
                            );
                            next[i] = z * h[l][i] + (1 - z) * candidate;
                        }
                    }

                    h[l] = next;
                    x = next;
                }

                var y = headBias;
                for (var i = 0; i < hidden; i++) {
                    y += head[i, 0] * x[i];
                }

                output[n] = (float)(skip ? y + input[n] : y);
            }

            return output;
        }

        static double Sigmoid(double x) => 1 / (1 + Math.Exp(-x));

        static double[,] Random(Random random, int rows, int columns, double scale) {
            var m = new double[rows, columns];
            for (var r = 0; r < rows; r++) {
                for (var c = 0; c < columns; c++) {
                    // Rounded to float, so the model and the reference see identical weights.
                    m[r, c] = (float)((random.NextDouble() * 2 - 1) * scale);
                }
            }

            return m;
        }

        static string Array2D(double[,] m) =>
            "["
            + string.Join(
                ",",
                Enumerable.Range(0, m.GetLength(0)).Select(r => Array1D(m, r))
            )
            + "]";

        static string Array1D(double[,] m, int row) =>
            "["
            + string.Join(
                ",",
                Enumerable.Range(0, m.GetLength(1))
                    .Select(c => ((float)m[row, c]).ToString("R", CultureInfo.InvariantCulture))
            )
            + "]";
    }

    /// <summary>A capture that passes its input through, recording <paramref name="inputLevelDbu" />.</summary>
    sealed class IdentityModel(double? inputLevelDbu) : ICaptureModel {
        public string Format => "fake";

        public string Architecture => "identity";

        public double ExpectedSampleRate => 48_000;

        public double? Loudness => null;

        public double? InputLevelDbu => inputLevelDbu;

        public void Reset(double sampleRate, int maxFrames) { }

        public void Process(ReadOnlySpan<float> input, Span<float> output) => input.CopyTo(output);

        public void Dispose() { }
    }
}
