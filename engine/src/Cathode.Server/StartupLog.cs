namespace Cathode.Server;

/// <summary>How long start-up took to each phase, before the server listens (a slow start shows where it went).</summary>
static partial class StartupLog {
    [LoggerMessage(Level = LogLevel.Information, Message = "Start-up: {Phase} after {Ms} ms")]
    public static partial void Phase(ILogger logger, string phase, long ms);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Problem}; starting on the default preset")]
    public static partial void StartupPresetFailed(ILogger logger, string problem);
}
