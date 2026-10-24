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
public readonly record struct BlockContext(
    double SampleRate,
    double Tempo,
    long SampleTime,
    HeldNotes Notes = default,
    float MixOffset = 0f,
    float GateOffsetDb = 0f,
    bool CabsOff = false
);
