namespace Cathode.Dsp.Filters;

/// <summary>
///     One stereo EQ band on a state-variable filter. A band at 0 dB that isn't moving is skipped entirely (unless it
///     is a high- or low-pass, which have no gain), so a flat EQ is bit-transparent and costs nothing.
/// </summary>
sealed class EqBand {
    SvfState left;
    SvfState right;

    public void Reset() {
        left.Reset();
        right.Reset();
    }

    public void Process(
        in StereoBuffer io,
        BiquadType type,
        double sampleRate,
        float frequency,
        float q,
        float gainDb,
        bool moving
    ) {
        if (gainDb == 0 && !moving && type is not (BiquadType.HighPass or BiquadType.LowPass)) {
            Reset(); // so a band that is switched back in starts clean
            return;
        }

        var c = SvfCoefficients.Design(type, sampleRate, frequency, q, gainDb);
        left.Process(c, io.Left);
        right.Process(c, io.Right);
    }
}
