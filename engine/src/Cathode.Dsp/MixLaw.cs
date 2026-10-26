using Cathode.Dsp.Parameters;

namespace Cathode.Dsp;

/// <summary>
///     How a block's Mix knob (0..1) sets its dry and wet gains. Every law gives exactly all dry at 0 and all wet at 1;
///     they differ in the middle (docs/08-blocks-parity.md §3, Mix laws).
/// </summary>
public enum MixLaw {
    /// <summary>A plain crossfade: dry 1 − mix, wet mix (both −6 dB at 50 %).</summary>
    Linear,

    /// <summary>Constant power: dry cos, wet sin of mix · 90° (both −3 dB at 50 %), so the sum keeps its loudness.</summary>
    ConstantPower,

    /// <summary>
    ///     The delay law: the dry stays at 0 dB up to 50 % and then falls to nothing at 100 %; the wet rises on a squared
    ///     taper, reaching 0 dB at 50 % (−12 dB at 25 %), so most of the knob's travel sits at quiet echo levels.
    /// </summary>
    Sweetened,

    /// <summary>Both at 0 dB at 50 %: each side is at full level up to the middle, then the other fades it out.</summary>
    DryUnity
}

/// <summary>The gains of a <see cref="MixLaw" />, and a per-sample ramp between two mix settings.</summary>
public static class MixLaws {
    /// <summary>
    ///     Kill Dry: mutes a block's dry signal, so Mix sets only the wet level (for a parallel path that already carries
    ///     the dry). The Global Mix offset still moves that Mix.
    /// </summary>
    public static ParamDescriptor KillDryParam { get; } = ParamDescriptor.Switch("killDry", "Kill Dry");

    /// <summary>Dry and wet gains for <paramref name="mix" /> (0..1). <paramref name="killDry" /> mutes the dry.</summary>
    public static (float Dry, float Wet) Gains(MixLaw law, float mix, bool killDry = false) {
        var (dry, wet) = mix <= 0f ? (1f, 0f)
        : mix >= 1f ? (0f, 1f)
        : law switch {
            MixLaw.ConstantPower => (MathF.Cos(mix * MathF.PI / 2f), MathF.Sin(mix * MathF.PI / 2f)),
            MixLaw.Sweetened => (Math.Min(1f, 2f * (1f - mix)), Math.Min(1f, 4f * mix * mix)),
            MixLaw.DryUnity => (Math.Min(1f, 2f * (1f - mix)), Math.Min(1f, 2f * mix)),
            _ => (1f - mix, mix)
        };
        return (killDry ? 0f : dry, wet);
    }
}

/// <summary>
///     The dry and wet gains of a block moving from one mix setting to another across a block of samples: both are
///     interpolated linearly between the law's gains at each end, so a steady knob gives the law's exact gains.
/// </summary>
public struct MixRamp {
    readonly float dryStep;
    readonly float wetStep;

    /// <summary>The gains of the current sample; call <see cref="Advance" /> first for each sample.</summary>
    public float Dry { get; private set; }

    public float Wet { get; private set; }

    public MixRamp(MixLaw law, float from, float to, int frames, bool killDry = false) {
        (Dry, Wet) = MixLaws.Gains(law, from, killDry);
        var (dryEnd, wetEnd) = MixLaws.Gains(law, to, killDry);
        dryStep = frames > 0 ? (dryEnd - Dry) / frames : 0f;
        wetStep = frames > 0 ? (wetEnd - Wet) / frames : 0f;
        if (frames <= 0) {
            (Dry, Wet) = (dryEnd, wetEnd);
        }
    }

    public void Advance() {
        Dry += dryStep;
        Wet += wetStep;
    }

    /// <summary>
    ///     <c>dry · Dry + wet · Wet</c> into <paramref name="wet" /> for a whole buffer, ramping as it goes: the span form
    ///     of a block's last step.
    /// </summary>
    public static void Blend(
        ReadOnlySpan<float> dry,
        Span<float> wet,
        MixLaw law,
        float from,
        float to,
        bool killDry = false
    ) {
        var ramp = new MixRamp(law, from, to, wet.Length, killDry);
        for (var i = 0; i < wet.Length; i++) {
            ramp.Advance();
            wet[i] = dry[i] * ramp.Dry + wet[i] * ramp.Wet;
        }
    }
}
