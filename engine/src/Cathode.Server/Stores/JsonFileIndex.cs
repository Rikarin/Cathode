namespace Cathode.Server.Stores;

/// <summary>
///     What a listing needs of every item of a <see cref="JsonFileStore{T}" /> (a preset's summary, a bank's slots),
///     kept in memory, so a listing does not read and parse every file again. Each listing still looks through the
///     folder, which costs a directory read, and reads only the files that are new or whose last-modified time or
///     length changed since the listing before; a file changed behind Cathode's back (edited by hand, synced, restored
///     from a backup) is therefore picked up without a restart. A save or delete through the store drops that file's
///     entry, so the store's own changes show whatever the file system's clock resolution.
/// </summary>
/// <remarks>
///     Entries are shared between listings: <c>project</c> must return something no caller changes. Unreadable files
///     are not remembered; they are tried (and logged) on every listing, as a file another program holds for a moment
///     must not stay hidden.
/// </remarks>
public sealed class JsonFileIndex<T, TEntry>
    where T : class {
    readonly Func<T, DateTimeOffset, TEntry> project;
    readonly Lock gate = new();

    /// <summary>By file name. Replaced by every listing, so entries of files that are gone go with them.</summary>
    Dictionary<string, Cached> entries = new(StringComparer.OrdinalIgnoreCase);

    public JsonFileIndex(JsonFileStore<T> store, Func<T, DateTimeOffset, TEntry> project) {
        Store = store;
        this.project = project;
        store.Changed += Forget;
    }

    /// <summary>The store this indexes: single items are read, saved and deleted through it.</summary>
    public JsonFileStore<T> Store { get; }

    /// <summary>Every readable item's entry, in no particular order.</summary>
    public IReadOnlyList<TEntry> All() {
        lock (gate) {
            var next = new Dictionary<string, Cached>(entries.Count, StringComparer.OrdinalIgnoreCase);
            var all = new List<TEntry>(entries.Count);
            foreach (var file in Store.Files()) {
                long length;
                DateTime written;
                try {
                    length = file.Length;
                    written = file.LastWriteTimeUtc;
                } catch (IOException) {
                    continue; // deleted since the folder listed it
                }

                if (!entries.TryGetValue(file.Name, out var cached)
                    || cached.Written != written
                    || cached.Length != length) {
                    if (Store.Read(file.FullName) is not { } item) {
                        continue;
                    }

                    cached = new(written, length, project(item, written));
                }

                next[file.Name] = cached;
                all.Add(cached.Entry);
            }

            entries = next;
            return all;
        }
    }

    void Forget(string path) {
        lock (gate) {
            entries.Remove(Path.GetFileName(path));
        }
    }

    readonly record struct Cached(DateTime Written, long Length, TEntry Entry);
}
