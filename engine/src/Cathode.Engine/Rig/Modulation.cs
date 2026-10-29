using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;

namespace Cathode.Engine.Rig;

/// <summary>
///     The controllers (docs/02-architecture.md §5.4), evaluated at control rate: once per sub-block of at most
///     <c>MaxBlockSize</c> frames, on the audio thread, from the <see cref="RigControls" /> parameters. Every source
///     reads 0..1. Synced LFOs and the sequencer lock their phase to the sample clock, so they stay in time with the
///     metronome and tempo-synced delays across preset changes.
/// </summary>
public sealed class Modulation(ParamBank rig, double sampleRate) {
    const double HysteresisDb = 3;

    readonly float[] values = new float[Enum.GetValues<ModSource>().Length];
    readonly Adsr adsr1 = new(1);
    readonly Adsr adsr2 = new(2);
    readonly int[] lfoRate = [Index("lfo1Rate"), Index("lfo2Rate")];
    readonly int[] lfoShape = [Index("lfo1Shape"), Index("lfo2Shape")];
    readonly int[] lfoTempo = [Index("lfo1Tempo"), Index("lfo2Tempo")];
    readonly Random random = new(1);
    readonly int level = Index("level");
    readonly int tempo = Index("tempo");
    readonly int envAttack = Index("envAttack");
    readonly int envRelease = Index("envRelease");
    readonly int envGain = Index("envGain");
    readonly int seqStages = Index("seqStages");
    readonly int seqRate = Index("seqRate");
    readonly int seqTempo = Index("seqTempo");
    readonly int seq1 = Index("seq1");
    readonly int pitch = Index("pitch");
    readonly int manualA = Index("manualA");
    readonly int scene1 = Index("scene1");
    readonly int ext1 = Index("ext1");
    double lfo1Phase;
    double lfo2Phase;
    double sequencerPhase;
    float lfo1Held;
    float lfo2Held;
    float follower; // linear peak of the input, before sensitivity

    /// <summary>Scene level as a linear gain (smoothed by its parameter).</summary>
    public float LevelGain { get; private set; } = 1f;

    public double Tempo => rig.Current(tempo);

    /// <summary>The sequencer stage playing, from 0, as of the last <see cref="Advance" />.</summary>
    public int SequencerStage { get; private set; }

    /// <summary>How many stages the sequencer steps through (<c>seqStages</c>), as of the last Advance.</summary>
    public int SequencerStages { get; private set; }

    /// <summary>The current value of a source, 0..1.</summary>
    public float Value(ModSource source) => values[(int)source];

    /// <summary>Audio thread: advance every controller over the next <paramref name="input" />.Length frames.</summary>
    public void Advance(ReadOnlySpan<float> input, long sampleTime) {
        var frames = input.Length;
        rig.BeginBlock();
        LevelGain = MathF.Pow(10f, rig.Advance(level, frames) / 20f);
        var bpm = rig.Advance(tempo, frames);
        var seconds = frames / sampleRate;

        // Envelope follower: a peak detector with separate attack and release.
        var attack = Coefficient(rig.Advance(envAttack, frames));
        var release = Coefficient(rig.Advance(envRelease, frames));
        var follower = this.follower;
        foreach (var x in input) {
            var magnitude = Math.Abs(x);
            follower += (magnitude - follower) * (magnitude > follower ? attack : release);
        }

        this.follower = follower < 1e-9f ? 0f : follower;
        var gain = MathF.Pow(10f, rig.Advance(envGain, frames) / 20f);
        values[(int)ModSource.Envelope] = Math.Min(1f, this.follower * gain);

        // ADSRs, gated by the input level crossing their threshold.
        var levelDb = 20 * Math.Log10(Math.Max(this.follower, 1e-6));
        values[(int)ModSource.Adsr1] = adsr1.Advance(rig, levelDb, seconds);
        values[(int)ModSource.Adsr2] = adsr2.Advance(rig, levelDb, seconds);

        values[(int)ModSource.Lfo1] = Lfo(1, ref lfo1Phase, ref lfo1Held, bpm, sampleTime, frames);
        values[(int)ModSource.Lfo2] = Lfo(2, ref lfo2Phase, ref lfo2Held, bpm, sampleTime, frames);

        // Sequencer: steps through its stages at the rate (or one stage per tempo division).
        var stages = (int)Math.Round(rig.Advance(seqStages, frames));
        var division = (int)rig.Current(seqTempo);
        if (division > 0) {
            var stageBeats = RigControls.DivisionBeats[division];
            sequencerPhase = sampleTime / sampleRate * (bpm / 60) / (stageBeats * stages) % 1;
        } else {
            sequencerPhase = (sequencerPhase + rig.Advance(seqRate, frames) * seconds / stages) % 1;
        }

        var stage = Math.Min(stages - 1, (int)(sequencerPhase * stages));
        values[(int)ModSource.Sequencer] = rig.Advance(seq1 + stage, frames);
        SequencerStage = stage;
        SequencerStages = stages;

        values[(int)ModSource.Pitch] = rig.Advance(pitch, frames);
        for (var i = 0; i < 4; i++) {
            values[(int)ModSource.ManualA + i] = rig.Advance(manualA + i, frames);
            values[(int)ModSource.Scene1 + i] = rig.Advance(scene1 + i, frames);
        }

        for (var i = 0; i < 8; i++) {
            values[(int)ModSource.Ext1 + i] = rig.Advance(ext1 + i, frames);
        }
    }

