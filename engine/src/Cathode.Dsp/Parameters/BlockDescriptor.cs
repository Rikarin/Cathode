namespace Cathode.Dsp.Parameters;

/// <summary>
///     Metadata for a block type. One descriptor drives the DSP parameter bank, REST validation,
///     the editor's generated UI and the generated test suites.
/// </summary>
public sealed class BlockDescriptor {
    private readonly Dictionary<string, int> _indexById;

    public BlockDescriptor(
        string typeId,
        string name,
        string category,
        IReadOnlyList<ParamDescriptor> parameters,
        int typeVersion = 1,
        int maxInstances = 4,
        int channels = 4
    ) {
        TypeId = typeId;
        Name = name;
        Category = category;
        Params = parameters;
        TypeVersion = typeVersion;
        MaxInstances = maxInstances;
        Channels = channels;
        _indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < parameters.Count; i++) {
            if (!_indexById.TryAdd(parameters[i].Id, i)) {
                throw new ArgumentException(
                    $"Duplicate parameter id '{parameters[i].Id}' in block '{typeId}'.",
                    nameof(parameters)
                );
            }
        }
    }

    public string TypeId { get; }

    public string Name { get; }

    public string Category { get; }

    public int TypeVersion { get; }

    /// <summary>Soft per-preset instance limit (the Axe-Fx III values are the defaults).</summary>
    public int MaxInstances { get; }

    /// <summary>Number of channels (A–D) the block offers.</summary>
    public int Channels { get; }

    public IReadOnlyList<ParamDescriptor> Params { get; }

    /// <summary>Returns the parameter index for <paramref name="id" />, or -1.</summary>
    public int IndexOf(string id) => _indexById.TryGetValue(id, out var index) ? index : -1;
}
