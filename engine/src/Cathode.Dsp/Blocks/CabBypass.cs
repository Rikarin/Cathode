namespace Cathode.Dsp.Blocks;

/// <summary>
///     The crossfade of a cab-modelling block (the Cab, the IR Player) over the callback where the global cab modelling
///     (<see cref="BlockContext.CabsOff" />) changes: between the processed signal and the dry copy taken before it.
/// </summary>
static class CabBypass {
    /// <summary>
    ///     Fades the processed <paramref name="io" /> toward the dry copy when <paramref name="off" />, else away
    ///     from it.
    /// </summary>
    public static void Crossfade(
        in StereoBuffer io,
        ReadOnlySpan<float> dryLeft,
        ReadOnlySpan<float> dryRight,
        bool off
    ) {
        var step = 1f / io.Length;
        for (var i = 0; i < io.Length; i++) {
            var wet = off ? 1f - (i + 1) * step : (i + 1) * step;
            io.Left[i] = dryLeft[i] + (io.Left[i] - dryLeft[i]) * wet;
            io.Right[i] = dryRight[i] + (io.Right[i] - dryRight[i]) * wet;
        }
    }
}
