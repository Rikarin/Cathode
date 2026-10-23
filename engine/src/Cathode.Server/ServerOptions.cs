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

    public EngineOptions Engine { get; set; } = new();
}

public sealed class EngineOptions {
    /// <summary><c>ipc</c> (production: a separate cathode-host process) or <c>inprocess</c> (tests, no audio).</summary>
    public string Mode { get; set; } = "ipc";

    /// <summary>The host's Unix domain socket, when <see cref="Mode" /> is <c>ipc</c>.</summary>
    public string SocketPath { get; set; } = Path.Combine(Path.GetTempPath(), "cathode-host.sock");
}
