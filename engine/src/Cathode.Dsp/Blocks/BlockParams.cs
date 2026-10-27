using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>Parameters that many blocks declare alike.</summary>
static class BlockParams {
    /// <summary>The output level, applied by the block's <see cref="OutputStage" />.</summary>
    public static ParamDescriptor Level { get; } =
        ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB");

    /// <summary>The output balance, applied by the block's <see cref="OutputStage" />.</summary>
    public static ParamDescriptor Balance { get; } = Pan("balance", "Balance", 0f);

    /// <summary>The wet/dry mix, 0 to 100 %.</summary>
    public static ParamDescriptor Mix(float defaultPercent) =>
        ParamDescriptor.Float("mix", "Mix", 0f, 100f, defaultPercent, "%");

    /// <summary>A level in dB, from silence up to <paramref name="maxDb" />, 0 dB by default.</summary>
    public static ParamDescriptor Gain(string id, string name, float maxDb) =>
        ParamDescriptor.Float(id, name, Decibels.SilenceDb, maxDb, 0f, "dB");

    /// <summary>A pan or balance, -100 % (left) to 100 % (right).</summary>
    public static ParamDescriptor Pan(string id, string name, float defaultPercent) =>
        ParamDescriptor.Float(id, name, -100f, 100f, defaultPercent, "%");

    /// <summary>
    ///     A peaking band's frequency, Q (0.1 to 10) and gain (±12 dB): <paramref name="id" /> followed by
    ///     <c>Frequency</c>, <c>Q</c> and <c>Gain</c>, named <paramref name="name" /> followed by Freq, Q and Gain.
    /// </summary>
    public static ParamDescriptor[] PeakingBand(string id, string name, float min, float max, float hz, float q) => [
        ParamDescriptor.Float(id + "Frequency", name + " Freq", min, max, hz, "Hz", ParamTaper.Log),
        ParamDescriptor.Float(id + "Q", name + " Q", 0.1f, 10f, q, string.Empty, ParamTaper.Log),
        ParamDescriptor.Float(id + "Gain", name + " Gain", -12f, 12f, 0f, "dB")
    ];

    /// <summary>
    ///     The controls of a <see cref="Reverb.Diffuser" /> on the wet signal: its mix, time (up to
    ///     <paramref name="maxMs" />), and its LFO's rate and depth.
    /// </summary>
    public static ParamDescriptor[] Diffusion(float maxMs) => [
        ParamDescriptor.Float("diffusionMix", "Diffusion Mix", 0f, 100f, 0f, "%"),
        ParamDescriptor.Float("diffusionTime", "Diffusion Time", 1f, maxMs, 30f, "ms", ParamTaper.Log),
        ParamDescriptor.Float("diffusionRate", "Diffusion LFO Rate", 0.05f, 5f, 0.4f, "Hz", ParamTaper.Log),
        ParamDescriptor.Float("diffusionDepth", "Diffusion LFO Depth", 0f, 100f, 0f, "%")
    ];
}
