using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Cathode.Server.Rig;

namespace Cathode.Server.Stores;

/// <summary>
///     One JSON file per item in a folder, written atomically (temp file then rename), so a crash never leaves a
///     half-written file. Ids become file names and are restricted to safe characters.
/// </summary>
public sealed class JsonFileStore<T>
    where T : class {
    readonly string folder;
    readonly string kind;
    readonly JsonTypeInfo<T> typeInfo;
    readonly Func<T, string> idOf;

    public JsonFileStore(string folder, string kind, JsonTypeInfo<T> typeInfo, Func<T, string> idOf) {
        this.folder = folder;
        this.kind = kind;
        this.typeInfo = typeInfo;
        this.idOf = idOf;
        Directory.CreateDirectory(folder);
    }

    /// <summary>Every readable item with its last-modified time. Unreadable files are skipped.</summary>
    public IEnumerable<(T Item, DateTimeOffset Modified)> All() =>
        Directory.EnumerateFiles(folder, "*.json")
            .Select(path => (Item: TryRead(path), Modified: (DateTimeOffset)File.GetLastWriteTimeUtc(path)))
            .Where(static e => e.Item is not null)
            .Select(static e => (e.Item!, e.Modified));

    public T? Get(string id) => File.Exists(PathOf(id)) ? TryRead(PathOf(id)) : null;

    public bool Exists(string id) => StoreIds.IsValid(id) && File.Exists(PathOf(id));

    public void Save(T item) {
        var path = PathOf(idOf(item));
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(item, typeInfo));
        File.Move(temporary, path, true);
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
        StoreIds.IsValid(id) ? Path.Combine(folder, id + ".json") : throw new RigException(400, $"Invalid {kind} id '{id}'.");

    T? TryRead(string path) {
        try {
            return JsonSerializer.Deserialize(File.ReadAllText(path), typeInfo);
        } catch (JsonException) {
            return null;
        }
    }
}

public static partial class StoreIds {
    /// <summary>Ids are file names, so only safe characters: no separators, no dots.</summary>
    public static bool IsValid(string id) => Pattern().IsMatch(id);

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex Pattern();
}