    /// <summary>The shaped, scaled value a modifier gives its parameter (before slew), in parameter units.</summary>
    public static float Map(float source, ModCurve curve, float min, float max, bool log) {
        var s = Math.Clamp(source, 0f, 1f);
        s = curve switch {
            ModCurve.Exponential => (MathF.Exp(3f * s) - 1f) / (MathF.Exp(3f) - 1f),
            ModCurve.Logarithmic => MathF.Log(1f + 19f * s) / MathF.Log(20f),
            ModCurve.SCurve => s * s * (3f - 2f * s),
            _ => s
        };
        return log && min > 0 && max > 0 ? min * MathF.Pow(max / min, s) : min + (max - min) * s;
    }

    float Lfo(int n, ref double phase, ref float held, double bpm, long sampleTime, int frames) {
        var rateIndex = lfoRate[n - 1];
        var shape = (int)rig.Current(lfoShape[n - 1]);
        var division = (int)rig.Current(lfoTempo[n - 1]);
        var previous = phase;
        if (division > 0) {
            phase = sampleTime / sampleRate * (bpm / 60) / RigControls.DivisionBeats[division] % 1;
        } else {
            phase = (phase + rig.Advance(rateIndex, frames) * frames / sampleRate) % 1;
        }

        if (phase < previous) {
            held = (float)random.NextDouble(); // a new cycle: the random shape picks its next level
        }

        var p = (float)phase;
        return shape switch {
            1 => p < 0.5f ? 2f * p : 2f - 2f * p,
            2 => p < 0.5f ? 1f : 0f,
            3 => p,
            4 => 1f - p,
            5 => held,
            > 5 => 0.5f + 0.5f * (float)Dsp.Modulation.Lfo.Shape((LfoShape)shape, p),
            _ => 0.5f + 0.5f * MathF.Sin(2f * MathF.PI * p)
        };
    }

    float Coefficient(float milliseconds) =>
        (float)(1 - Math.Exp(-1.0 / Math.Max(1e-6, milliseconds * 0.001 * sampleRate)));

    static int Index(string id) => RigControls.Descriptor.IndexOf(id);

    /// <summary>Attack, decay, sustain, release, triggered while the input is above the threshold.</summary>
    sealed class Adsr(int n) {
        readonly int thresholdIndex = Index($"adsr{n}Threshold");
        readonly int attackIndex = Index($"adsr{n}Attack");
        readonly int decayIndex = Index($"adsr{n}Decay");
        readonly int sustainIndex = Index($"adsr{n}Sustain");
        readonly int releaseIndex = Index($"adsr{n}Release");
        int stage; // 0 idle, 1 attack, 2 decay/sustain, 3 release
        double level;

        public float Advance(ParamBank rig, double inputDb, double seconds) {
            var threshold = rig.Current(thresholdIndex);
            var attack = rig.Current(attackIndex) * 0.001;
            var decay = rig.Current(decayIndex) * 0.001;
            var sustain = rig.Current(sustainIndex);
            var release = rig.Current(releaseIndex) * 0.001;
            var gate = stage is 1 or 2 ? inputDb > threshold - HysteresisDb : inputDb > threshold;
            if (gate && stage is 0 or 3) {
                stage = 1;
            } else if (!gate && stage is 1 or 2) {
                stage = 3;
            }

            switch (stage) {
                case 1:
                    level += seconds / attack;
                    if (level >= 1) {
                        level = 1;
                        stage = 2;
                    }

                    break;
                case 2:
                    level = Math.Max(sustain, level - seconds / decay * (1 - sustain));
                    break;
                case 3:
                    level -= seconds / release;
                    if (level <= 0) {
                        level = 0;
                        stage = 0;
                    }

                    break;
            }

            return (float)level;
        }
    }
}
