namespace Cathode.Dsp;

/// <summary>
///     A setting read once per block (a delay time, say) that glides there sample by sample from where the last block
///     left it, so a parameter moved by a pedal, a modifier or a scene sweeps instead of stepping once per callback.
///     The first block after <see cref="Reset" /> starts at its setting; a setting that holds is exact.
/// </summary>
public struct SampleGlide {
    float from;
    float target;
    float step;
    bool primed;

    /// <summary>The next <see cref="Begin" /> starts at its setting instead of gliding to it.</summary>
    public void Reset() => primed = false;

    /// <summary>
    ///     Audio thread: glides from the last block's setting to <paramref name="setting" /> over the next
    ///     <paramref name="frames" />.
    /// </summary>
    public void Begin(float setting, int frames) {
        from = primed ? target : setting;
        primed = true;
        target = setting;
        step = frames > 0 ? (setting - from) / frames : 0f;
    }

    /// <summary>
    ///     The value at sample <paramref name="index" /> of the block: the setting itself at the last
    ///     sample.
    /// </summary>
    public readonly float At(int index) => step == 0f ? target : from + step * (index + 1);
}
