using Cathode.Dsp.Parameters;
using System.Collections.Immutable;

namespace Cathode.Dsp;

/// <summary>Small stereo helpers shared by the blocks' common parameters.</summary>
public static class Stereo {
    /// <summary>The options of an Input Select parameter: how a mono effect hears a stereo input.</summary>
    public static readonly ImmutableArray<string> InputSelectOptions = ["L+R", "Left", "Right"];

    /// <summary>The mono signal an effect hears for Input Select <paramref name="select" />.</summary>
    public static float Select(int select, float left, float right) =>
        select switch {
            1 => left,
            2 => right,
            _ => 0.5f * (left + right)
        };

    /// <summary>
    ///     Places the left input at <paramref name="panLeft" /> and the right at <paramref name="panRight" /> (−1..1,
    ///     each with the balance law: the far side is turned down). −1 and 1 leave a pair untouched; both at 0 make it
    ///     mono.
    /// </summary>
    public static void Pan(in StereoBuffer io, float panLeft, float panRight) {
        if (panLeft == -1f && panRight == 1f) {
            return;
        }

        var (leftToLeft, leftToRight) = (Math.Min(1f, 1f - panLeft), Math.Min(1f, 1f + panLeft));
        var (rightToLeft, rightToRight) = (Math.Min(1f, 1f - panRight), Math.Min(1f, 1f + panRight));
        for (var i = 0; i < io.Length; i++) {
            var left = io.Left[i];
            var right = io.Right[i];
            io.Left[i] = left * leftToLeft + right * rightToLeft;
            io.Right[i] = left * leftToRight + right * rightToRight;
        }
    }

    /// <summary>Phase-inverts the left, right or both sides (Phase Reverse 1, 2 or 3; 0 leaves both).</summary>
    public static void Invert(in StereoBuffer io, int phaseReverse) {
        if (phaseReverse is 1 or 3) {
            for (var i = 0; i < io.Length; i++) {
                io.Left[i] = -io.Left[i];
            }
        }

        if (phaseReverse is 2 or 3) {
            for (var i = 0; i < io.Length; i++) {
                io.Right[i] = -io.Right[i];
            }
        }
    }

    /// <summary>The Stereo Spread parameter (−200..200 %, default 100 %: the pair as it is).</summary>
    public static ParamDescriptor SpreadParam(string id = "stereoSpread", string name = "Stereo Spread") =>
        ParamDescriptor.Float(id, name, -200f, 200f, 100f, "%");

    /// <summary>
    ///     Stereo Spread on a wet pair, as a gain on its side (difference) signal with the mid kept: 1 leaves it as it
    ///     is, 0 folds it to mono, −1 swaps the sides, and beyond ±1 (up to ±2, the guide's ±200 %) the image widens
    ///     past the speakers; values between blend.
    /// </summary>
    public static void Spread(ref float left, ref float right, float spread) {
        if (spread == 1f) {
            return;
        }

        var (l, r) = (left, right);
        left = 0.5f * ((1 + spread) * l + (1 - spread) * r);
        right = 0.5f * ((1 + spread) * r + (1 - spread) * l);
    }
}
