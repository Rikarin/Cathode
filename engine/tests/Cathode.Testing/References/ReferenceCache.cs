using System.Security.Cryptography;

namespace Cathode.Testing.References;

/// <summary>What the cache holds for one reference.</summary>
public enum ReferenceState {
    /// <summary>Not in the cache.</summary>
    Missing,

    /// <summary>Present and its SHA-256 matches the manifest.</summary>
    Verified,

    /// <summary>Present, but the manifest has no hash for it yet (a hand-fetched file).</summary>
    Unverified,

    /// <summary>Present with a different SHA-256: not used.</summary>
    Mismatch
}

/// <summary>
///     The local folder that holds reference files, outside the repository: <c>$CATHODE_REFERENCES</c>, or
///     <c>~/.cache/cathode/references</c>. Redistributable files are put there by <c>cathode-references fetch</c>;
///     local-only ones (T3K) by the user, named <see cref="ReferenceEntry.FileName" />. Nothing here downloads.
/// </summary>
public sealed class ReferenceCache(string root) {
    public const string EnvironmentVariable = "CATHODE_REFERENCES";

    /// <summary>
    ///     Set to <c>1</c> where the redistributable references must be present (CI after
    ///     <c>
    ///         cathode-references
    ///         fetch
    ///     </c>
    ///     ), so a missing one fails its test instead of skipping it.
    /// </summary>
    public const string RequiredVariable = "CATHODE_REFERENCES_REQUIRED";

    public string Root { get; } = root;

    /// <summary>The cache configured by the environment, or the default location.</summary>
    public static ReferenceCache Default { get; } = new(DefaultRoot());

    /// <summary>True when <see cref="RequiredVariable" /> is set to <c>1</c>.</summary>
    public static bool Required => Environment.GetEnvironmentVariable(RequiredVariable) == "1";

    public bool Exists => Directory.Exists(Root);

    public string PathOf(ReferenceEntry entry) => Path.Combine(Root, entry.FileName);

    public ReferenceState StateOf(ReferenceEntry entry) {
        var path = PathOf(entry);
        if (!File.Exists(path)) {
            return ReferenceState.Missing;
        }

        if (entry.Sha256 is null) {
            return ReferenceState.Unverified;
        }

        return string.Equals(Sha256Of(path), entry.Sha256, StringComparison.OrdinalIgnoreCase)
            ? ReferenceState.Verified
            : ReferenceState.Mismatch;
    }

    /// <summary>The usable file for <paramref name="entry" />, or null when it is missing or fails its hash.</summary>
    public string? TryLocate(ReferenceEntry entry) =>
        StateOf(entry) is ReferenceState.Verified or ReferenceState.Unverified ? PathOf(entry) : null;

    public static string Sha256Of(string path) {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static string Sha256Of(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));

    static string DefaultRoot() {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured)) {
            return Path.GetFullPath(configured);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".cache", "cathode", "references");
    }
}
