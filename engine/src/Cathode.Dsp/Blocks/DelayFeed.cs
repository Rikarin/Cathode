using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Blocks;

/// <summary>What the multi-line delays (the Multi Delay, the Plex) share around their lines.</summary>
static class DelayFeed {
    /// <summary>
    ///     The per-sample steps of the first <paramref name="count" /> lines, each from its
    ///     <paramref name="current" /> delay to its <paramref name="target" /> over a callback of
    ///     <paramref name="frames" />, so time changes glide.
    /// </summary>
    public static void Glide(
        Span<float> step,
        ReadOnlySpan<float> target,
        ReadOnlySpan<float> current,
        int count,
        int frames
    ) {
        for (var i = 0; i < count; i++) {
            step[i] = (target[i] - current[i]) / frames;
        }
    }

    /// <summary>
    ///     What enters the lines for one sample: the selected input (<see cref="Stereo.Select" />) at the input gain,
    ///     swelled by the <paramref name="envelope" /> when it is on.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Input(
        ref SwellEnvelope envelope,
        int select,
        float dryLeft,
        float dryRight,
        float inputGain
    ) {
        var x = Stereo.Select(select, dryLeft, dryRight) * inputGain;
        if (envelope.Active) {
            x *= envelope.Tick(Math.Max(MathF.Abs(dryLeft), MathF.Abs(dryRight)));
        }

        return x;
    }
}
