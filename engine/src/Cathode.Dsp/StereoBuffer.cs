namespace Cathode.Dsp;

/// <summary>A pair of equally long channel spans. Blocks process these in place.</summary>
public readonly ref struct StereoBuffer
{
    public StereoBuffer(Span<float> left, Span<float> right)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException("Left and right channels must have the same length.", nameof(right));
        }

        Left = left;
        Right = right;
    }

    public Span<float> Left { get; }

    public Span<float> Right { get; }

    public int Length => Left.Length;

    public StereoBuffer Slice(int start, int length) => new(Left.Slice(start, length), Right.Slice(start, length));
}
