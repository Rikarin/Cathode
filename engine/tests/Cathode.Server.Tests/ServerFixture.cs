using System.Net.Http.Json;
using Cathode.Contracts;
using Cathode.Dsp;
using Cathode.Engine.Graph;
using Cathode.EngineHost;
using Cathode.Server.Engine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Sdk;

namespace Cathode.Server.Tests;

/// <summary>
///     A Server with a throwaway data folder and, by default, an in-process engine (null audio). Pass a socket path
///     to talk to a separate cathode-host over IPC instead, a token to require one on every request, and an editor
///     folder to serve the editor's web build at the root, as a headless rig does.
/// </summary>
public sealed class ServerFixture(string? socketPath = null, string? token = null, string? editorDirectory = null)
    : WebApplicationFactory<Program> {
    readonly string dataDirectory = Directory.CreateTempSubdirectory("cathode-server-").FullName;

    /// <summary>The test that made this server: its warnings and errors go to that test's output.</summary>
    readonly ITestOutputHelper? output = TestContext.Current.TestOutputHelper;

    readonly System.Collections.Concurrent.ConcurrentQueue<string> log = new();

    /// <summary>The server's warnings and errors so far, for a failure message.</summary>
    public string Log => log.IsEmpty ? "(no warnings)" : string.Join("\n", log);

    /// <summary>The in-process engine host (in-process mode only).</summary>
    public EngineHostService Host =>
        ((InProcessEngineLink)Services.GetRequiredService<EngineConnection>().Link!).Service;

    public CompiledGraph? Graph =>
        (Services.GetRequiredService<EngineConnection>().Link as InProcessEngineLink)?.Service.Engine.Current;

    public async Task<HttpClient> ConnectedClientAsync() {
        var client = CreateClient();
        // Generous: a loaded CI runner can take a while to start a separate host process and connect to it, and
        // "connected" includes resending the rig (compiling its graph) to the host.
        await Eventually(
            async () => (await client.GetFromJsonAsync<SystemInfo>("/api/v1/system"))!.Engine.Connected,
            60
        );
        return client;
    }

    public IDspBlock Block(string id) {
        var graph = Graph ?? throw new InvalidOperationException("No graph.");
        var slot = graph.SlotOf(id);
        return slot >= 0
            ? graph.Block(slot)
            : throw new InvalidOperationException($"No block '{id}' in the running graph (yet).");
    }

    public float Target(string block, string param) {
        var dsp = Block(block);
        return dsp.Params.GetTarget(dsp.Descriptor.IndexOf(param));
    }

    public bool IsBypassed(string block) => Graph!.IsBypassed(Graph.SlotOf(block));

    /// <summary>
    ///     Engine commands are asynchronous; waits up to <paramref name="seconds" /> for <paramref name="condition" />.
    ///     An exception counts as "not yet" (the graph may not have swapped in) until the deadline, then it is reported.
    ///     The default deadline is generous: it costs nothing when the condition is met (polled every 20 ms), and a
    ///     machine running several test projects at once can take seconds to compile a graph or run a MIDI action.
    /// </summary>
    public static async Task Eventually(Func<Task<bool>> condition, int seconds = 30, Func<string>? describe = null) {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        Exception? last = null;
        while (DateTime.UtcNow < deadline) {
            try {
                if (await condition()) {
                    return;
                }
            } catch (Exception e) when (e is not XunitException) {
                last = e;
            }

            await Task.Delay(20);
        }

        Assert.Fail(
            $"Condition not met within {seconds} s.{(last is null ? string.Empty : $" Last error: {last}")}"
            + (describe is null ? string.Empty : $"\n{describe()}")
        );
    }

    public static Task Eventually(Func<bool> condition, Func<string>? describe = null) =>
        Eventually(() => Task.FromResult(condition()), describe: describe);

    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.UseSetting("Cathode:DataDirectory", dataDirectory);
        builder.UseSetting("Cathode:Engine:Mode", socketPath is null ? "inprocess" : "ipc");
        if (token is not null) {
            builder.UseSetting("Cathode:Token", token);
        }

        if (socketPath is not null) {
            builder.UseSetting("Cathode:Engine:SocketPath", socketPath);
        }

        if (editorDirectory is not null) {
            builder.UseSetting("Cathode:EditorDirectory", editorDirectory);
        }

        // Warnings and errors (a failed MIDI action, a command the engine rejected) into the test's output, where a
        // failure shows them; the console's request-by-request information only buried them.
        builder.ConfigureLogging(logging => {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Warning);
                logging.AddProvider(new TestOutputLoggerProvider(output, log.Enqueue));
            }
        );
    }

    protected override void Dispose(bool disposing) {
        base.Dispose(disposing);
        if (disposing) {
            Directory.Delete(dataDirectory, true);
        }
    }
}

/// <summary>Writes log entries to a test's output; entries after the test has finished are dropped.</summary>
sealed class TestOutputLoggerProvider(ITestOutputHelper? output, Action<string> keep) : ILoggerProvider {
    public ILogger CreateLogger(string categoryName) => new Logger(output, keep, categoryName);

    public void Dispose() { }

    sealed class Logger(ITestOutputHelper? output, Action<string> keep, string category) : ILogger {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) {
            if (!IsEnabled(logLevel)) {
                return;
            }

            var line = $"{DateTime.Now:HH:mm:ss.fff} {logLevel} {category}: {formatter(state, exception)}"
                + (exception is null ? string.Empty : $"\n{exception}");
            keep(line);
            try {
                output?.WriteLine(line);
            } catch (InvalidOperationException) {
                // the test has finished
            }
        }
    }
}
