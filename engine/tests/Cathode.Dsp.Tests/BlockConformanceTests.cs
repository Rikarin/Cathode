using Cathode.Dsp.Parameters;
using Cathode.Testing;
using CsCheck;

namespace Cathode.Dsp.Tests;

/// <summary>
/// Properties every registered block must satisfy (docs/03-testing-strategy.md §3). New blocks join this suite
/// automatically by being registered in <see cref="BlockRegistry.CreateDefault"/>.
/// </summary>
public class BlockConformanceTests
{
    private const int MaxBlock = 64;

    /// <summary>
    /// Blow-up ceiling. Legitimate gain can be very large (five PEQ bands stacked at +24 dB plus +20 dB level is
    /// +140 dB), while a genuinely unstable filter grows exponentially and passes any fixed level within a render, or
    /// goes non-finite. Marginal instabilities are covered by the decay-to-silence test.
    /// </summary>
    private const double PeakBoundDb = 160;
    private static readonly ProcessSpec Spec = new(48_000, MaxBlock);
    private static readonly BlockRegistry Registry = BlockRegistry.CreateDefault();

    public static TheoryData<string> BlockTypes => [.. Registry.Descriptors.Select(static d => d.TypeId)];

    [Theory]
    [MemberData(nameof(BlockTypes))]
    public void Descriptor_is_consistent(string typeId)
    {
        var descriptor = Registry.GetDescriptor(typeId);

        Assert.NotEmpty(descriptor.Params);
        foreach (var p in descriptor.Params)
        {
            Assert.InRange(p.Default, p.Min, p.Max);
            Assert.True(p.Min < p.Max, $"{p.Id}: empty range");
            if (p.Kind == ParamKind.Enum)
            {
                Assert.Equal(p.Options!.Count - 1, p.Max);
            }
        }
    }

    [Theory]
    [MemberData(nameof(BlockTypes))]
    public void Output_is_finite_and_bounded_for_any_parameters_and_stimulus(string typeId)
    {
        var descriptor = Registry.GetDescriptor(typeId);
        Gen.Select(Gen.Float[0f, 1f].Array[descriptor.Params.Count], Gen.Float[0f, 1f].Array[descriptor.Params.Count], Gen.Int[0, 3], Gen.Int)
            .Sample(
                (start, jump, stimulus, seed) =>
                {
                    using var block = Create(typeId, start);
                    var input = Stimulus(stimulus, seed, 9_600);
                    var (left, right) = BlockRender.Stereo(block, input, input, _ => MaxBlock);

                    // Jump every parameter at once mid-stream, as a preset or scene change would.
                    ApplyNormalised(block, jump);
                    var (left2, right2) = BlockRender.Stereo(block, input, input, _ => MaxBlock);

                    foreach (var output in new[] { left, right, left2, right2 })
                    {
                        Assert.True(Analysis.AllFinite(output), "non-finite output");
                        Assert.True(Analysis.ToDb(Analysis.Peak(output)) < PeakBoundDb, $"output peak {Analysis.ToDb(Analysis.Peak(output)):0.0} dBFS");
                    }
                },
                iter: 200); // nightly runs raise this via CsCheck_Iter
    }

    [Theory]
    [MemberData(nameof(BlockTypes))]
    public void Rendering_is_deterministic_and_reset_restores_a_fresh_state(string typeId)
    {
        var input = Signals.Noise(4_800, seed: 7);
        using var first = Create(typeId, null);
        using var second = Create(typeId, null);

        var a = BlockRender.Mono(first, input, MaxBlock);
        var b = BlockRender.Mono(second, input, MaxBlock);
        first.Reset();
        var c = BlockRender.Mono(first, input, MaxBlock);

        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Theory]
    [MemberData(nameof(BlockTypes))]
    public void Output_does_not_depend_on_block_size(string typeId)
    {
        var input = Signals.Noise(9_600, seed: 11);
        using var fixedSize = Create(typeId, null);
        using var variableSize = Create(typeId, null);
        var random = new Random(5);

        var reference = BlockRender.Mono(fixedSize, input, MaxBlock);
        var varied = BlockRender.Stereo(variableSize, input, input, _ => random.Next(1, MaxBlock + 1)).Left;

        Assert.True(Analysis.NullResidualDb(reference, varied) < -140);
    }

    [Theory]
    [MemberData(nameof(BlockTypes))]
    public void Process_does_not_allocate(string typeId)
    {
        using var block = Create(typeId, null);
        var left = Signals.Noise(MaxBlock, seed: 1);
        var right = Signals.Noise(MaxBlock, seed: 2);
        var context = new BlockContext(Spec.SampleRate, 120, 0);
        var low = Enumerable.Repeat(0.1f, block.Params.Count).ToArray();
        var high = Enumerable.Repeat(0.9f, block.Params.Count).ToArray();
        var flip = false;

        var allocated = AllocationProbe.Measure(() =>
        {
            for (var i = 0; i < 2_000; i++)
            {
                // Keep a parameter moving so the smoothing paths run too.
                if (i % 100 == 0)
                {
                    flip = !flip;
                    ApplyNormalised(block, flip ? high : low);
                }

                block.Process(new StereoBuffer(left, right), context);
            }
        });

        Assert.Equal(0, allocated);
    }

    [Theory]
    [MemberData(nameof(BlockTypes))]
    public void Silence_after_a_burst_decays_without_subnormals(string typeId)
    {
        using var block = Create(typeId, null);
        var input = Signals.Concat(Signals.Noise(4_800, seed: 3), new float[48_000 * 5]);

        var output = BlockRender.Mono(block, input, MaxBlock);
        var tail = output.AsSpan(output.Length - 4_800);

        Assert.False(Analysis.AnySubnormal(output), "subnormal samples in output");
        Assert.True(Analysis.ToDb(Analysis.Peak(tail)) < -120, "tail did not decay");
    }

    private static IDspBlock Create(string typeId, float[]? normalised)
    {
        var block = Registry.Create(typeId);
        if (normalised is not null)
        {
            ApplyNormalised(block, normalised);
        }

        block.Prepare(Spec);
        return block;
    }

    /// <summary>Maps values in [0, 1] onto each parameter's range.</summary>
    private static void ApplyNormalised(IDspBlock block, float[] normalised)
    {
        for (var i = 0; i < block.Params.Count; i++)
        {
            var d = block.Params.Descriptor(i);
            block.Params.SetTarget(i, d.Min + normalised[i] * (d.Max - d.Min));
        }
    }

    private static float[] Stimulus(int kind, int seed, int length) => kind switch
    {
        0 => Signals.Noise(length, seed),
        1 => Signals.Sine(997, 48_000, length).Select(static s => s >= 0 ? 1f : -1f).ToArray(), // full-scale square
        2 => Signals.Step(length), // DC
        _ => new float[length],
    };
}
