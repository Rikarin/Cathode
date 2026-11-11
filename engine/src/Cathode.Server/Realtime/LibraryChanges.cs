using Cathode.Contracts;

namespace Cathode.Server.Realtime;

/// <summary>
///     Tells every client that something stored changed outside the working copy: a preset deleted, duplicated or
///     imported, a setlist or bank saved, an asset uploaded. The stores call it after each write, so no route (and no
///     MIDI action or batch tool) can change the library without an event. A client reads that kind's list again.
/// </summary>
public sealed class LibraryChanges(EventHub hub) {
    public const string Presets = "presets";
    public const string Setlists = "setlists";
    public const string Banks = "banks";
    public const string Assets = "assets";
    public const string BlockPresets = "blockPresets";
    public const string Snapshots = "snapshots";
    public const string Templates = "templates";

    public void Publish(string kind) =>
        hub.Publish(new("libraryChanged", kind), ContractsJsonContext.Default.LibraryChanged);

    /// <summary>
    ///     What a store calls after a write: publishes <paramref name="kind" />, or nothing without a hub.
    /// </summary>
    public static Action? For(LibraryChanges? changes, string kind) =>
        changes is null ? null : () => changes.Publish(kind);
}
