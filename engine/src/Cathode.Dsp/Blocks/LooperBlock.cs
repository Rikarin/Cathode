using Cathode.Dsp.Filters;
using Cathode.Dsp.Looping;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     The looper as a grid block, so it can sit anywhere in the chain (before the delays and reverbs, say) and take the
///     bypass modes. Its transport is the engine's looper commands (REST, MIDI, footswitches), which go to this block
///     while its preset is running instead of the rig looper.
/// </summary>
/// <remarks>
///     The Config page: Max Loop Time (up to 300 s; the memory is committed only as it is recorded), Once, Trim Start and
///     End, threshold recording, Playback and Dry Level, Overdub Level (each pass scales what the loop held), the seam
///     Crossfade, Loop Quantize (the length rounded to whole beats of the tempo), what a second Record press does, Speed,
///     and Low and High Cut on the playback. A preset change to another preset with a Looper block hands the loop over
///     (<see cref="TakeLoop" />), so it keeps playing; to a preset without one, the loop stops and is dropped.
/// </remarks>
public sealed class LooperBlock() : DspBlock(Info), ILooperBlock {
    public const int MaxTime = 0;
    public const int Once = 1;
    public const int TrimStart = 2;
    public const int TrimEnd = 3;
    public const int Threshold = 4;
    public const int ThresholdLevel = 5;
    public const int PlaybackLevel = 6;
    public const int DryLevel = 7;
    public const int OverdubLevel = 8;
    public const int Crossfade = 9;
    public const int Quantize = 10;
    public const int SecondPress = 11;
    public const int Speed = 12;
    public const int LowCut = 13;
    public const int HighCut = 14;
    public const int Level = 15;
    public const int Balance = 16;

    static readonly double[] Speeds = [1, 0.5, 2];

    Looper? looper;
    WetCuts cuts;
    float playGain;
    float dryGain;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "looper",
        "Looper",
        "Utility",
        [
            new(
                "maxTime",
                "Max Loop Time",
                ParamKind.Float,
                1f,
                (float)Looper.MaxSeconds,
                60f,
                "s",
                Smoothing: ParamSmoothing.Step
            ),
            ParamDescriptor.Switch("once", "Once"),
            ParamDescriptor.Float("trimStart", "Trim Start", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("trimEnd", "Trim End", 0f, 100f, 100f, "%"),
            ParamDescriptor.Switch("threshold", "Threshold Record"),
            ParamDescriptor.Float("thresholdLevel", "Threshold Level", -60f, 0f, -40f, "dB"),
            ParamDescriptor.Float("playbackLevel", "Playback Level", Decibels.SilenceDb, 12f, 0f, "dB"),
            ParamDescriptor.Float("dryLevel", "Dry Level", Decibels.SilenceDb, 12f, 0f, "dB"),
            ParamDescriptor.Float("overdubLevel", "Overdub Level", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("crossfade", "Crossfade", 0f, 100f, 10f, "ms"),
            ParamDescriptor.Switch("quantize", "Loop Quantize"),
            ParamDescriptor.Choice("secondPress", "Record 2nd Press", ["Overdub", "Play", "Stop"], 1),
            ParamDescriptor.Choice("speed", "Speed", ["Normal", "Half", "Double"]),
            WetCuts.LowCutParam("lowCut", "Low Cut"),
            WetCuts.HighCutParam("highCut", "High Cut"),
            BlockParams.Level,
            BlockParams.Balance
        ],
        maxInstances: 1,
        channels: 2,
        pages: [
            new(
                "Basic",
                [
                    "playbackLevel", "dryLevel", "overdubLevel", "speed", "threshold", "thresholdLevel", "once",
                    "lowCut", "highCut"
                ]
            ),
            new("Advanced", ["maxTime", "trimStart", "trimEnd", "crossfade", "quantize", "secondPress"])
        ]
    );

    public Looper? Looper => Volatile.Read(ref looper);

    public bool TakeLoop(ILooperBlock outgoing) {
        if (outgoing is not LooperBlock other || other.looper is null || looper is null) {
            return false;
        }

        (looper, other.looper) = (other.looper, looper);
        return true;
    }

    /// <summary>The Config page as the looper takes it, at <paramref name="tempo" /> BPM (0: no quantising).</summary>
    LooperSettings Settings(double tempo) {
        var rate = Spec.SampleRate;
        return new() {
            MaxSamples = (int)(Params.Current(MaxTime) * rate),
            Once = Params.Current(Once) >= 0.5f,
            TrimStart = Params.Current(TrimStart) / 100f,
            TrimEnd = Params.Current(TrimEnd) / 100f,
            Threshold = Params.Current(Threshold) >= 0.5f ? Decibels.ToGain(Params.Current(ThresholdLevel)) : 0f,
            OverdubLevel = Params.Current(OverdubLevel) / 100f,
            CrossfadeSamples = (int)(Params.Current(Crossfade) * 0.001 * rate),
            BeatSamples = Params.Current(Quantize) >= 0.5f && tempo > 0 ? 60.0 * rate / tempo : 0,
            SecondPress = (LooperSecondPress)(int)Params.Current(SecondPress),
            Speed = Speeds[Math.Clamp((int)Params.Current(Speed), 0, Speeds.Length - 1)]
        };
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        looper?.Dispose();
        Volatile.Write(ref looper, new(spec.SampleRate, Looper.MaxSeconds));
    }

    protected override void OnReset() {
        looper?.Reset();
        looper?.Configure(Settings(0));
        cuts.Reset();
        playGain = Decibels.ToGain(Params.Current(PlaybackLevel));
        dryGain = Decibels.ToGain(Params.Current(DryLevel));
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            Interlocked.Exchange(ref looper, null)?.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var n = io.Length;
        var rate = Spec.SampleRate;
        foreach (var index in (ReadOnlySpan<int>)[TrimStart, TrimEnd, ThresholdLevel, OverdubLevel, Crossfade]) {
            Params.Advance(index, n);
        }

        var settings = Settings(context.Tempo);
        cuts.Design(rate, Params.Advance(LowCut, n), Params.Advance(HighCut, n));
        var playEnd = Decibels.ToGain(Params.Advance(PlaybackLevel, n));
        var dryEnd = Decibels.ToGain(Params.Advance(DryLevel, n));
        var loop = looper!;
        loop.Configure(settings);
        if (loop.State == LooperState.Empty) {
            // Nothing to play or record: only the dry level applies.
            GainRamp.Apply(io.Left, dryGain, dryEnd);
            GainRamp.Apply(io.Right, dryGain, dryEnd);
        } else {
            var play = playGain;
            var dry = dryGain;
            var playStep = (playEnd - playGain) / n;
            var dryStep = (dryEnd - dryGain) / n;
            for (var i = 0; i < n; i++) {
                play += playStep;
                dry += dryStep;
                var (left, right) = loop.Tick(io.Left[i], io.Right[i]);
                cuts.Tick(ref left, ref right);
                io.Left[i] = io.Left[i] * dry + left * play;
                io.Right[i] = io.Right[i] * dry + right * play;
            }
        }

        playGain = playEnd;
        dryGain = dryEnd;
        cuts.FlushDenormals();
        output.Apply(io, Params.Advance(Level, n), Params.Advance(Balance, n));
    }
}
