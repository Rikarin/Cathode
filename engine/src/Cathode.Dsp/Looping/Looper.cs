using System.Numerics;
using System.Runtime.InteropServices;

namespace Cathode.Dsp.Looping;

public enum LooperAction {
    Record,
    Play,
    Stop,
    Overdub,
    Undo,
    Reverse,
    HalfSpeed,
    Clear
}

public enum LooperState {
    Empty,
    Recording,
    Playing,
    Overdubbing,
    Stopped,

    /// <summary>Record was pressed with threshold recording on: the recording starts when the input crosses it.</summary>
    Armed
}

/// <summary>What pressing Record again while recording does.</summary>
public enum LooperSecondPress {
    Overdub,
    Play,
    Stop
}

/// <summary>The looper's Config page (docs/08-blocks-parity.md §6.14). <see cref="Default" /> is the rig looper's.</summary>
public readonly record struct LooperSettings {
    /// <summary>The longest recording in samples; 0 (or more than the capacity) uses the whole capacity.</summary>
    public int MaxSamples { get; init; }

    /// <summary>Playback stops at the end of the loop instead of repeating.</summary>
    public bool Once { get; init; }

    /// <summary>Where playback starts and ends, as fractions of the recorded loop.</summary>
    public float TrimStart { get; init; }

    public float TrimEnd { get; init; }

    /// <summary>Linear input level that starts an armed recording; 0 records at once.</summary>
    public float Threshold { get; init; }

    /// <summary>The gain every overdub pass applies to what the loop held (1 keeps it, 0.5 halves it per pass).</summary>
    public float OverdubLevel { get; init; }

    /// <summary>How long the performance after a recording closes is faded into the loop's start, in samples.</summary>
    public int CrossfadeSamples { get; init; }

    /// <summary>A beat in samples: a closing recording's length is rounded to whole beats. 0 leaves it.</summary>
    public double BeatSamples { get; init; }

    public LooperSecondPress SecondPress { get; init; }

    /// <summary>Playback speed before the half-speed toggle (0.5, 1 or 2).</summary>
    public double Speed { get; init; }

    public static LooperSettings Default =>
        new() { TrimEnd = 1f, OverdubLevel = 1f, SecondPress = LooperSecondPress.Play, Speed = 1 };
}

/// <summary>
///     A stereo looper: the rig's at the end of the chain (Cathode.Engine), or the <c>looper</c> grid block. Up to the
///     capacity given at construction.
/// </summary>
/// <remarks>
///     <para>
///         Playback is <c>base + layer</c>. Each overdub pass writes into <c>layer</c>; the first time a pass reaches a
///         sample it folds the previous pass into <c>base</c>, scales the result by the overdub level and marks the
///         sample. Undo then clears the marked samples of <c>layer</c> and divides the overdub level back out of
///         <c>base</c>, which removes exactly the latest pass with no copy of the loop on the audio thread.
///     </para>
///     <para>
///         The memory is zeroed lazily by the OS (<see cref="NativeMemory.AllocZeroed(nuint, nuint)" />), so 300 s of
///         capacity costs only what is recorded; a clear zeroes only the samples that were used. Playback fades in and
///         out over 2 ms on play and stop. The crossfade blends the playing that follows a closing recording into the
///         loop's first samples during its first pass, so the seam continues the performance instead of clicking.
///     </para>
/// </remarks>
public sealed unsafe class Looper : IDisposable {
    /// <summary>The rig looper's default capacity.</summary>
    public const double DefaultSeconds = 60;

    /// <summary>The longest loop the grid block offers (the Axe-Fx III's Max Loop Time).</summary>
    public const double MaxSeconds = 300;

    const double FadeMs = 2;

    readonly float* baseLeft;
    readonly float* baseRight;
    readonly float* layerLeft;
    readonly float* layerRight;
    readonly ulong* touched; // one bit per sample: reached by the current overdub pass
    readonly float fadeStep;
    LooperSettings settings = LooperSettings.Default;
    int extent; // samples [0, extent) may be non-zero
    int start;
    int end;
    bool layerUndoable;
    float passLevel = 1f;
    float fade;
    int seamLength;
    int seamPosition;

