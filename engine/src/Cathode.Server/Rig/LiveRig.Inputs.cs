using Cathode.Contracts;
using Cathode.Dsp.Routing;

namespace Cathode.Server.Rig;

/// <summary>
///     Input blocks and hardware sources: each source feeds at most one Input block, as on the Axe-Fx III (one block
///     per input). An Input's Source is a setting of the block, written to every channel at once, so channel A tells
///     it.
/// </summary>
public sealed partial class LiveRig {
    const string SourceParam = "source";

    /// <summary>The hardware input an Input block reads (Input 1 when it has never been set).</summary>
    static int SourceOf(PresetBlock block) =>
        block.Channels.TryGetValue(Preset.ChannelNames[0], out var values)
        && values.TryGetValue(SourceParam, out var source)
            ? (int)source
            : 0;

    /// <summary>The first hardware input no Input block of the preset reads.</summary>
    static int FreeSource(Preset p) {
        var used = p.Blocks.Where(static b => b.Type == BuiltInTypes.Input).Select(SourceOf).ToHashSet();
        return Enumerable.Range(0, HardwareInputs.Names.Length).FirstOrDefault(s => !used.Contains(s), -1) is var free
            and >= 0
            ? free
            : throw RigException.Conflict("Every hardware input already feeds an Input block.");
    }

    /// <summary>Refuses a source another Input block of the working copy already reads.</summary>
    void CheckSource(PresetBlock block, int source) {
        if (preset.Blocks.FirstOrDefault(b => b.Type == BuiltInTypes.Input && b.Id != block.Id && SourceOf(b) == source)
            is { } other) {
            throw RigException.Conflict(
                $"{HardwareInputs.Names[source]} already feeds '{other.Id}': a hardware input feeds one Input block."
            );
        }
    }

    /// <summary>Sets an Input block's source on every channel, so switching channels never changes the input.</summary>
    static void SetSource(PresetBlock block, int source, int channels) {
        foreach (var channel in Preset.ChannelNames[..channels]) {
            block.Channel(channel)[SourceParam] = source;
        }
    }
}
