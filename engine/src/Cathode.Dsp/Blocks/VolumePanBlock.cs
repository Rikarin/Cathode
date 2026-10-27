using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Volume/Pan: a volume pedal (Volume, through a choice of pot tapers), an output Level and Balance, input select
///     and per-side panning. Auto-Swell fades each note in from silence over Attack once the input crosses Threshold,
///     like a guitarist rolling the volume knob up after picking. Gains are interpolated per sample, so changes never
///     click, and at the default settings the block is bit-transparent.
/// </summary>
public sealed class VolumePanBlock() : DspBlock(Info) {
    public const int Level = 0;
    public const int Balance = 1;
    public const int Volume = 2;
    public const int Taper = 3;
    public const int Type = 4;
    public const int Threshold = 5;
    public const int Attack = 6;
    public const int InputSelect = 7;
    public const int PanLeft = 8;
    public const int PanRight = 9;

    public const int VolumeType = 0;
    public const int AutoSwell = 1;

    /// <summary>Hysteresis below Threshold that re-arms the swell for the next note.</summary>
    const float RearmRatio = 0.5f;

    const double EnvelopeReleaseMs = 30;

    /// <summary>
    ///     The pot tapers: linear, then audio tapers named by their resistance at half rotation, then an S curve.
    /// </summary>
    static readonly string[] Tapers = ["Linear", "Log 30A", "Log 20A", "Log 15A", "Log 10A", "Log 5A", "S-Taper"];

    static readonly float[] TaperMidpoints = [0.5f, 0.3f, 0.2f, 0.15f, 0.1f, 0.05f];

    OutputStage output;
    float volumeGain = 1f;
    float swell = 1f;
    float envelope;
    float envelopeRelease;
    bool open;

    public static BlockDescriptor Info { get; } = new(
        "volpan",
        "Volume/Pan",
        "Utility",
        [
            BlockParams.Level,
            BlockParams.Balance,
            ParamDescriptor.Float("volume", "Volume", 0f, 100f, 100f, "%"),
            ParamDescriptor.Choice("taper", "Taper", Tapers),
            ParamDescriptor.Choice("type", "Type", ["Volume", "Auto-Swell"]),
            ParamDescriptor.Float("threshold", "Threshold", -80f, 0f, -40f, "dB"),
            ParamDescriptor.Float("attack", "Attack", 10f, 2_000f, 300f, "ms", ParamTaper.Log),
            ParamDescriptor.Choice("inputSelect", "Input Select", ["Stereo", "Left Only", "Right Only"]),
            ParamDescriptor.Float("panLeft", "Pan Left", -100f, 100f, -100f, "%"),
            ParamDescriptor.Float("panRight", "Pan Right", -100f, 100f, 100f, "%")
        ]
    );

    /// <summary>
    ///     The gain of a volume pedal at <paramref name="volume" /> (0..1) through <paramref name="taper" />.
    /// </summary>
    public static float TaperGain(int taper, float volume) {
        var v = Math.Clamp(volume, 0f, 1f);
        if (taper >= TaperMidpoints.Length) {
            return v * v * (3 - 2 * v); // S-taper
        }

        if (taper <= 0) {
            return v;
        }

        // An audio taper b^v, offset to start at zero and scaled to reach one, passing the pot's midpoint at half
        // rotation.
        var root = 1 / TaperMidpoints[taper] - 1;
        var b = root * root;
        return (MathF.Pow(b, v) - 1) / (b - 1);
    }

    static void SelectInput(in StereoBuffer io, int select) {
        if (select == 1) {
            io.Left.CopyTo(io.Right);
        } else if (select == 2) {
            io.Right.CopyTo(io.Left);
        }
    }

    /// <summary>
    ///     Auto-Swell: a peak envelope of the input opens the swell when it crosses <paramref name="threshold" />,
    ///     which then rises from zero to one in <paramref name="attackMs" />; it re-arms once the envelope falls 6 dB
    ///     below.
    /// </summary>
    void Swell(in StereoBuffer io, float threshold, float attackMs) {
        var step = (float)(1 / Math.Max(1, attackMs * 0.001 * Spec.SampleRate));
        for (var i = 0; i < io.Length; i++) {
            var peak = Math.Max(MathF.Abs(io.Left[i]), MathF.Abs(io.Right[i]));
            envelope = peak > envelope ? peak : envelope * envelopeRelease;
            if (!open && envelope > threshold) {
                open = true;
                swell = 0;
            } else if (open && envelope < threshold * RearmRatio) {
                open = false;
            }

            if (open && swell < 1f) {
                swell = Math.Min(1f, swell + step);
            }

            var gain = swell * swell * (3 - 2 * swell); // an S-shaped fade, like a hand on the knob
            io.Left[i] *= gain;
            io.Right[i] *= gain;
        }

        if (envelope < 1e-20f) {
            envelope = 0;
        }
    }

    protected override void OnPrepare(in ProcessSpec spec) =>
        envelopeRelease = (float)Math.Exp(-1 / (EnvelopeReleaseMs * 0.001 * spec.SampleRate));

    protected override void OnReset() {
        output.Reset(Params.Current(Level), Params.Current(Balance));
        volumeGain = TaperGain((int)Params.Current(Taper), Params.Current(Volume) / 100f);
        swell = (int)Params.Current(Type) == AutoSwell ? 0f : 1f;
        envelope = 0;
        open = false;
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var n = io.Length;
        SelectInput(io, (int)Params.Current(InputSelect));
        Stereo.Pan(io, Params.Advance(PanLeft, n) / 100f, Params.Advance(PanRight, n) / 100f);

        if ((int)Params.Current(Type) == AutoSwell) {
            Swell(io, Decibels.ToGain(Params.Advance(Threshold, n)), Params.Advance(Attack, n));
        } else {
            swell = 1f;
        }

        var volumeGain = TaperGain((int)Params.Current(Taper), Params.Advance(Volume, n) / 100f);
        GainRamp.Apply(io.Left, this.volumeGain, volumeGain);
        GainRamp.Apply(io.Right, this.volumeGain, volumeGain);
        this.volumeGain = volumeGain;
        output.Apply(io, Params.Advance(Level, n), Params.Advance(Balance, n));
    }
}