    public LooperState State { get; private set; } = LooperState.Empty;

    public bool IsReversed { get; private set; }

    public bool IsHalfSpeed { get; private set; }

    /// <summary>Loop length in samples (0 when empty).</summary>
    public int Length { get; private set; }

    public double Position { get; private set; }

    /// <summary>Capacity in samples.</summary>
    public int Capacity { get; }

    int MaxSamples => settings.MaxSamples > 0 ? Math.Min(settings.MaxSamples, Capacity) : Capacity;

    public Looper(double sampleRate, double seconds = DefaultSeconds) {
        Capacity = (int)(Math.Clamp(seconds, 1, MaxSeconds) * sampleRate);
        var bytes = (nuint)Capacity * sizeof(float);
        baseLeft = (float*)NativeMemory.AllocZeroed(bytes);
        baseRight = (float*)NativeMemory.AllocZeroed(bytes);
        layerLeft = (float*)NativeMemory.AllocZeroed(bytes);
        layerRight = (float*)NativeMemory.AllocZeroed(bytes);
        touched = (ulong*)NativeMemory.AllocZeroed((nuint)((Capacity + 63) / 64), sizeof(ulong));
        fadeStep = (float)(1.0 / Math.Max(1.0, FadeMs * 0.001 * sampleRate));
        UpdateWindow();
    }

    /// <summary>Audio thread, once per block: the Config settings.</summary>
    public void Configure(in LooperSettings next) {
        settings = next;
        UpdateWindow();
        if (State == LooperState.Recording && Length >= MaxSamples) {
            Close(LooperState.Playing);
        }
    }

    /// <summary>Audio thread.</summary>
    public void Apply(LooperAction action) {
        switch (action) {
            case LooperAction.Record:
                if (State == LooperState.Armed) {
                    State = LooperState.Empty;
                } else if (State is LooperState.Empty or LooperState.Stopped && Length == 0) {
                    Position = 0;
                    State = settings.Threshold > 0 ? LooperState.Armed : LooperState.Recording;
                } else if (State == LooperState.Recording) {
                    switch (settings.SecondPress) {
                        case LooperSecondPress.Overdub:
                            Close(LooperState.Overdubbing);
                            StartOverdub();
                            break;
                        case LooperSecondPress.Stop:
                            Close(LooperState.Stopped);
                            break;
                        default:
                            Close(LooperState.Playing);
                            break;
                    }
                } else if (Length > 0) {
                    StartOverdub();
                }

                break;
            case LooperAction.Play:
                if (State == LooperState.Recording) {
                    Close(LooperState.Playing);
                } else if (Length > 0) {
                    if (State == LooperState.Stopped) {
                        Position = IsReversed ? end - 1 : start;
                    }

                    State = LooperState.Playing;
                }

                break;
            case LooperAction.Overdub:
                if (State == LooperState.Recording) {
                    Close(LooperState.Overdubbing);
                    StartOverdub();
                } else if (State == LooperState.Overdubbing) {
                    State = LooperState.Playing;
                } else if (Length > 0) {
                    StartOverdub();
                }

                break;
            case LooperAction.Stop:
                if (State == LooperState.Recording) {
                    Close(LooperState.Stopped);
                } else if (State == LooperState.Armed) {
                    State = LooperState.Empty;
                } else if (Length > 0) {
                    State = LooperState.Stopped;
                }

                break;
            case LooperAction.Undo:
                if (layerUndoable) {
                    var restore = passLevel > 0 ? 1f / passLevel : 1f;
                    var words = (Length + 63) / 64;
                    for (var w = 0; w < words; w++) {
                        var bits = touched[w];
                        while (bits != 0) {
                            var i = w * 64 + BitOperations.TrailingZeroCount(bits);
                            layerLeft[i] = 0;
                            layerRight[i] = 0;
                            if (passLevel != 1f) {
                                baseLeft[i] *= restore;
                                baseRight[i] *= restore;
                            }

                            bits &= bits - 1;
                        }
                    }

                    layerUndoable = false;
                    if (State == LooperState.Overdubbing) {
                        State = LooperState.Playing;
                    }
                }

                break;
            case LooperAction.Reverse:
                IsReversed = !IsReversed;
                break;
            case LooperAction.HalfSpeed:
                IsHalfSpeed = !IsHalfSpeed;
                break;
            case LooperAction.Clear:
                Clear();
                break;
        }
    }

