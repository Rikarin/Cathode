using Cathode.Server.Stores;
using Microsoft.AspNetCore.Http.HttpResults;
using static Cathode.Server.Api.CathodeApi;

namespace Cathode.Server.Api;

/// <summary>Setlists and banks: lists of presets, read, written and deleted by id.</summary>
static class LibraryApi {
    public static void Map(RouteGroupBuilder api) {
        MapEntries(
            api.MapGroup("/setlists").WithTags("Setlists"),
            "Setlist",
            static books => books.Setlists(),
            static (books, id) => books.Setlist(id),
            static (books, id, setlist) => {
                setlist.Id = id;
                books.Save(setlist);
            },
            static (books, id) => books.DeleteSetlist(id)
        );
        MapEntries(
            api.MapGroup("/banks").WithTags("Banks"),
            "Bank",
            static books => books.Banks(),
            static (books, id) => books.Bank(id),
            static (books, id, bank) => {
                bank.Id = id;
                books.Save(bank);
            },
            static (books, id) => books.DeleteBank(id)
        );
    }

    /// <summary>
    ///     <c>GET /</c> (named <c>List</c> + <paramref name="name" /> + s), and <c>GET</c>, <c>PUT</c> and
    ///     <c>DELETE /{id}</c> (<c>Get</c>, <c>Put</c> and <c>Delete</c> + <paramref name="name" />) for one kind of
    ///     library entry. A put stores the entry under the id of its route and answers with it.
    /// </summary>
    static void MapEntries<T>(
        RouteGroupBuilder group,
        string name,
        Func<Library, T[]> list,
        Func<Library, string, T?> get,
        Action<Library, string, T> save,
        Func<Library, string, bool> delete
    ) where T : class {
        group.MapGet("/", (Library books) => TypedResults.Ok(list(books))).WithName("List" + name + "s");
        group.MapGet(
                ById,
                Results<Ok<T>, NotFound>(string id, Library books) =>
                    get(books, id) is { } entry ? TypedResults.Ok(entry) : TypedResults.NotFound()
            )
            .WithName("Get" + name);
        group.MapPut(
                ById,
                (string id, T entry, Library books) => {
                    save(books, id, entry);
                    return TypedResults.Ok(entry);
                }
            )
            .WithName("Put" + name);
        group.MapDelete(
                ById,
                Results<NoContent, NotFound>(string id, Library books) =>
                    delete(books, id) ? TypedResults.NoContent() : TypedResults.NotFound()
            )
            .WithName("Delete" + name);
    }
}
