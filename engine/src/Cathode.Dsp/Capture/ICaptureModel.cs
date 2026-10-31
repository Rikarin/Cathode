namespace Cathode.Dsp.Capture;

/// <summary>
///     A neural capture of an amp or pedal (a NAM model, an AIDA-X model). Load and <see cref="Reset" /> run on the
///     control thread; <see cref="Process" /> is real-time safe.
/// </summary>
public interface ICaptureModel : IDisposable {
    /// <summary>The model format, e.g. <c>NAM</c> or <c>AIDA-X</c>.</summary>
    string Format { get; }

    /// <summary>The network, e.g. <c>WaveNet</c> or <c>LSTM-16</c>.</summary>
    string Architecture { get; }

    /// <summary>
    ///     The rate the model was trained at (0 or less when the file does not say; 48 kHz is assumed then). The amp
    ///     runs the model at this rate, resampling around it when the engine runs at another
    ///     (<see cref="CaptureResampler" />).
    /// </summary>
    double ExpectedSampleRate { get; }

    /// <summary>Output loudness in dB, when the file records it; used to normalise models to a common level.</summary>
    double? Loudness { get; }

    /// <summary>
    ///     The analog level in dBu that reached full scale on the interface the capture was made with (NAM's
    ///     <c>input_level_dbu</c>), when the file records it. The amp scales its input by the difference to Input 1's
    ///     calibration, so the capture is driven as hard as the real amp was by the same guitar.
    /// </summary>
    double? InputLevelDbu { get; }

    /// <summary>
    ///     Control thread: allocate for blocks of up to <paramref name="maxFrames" />, clear the state and pre-warm.
    /// </summary>
    void Reset(double sampleRate, int maxFrames);

    /// <summary>Audio thread. <paramref name="input" /> and <paramref name="output" /> may be the same span.</summary>
    void Process(ReadOnlySpan<float> input, Span<float> output);
}
