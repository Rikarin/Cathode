namespace Cathode.Dsp.Filters;

/// <summary>
///     One stereo EQ band on a state-variable filter. A band at 0 dB that isn't moving is skipped entirely, so a flat
///     EQ is bit-transparent and costs nothing.
/// </summary>
internal sealed class EqBand {
    SvfState _left;
    SvfState _right;

    public void Reset() {
        _left.Reset();
        _right.Reset();
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
        if (gainDb == 0 && !moving) {
            Reset(); // so a band that is switched back in starts clean
            return;
        }

        var c = SvfCoefficients.Design(type, sampleRate, frequency, q, gainDb);
        _left.Process(c, io.Left);
        _right.Process(c, io.Right);
    }
}
