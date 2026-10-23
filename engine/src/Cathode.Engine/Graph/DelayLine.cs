namespace Cathode.Engine.Graph;

/// <summary>Fixed integer stereo delay used for latency compensation between parallel paths.</summary>
sealed class DelayLine {
    readonly float[] left;
    readonly float[] right;
    int position;

    public int Samples { get; }

    public DelayLine(int samples) {
        Samples = samples;
        left = new float[samples];
        right = new float[samples];
    }

    public void Process(Span<float> left, Span<float> right) {
        var position = this.position;
        for (var i = 0; i < left.Length; i++) {
            (left[i], this.left[position]) = (this.left[position], left[i]);
            (right[i], this.right[position]) = (this.right[position], right[i]);
            if (++position == Samples) {
                position = 0;
            }
        }

        this.position = position;
    }

    public void Reset() {
        Array.Clear(left);
        Array.Clear(right);
        position = 0;
    }
}
