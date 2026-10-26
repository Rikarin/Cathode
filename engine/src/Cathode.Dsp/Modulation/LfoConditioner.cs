using Cathode.Dsp.Parameters;
using System.Collections.Immutable;

namespace Cathode.Dsp.Modulation;

/// <summary>
///     What the modulation blocks do to an LFO after its waveform (docs/08-blocks-parity.md, Flanger, Phaser and
///     Filter): <b>Quantize</b> steps it into a number of levels, and <b>Hicut</b> rounds it with a one-pole low-pass,
///     which also softens the steps. Off (no quantize, Hicut at <see cref="HicutOff" />) passes the value through
///     untouched. One instance conditions a left and a right LFO.
/// </summary>
public struct LfoConditioner {
    public const float HicutOff = 50f;

    /// <summary>The options of a Quantize parameter; index 0 is off.</summary>
    public static readonly ImmutableArray<string> QuantizeOptions = ["Off", "2", "3", "4", "6", "8", "12", "16"];

    /// <summary>The options of an Auto Depth parameter.</summary>
    public static readonly ImmutableArray<string> AutoDepthOptions = ["Off", "Low", "High"];

    static readonly int[] LevelCounts = [0, 2, 3, 4, 6, 8, 12, 16];

    float coefficient;
    int levels;
    float left;
    float right;
    bool primed;

    /// <summary>True when the conditioner passes values through unchanged.</summary>
    public readonly bool IsOff => coefficient >= 1f && levels == 0;

    /// <summary>An LFO Hicut parameter from 0.5 Hz to <see cref="HicutOff" /> (off).</summary>
    public static ParamDescriptor HicutParam(string id = "lfoHicut") =>
        ParamDescriptor.Float(id, "LFO Hicut", 0.5f, HicutOff, HicutOff, "Hz", ParamTaper.Log);

    public static ParamDescriptor QuantizeParam(string id = "lfoQuantize") =>
        ParamDescriptor.Choice(id, "LFO Quantize", QuantizeOptions);

    public static ParamDescriptor AutoDepthParam(string id = "autoDepth") =>
        ParamDescriptor.Choice(id, "Auto Depth", AutoDepthOptions);

    /// <summary>
    ///     The depth scale for Auto Depth <paramref name="mode" /> at <paramref name="rateHz" />: 1 up to 1 Hz, then
    ///     falling as one over the square root of the rate (Low) or one over the rate (High), so the pitch wobble a swept
    ///     delay makes, which grows with depth times rate, stops growing as the rate rises.
    /// </summary>
    public static float AutoDepth(int mode, double rateHz) =>
        mode switch {
            1 when rateHz > 1 => (float)(1 / Math.Sqrt(rateHz)),
            2 when rateHz > 1 => (float)(1 / rateHz),
            _ => 1f
        };

    /// <summary>The number of levels of Quantize option <paramref name="option" /> (0 when off).</summary>
    public static int Levels(int option) => LevelCounts[Math.Clamp(option, 0, LevelCounts.Length - 1)];

    /// <summary>Steps a bipolar value into <paramref name="levels" /> evenly spaced levels from −1 to 1.</summary>
    public static float Quantize(float value, int levels) {
        if (levels < 2) {
            return value;
        }

        var steps = levels - 1;
        return MathF.Round(0.5f * (Math.Clamp(value, -1f, 1f) + 1) * steps) / steps * 2 - 1;
    }

    public void Reset() {
        left = 0;
        right = 0;
        primed = false;
    }

    /// <summary>Once per block: the hicut (Hz) and the Quantize option.</summary>
    public void Design(double sampleRate, float hicut, int quantize) {
        coefficient = hicut >= HicutOff ? 1f : 1f - MathF.Exp(-2f * MathF.PI * hicut / (float)sampleRate);
        levels = Levels(quantize);
    }

    /// <summary>Conditions one sample of the left and right LFOs.</summary>
    public void Tick(ref float leftValue, ref float rightValue) {
        if (levels > 0) {
            leftValue = Quantize(leftValue, levels);
            rightValue = Quantize(rightValue, levels);
        }

        if (!primed) {
            // Start on the waveform rather than gliding up from zero.
            (left, right, primed) = (leftValue, rightValue, true);
        }

        if (coefficient < 1f) {
            left += coefficient * (leftValue - left);
            right += coefficient * (rightValue - right);
            (leftValue, rightValue) = (left, right);
        } else {
            (left, right) = (leftValue, rightValue);
        }
    }
}
