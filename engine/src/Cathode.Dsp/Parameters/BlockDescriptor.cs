namespace Cathode.Dsp.Parameters;

/// <summary>An editor page of a block: its name and the parameters on it, in the order they show.</summary>
public sealed record ParamPage(string Name, IReadOnlyList<string> Params);

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

    /// <summary>
    ///     The editor's pages, in order (Basic, Tone, Advanced, ...), as Axe-Edit groups a block's controls; empty
    ///     for a block shown on one page. A parameter no page lists goes on the first.
    /// </summary>
    public IReadOnlyList<string> Pages { get; }

    public BlockDescriptor(
        string typeId,
        string name,
        string category,
        IReadOnlyList<ParamDescriptor> parameters,
        int typeVersion = 1,
        int maxInstances = 4,
        int channels = 4,
        IReadOnlyList<string>? typeResets = null,
        IReadOnlyList<ParamPage>? pages = null
    ) {
        TypeId = typeId;
        Name = name;
        Category = category;
        Params = Paged(typeId, parameters, pages ?? []);
        Pages = [..(pages ?? []).Select(static p => p.Name)];
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

    /// <summary>
    ///     The parameters with their <see cref="ParamDescriptor.Page" /> set, in their own order (presets and the
    ///     parameter bank index them by position): each listed one on its page, the rest on the first.
    /// </summary>
    static ParamDescriptor[] Paged(
        string typeId,
        IReadOnlyList<ParamDescriptor> parameters,
        IReadOnlyList<ParamPage> pages
    ) {
        if (pages.Count == 0) {
            return [..parameters];
        }

        var pageOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var page in pages) {
            if (pages.Count(p => p.Name == page.Name) > 1) {
                throw new ArgumentException($"Block '{typeId}' has two pages named '{page.Name}'.", nameof(pages));
            }

            foreach (var id in page.Params) {
                if (!parameters.Any(p => p.Id == id)) {
                    throw new ArgumentException(
                        $"Page '{page.Name}' of block '{typeId}' lists unknown parameter '{id}'.",
                        nameof(pages)
                    );
                }

                if (!pageOf.TryAdd(id, page.Name)) {
                    throw new ArgumentException($"Block '{typeId}' puts '{id}' on two pages.", nameof(pages));
                }
            }
        }

        return [..parameters.Select(p => p with { Page = pageOf.GetValueOrDefault(p.Id, pages[0].Name) })];
    }

    /// <summary>Returns the parameter index for <paramref name="id" />, or -1.</summary>
    public int IndexOf(string id) => indexById.TryGetValue(id, out var index) ? index : -1;

    /// <summary>Allocation-free <see cref="IndexOf(string)" />, for ids parsed straight out of an IPC frame.</summary>
    public int IndexOf(ReadOnlySpan<char> id) => indexByName.TryGetValue(id, out var index) ? index : -1;
}
