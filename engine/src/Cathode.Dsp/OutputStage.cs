namespace Cathode.Dsp;

/// <summary>
///     The common Level and Balance at a block's output (docs/08-blocks-parity.md, "Common mix and level"): balance
///     turns the opposite side down, so the centre is unity on both, and both act on wet and dry alike. Gains are
///     interpolated per sample, so changes never click.
/// </summary>
public struct OutputStage {
    float left;
    float right;

    /// <summary>
    ///     Jumps to the gains for <paramref name="levelDb" /> and <paramref name="balance" /> (−100..100 %).
    /// </summary>
    public void Reset(float levelDb, float balance) => (left, right) = Gains(levelDb, balance);

    /// <summary>Applies the gains, ramping from the previous block's to these across <paramref name="io" />.</summary>
    public void Apply(in StereoBuffer io, float levelDb, float balance) {
        var (endLeft, endRight) = Gains(levelDb, balance);
        GainRamp.Apply(io.Left, left, endLeft);
        GainRamp.Apply(io.Right, right, endRight);
        left = endLeft;
        right = endRight;
    }

    /// <summary>Left and right gains: the level, with the side away from the balance turned down linearly.</summary>
    public static (float Left, float Right) Gains(float levelDb, float balance) {
        var gain = Decibels.ToGain(levelDb);
        var b = Math.Clamp(balance / 100f, -1f, 1f);
        return (gain * Math.Min(1f, 1f - b), gain * Math.Min(1f, 1f + b));
    }
}
