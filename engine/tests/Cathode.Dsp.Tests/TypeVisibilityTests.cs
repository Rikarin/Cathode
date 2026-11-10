using Cathode.Dsp.Blocks;
using Cathode.Dsp.Parameters;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     Controls that only some of a block's types read are shown on exactly those types: moved off its default, a
///     shown one changes the sound and a hidden one leaves it bit for bit as it was.
/// </summary>
public class TypeVisibilityTests {
    public static TheoryData<int> TremoloTypes => [..Enumerable.Range(0, TremoloBlock.TypeCount)];

    public static TheoryData<int> EnhancerTypes => [..Enumerable.Range(0, EnhancerBlock.TypeCount)];

    public static TheoryData<int> DelayTypes => [..Enumerable.Range(0, DelayBlock.TypeCount)];

    [Theory]
    [MemberData(nameof(TremoloTypes))]
    public void Tremolo_shows_pan_center_on_the_panner_and_lfo_phase_on_the_rest(int type) {
        Assert.Equal(type == TremoloBlock.Panner, Shown(TremoloBlock.Info, "panCenter", type));
        Assert.Equal(type != TremoloBlock.Panner, Shown(TremoloBlock.Info, "phase", type));
        AssertShownIsHeard<TremoloBlock>(type, ("panCenter", 40f), ("phase", 90f));
    }

    [Theory]
    [MemberData(nameof(EnhancerTypes))]
    public void Enhancer_shows_time_on_the_delay_types_and_the_cuts_on_the_side_types(int type) {
        Assert.Equal(type is EnhancerBlock.Classic or EnhancerBlock.Haas, Shown(EnhancerBlock.Info, "time", type));
        Assert.Equal(type != EnhancerBlock.Haas, Shown(EnhancerBlock.Info, "lowCut", type));
        Assert.Equal(type != EnhancerBlock.Haas, Shown(EnhancerBlock.Info, "highCut", type));
        AssertShownIsHeard<EnhancerBlock>(type, ("time", 20f), ("lowCut", 800f), ("highCut", 3_000f));
    }

    [Theory]
    [MemberData(nameof(DelayTypes))]
    public void Delay_hides_bit_reduction_on_vintage_digital(int type) {
        Assert.Equal(type != DelayBlock.VintageDigital, Shown(DelayBlock.Info, "bitReduction", type));
        AssertShownIsHeard<DelayBlock>(type, ("bitReduction", 16f));
    }

    static bool Shown(BlockDescriptor info, string id, int type) =>
        info.Params.Single(p => p.Id == id).TypeLabels is not { } labels || labels[type] != string.Empty;

    static void AssertShownIsHeard<T>(int type, params (string Id, float Value)[] moves)
        where T : IDspBlock, new() {
        var reference = Render();
        foreach (var (id, value) in moves) {
            using var probe = new T();
            var shown = Shown(probe.Descriptor, id, type);
            var changed = !Render((probe.Descriptor.IndexOf(id), value)).SequenceEqual(reference);
            Assert.True(shown == changed, $"type {type}: {id} is {(shown ? "shown" : "hidden")} but changed={changed}");
        }

        float[] Render(params (int Index, float Value)[] settings) {
            using var block = TestBlocks.Create<T>([(0, type), ..settings]);
            var (left, right) = BlockRender.Stereo(
                block,
                Signals.Noise(48_000, 3, 0.25f),
                Signals.Noise(48_000, 4, 0.25f),
                static _ => 64
            );
            return [..left, ..right];
        }
    }
}
