using Cathode.Server.Rig;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace Cathode.Server.Stores;

/// <summary>
///     One JSON file per item in a folder, written atomically (a temporary file of each save's own, flushed to the disk,
///     then renamed: <see cref="AtomicRename.Write" />), so a crash or a power cut never leaves a half-written file. Ids
///     become file names and are restricted to safe characters. An <c>upgrade</c> rewrites a
///     file's JSON before it is read (a stored format's migrations, <see cref="PresetMigrations" />).
/// </summary>
/// <remarks>
///     Reads share delete, and a save renames with POSIX semantics on Windows (<see cref="AtomicRename" />), so a save
///     replaces the file under a reader, who keeps the old content. A save or read that still meets a sharing violation
///     (an indexer or a virus scanner holding the file without delete sharing a moment) is retried briefly.
/// </remarks>
public sealed class JsonFileStore<T>
    where T : class {
    readonly string folder;
    readonly string kind;
    readonly JsonTypeInfo<T> typeInfo;
    readonly Func<T, string> idOf;
    readonly Func<JsonNode?, JsonNode?>? upgrade;
    readonly ILogger? logger;

    public JsonFileStore(
        string folder,
        string kind,
        JsonTypeInfo<T> typeInfo,
        Func<T, string> idOf,
        Func<JsonNode?, JsonNode?>? upgrade = null,
        ILogger? logger = null
    ) {
        this.folder = folder;
        this.kind = kind;
        this.typeInfo = typeInfo;
        this.idOf = idOf;
        this.upgrade = upgrade;
        this.logger = logger;
        Directory.CreateDirectory(folder);
    }

    /// <summary>Every readable item with its last-modified time. Unreadable files are skipped (and logged).</summary>
    public IEnumerable<(T Item, DateTimeOffset Modified)> All() =>
        Directory.EnumerateFiles(folder, "*.json")
            .Select(path => (Item: TryRead(path), Modified: (DateTimeOffset)File.GetLastWriteTimeUtc(path)))
            .Where(static e => e.Item is not null)
            .Select(static e => (e.Item!, e.Modified));

    public T? Get(string id) => File.Exists(PathOf(id)) ? TryRead(PathOf(id)) : null;

    public bool Exists(string id) => StoreIds.IsValid(id) && File.Exists(PathOf(id));

    public void Save(T item) {
        AtomicRename.Write(PathOf(idOf(item)), JsonSerializer.Serialize(item, typeInfo));
    }

    public bool Delete(string id) {
        var path = PathOf(id);
        if (!File.Exists(path)) {
            return false;
        }

        File.Delete(path);
        return true;
    }

    string PathOf(string id) =>
        StoreIds.IsValid(id)
            ? Path.Combine(folder, id + ".json")
            : throw new RigException(400, $"Invalid {kind} id '{id}'.");

    T? TryRead(string path) {
        try {
            return AtomicRename.Retrying(() => {
                    using var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete
                    );
                    return upgrade is null
                        ? JsonSerializer.Deserialize(stream, typeInfo)
                        : upgrade(JsonNode.Parse(stream))?.Deserialize(typeInfo);
                }
            );
        } catch (Exception e) when (StoreLog.IsDamage(e)) {
            // One damaged, hand-edited or half-synced file is skipped rather than failing every listing.
            if (logger is not null) {
                StoreLog.Unreadable(logger, kind, path, e.Message);
            }

            return null;
        }
    }
}

static partial class StoreLog {
    /// <summary>
    ///     What reading a stored file throws when the file, not the Server, is at fault: not JSON, JSON of the wrong
    ///     shape (a migration's or the serializer's type checks), gone or held by another program past the retries.
    /// </summary>
    public static bool IsDamage(Exception e) =>
        e is JsonException
            or InvalidOperationException
            or FormatException
            or OverflowException
            or NotSupportedException
            or IOException
            or UnauthorizedAccessException;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped the unreadable {Kind} file {Path}: {Error}")]
    public static partial void Unreadable(ILogger logger, string kind, string path, string error);
}

public static partial class StoreIds {
    /// <summary>Ids are file names, so only safe characters: no separators, no dots.</summary>
    public static bool IsValid(string id) => Pattern().IsMatch(id);

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex Pattern();
}
