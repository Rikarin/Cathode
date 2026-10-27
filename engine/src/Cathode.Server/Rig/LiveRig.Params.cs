using Cathode.Contracts;
using Cathode.Dsp.Parameters;
using Cathode.Ipc;

namespace Cathode.Server.Rig;

/// <summary>
///     The steps of <see cref="SetParamsAsync" />: checking, storing, resetting and sending parameter values.
/// </summary>
public sealed partial class LiveRig {
    static void CheckParamNames(BlockDescriptor descriptor, IReadOnlyDictionary<string, float> values) {
        var unknown = values.Keys.Where(k => descriptor.IndexOf(k) < 0).ToList();
        if (unknown.Count > 0) {
            throw new RigException(
                400,
                $"{descriptor.Name} has no parameter(s) {string.Join(", ", unknown)}.",
                unknown
            );
        }
    }

    /// <summary>The hardware input an Input block's update chooses, if it sets one.</summary>
    static int? RequestedSource(
        PresetBlock block,
        BlockDescriptor descriptor,
        IReadOnlyDictionary<string, float> values
    ) =>
        block.Type == BuiltInTypes.Input && values.TryGetValue(SourceParam, out var rawSource)
            ? (int)descriptor.Params[descriptor.IndexOf(SourceParam)].Sanitize(rawSource)
            : null;

    /// <summary>Whether the update picks another model (the block's <c>type</c> parameter) than stored.</summary>
    static bool ChangesType(
        BlockDescriptor descriptor,
        IReadOnlyDictionary<string, float> values,
        Dictionary<string, float> stored
    ) {
        var typeParam = descriptor.IndexOf("type") is var typeIndex and >= 0
            ? descriptor.Params[typeIndex]
            : null;
        return typeParam is not null
            && values.TryGetValue("type", out var type)
            && typeParam.Sanitize(type) != stored.GetValueOrDefault("type", typeParam.Default);
    }

    /// <summary>
    ///     Stores the sanitized values, adding each to <paramref name="sent" />; true when one needs a recompile.
    /// </summary>
    static bool StoreParams(
        string id,
        BlockDescriptor descriptor,
        IReadOnlyDictionary<string, float> values,
        Dictionary<string, float> stored,
        List<ParamValue> sent
    ) {
        var recompile = false;
        // A value that is not a number takes the default on the type the block has after the update.
        var type = descriptor.TypeOf(values.ContainsKey("type") ? values : stored);
        foreach (var (param, raw) in values) {
            var index = descriptor.IndexOf(param);
            var descriptorParam = descriptor.Params[index];
            var value = descriptorParam.Sanitize(raw, type);
            recompile |= descriptorParam.Recompile && descriptor.ValueOf(index, stored) != value;
            stored[param] = value;
            sent.Add(new(id, param, value));
        }

        return recompile;
    }

    /// <summary>
    ///     Puts the parameters a model change resets (and the update did not set) back to their defaults on the new
    ///     model, adding each to <paramref name="sent" />; true when one needs a recompile. A parameter whose default
    ///     depends on the model (<see cref="ParamDescriptor.TypeDefaults" />) and that the channel does not store is
    ///     the new model's default now, so the engine is sent it too.
    /// </summary>
    static bool ResetForNewType(
        string id,
        BlockDescriptor descriptor,
        IReadOnlyDictionary<string, float> values,
        Dictionary<string, float> stored,
        List<ParamValue> sent
    ) {
        var recompile = false;
        foreach (var param in descriptor.TypeResets.Where(r => !values.ContainsKey(r))) {
            var index = descriptor.IndexOf(param);
            var value = descriptor.DefaultOf(index, stored);
            recompile |= descriptor.Params[index].Recompile && stored.GetValueOrDefault(param, value) != value;
            stored[param] = value;
            sent.Add(new(id, param, value));
        }

        foreach (var p in descriptor.Params.Where(p => p.TypeDefaults is not null && !stored.ContainsKey(p.Id))) {
            sent.Add(new(id, p.Id, descriptor.DefaultOf(descriptor.IndexOf(p.Id), stored)));
        }

        return recompile;
    }

    /// <summary>A block linked to a global block writes the channel through to it.</summary>
    void ShareWithGlobal(string link, string target, Dictionary<string, float> stored) =>
        globals.Update(g => {
                if (g.Blocks.Find(b => b.Id == link) is { } shared) {
                    shared.Channels[target] = new(stored);
                }
            }
        );

    /// <summary>
    ///     Sends the new values to the engine for the block (when <paramref name="active" />, its channel is playing)
    ///     and the linked blocks <paramref name="running" /> on that channel, or recompiles when one needs it.
    /// </summary>
    async Task SendParamsAsync(bool recompile, bool active, List<PresetBlock> running, List<ParamValue> sent) {
        if (recompile && (active || running.Count > 0)) {
            await SyncGraphAsync(); // a latency change (look-ahead): the compiler has to see it
            return;
        }

        if (active) {
            engine.SetParams(generation, sent);
        }

        foreach (var other in running) {
            engine.SetParams(generation, Retargeted(sent, other.Id));
        }
    }

    /// <summary>The values as sent to another block (a linked one).</summary>
    static List<ParamValue> Retargeted(List<ParamValue> sent, string block) =>
        sent.ConvertAll(v => v with { Block = block });
}
