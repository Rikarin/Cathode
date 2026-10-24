using Cathode.Dsp.Blocks;

namespace Cathode.Dsp.Tests;

/// <summary>The registry's record of each block type, which the Server reads instead of creating blocks.</summary>
public sealed class BlockRegistryTests {
    static readonly BlockRegistry Registry = BlockRegistry.CreateDefault();

    public static TheoryData<string> BlockTypes => [..Registry.Descriptors.Select(static d => d.TypeId)];

    [Theory]
    [MemberData(nameof(BlockTypes))]
    public void The_asset_slots_on_record_are_the_blocks_own(string typeId) {
        using var block = Registry.Create(typeId);

        Assert.Equal(block is IAssetConsumer consumer ? consumer.AssetSlots : [], Registry.AssetSlots(typeId));
    }

    [Fact]
    public void A_replacement_keeps_the_slots_unless_given_others() {
        var registry = BlockRegistry.CreateDefault();

        registry.Replace(CabBlock.Info, static () => new CabBlock());

        Assert.Equal(["slot1", "slot2", "slot3", "slot4"], registry.AssetSlots(CabBlock.Info.TypeId));
    }
}
