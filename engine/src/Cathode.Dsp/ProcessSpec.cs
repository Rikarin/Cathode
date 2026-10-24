namespace Cathode.Dsp;

/// <summary>Fixed processing configuration handed to a block in <see cref="IDspBlock.Prepare" />.</summary>
/// <param name="SampleRate">Sample rate in Hz. The engine runs at 48 kHz internally by default.</param>
/// <param name="MaxBlockSize">Largest number of frames a single <c>Process</c> call will receive.</param>
public readonly record struct ProcessSpec(double SampleRate, int MaxBlockSize) {
    public static ProcessSpec Default { get; } = new(48_000, 64);
}

/// <summary>Per-callback context passed to every block.</summary>
/// <param name="SampleRate">Current sample rate in Hz.</param>
/// <param name="Tempo">Current tempo in BPM.</param>
/// <param name="SampleTime">Absolute sample position of the first frame in this block.</param>
/// <param name="Notes">The MIDI notes held down on the rig's MIDI input (any channel).</param>
/// <param name="MixOffset">
///     The global Effects Mix offset (−50..50 %), added to the Mix of every block whose Global Mix is on
///     (<see cref="EffectsMix" />).
/// </param>
/// <param name="GateOffsetDb">The global noise gate offset, added to the Input node's gate threshold.</param>
/// <param name="CabsOff">
///     The global cab modelling switch is off (a player using a real cab): Cab and IR Player blocks pass audio through.
/// </param>
/// <param name="InputCalibrationDb">
///     Input 1's calibration: the volts at the instrument jack that reach full scale, in dB re 1 V (0 = 1 V, the
///     default). Blocks that model circuits by voltage (the Circuit Amp and Drive) scale their input by
///     <see cref="InputVolts" />, so the drive they get does not depend on the interface's gain.
/// </param>
public readonly record struct BlockContext(
    double SampleRate,
    double Tempo,
    long SampleTime,
    HeldNotes Notes = default,
    float MixOffset = 0f,
    float GateOffsetDb = 0f,
    bool CabsOff = false,
    float InputCalibrationDb = 0f
) {
    /// <summary>Volts at the instrument jack per unit of signal (<see cref="InputCalibrationDb" />).</summary>
    public double InputVolts => InputCalibrationDb == 0f ? 1.0 : Math.Pow(10, InputCalibrationDb / 20.0);
}
