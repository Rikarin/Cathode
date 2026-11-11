namespace Cathode.Engine.Graph;

/// <summary>Fixed integer stereo delay used for latency compensation between parallel paths.</summary>
sealed class DelayLine(int samples) {
    readonly float[] left = new float[samples];
    readonly float[] right = new float[samples];
    int position;

    public int Samples { get; } = samples;

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
