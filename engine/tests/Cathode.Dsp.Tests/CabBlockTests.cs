using Cathode.Dsp.Blocks;
using Cathode.Dsp.Convolution;
using Cathode.Dsp.IO;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public sealed class CabBlockTests : IDisposable
{
    private static readonly ProcessSpec Spec = new(48_000, 64);
    private readonly string _folder = Directory.CreateTempSubdirectory("cathode-cab-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Impulse_response_is_the_normalised_ir_with_zero_latency()
    {
        var ir = CabLikeIr(1_500, seed: 1);
        using var cab = Create(("ir1", WriteIr("a.wav", ir, 48_000)));

        var output = BlockRender.Mono(cab, Signals.Impulse(2_000), 64);

        var energy = MathF.Sqrt(ir.Sum(static v => v * v));
        for (var i = 0; i < ir.Length; i++)
        {
            Assert.Equal(ir[i] / energy, output[i], 1e-5f);
        }

        Assert.Equal(1.0, output.Sum(static v => (double)v * v), 1e-4); // unit energy
    }

    [Fact]
    public void Two_irs_are_blended_by_their_levels()
    {
        var a = CabLikeIr(800, seed: 2);
        var b = CabLikeIr(1_200, seed: 3);
        using var cab = Create(("ir1", WriteIr("a.wav", a, 48_000)), ("ir2", WriteIr("b.wav", b, 48_000)));
        cab.Params.SetTarget(CabBlock.Level2, -6.0206f);
        cab.Reset();

        var output = BlockRender.Mono(cab, Signals.Impulse(1_500), 64);

        var ea = MathF.Sqrt(a.Sum(static v => v * v));
        var eb = MathF.Sqrt(b.Sum(static v => v * v));
        for (var i = 0; i < 1_200; i++)
        {
            var expected = (i < a.Length ? a[i] / ea : 0f) + 0.5f * b[i] / eb;
            Assert.Equal(expected, output[i], 1e-4f);
        }
    }

    [Fact]
    public void Irs_at_other_sample_rates_are_resampled()
    {
        var ir = CabLikeIr(1_000, seed: 4);
        using var cab = Create(("ir1", WriteIr("a.wav", ir, 44_100)));

        var prepared = cab.PreparedImpulseResponse(0)!;

        Assert.Equal((int)Math.Ceiling(1_000 * 48_000 / 44_100.0), prepared.Length);
        var expected = Resampler.Resample(ir, 44_100, 48_000);
        var norm = 1f / MathF.Sqrt(expected.Sum(static v => v * v));
        Assert.True(Analysis.NullResidualDb(prepared, expected.Select(v => v * norm).ToArray()) < -120);
    }

    [Fact]
    public void Long_irs_are_truncated_with_a_fade()
    {
        using var cab = Create(("ir1", WriteIr("long.wav", Signals.Noise(20_000, seed: 5), 48_000)));

        var prepared = cab.PreparedImpulseResponse(0)!;

        Assert.Equal(CabBlock.MaxIrLength, prepared.Length);
        Assert.Equal(0f, prepared[^1], 1e-6f);
    }

    [Fact]
    public void Stereo_irs_process_each_channel_separately()
    {
        var left = CabLikeIr(400, seed: 6);
        var right = CabLikeIr(400, seed: 7);
        var path = Path.Combine(_folder, "stereo.wav");
        WavFile.Write(path, new AudioData(48_000, [left, right]));
        using var cab = Create(("ir1", path));

        var (outLeft, outRight) = BlockRender.Stereo(cab, Signals.Impulse(500), Signals.Impulse(500), _ => 64);

        Assert.True(Analysis.NullResidualDb(outLeft, outRight) > -40, "left and right should differ");
        Assert.Equal(cab.PreparedImpulseResponse(0, 1)![10], outRight[10], 1e-5f);
    }

    [Fact]
    public void Output_does_not_depend_on_block_size_and_does_not_allocate()
    {
        var ir = CabLikeIr(3_000, seed: 8);
        using var a = Create(("ir1", WriteIr("a.wav", ir, 48_000)));
        using var b = Create(("ir1", WriteIr("b.wav", ir, 48_000)));
        a.Params.SetTarget(CabBlock.LowCut, 80);
        b.Params.SetTarget(CabBlock.LowCut, 80);
        a.Reset();
        b.Reset();
        var input = Signals.Noise(9_600, seed: 9);
        var random = new Random(1);

        var fixedSize = BlockRender.Mono(a, input, 64);
        var varied = BlockRender.Stereo(b, input, input, _ => random.Next(1, 65)).Left;
        var left = Signals.Noise(64, seed: 1);
        var right = Signals.Noise(64, seed: 2);
        var allocated = AllocationProbe.Measure(() =>
        {
            for (var i = 0; i < 1_000; i++)
            {
                a.Process(new StereoBuffer(left, right), default);
            }
        });

        Assert.True(Analysis.NullResidualDb(fixedSize, varied) < -120);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Unknown_slots_and_unreadable_files_throw()
    {
        using var cab = new CabBlock();
        var garbage = Path.Combine(_folder, "garbage.wav");
        File.WriteAllText(garbage, "not a wav file");

        Assert.Throws<ArgumentException>(() => cab.LoadAsset("ir3", garbage));
        Assert.Throws<InvalidDataException>(() => cab.LoadAsset("ir1", garbage));
        Assert.Throws<FileNotFoundException>(() => cab.LoadAsset("ir1", Path.Combine(_folder, "missing.wav")));
    }

    private CabBlock Create(params (string Slot, string Path)[] irs)
    {
        var cab = new CabBlock();
        foreach (var (slot, path) in irs)
        {
            cab.LoadAsset(slot, path);
        }

        cab.Prepare(Spec);
        return cab;
    }

    private string WriteIr(string name, float[] ir, int sampleRate)
    {
        var path = Path.Combine(_folder, name);
        WavFile.Write(path, new AudioData(sampleRate, [ir]));
        return path;
    }

    /// <summary>A decaying, low-passed noise burst: roughly the shape of a close-miked cabinet IR.</summary>
    private static float[] CabLikeIr(int length, int seed)
    {
        var noise = Signals.Noise(length, seed);
        var ir = new float[length];
        float state = 0;
        for (var i = 0; i < length; i++)
        {
            state += 0.3f * (noise[i] - state);
            ir[i] = state * MathF.Exp(-i / (length / 5f));
        }

        return ir;
    }
}
