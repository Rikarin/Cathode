using Cathode.Dsp.Routing;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     The side buffer of a block whose detector can listen to another grid row or to a hardware input instead of its
///     own input (the Pitch block's tracker, the Vocoder's analysis): the chosen row's cabled input is collected here
///     instead of being mixed into the block's input, and a chosen hardware input is read into it.
/// </summary>
sealed class SideBuffer {
    int frames;

    public float[] Left { get; private set; } = [];

    public float[] Right { get; private set; } = [];

    public void Allocate(int maxBlockSize) {
        Left = new float[maxBlockSize];
        Right = new float[maxBlockSize];
    }

    public void Reset() => frames = 0;

    /// <summary>Whether it holds a whole callback of <paramref name="length" /> frames.</summary>
    public bool Holds(int length) => frames == length;

    /// <summary>
    ///     Hardware input <paramref name="source" /> (<see cref="HardwareInputs" />) into the buffer, as a chosen row
    ///     would arrive there; silence without the graph's inputs (<paramref name="port" /> null).
    /// </summary>
    public void Load(InputPort? port, int source, int length) {
        if (length > Left.Length) {
            return;
        }

        if (port is not null) {
            port.Read(source, Left.AsSpan(0, length), Right.AsSpan(0, length));
        } else {
            Array.Clear(Left, 0, length);
            Array.Clear(Right, 0, length);
        }

        frames = length;
    }

    /// <summary>
    ///     A cabled input: collected here when it comes from the <paramref name="chosen" /> row, otherwise added into
    ///     <paramref name="destination" /> as any other block's input would be.
    /// </summary>
    public void Mix(bool chosen, in StereoBuffer source, in StereoBuffer destination) {
        if (!chosen || source.Length > Left.Length) {
            for (var i = 0; i < source.Length; i++) {
                destination.Left[i] += source.Left[i];
                destination.Right[i] += source.Right[i];
            }

            return;
        }

        if (frames != source.Length) {
            Array.Clear(Left, 0, source.Length);
            Array.Clear(Right, 0, source.Length);
            frames = source.Length;
        }

        for (var i = 0; i < source.Length; i++) {
            Left[i] += source.Left[i];
            Right[i] += source.Right[i];
        }
    }

    /// <summary>
    ///     After the callback: clears the frames used when it <paramref name="held" /> them, ready for the next.
    /// </summary>
    public void Consume(bool held, int length) {
        if (held) {
            Array.Clear(Left, 0, length);
            Array.Clear(Right, 0, length);
        }

        frames = 0;
    }
}
