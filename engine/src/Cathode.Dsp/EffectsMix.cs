using Cathode.Dsp.Parameters;

namespace Cathode.Dsp;

/// <summary>
///     Global Mix (docs/08-blocks-parity.md §3): a block whose Global Mix switch is on adds the rig's Effects Mix
///     offset (<see cref="BlockContext.MixOffset" />, a global setting) to its own Mix, so one control makes every wet
///     effect wetter or drier for a room.
/// </summary>
public static class EffectsMix {
    /// <summary>The per-block switch.</summary>
    public static ParamDescriptor Param { get; } = ParamDescriptor.Switch("globalMix", "Global Mix");

    /// <summary>
    ///     The Mix (0..100 %) a block uses: its own, plus the offset when <paramref name="globalMix" /> is on.
    /// </summary>
    public static float Apply(float mix, float globalMix, in BlockContext context) =>
        globalMix >= 0.5f && context.MixOffset != 0f ? Math.Clamp(mix + context.MixOffset, 0f, 100f) : mix;
}
