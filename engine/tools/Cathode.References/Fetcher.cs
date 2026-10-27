using Cathode.Testing.References;

namespace Cathode.References;

/// <summary>
///     Downloads the redistributable references into the cache, one file at a time, and verifies each SHA-256. This is
///     the only network access in the harness. It refuses anything not marked redistributable or under a licence that
///     is not GPL-compatible: T3K and non-commercial captures must be fetched by hand (the Tone3000 terms forbid
///     scripted downloads).
/// </summary>
sealed class Fetcher(ReferenceCache cache, HttpClient http) {
    /// <summary>Licences whose files may be downloaded by script and could be committed to a GPL-3.0 repository.</summary>
    static readonly HashSet<string> Redistributable = new(StringComparer.OrdinalIgnoreCase) {
        "GPL-3.0",
        "GPL-3.0-only",
        "GPL-3.0-or-later",
        "MIT",
        "BSD-3-Clause",
        "CC0-1.0",
        "CC-BY-4.0",
        "CC-BY-SA-4.0"
    };

    public static bool MayFetch(ReferenceEntry entry) =>
        entry.Redistributable && Redistributable.Contains(entry.Licence);

    /// <summary>Fetches every missing (or, with <paramref name="force" />, every) redistributable reference.</summary>
    public async Task<int> FetchAllAsync(
        IEnumerable<ReferenceEntry> entries,
        bool force,
        CancellationToken cancellationToken
    ) {
        Directory.CreateDirectory(cache.Root);
        var failures = 0;
        foreach (var entry in entries.Where(MayFetch)) {
            var state = cache.StateOf(entry);
            if (state == ReferenceState.Verified && !force) {
                Console.WriteLine($"  ok       {entry.Id}");
                continue;
            }

            try {
                await FetchAsync(entry, cancellationToken);
                Console.WriteLine($"  fetched  {entry.Id} ({entry.Licence}, {entry.Author})");
            } catch (Exception e) when (e is HttpRequestException
                                            or InvalidDataException
                                            or IOException
                                            or TaskCanceledException) {
                failures++;
                Console.Error.WriteLine($"  FAILED   {entry.Id}: {e.Message}");
            }
        }

        return failures;
    }

    async Task FetchAsync(ReferenceEntry entry, CancellationToken cancellationToken) {
        if (!MayFetch(entry)) {
            throw new InvalidOperationException($"{entry.Id} ({entry.Licence}) may not be downloaded by script.");
        }

        var data = await http.GetByteArrayAsync(new Uri(entry.Url), cancellationToken);
        var hash = ReferenceCache.Sha256Of(data);
        if (entry.Sha256 is not null && !string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidDataException(
                $"SHA-256 mismatch: expected {entry.Sha256}, got {hash}. The upstream file changed; review it and update the manifest."
            );
        }

        var path = cache.PathOf(entry);
        var temporary = path + ".part";
        await File.WriteAllBytesAsync(temporary, data, cancellationToken);
        File.Move(temporary, path, true);
    }
}
