namespace Cathode.Engine;

/// <summary>
///     One callback's worth of stereo input and output, plus the effects-loop channels when the device has them
///     (<see cref="ReturnLeft" />, <see cref="SendLeft" />; empty otherwise). All non-empty spans have the same length.
/// </summary>
public readonly ref struct AudioIO {
    public ReadOnlySpan<float> InputLeft { get; }

    public ReadOnlySpan<float> InputRight { get; }

    public Span<float> OutputLeft { get; }

    public Span<float> OutputRight { get; }

    /// <summary>The device channels an external effects loop comes back on (empty without one).</summary>
    public ReadOnlySpan<float> ReturnLeft { get; }

    public ReadOnlySpan<float> ReturnRight { get; }

    /// <summary>The device channels that feed an external effects loop (empty without one).</summary>
    public Span<float> SendLeft { get; }

    public Span<float> SendRight { get; }

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

    public AudioIO(
        ReadOnlySpan<float> inputLeft,
        ReadOnlySpan<float> inputRight,
        Span<float> outputLeft,
        Span<float> outputRight,
        ReadOnlySpan<float> returnLeft,
        ReadOnlySpan<float> returnRight,
        Span<float> sendLeft,
        Span<float> sendRight
    )
        : this(inputLeft, inputRight, outputLeft, outputRight) {
        ReturnLeft = returnLeft;
        ReturnRight = returnRight;
        SendLeft = sendLeft;
        SendRight = sendRight;
    }

    /// <summary>The main channels only: blocks reach the loop channels through <see cref="Dsp.Routing.HardwareLoop" />.</summary>
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
