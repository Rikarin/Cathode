using Cathode.Contracts;
using Cathode.Dsp.Spectrum;
using Cathode.Engine.Telemetry;
using Cathode.Server.Rig;

namespace Cathode.Server.Engine;

/// <summary>Spectrum analysis of the engine's output, read from the telemetry region's scope ring.</summary>
public sealed partial class EngineConnection {
    /// <summary>RTA and Tone Match window: 4096 samples (85 ms at 48 kHz).</summary>
    const int AnalysisWindow = ToneMatchDesign.GridSize;

    /// <summary>RTA bands per octave.</summary>
    const int RtaResolution = 6;

    static readonly double[] RtaCenters = FractionalOctave.Centers(RtaResolution);
    static readonly float[] RtaFrequencies = [..RtaCenters.Select(static c => (float)c)];

    volatile bool rtaEnabled;
    int rtaTick;
    PowerSpectrum? rtaSpectrum;
    float[]? rtaWindow;

    /// <summary>Whether WebSocket clients get RTA frames.</summary>
    public bool RtaEnabled {
        get => rtaEnabled;
        set => rtaEnabled = value;
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
        var spectrum = new PowerSpectrum(AnalysisWindow);
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

    /// <summary>Meter loop, every other tick: one RTA frame of the latest output.</summary>
    void PublishRta(TelemetryRegion region) {
        if (!rtaEnabled || ++rtaTick % 2 != 0) {
            return;
        }

        rtaSpectrum ??= new(AnalysisWindow);
        rtaWindow ??= new float[AnalysisWindow];
        if (!region.ReadScope(rtaWindow)) {
            return;
        }

        rtaSpectrum.Clear();
        var levels = rtaSpectrum.AddFrame(rtaWindow)
            ? FractionalOctave.Bands(rtaSpectrum.Mean(), region.SampleRate, RtaCenters, RtaResolution)
            : [..RtaCenters.Select(static _ => -200f)];

        // A full-scale sine: power N/4 in its bin (Hann-normalised), so shift by 10 log10(N/2) and the sine's 3 dB.
        var offset = (float)(10 * Math.Log10(AnalysisWindow / 2.0) - 10 * Math.Log10(2));
        for (var i = 0; i < levels.Length; i++) {
            levels[i] = Math.Max(-120f, levels[i] - offset);
        }

        hub.Publish(new RtaFrame("rta", RtaFrequencies, levels), ContractsJsonContext.Default.RtaFrame);
    }
}
