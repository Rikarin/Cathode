using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>Volume/Pan: level in dB plus a balance control. Gain is interpolated per sample, so changes never click.</summary>
public sealed class VolumePanBlock : DspBlock {
    public const int Level = 0;
    public const int Balance = 1;

    public static BlockDescriptor Info { get; } = new(
        "volpan",
        "Volume/Pan",
        "Utility",
        [
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%")
        ]
    );

    float _gainLeft;
    float _gainRight;

    public VolumePanBlock()
        : base(Info) { }

    protected override void OnReset() => (_gainLeft, _gainRight) = TargetGains();

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        Params.Advance(Level, io.Length);
        Params.Advance(Balance, io.Length);
        var (endLeft, endRight) = TargetGains();
        GainRamp.Apply(io.Left, _gainLeft, endLeft);
        GainRamp.Apply(io.Right, _gainRight, endRight);
        _gainLeft = endLeft;
        _gainRight = endRight;
    }

    (float Left, float Right) TargetGains() {
        var gain = Decibels.ToGain(Params.Current(Level));
        var balance = Params.Current(Balance) / 100f;
        return (gain * Math.Min(1f, 1f - balance), gain * Math.Min(1f, 1f + balance));
    }
}
