namespace Cathode.Server;

/// <summary>Configuration section <c>Cathode</c>.</summary>
public sealed class ServerOptions {
    /// <summary>Where presets and assets live.</summary>
    public string DataDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cathode");

    /// <summary>
    ///     Access token every request must present (<c>Authorization: Bearer</c>). Required when listening beyond
    ///     localhost; the desktop app generates one per run.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    ///     The editor's web build to serve at the root (a headless rig's packaged <c>editor</c> folder), or null to
    ///     serve only the API, as the desktop app's sidecar does.
    /// </summary>
    public string? EditorDirectory { get; set; }

    public EngineOptions Engine { get; set; } = new();
}

/// <summary>
///     The engine host is always a separate cathode-host process (run it with <c>--null-audio</c> for no device); an
///     engine in the Server's process exists only in the tests (Engine/EngineLinks.cs).
/// </summary>
public sealed class EngineOptions {
    /// <summary>The host's Unix domain socket.</summary>
    public string SocketPath { get; set; } = Path.Combine(Path.GetTempPath(), "cathode-host.sock");
}