    /// <summary>Audio thread: empties the loop and returns to the initial transport (forward, full speed).</summary>
    public void Reset() {
        Clear();
        IsReversed = false;
        IsHalfSpeed = false;
    }

    /// <summary>
    ///     Audio thread, per sample: records from or overdubs <paramref name="left" />/<paramref name="right" /> and
    ///     returns the loop's playback.
    /// </summary>
    public (float Left, float Right) Tick(float left, float right) {
        switch (State) {
            case LooperState.Empty:
                return (0f, 0f);
            case LooperState.Armed:
                if (MathF.Max(MathF.Abs(left), MathF.Abs(right)) < settings.Threshold) {
                    return (0f, 0f);
                }

                State = LooperState.Recording;
                return Record(left, right);
            case LooperState.Recording:
                return Record(left, right);
            case LooperState.Stopped when fade <= 0f:
                return (0f, 0f);
        }

        var playing = State != LooperState.Stopped;
        fade = playing ? MathF.Min(1f, fade + fadeStep) : MathF.Max(0f, fade - fadeStep);
        var index = (int)Position;
        var span = end - start;
        float playLeft, playRight;
        if (seamLength > 0 && (IsReversed || IsHalfSpeed || settings.Speed != 1 || index != start + seamPosition)) {
            seamLength = 0; // the seam is only blended on a forward first pass at normal speed
        }

        if (seamLength > 0) {
            // The loop's start fades in under the live playing, which is stored over it with the opposite fade.
            var w = (float)seamPosition / seamLength;
            playLeft = baseLeft[index] * w;
            playRight = baseRight[index] * w;
            baseLeft[index] = playLeft + left * (1f - w);
            baseRight[index] = playRight + right * (1f - w);
            if (++seamPosition >= seamLength) {
                seamLength = 0;
            }
        } else {
            var next = IsReversed ? index - 1 < start ? end - 1 : index - 1 : index + 1 >= end ? start : index + 1;
            var frac = IsReversed ? 0f : (float)(Position - index);
            playLeft = Sample(baseLeft, layerLeft, index, next, frac);
            playRight = Sample(baseRight, layerRight, index, next, frac);
            if (State == LooperState.Overdubbing) {
                Fold(index);
                layerLeft[index] += left;
                layerRight[index] += right;
            }
        }

        var output = (playLeft * fade, playRight * fade);
        Position += (IsReversed ? -1.0 : 1.0) * settings.Speed * (IsHalfSpeed ? 0.5 : 1.0);
        if (Position >= end || Position < start) {
            var wrapped = (Position - start) % span;
            Position = start + (wrapped < 0 ? wrapped + span : wrapped);
            if (settings.Once && State == LooperState.Playing) {
                State = LooperState.Stopped;
                fade = 0f;
                Position = IsReversed ? end - 1 : start;
            }
        }

        return output;
    }

    /// <summary>Audio thread: the rig's use, recording from and adding the playback into the signal in place.</summary>
    public void Process(Span<float> left, Span<float> right) {
        if (State == LooperState.Empty) {
            return;
        }

        for (var i = 0; i < left.Length; i++) {
            var (playLeft, playRight) = Tick(left[i], right[i]);
            left[i] += playLeft;
            right[i] += playRight;
        }
    }

