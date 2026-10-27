namespace Cathode.EngineHost;

/// <summary>
///     The launcher's lifeline. A process its launcher starts with standard input on a pipe (the desktop app, with
///     <c>--watch-stdin</c> on cathode-host and <c>Cathode:WatchStdin=true</c> on cathode-server) stops when the pipe
///     closes, which the operating system does however the launcher ends: a clean quit, a crash or a force quit, on
///     every platform. Whatever arrives on the pipe is ignored. Opt-in: under systemd or a terminal, standard input is
///     not a lifeline.
/// </summary>
public static class StdinWatch {
    /// <summary>
    ///     Calls <paramref name="closed" />, from a background thread, once <paramref name="input" /> ends.
    /// </summary>
    public static Thread Start(Stream input, Action closed) {
        var thread = new Thread(() => {
                var buffer = new byte[256];
                try {
                    while (input.Read(buffer) > 0) { }
                } catch (Exception e) when (e is IOException or ObjectDisposedException) {
                    // A broken pipe ends the lifeline too.
                }

                try {
                    closed();
                } catch (ObjectDisposedException) {
                    // Already shutting down.
                }
            }
        ) { IsBackground = true, Name = "stdin watch" };
        thread.Start();
        return thread;
    }
}
