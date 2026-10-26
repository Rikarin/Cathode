using Cathode.Contracts;
using Cathode.Dsp.Amp;
using Cathode.Server.Stores;

namespace Cathode.Server.Rig;

/// <summary>
///     Operations on stored presets for the editor's Manage Presets view: duplicate, Reset Amp blocks and Parameter
///     Batch Set. They change the library, not the working copy; the editor reloads the working copy when it is one of
///     them.
/// </summary>
public sealed class PresetTools(PresetStore presets, BlockCatalog catalog) {
    /// <summary>The Circuit Amp block type (Cathode.Dsp.Blocks.CircuitAmpBlock).</summary>
    public const string CircuitAmp = "circuitamp";

    public PresetSummary Duplicate(string id, DuplicatePresetRequest request) {
        var copy = presets.Get(id) ?? throw RigException.NotFound($"No preset '{id}'.");
        copy.Id = request.Id ?? Preset.NewId();
        if (!StoreIds.IsValid(copy.Id)) {
            throw new RigException(400, $"Invalid preset id '{copy.Id}'.");
        }

        if (presets.Exists(copy.Id)) {
            throw RigException.Conflict($"Preset id '{copy.Id}' is taken.");
        }

        copy.Name = string.IsNullOrWhiteSpace(request.Name) ? copy.Name + " copy" : request.Name.Trim();
        presets.Save(copy);
        return PresetStore.Summary(copy, DateTimeOffset.UtcNow);
    }

    /// <summary>
    ///     Reset Amp blocks: every Circuit Amp parameter of the presets goes back to its default, except the controls on
    ///     the model's front panel (the knobs and switches its <see cref="AmpPanel" /> names) and the ones a type change
    ///     keeps (type, level, balance, input select). The expert settings, in short, start over.
    /// </summary>
    public BatchResult ResetAmps(IEnumerable<string> ids) {
        var descriptor = catalog.Descriptor(CircuitAmp)
            ?? throw new RigException(500, "The Circuit Amp block is not registered.");
        var resets = descriptor.TypeResets.ToHashSet(StringComparer.Ordinal);
        var type = descriptor.Params[descriptor.IndexOf("type")];
        return Batch(
            ids,
            preset => {
                var changed = false;
                foreach (var block in preset.Blocks.Where(static b => b.Type == CircuitAmp)) {
                    foreach (var values in block.Channels.Values) {
                        var model = (int)type.Sanitize(values.GetValueOrDefault("type", type.Default));
                        var panel = AmpDesign.All[Math.Clamp(model, 0, AmpDesign.All.Count - 1)].Panel;
                        foreach (var id in values.Keys.ToList()) {
                            if (resets.Contains(id) && string.IsNullOrEmpty(panel.Label(id, string.Empty))) {
                                changed |= values.Remove(id);
                            }
                        }
                    }
                }

                return changed;
            }
        );
    }

    /// <summary>Parameter Batch Set: one value into the matching block (same id and type) of each preset.</summary>
    public BatchResult BatchSet(BatchSetRequest request) {
        var descriptor = catalog.Descriptor(request.Type)
            ?? throw new RigException(400, $"'{request.Type}' is not a block type with parameters.");
        var index = descriptor.IndexOf(request.Param);
        if (index < 0) {
            throw new RigException(400, $"{descriptor.Name} has no parameter '{request.Param}'.");
        }

        var channels = Preset.ChannelNames[..descriptor.Channels];
        if (request.Channel is { } only) {
            channels = channels.Contains(only)
                ? [only]
                : throw new RigException(400, $"Channel '{only}' does not exist on a {descriptor.Name}.");
        }

        var value = descriptor.Params[index].Sanitize(request.Value);
        return Batch(
            request.Presets,
            preset => {
                if (preset.Blocks.Find(b => b.Id == request.Block && b.Type == request.Type) is not { } block) {
                    return false;
                }

                foreach (var channel in channels) {
                    block.Channel(channel)[request.Param] = value;
                }

                return true;
            }
        );
    }

    BatchResult Batch(IEnumerable<string> ids, Func<Preset, bool> change) {
        List<string> changed = [];
        List<string> skipped = [];
        foreach (var id in ids.Distinct()) {
            var preset = StoreIds.IsValid(id) ? presets.Get(id) : null;
            // Factory presets are read-only: Reset Amp blocks and Batch Set leave them alone.
            if (preset is not null && preset.Factory != true && change(preset)) {
                presets.Save(preset);
                changed.Add(id);
            } else {
                skipped.Add(id);
            }
        }

        return new(changed, skipped);
    }
}
