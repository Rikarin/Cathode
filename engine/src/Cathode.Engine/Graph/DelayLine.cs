namespace Cathode.Engine.Graph;

/// <summary>Fixed integer stereo delay used for latency compensation between parallel paths.</summary>
internal sealed class DelayLine {
    private readonly float[] _left;
    private readonly float[] _right;
    private int _position;

    public DelayLine(int samples) {
        Samples = samples;
        _left = new float[samples];
        _right = new float[samples];
    }

    public int Samples { get; }

    public void Process(Span<float> left, Span<float> right) {
        var position = _position;
        for (var i = 0; i < left.Length; i++) {
            (left[i], _left[position]) = (_left[position], left[i]);
            (right[i], _right[position]) = (_right[position], right[i]);
            if (++position == Samples) {
                position = 0;
            }
        }

        _position = position;
    }

    public void Reset() {
        Array.Clear(_left);
        Array.Clear(_right);
        _position = 0;
    }
}
