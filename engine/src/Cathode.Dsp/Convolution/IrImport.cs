using Cathode.Dsp.Cabinets;
using Cathode.Dsp.IO;
using System.Numerics;

namespace Cathode.Dsp.Convolution;

/// <summary>How an IR file is processed on import (<see cref="IrImport" />).</summary>
public enum IrProcessing {
    /// <summary>Kept as it is.</summary>
    AsIs,

    /// <summary>The leading silence (before the first sample within 60 dB of the peak) removed.</summary>
    AutoTrim,

    /// <summary>Made minimum phase: the same magnitude response with all its energy as early as it can be.</summary>
    MinimumPhase
}

/// <summary>Which channels of a stereo IR file are kept on import.</summary>
public enum IrChannels {
    Both,
    Left,
    Right,
    Sum
}

/// <summary>Import settings for an IR file.</summary>
/// <param name="Processing">As-is, auto-trim or minimum phase.</param>
/// <param name="Polarity">Invert the IR if its main peak is negative, so it is positive.</param>
/// <param name="Channels">For a stereo file: both channels, one of them, or their average.</param>
/// <param name="MaxLengthMs">Cut longer IRs to this length with a short fade; null keeps the whole IR.</param>
public readonly record struct IrImportSettings(
    IrProcessing Processing = IrProcessing.AsIs,
    bool Polarity = false,
    IrChannels Channels = IrChannels.Both,
    double? MaxLengthMs = null
) {
    public bool IsDefault => this == default;
}

/// <summary>
///     Prepares an impulse response file for the asset store (Cab-Lab's import options): choose the channels, trim the
///     leading silence or make the IR minimum phase, make its main peak positive, and cut it to a maximum length.
/// </summary>
/// <remarks>
///     Minimum phase is by the real cepstrum (Oppenheim and Schafer, Discrete-Time Signal Processing, §12, as
///     <see cref="CabIrSynthesizer" /> does it): the log magnitude's cepstrum is folded onto its causal half and taken
///     back, on an FFT four times the IR (at least 8,192) so the cepstrum's aliasing stays negligible. The magnitude is
///     floored 140 dB below its peak so the logarithm stays finite. A stereo IR's channels are trimmed by the same
///     amount, and inverted together, so the image between them is kept.
/// </remarks>
public static class IrImport {
    /// <summary>Leading samples quieter than this, relative to the peak, are trimmed: −60 dB.</summary>
    public const float TrimThreshold = 1e-3f;

    const int FadeLength = 64;
    const double FloorDb = -140;

    public static AudioData Process(AudioData audio, in IrImportSettings settings) {
        var channels = Select(audio.Channels, settings.Channels);
        switch (settings.Processing) {
            case IrProcessing.AutoTrim: {
                var start = Onset(channels);
                channels = [..channels.Select(c => c[start..])];
                break;
            }
            case IrProcessing.MinimumPhase:
                channels = [..channels.Select(MinimumPhase)];
                break;
            case IrProcessing.AsIs:
                // The response is kept as recorded.
                break;
        }

        if (settings.Polarity && MainPeak(channels) < 0) {
            foreach (var channel in channels) {
                for (var i = 0; i < channel.Length; i++) {
                    channel[i] = -channel[i];
                }
            }
        }

        if (settings.MaxLengthMs is { } ms) {
            var length = Math.Max(FadeLength, (int)Math.Round(ms / 1_000 * audio.SampleRate));
            channels = [..channels.Select(c => Truncate(c, length))];
        }

        return audio with { Channels = channels };
    }

    /// <summary>The first sample of any channel within 60 dB of the IR's peak.</summary>
    public static int Onset(IReadOnlyList<float[]> channels) {
        var peak = channels.Max(static c => c.Length == 0 ? 0f : c.Max(MathF.Abs));
        var threshold = peak * TrimThreshold;
        var onset = int.MaxValue;
        foreach (var channel in channels) {
            var i = Array.FindIndex(channel, v => MathF.Abs(v) >= threshold);
            if (i >= 0) {
                onset = Math.Min(onset, i);
            }
        }

        return onset == int.MaxValue ? 0 : onset;
    }

    /// <summary>The value of the largest sample by magnitude, over every channel (its sign is the IR's polarity).</summary>
    public static float MainPeak(IReadOnlyList<float[]> channels) {
        var peak = 0f;
        foreach (var channel in channels) {
            foreach (var v in channel) {
                if (MathF.Abs(v) > MathF.Abs(peak)) {
                    peak = v;
                }
            }
        }

        return peak;
    }

    /// <summary>The minimum-phase IR with the magnitude response of <paramref name="ir" />, as long as it.</summary>
    public static float[] MinimumPhase(float[] ir) {
        if (ir.Length < 2) {
            return [..ir];
        }

        var n = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(8_192, 4 * ir.Length));
        var half = n / 2;
        using var fft = new NativeFft(n);
        using var reBuffer = new NativeBuffer(n);
        using var imBuffer = new NativeBuffer(n);
        var re = reBuffer.Span;
        var im = imBuffer.Span;
        ir.CopyTo(re);
        fft.Forward(re, im);

        var peak = 0.0;
        for (var k = 0; k <= half; k++) {
            peak = Math.Max(peak, Math.Sqrt((double)re[k] * re[k] + (double)im[k] * im[k]));
        }

        if (peak == 0) {
            return new float[ir.Length];
        }

        var floor = peak * Math.Pow(10, FloorDb / 20);
        for (var k = 0; k <= half; k++) {
            var magnitude = Math.Sqrt((double)re[k] * re[k] + (double)im[k] * im[k]);
            re[k] = (float)Math.Log(Math.Max(magnitude, floor));
        }

        // Real cepstrum of the log magnitude, folded onto its causal half, back to a log spectrum and exponentiated.
        for (var k = 1; k < half; k++) {
            re[n - k] = re[k];
        }

        im.Clear();
        fft.Inverse(re, im);
        for (var i = 1; i < half; i++) {
            re[i] *= 2f;
            re[n - i] = 0f;
        }

        im.Clear();
        fft.Forward(re, im);
        for (var k = 0; k <= half; k++) {
            var magnitude = Math.Exp(re[k]);
            double phase = im[k];
            re[k] = (float)(magnitude * Math.Cos(phase));
            im[k] = (float)(magnitude * Math.Sin(phase));
        }

        im[0] = 0f;
        im[half] = 0f;
        for (var k = 1; k < half; k++) {
            re[n - k] = re[k];
            im[n - k] = -im[k];
        }

        fft.Inverse(re, im);
        return [..re[..ir.Length]];
    }

    static float[][] Select(float[][] channels, IrChannels choice) {
        if (channels.Length < 2) {
            return [..channels.Select(static c => c.ToArray())];
        }

        return choice switch {
            IrChannels.Left => [channels[0].ToArray()],
            IrChannels.Right => [channels[1].ToArray()],
            IrChannels.Sum => [channels[0].Select((v, i) => 0.5f * (v + channels[1][i])).ToArray()],
            _ => [channels[0].ToArray(), channels[1].ToArray()]
        };
    }

    static float[] Truncate(float[] ir, int length) {
        if (ir.Length <= length) {
            return ir;
        }

        var cut = ir[..length];
        for (var i = 0; i < FadeLength; i++) {
            cut[length - FadeLength + i] *= 0.5f * (1f + MathF.Cos(MathF.PI * (i + 1) / FadeLength));
        }

        return cut;
    }
}
