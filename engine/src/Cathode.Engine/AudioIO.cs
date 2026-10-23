namespace Cathode.Engine;

/// <summary>One callback's worth of stereo input and output. All spans have the same length.</summary>
public readonly ref struct AudioIO {
    public ReadOnlySpan<float> InputLeft { get; }

    public ReadOnlySpan<float> InputRight { get; }

    public Span<float> OutputLeft { get; }

    public Span<float> OutputRight { get; }

    public int Frames => OutputLeft.Length;

    public AudioIO(
        ReadOnlySpan<float> inputLeft,
        ReadOnlySpan<float> inputRight,
        Span<float> outputLeft,
        Span<float> outputRight
    ) {
        var frames = outputLeft.Length;
        if (inputLeft.Length != frames || inputRight.Length != frames || outputRight.Length != frames) {
            throw new ArgumentException("All channel spans must have the same length.");
        }

        InputLeft = inputLeft;
        InputRight = inputRight;
        OutputLeft = outputLeft;
        OutputRight = outputRight;
    }

    public AudioIO Slice(int start, int length) =>
        new(
            InputLeft.Slice(start, length),
            InputRight.Slice(start, length),
            OutputLeft.Slice(start, length),
            OutputRight.Slice(start, length)
        );
}

/// <summary>Anything that can fill an audio callback: the engine, or a test double.</summary>
public interface IAudioProcessor {
    void Process(in AudioIO io);
}
