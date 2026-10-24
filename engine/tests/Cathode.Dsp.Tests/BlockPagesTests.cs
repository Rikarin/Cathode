using System.Text.RegularExpressions;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Tests;

/// <summary>The editor pages every registered block declares (<see cref="BlockDescriptor.Pages" />).</summary>
public class BlockPagesTests {
    /// <summary>A page holds no more controls than the editor's rack shows without crowding.</summary>
    const int MaxControlsPerPage = 40;

    /// <summary>Above this many controls of its own a block is split into pages.</summary>
    const int MaxSinglePageControls = 20;

    /// <summary>Shown apart from the pages: the output stage on the editor's right, the type in its sidebar.</summary>
    static readonly HashSet<string> ShownElsewhere = ["level", "mix", "balance", "pan", "type"];

    /// <summary>
    ///     Parameters the editor shows on a block's own panel instead of as knobs: the Cab's mic slots and the Pitch
    ///     block's custom shifter tables.
    /// </summary>
    static readonly Dictionary<string, Regex> OnPanel = new() {
        ["cab"] = new("^m[1-4][a-z]+$"),
        ["pitch"] = new("^custom[1-4][A-G]s?$")
    };

    static readonly BlockRegistry Registry = BlockRegistry.CreateDefault();

    public static TheoryData<string> BlockTypes => [..Registry.Descriptors.Select(static d => d.TypeId)];

    [Theory]
    [MemberData(nameof(BlockTypes))]
    public void Every_page_holds_a_control_and_none_holds_too_many(string typeId) {
        var descriptor = Registry.GetDescriptor(typeId);

        foreach (var page in descriptor.Pages) {
            var controls = Controls(descriptor).Count(p => p.Page == page);
            Assert.True(controls > 0, $"{typeId}: page '{page}' has no control of its own");
            Assert.True(controls <= MaxControlsPerPage, $"{typeId}: page '{page}' has {controls} controls");
        }
    }

    [Theory]
    [MemberData(nameof(BlockTypes))]
    public void A_paged_block_puts_every_parameter_on_one_of_its_pages(string typeId) {
        var descriptor = Registry.GetDescriptor(typeId);

        foreach (var p in descriptor.Params) {
            Assert.Equal(descriptor.Pages.Count == 0, p.Page is null);
            if (p.Page is { } page) {
                Assert.Contains(page, descriptor.Pages);
            }
        }
    }

    [Theory]
    [MemberData(nameof(BlockTypes))]
    public void A_large_block_has_several_pages(string typeId) {
        var descriptor = Registry.GetDescriptor(typeId);

        var controls = Controls(descriptor).Count();
        if (controls > MaxSinglePageControls) {
            Assert.True(descriptor.Pages.Count >= 2, $"{typeId} shows {controls} controls on one page");
        }
    }

    static IEnumerable<ParamDescriptor> Controls(BlockDescriptor descriptor) =>
        descriptor.Params.Where(p =>
            !ShownElsewhere.Contains(p.Id)
            && !(OnPanel.TryGetValue(descriptor.TypeId, out var panel) && panel.IsMatch(p.Id))
        );
}
