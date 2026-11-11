using Cathode.Dsp.IO;
using Cathode.Server.Engine;
using Cathode.Server.Stores;

namespace Cathode.Server.Rig;

/// <summary>
///     Built-in reamp: a DI recording played through the working copy offline, faster than real time and with no
///     interface. The render is the rig as a freshly connected engine would play it: the working copy with its unsaved
///     edits on the active scene, and the global settings (Input 1's gain and calibration, the output EQ and level),
///     with the rig unmuted and the metronome off. It runs in an engine of its own
///     (<see cref="IOfflineRenderer" />), so what is playing is not interrupted.
/// </summary>
public sealed class Reamp(LiveRig rig, IOfflineRenderer renderer) {
    /// <summary>The largest recording a render takes (about 15 minutes of 24-bit stereo at 48 kHz).</summary>
    public const long MaxBytes = 256L * 1024 * 1024;

    /// <summary>The longest tail a render adds, in seconds.</summary>
    public const double MaxTailSeconds = 60;

    const int MinSampleRate = 44_100;
    const int MaxSampleRate = 96_000;

    /// <summary>
    ///     Renders the WAV in <paramref name="input" /> and returns the result: Output 1 as a stereo float WAV at the
    ///     recording's sample rate, in time with it, <paramref name="tailSeconds" /> longer. The stream's file deletes
    ///     itself on close.
    /// </summary>
    public async Task<FileStream> RenderAsync(Stream input, double tailSeconds, CancellationToken cancellationToken) {
        if (!double.IsFinite(tailSeconds) || tailSeconds is < 0 or > MaxTailSeconds) {
            throw new RigException(400, $"The tail is 0 to {MaxTailSeconds:0} seconds.");
        }

        var name = Path.Combine(Path.GetTempPath(), $"cathode-render-{Guid.NewGuid():n}");
        var (inputPath, outputPath) = (name + "-in.wav", name + "-out.wav");
        try {
            await using (var file = File.Create(inputPath)) {
                await PresetPackage.CopyLimitedAsync(input, file, MaxBytes, "The recording", cancellationToken);
                file.Position = 0;
                Check(file);
            }

            var (graph, values) = await rig.RenderStateAsync();
            await renderer.RenderAsync(
                new() {
                    Graph = graph,
                    Params = values,
                    Input = inputPath,
                    Output = outputPath,
                    TailSeconds = tailSeconds
                },
                cancellationToken
            );
            return new(
                outputPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81_920,
                FileOptions.DeleteOnClose | FileOptions.Asynchronous
            );
        } catch {
            File.Delete(outputPath);
            throw;
        } finally {
            File.Delete(inputPath);
        }
    }

    /// <summary>
    ///     Refuses (400) what is not a WAV file the engine reads, an empty one, or a sample rate it does not run at.
    /// </summary>
    static void Check(Stream file) {
        WavHeader header;
        try {
            header = WavFile.ReadHeader(file);
        } catch (Exception e) when (e is InvalidDataException or EndOfStreamException) {
            throw new RigException(400, $"The recording is not a WAV file Cathode reads: {e.Message}");
        }

        if (header.Frames == 0) {
            throw new RigException(400, "The recording is empty.");
        }

        if (header.SampleRate is < MinSampleRate or > MaxSampleRate) {
            throw new RigException(
                400,
                $"The recording's sample rate is {header.SampleRate} Hz; the engine runs at {MinSampleRate} to "
                + $"{MaxSampleRate} Hz."
            );
        }
    }
}
