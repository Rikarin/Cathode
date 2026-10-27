using Cathode.Dsp.Blocks;
using Cathode.Dsp.Capture;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>Captures run at the rate they were trained at, whatever rate the engine runs at.</summary>
public class CaptureResamplerTests {
    [Theory]
    [InlineData(44_100)]
    [InlineData(88_200)]
    [InlineData(96_000)]
    public void A_capture_trained_at_48k_runs_at_48k_and_comes_back_delayed_by_the_reported_latency(int engineRate) {
        var model = new FakeModel(48_000, identity: true);
        using var block = Amp(model, engineRate, 64);

        Assert.NotNull(block.Resampler);
        Assert.Equal(48_000, model.ResetRate);
        Assert.True(block.LatencySamples > 0);

        var input = Signals.Sine(1_000, engineRate, engineRate / 2, 0.5f);
        var random = new Random(4);
        var output = BlockRender.Stereo(block, input, input, _ => random.Next(1, 65)).Left;

        // An identity model through both conversions is the input, delayed by exactly the reported latency.
        var latency = block.LatencySamples;
        var start = latency + 2_000;
        var residual = output.AsSpan(start).ToArray().Select((y, i) => y - input[start - latency + i]).ToArray();
        Assert.True(
            Analysis.ToDb(Analysis.Rms(residual) / Analysis.Rms(output.AsSpan(start))) < -60,
            "the resampled path is the input, delayed"
        );
    }

    [Fact]
    public void The_tone_does_not_depend_on_the_engine_rate() {
        // A model whose filter is defined in samples, as a neural capture's are: a one-pole low-pass at 2 kHz at 48
        // kHz. Run at 96 kHz without resampling its corner would move up an octave.
        static double GainAt(int engineRate, double frequency) {
            using var block = Amp(new FakeModel(48_000, identity: false), engineRate, 64);
            var input = Signals.Sine(frequency, engineRate, engineRate, 0.25f);
            var output = BlockRender.Stereo(block, input, input, static _ => 64).Left;
            return Analysis.ToDb(
                Analysis.Rms(output.AsSpan(engineRate / 2)) / Analysis.Rms(input.AsSpan(engineRate / 2))
            );
        }

        foreach (var frequency in new[] { 500.0, 2_000, 6_000 }) {
            var reference = GainAt(48_000, frequency);
            Assert.Equal(reference, GainAt(96_000, frequency), 0.1);
            Assert.Equal(reference, GainAt(44_100, frequency), 0.1);
        }
    }

    [Fact]
    public void At_the_trained_rate_nothing_is_resampled() {
        var model = new FakeModel(48_000, identity: true);
        using var block = Amp(model, 48_000, 64);

        Assert.Null(block.Resampler);
        Assert.Equal(0, block.LatencySamples);
        Assert.Equal(48_000, model.ResetRate);
    }

    [Fact]
    public void A_model_without_a_rate_is_taken_as_48k() {
        var model = new FakeModel(-1, identity: true);
        using var block = Amp(model, 44_100, 64);

        Assert.NotNull(block.Resampler);
        Assert.Equal(48_000, model.ResetRate);
    }

    [Fact]
    public void Resampled_processing_does_not_allocate() {
        using var block = Amp(new FakeModel(48_000, identity: false), 44_100, 64);
        var left = Signals.Noise(64, 1, 0.3f);
        var right = new float[64];
        var context = new BlockContext(44_100, 120, 0);

        var bytes = AllocationProbe.Measure(() => {
                for (var i = 0; i < 200; i++) {
                    block.Process(new(left, right), context);
                }
            }
        );

        Assert.Equal(0, bytes);
    }

    static CaptureAmpBlock Amp(FakeModel model, int engineRate, int maxBlock) {
        var block = new CaptureAmpBlock(_ => model);
        block.LoadAsset(CaptureAmpBlock.ModelSlot, "fake");
        block.Params.SetTarget(CaptureAmpBlock.Normalize, 0);
        block.Prepare(new(engineRate, maxBlock));
        return block;
    }

    sealed class FakeModel(double trainedRate, bool identity) : ICaptureModel {
        float state;
        int maxFrames;

        public double ResetRate { get; private set; }

        public string Format => "fake";

        public string Architecture => identity ? "identity" : "one-pole";

        public double ExpectedSampleRate => trainedRate;

        public double? Loudness => null;

        public void Reset(double sampleRate, int maxFrames) {
            ResetRate = sampleRate;
            this.maxFrames = maxFrames;
            state = 0;
        }

        public void Process(ReadOnlySpan<float> input, Span<float> output) {
            if (input.Length > maxFrames) {
                throw new InvalidOperationException($"{input.Length} frames for a model reset for {maxFrames}");
            }

            const float coefficient = 0.2335f; // 1 - exp(-2 pi 2000 / 48000)
            for (var i = 0; i < input.Length; i++) {
                if (identity) {
                    output[i] = input[i];
                } else {
                    state += coefficient * (input[i] - state);
                    output[i] = state;
                }
            }
        }

        public void Dispose() { }
    }
}
