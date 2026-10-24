using Cathode.Contracts;
using Cathode.Dsp.Spectrum;
using Cathode.Engine.Telemetry;
using Cathode.Server.Rig;

namespace Cathode.Server.Engine;

/// <summary>Spectrum analysis of the engine's output, read from the telemetry region's scope ring.</summary>
public sealed partial class EngineConnection {
    /// <summary>RTA and Tone Match window: 4096 samples (85 ms at 48 kHz).</summary>
    const int AnalysisWindow = ToneMatchDesign.GridSize;

    /// <summary>The RTA's band resolutions, in bands per octave.</summary>
    public static readonly int[] RtaResolutions = [3, 6, 12];

    /// <summary>The RTA's windows, by their API names.</summary>
    public static readonly string[] RtaWindows = ["hann", "blackmanHarris", "flatTop", "rectangular"];

    volatile bool rtaEnabled;
    volatile RtaSettings rtaSettings = new(6, 0, SpectrumWindow.Hann);
    int rtaTick;
    long analyzerSeen;
    PowerSpectrum? rtaSpectrum;
    SpectrumWindow rtaSpectrumWindow;
    float[]? rtaWindow;
    float[]? rtaLevels;

    /// <summary>Whether WebSocket clients get RTA frames.</summary>
    public bool RtaEnabled {
        get => rtaEnabled;
        set => rtaEnabled = value;
    }

    /// <summary>Applies an RTA request: on or off, and its resolution, decay and window. Throws on invalid settings.</summary>
    public void SetRta(RtaRequest request) {
        var window = Array.IndexOf(RtaWindows, request.Window);
        if (!RtaResolutions.Contains(request.BandsPerOctave) || window < 0 || request.DecayMs is < 0 or > 10_000) {
            throw new RigException(
                400,
                "The RTA takes 3, 6 or 12 bands per octave, a decay of 0-10000 ms and a hann, blackmanHarris, flatTop or rectangular window."
            );
        }

        rtaSettings = new(request.BandsPerOctave, request.DecayMs, (SpectrumWindow)window);
        rtaEnabled = request.Enabled;
    }

    /// <summary>
    ///     Averages the output's power spectrum (<see cref="ToneMatchDesign.GridSize" /> points, silence skipped) for
    ///     <paramref name="duration" />. Returns the mean and the number of frames it holds.
    /// </summary>
    public async Task<(double[] Mean, int Frames, double SampleRate)> CaptureSpectrumAsync(
        TimeSpan duration,
        CancellationToken cancellationToken
    ) {
        var region = monitor?.Region ?? throw new RigException(503, "The engine's telemetry is not available.");
        var spectrum = new PowerSpectrum();
        var window = new float[AnalysisWindow];

        // Half-overlapping frames: a new one every half window.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(AnalysisWindow / 2 / region.SampleRate));
        var end = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < end && await timer.WaitForNextTickAsync(cancellationToken)) {
            if (region.ReadScope(window)) {
                spectrum.AddFrame(window);
            }
        }

        return (spectrum.Mean(), spectrum.Frames, region.SampleRate);
    }

    /// <summary>
    ///     Meter loop, every other tick: one RTA frame of the latest output, or of the RTA block's signal while one is
    ///     running (its ring keeps growing). Levels fall with the decay time and rise at once, like a meter.
    /// </summary>
    void PublishRta(TelemetryRegion region) {
        if (!rtaEnabled || ++rtaTick % 2 != 0) {
            return;
        }

        var settings = rtaSettings;
        if (rtaSpectrum is null || rtaSpectrumWindow != settings.Window) {
            rtaSpectrum = new(AnalysisWindow, settings.Window);
            rtaSpectrumWindow = settings.Window;
        }

        rtaWindow ??= new float[AnalysisWindow];
        var written = region.AnalyzerWritten;
        var fromBlock = written != analyzerSeen;
        analyzerSeen = written;
        if (!(fromBlock ? region.ReadAnalyzer(rtaWindow) : region.ReadScope(rtaWindow))) {
            return;
        }

        var centers = settings.Centers;
        rtaSpectrum.Clear();
        var levels = rtaSpectrum.AddFrame(rtaWindow)
            ? FractionalOctave.Bands(rtaSpectrum.Mean(), region.SampleRate, centers, settings.BandsPerOctave)
            : [..centers.Select(static _ => -200f)];

        // A full-scale sine: power N/4 in its bin (window-normalised), so shift by 10 log10(N/2) and the sine's 3 dB.
        var offset = (float)(10 * Math.Log10(AnalysisWindow / 2.0) - 10 * Math.Log10(2));
        for (var i = 0; i < levels.Length; i++) {
            levels[i] = Math.Max(-120f, levels[i] - offset);
        }

        // Decay: a level falls by 60 dB per decay time (about 30 frames a second, every other one published).
        if (settings.DecayMs > 0 && rtaLevels is { } previous && previous.Length == levels.Length) {
            var fall = (float)(60.0 * (2 * MeterInterval.TotalMilliseconds) / settings.DecayMs);
            for (var i = 0; i < levels.Length; i++) {
                levels[i] = Math.Max(levels[i], previous[i] - fall);
            }
        }

        rtaLevels = levels;
        hub.Publish(
            new("rta", settings.Frequencies, levels, fromBlock ? "block" : "output"),
            ContractsJsonContext.Default.RtaFrame
        );
    }

    sealed class RtaSettings(int bandsPerOctave, float decayMs, SpectrumWindow window) {
        public int BandsPerOctave { get; } = bandsPerOctave;

        public float DecayMs { get; } = decayMs;

        public SpectrumWindow Window { get; } = window;

        public double[] Centers { get; } = FractionalOctave.Centers(bandsPerOctave);

        public float[] Frequencies => [..Centers.Select(static c => (float)c)];
    }
}
