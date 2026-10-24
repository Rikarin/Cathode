namespace Cathode.Dsp.Parameters;

/// <summary>
///     Metadata for a block type. One descriptor drives the DSP parameter bank, REST validation,
///     the editor's generated UI and the generated test suites.
/// </summary>
public sealed class BlockDescriptor {
    readonly Dictionary<string, int> indexById;
    readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> indexByName;

    public string TypeId { get; }

    public string Name { get; }

    public string Category { get; }

    public int TypeVersion { get; }

    /// <summary>Soft per-preset instance limit (the Axe-Fx III values are the defaults).</summary>
    public int MaxInstances { get; }

    /// <summary>Number of channels (A–D) the block offers.</summary>
    public int Channels { get; }

    public IReadOnlyList<ParamDescriptor> Params { get; }

    /// <summary>
    ///     The parameters set back to their defaults when the block's <c>type</c> changes: the settings that belong to
    ///     one model (the Circuit Amp's knobs, switches and expert values), so the new model starts as it ships rather
    ///     than with the old one's settings. Empty for a block whose settings carry over between types.
    /// </summary>
    public IReadOnlyList<string> TypeResets { get; }

    public BlockDescriptor(
        string typeId,
        string name,
        string category,
        IReadOnlyList<ParamDescriptor> parameters,
        int typeVersion = 1,
        int maxInstances = 4,
        int channels = 4,
        IReadOnlyList<string>? typeResets = null
    ) {
        TypeId = typeId;
        Name = name;
        Category = category;
        Params = parameters;
        TypeVersion = typeVersion;
        MaxInstances = maxInstances;
        Channels = channels;
        TypeResets = typeResets ?? [];
        indexById = new(StringComparer.Ordinal);
        for (var i = 0; i < parameters.Count; i++) {
            if (!indexById.TryAdd(parameters[i].Id, i)) {
                throw new ArgumentException(
                    $"Duplicate parameter id '{parameters[i].Id}' in block '{typeId}'.",
                    nameof(parameters)
                );
            }
        }

        indexByName = indexById.GetAlternateLookup<ReadOnlySpan<char>>();
        if (TypeResets.FirstOrDefault(r => r == "type" || !indexById.ContainsKey(r)) is { } bad) {
            throw new ArgumentException($"Block '{typeId}' cannot reset '{bad}' on a type change.", nameof(typeResets));
        }
    }

    /// <summary>Returns the parameter index for <paramref name="id" />, or -1.</summary>
    public int IndexOf(string id) => indexById.TryGetValue(id, out var index) ? index : -1;

    /// <summary>Allocation-free <see cref="IndexOf(string)" />, for ids parsed straight out of an IPC frame.</summary>
    public int IndexOf(ReadOnlySpan<char> id) => indexByName.TryGetValue(id, out var index) ? index : -1;
}