    /// <summary>Control thread: the loop as it plays (base + layer), for export. Read it while stopped for a clean copy.</summary>
    public (float[] Left, float[] Right) Snapshot() {
        var left = new float[Length];
        var right = new float[Length];
        for (var i = 0; i < Length; i++) {
            left[i] = baseLeft[i] + layerLeft[i];
            right[i] = baseRight[i] + layerRight[i];
        }

        return (left, right);
    }

    public void Dispose() {
        NativeMemory.Free(baseLeft);
        NativeMemory.Free(baseRight);
        NativeMemory.Free(layerLeft);
        NativeMemory.Free(layerRight);
        NativeMemory.Free(touched);
    }

    (float Left, float Right) Record(float left, float right) {
        if (Length < MaxSamples) {
            baseLeft[Length] = left;
            baseRight[Length] = right;
            Length++;
            extent = Math.Max(extent, Length);
        }

        if (Length >= MaxSamples) {
            Close(LooperState.Playing);
        }

        return (0f, 0f);
    }

    void Clear() {
        var bytes = (nuint)extent * sizeof(float);
        NativeMemory.Clear(baseLeft, bytes);
        NativeMemory.Clear(baseRight, bytes);
        NativeMemory.Clear(layerLeft, bytes);
        NativeMemory.Clear(layerRight, bytes);
        NativeMemory.Clear(touched, (nuint)((extent + 63) / 64) * sizeof(ulong));
        extent = 0;
        Length = 0;
        Position = 0;
        fade = 0f;
        seamLength = 0;
        layerUndoable = false;
        State = LooperState.Empty;
        UpdateWindow();
    }

    void Close(LooperState next) {
        if (settings.BeatSamples > 0 && Length > 0) {
            // Loop Quantize: the nearest whole number of beats that fits; the samples past the recording are silent.
            var beats = Math.Max(1, (int)Math.Round(Length / settings.BeatSamples));
            while (beats > 1 && (int)Math.Round(beats * settings.BeatSamples) > MaxSamples) {
                beats--;
            }

            Length = Math.Clamp((int)Math.Round(beats * settings.BeatSamples), 1, MaxSamples);
            extent = Math.Max(extent, Length);
        }

        State = Length > 0 ? next : LooperState.Empty;
        UpdateWindow();
        Position = IsReversed && Length > 0 ? end - 1 : start;
        fade = next == LooperState.Stopped ? 0f : 1f;
        seamLength = start == 0 ? Math.Min(settings.CrossfadeSamples, Length / 2) : 0;
        seamPosition = 0;
    }

    void UpdateWindow() {
        if (Length == 0) {
            start = 0;
            end = 1;
            return;
        }

        var a = Math.Clamp(settings.TrimStart, 0f, 1f);
        var b = Math.Clamp(settings.TrimEnd, 0f, 1f);
        start = Math.Min((int)MathF.Round(Math.Min(a, b) * Length), Length - 1);
        end = Math.Clamp((int)MathF.Round(Math.Max(a, b) * Length), start + 1, Length);
    }

    void StartOverdub() {
        NativeMemory.Clear(touched, (nuint)((Length + 63) / 64) * sizeof(ulong));
        layerUndoable = true;
        passLevel = settings.OverdubLevel;
        State = LooperState.Overdubbing;
    }

    /// <summary>
    ///     The first time this pass reaches a sample, the previous pass moves from the layer into the base, and the two
    ///     take the pass's overdub level.
    /// </summary>
    void Fold(int index) {
        ref var word = ref touched[index >> 6];
        var bit = 1UL << (index & 63);
        if ((word & bit) != 0) {
            return;
        }

        word |= bit;
        baseLeft[index] = (baseLeft[index] + layerLeft[index]) * passLevel;
        baseRight[index] = (baseRight[index] + layerRight[index]) * passLevel;
        layerLeft[index] = 0;
        layerRight[index] = 0;
    }

    static float Sample(float* baseline, float* layer, int index, int next, float frac) {
        var a = baseline[index] + layer[index];
        if (frac == 0) {
            return a;
        }

        var b = baseline[next] + layer[next];
        return a + (b - a) * frac;
    }
}
