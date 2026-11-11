using Cathode.Server.Stores;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using static Cathode.Server.Api.CathodeApi;

namespace Cathode.Server.Api;

/// <summary>
///     Setlists and banks: lists of presets, read, written and deleted by id. And the whole library as one file:
///     backup and restore (<see cref="LibraryBackup" />).
/// </summary>
static class LibraryApi {
    public static void Map(RouteGroupBuilder api) {
        var library = api.MapGroup("/library").WithTags("Library");
        library.MapGet(
                "/backup",
                (LibraryBackup backup, CancellationToken cancellationToken) => ZipFile(
                    $"Cathode backup {DateTime.Now:yyyy-MM-dd}{LibraryBackup.Extension}",
                    file => backup.WriteAsync(file, cancellationToken)
                )
            )
            .Produces(200, contentType: "application/zip")
            .WithName("BackupLibrary")
            .WithSummary(
                "The whole library as one .cthbackup file: the global settings, the user's presets, block presets, "
                + "templates, snapshots, setlists and banks, and every IR and capture. Factory content is not in it."
            );
        library.MapPost(
                "/restore",
                async (HttpRequest request, LibraryBackup backup, CancellationToken cancellationToken) =>
                    TypedResults.Ok(await backup.RestoreAsync(request.Body, cancellationToken))
            )
            .Accepts<Stream>("application/zip")
            .WithName("RestoreLibrary")
            .WithMetadata(new RequestSizeLimitAttribute(LibraryBackup.MaxBackupBytes))
            .WithSummary(
                "Puts a .cthbackup file into the library: an entry with the id of one already there replaces it, "
                + "everything else stays, and the global settings are replaced. The working copy is not touched. "
                + "Answers with what was restored and what was left out (and why)."
            );
        MapEntries(
            api.MapGroup("/setlists").WithTags("Setlists"),
            "Setlist",
            static books => books.Setlists(),
            static (books, id) => books.Setlist(id),
            static (books, id, setlist, expected) => {
                setlist.Id = id;
                books.Save(setlist, expected);
                return setlist.Revision;
            },
            static (books, id) => books.DeleteSetlist(id)
        );
        MapEntries(
            api.MapGroup("/banks").WithTags("Banks"),
            "Bank",
            static books => books.Banks(),
            null, // a client lists the banks; nothing reads one alone
            static (books, id, bank, expected) => {
                bank.Id = id;
                books.Save(bank, expected);
                return bank.Revision;
            },
            static (books, id) => books.DeleteBank(id)
        );
    }

    /// <summary>
    ///     <c>GET /</c> (named <c>List</c> + <paramref name="name" /> + s), and <c>GET</c> (when
    ///     <paramref name="get" /> is given), <c>PUT</c> and <c>DELETE /{id}</c> (<c>Get</c>, <c>Put</c> and
    ///     <c>Delete</c> + <paramref name="name" />) for one kind of library entry. A put stores the entry under the id
    ///     of its route and answers with it, its new revision as the ETag; with <c>If-Match</c> it is refused (412)
    ///     when the stored entry is not at that revision.
    /// </summary>
    static void MapEntries<T>(
        RouteGroupBuilder group,
        string name,
        Func<Library, T[]> list,
        Func<Library, string, T?>? get,
        Func<Library, string, T, long?, long?> save,
        Func<Library, string, bool> delete
    ) where T : class {
        var noun = name.ToLowerInvariant();
        group.MapGet("/", (Library books) => TypedResults.Ok(list(books))).WithName("List" + name + "s");
        if (get is not null) {
            group.MapGet(
                    ById,
                    Results<Ok<T>, NotFound>(string id, Library books) =>
                        get(books, id) is { } entry ? TypedResults.Ok(entry) : TypedResults.NotFound()
                )
                .WithName("Get" + name);
        }

        group.MapPut(
                ById,
                (string id, T entry, Library books, HttpContext http) => {
                    if (save(books, id, entry, Expected(http)) is { } revision) {
                        http.Response.Headers.ETag = $"\"{revision.ToString(CultureInfo.InvariantCulture)}\"";
                    }

                    return TypedResults.Ok(entry);
                }
            )
            .WithName("Put" + name)
            .WithSummary(
                $"Stores the {noun} under the id of the route and answers with it, its new revision in 'revision' and "
                + "as the ETag. With If-Match (the revision it was read at), refused with 412 when it was stored or "
                + "deleted in between (another editor); without, it replaces whatever is stored."
            );
        group.MapDelete(
                ById,
                Results<NoContent, NotFound>(string id, Library books) =>
                    delete(books, id) ? TypedResults.NoContent() : TypedResults.NotFound()
            )
            .WithName("Delete" + name);
    }
}
