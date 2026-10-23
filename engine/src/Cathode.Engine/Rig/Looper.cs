using System.Runtime.InteropServices;
using Cathode.Dsp;

namespace Cathode.Engine.Rig;

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
    Stopped
}

/// <summary>
///     A stereo looper at the end of the rig (after the scene level, before the global EQ), so a loop keeps playing
///     through preset and scene changes. Up to <see cref="MaxSeconds" />.
/// </summary>
/// <remarks>
///     Playback is <c>base + layer</c>. Each overdub pass writes into <c>layer</c>; the first time a pass reaches a
///     sample it folds the previous pass into <c>base</c> and marks the sample. Undo then clears the marked samples of
///     <c>layer</c>, which removes exactly the latest pass with no copy of the loop on the audio thread.
/// </remarks>
public sealed unsafe class Looper : IDisposable {
    public const double MaxSeconds = 60;

    readonly NativeBuffer baseLeft;
    readonly NativeBuffer baseRight;
    readonly NativeBuffer layerLeft;
    readonly NativeBuffer layerRight;
    readonly ulong* touched; // one bit per sample: reached by the current overdub pass
    readonly int capacity;
    readonly int words;
    int length;
    double position;
    bool reverse;
    bool half;
    bool layerUndoable;

    public LooperState State { get; private set; } = LooperState.Empty;

    public bool IsReversed => reverse;

    public bool IsHalfSpeed => half;

    /// <summary>Loop length in samples (0 when empty).</summary>
    public int Length => length;

    public double Position => position;

    public Looper(double sampleRate, double seconds = MaxSeconds) {
        capacity = (int)(Math.Clamp(seconds, 1, MaxSeconds) * sampleRate);
        baseLeft = new(capacity);
        baseRight = new(capacity);
        layerLeft = new(capacity);
        layerRight = new(capacity);
        words = (capacity + 63) / 64;
        touched = (ulong*)NativeMemory.AllocZeroed((nuint)words, sizeof(ulong));
    }

    /// <summary>Audio thread.</summary>
    public void Apply(LooperAction action) {
        switch (action) {
            case LooperAction.Record:
                if (State is LooperState.Empty or LooperState.Stopped && length == 0) {
                    position = 0;
                    State = LooperState.Recording;
                } else if (State == LooperState.Recording) {
                    Close(LooperState.Playing);
                } else if (length > 0) {
                    StartOverdub();
                }

                break;
            case LooperAction.Play:
                if (State == LooperState.Recording) {
                    Close(LooperState.Playing);
                } else if (length > 0) {
                    if (State == LooperState.Stopped) {
                        position = reverse ? length - 1 : 0;
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
                } else if (length > 0) {
                    StartOverdub();
                }

                break;
            case LooperAction.Stop:
                if (State == LooperState.Recording) {
                    Close(LooperState.Stopped);
                } else if (length > 0) {
                    State = LooperState.Stopped;
                }

                break;
            case LooperAction.Undo:
                if (layerUndoable) {
                    for (var w = 0; w < words; w++) {
                        var bits = touched[w];
                        while (bits != 0) {
                            var i = w * 64 + System.Numerics.BitOperations.TrailingZeroCount(bits);
                            layerLeft[i] = 0;
                            layerRight[i] = 0;
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
                reverse = !reverse;
                break;
            case LooperAction.HalfSpeed:
                half = !half;
                break;
            case LooperAction.Clear:
                baseLeft.Clear();
                baseRight.Clear();
                layerLeft.Clear();
                layerRight.Clear();
                new Span<ulong>(touched, words).Clear();
                length = 0;
                position = 0;
                layerUndoable = false;
                State = LooperState.Empty;
                break;
        }
    }

    /// <summary>Audio thread: records from and plays into <paramref name="left" />/<paramref name="right" /> in place.</summary>
    public void Process(Span<float> left, Span<float> right) {
        switch (State) {
            case LooperState.Recording:
                for (var i = 0; i < left.Length && length < capacity; i++) {
                    baseLeft[length] = left[i];
                    baseRight[length] = right[i];
                    length++;
                }

                if (length >= capacity) {
                    Close(LooperState.Playing);
                }

                return;
            case LooperState.Playing or LooperState.Overdubbing:
                var step = (reverse ? -1.0 : 1.0) * (half ? 0.5 : 1.0);
                for (var i = 0; i < left.Length; i++) {
                    var index = (int)position;
                    var next = reverse ? (index - 1 + length) % length : (index + 1) % length;
                    var frac = (float)(position - index);
                    if (reverse) {
                        frac = 0;
                    }

                    var playLeft = Sample(baseLeft, layerLeft, index, next, frac);
                    var playRight = Sample(baseRight, layerRight, index, next, frac);
                    if (State == LooperState.Overdubbing) {
                        Fold(index);
                        layerLeft[index] += left[i];
                        layerRight[index] += right[i];
                    }

                    left[i] += playLeft;
                    right[i] += playRight;
                    position += step;
                    if (position >= length) {
                        position -= length;
                    } else if (position < 0) {
                        position += length;
                    }
                }

                return;
        }
    }

    /// <summary>Control thread: the loop as it plays (base + layer), for export. Read it while stopped for a clean copy.</summary>
    public (float[] Left, float[] Right) Snapshot() {
        var left = new float[length];
        var right = new float[length];
        for (var i = 0; i < length; i++) {
            left[i] = baseLeft[i] + layerLeft[i];
            right[i] = baseRight[i] + layerRight[i];
        }

        return (left, right);
    }

    public void Dispose() {
        baseLeft.Dispose();
        baseRight.Dispose();
        layerLeft.Dispose();
        layerRight.Dispose();
        NativeMemory.Free(touched);
    }

    void Close(LooperState next) {
        State = length > 0 ? next : LooperState.Empty;
        position = reverse && length > 0 ? length - 1 : 0;
    }

    void StartOverdub() {
        new Span<ulong>(touched, words).Clear();
        layerUndoable = true;
        State = LooperState.Overdubbing;
    }

    /// <summary>The first time this pass reaches a sample, the previous pass moves from the layer into the base.</summary>
    void Fold(int index) {
        ref var word = ref touched[index >> 6];
        var bit = 1UL << (index & 63);
        if ((word & bit) != 0) {
            return;
        }

        word |= bit;
        baseLeft[index] += layerLeft[index];
        baseRight[index] += layerRight[index];
        layerLeft[index] = 0;
        layerRight[index] = 0;
    }

    static float Sample(NativeBuffer baseline, NativeBuffer layer, int index, int next, float frac) {
        var a = baseline[index] + layer[index];
        if (frac == 0) {
            return a;
        }

        var b = baseline[next] + layer[next];
        return a + (b - a) * frac;
    }
}
