namespace Cathode.Dsp.Resampling;

/// <summary>
///     2x oversampling with one half-band stage (the steep first stage of <see cref="Oversampler4x" />, 100 dB
///     stopband, transition from about 20 kHz at 48 kHz). Process a block with <see cref="Up" />, run the nonlinearity
///     on the returned span, then <see cref="Down" />. Allocation-free after construction.
/// </summary>
public sealed class Oversampler2x {
    static readonly (float[], float[]) Branches =
        HalfBandStage.Split(HalfBand.Design(Oversampler4x.AttenuationDb, Oversampler4x.FirstTransition));

    readonly HalfBandStage stage = new(Branches);
    float[] twice;

    public Oversampler2x(int maxBlockSize) {
        twice = new float[maxBlockSize * 2];
    }

    /// <summary>The filters in use (<see cref="OversamplingPhase" />).</summary>
    public OversamplingPhase Phase => stage.Phase;

    /// <summary>Delay of <see cref="Up" /> then <see cref="Down" />, in base-rate samples.</summary>
    public int LatencySamples => stage.RoundTripLatency;

    /// <summary>Control thread: choose the filters (clears their state).</summary>
    public void SetPhase(OversamplingPhase phase) => stage.SetPhase(phase, Oversampler4x.FirstTransition);

    /// <summary>
    ///     Makes room for blocks of <paramref name="maxBlockSize" />, keeping the filters. Control thread only.
    /// </summary>
    public void Resize(int maxBlockSize) {
        if (twice.Length != maxBlockSize * 2) {
            twice = new float[maxBlockSize * 2];
        }
    }

    /// <summary>Upsamples <paramref name="input" /> and returns the 2x buffer to process in place.</summary>
    public Span<float> Up(ReadOnlySpan<float> input) {
        var twice = this.twice.AsSpan(0, input.Length * 2);
        stage.Up(input, twice);
        return twice;
    }

    /// <summary>Downsamples the 2x buffer returned by <see cref="Up" /> into <paramref name="output" />.</summary>
    public void Down(Span<float> output) => stage.Down(twice.AsSpan(0, output.Length * 2), output);

    public void Reset() => stage.Reset();

    public void FlushDenormals() => stage.FlushDenormals();
}
