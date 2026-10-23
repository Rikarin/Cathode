namespace Cathode.Contracts;

/// <summary>
///     Pushed about 15 times a second while the RTA is on (<c>PUT /api/v1/rta</c>): the output's spectrum in
///     sixth-octave bands. A full-scale sine reads 0 dB in its band.
/// </summary>
public sealed record RtaFrame(string Type, float[] Frequencies, float[] Levels);

public sealed record RtaRequest(bool Enabled);

/// <summary>
///     Learn a Tone Match: the Server listens to the rig's output for <c>Seconds</c> while you play, compares it with the
///     <c>Reference</c> recording (a .wav asset) and stores the correction as the block's <c>match</c> asset.
/// </summary>
/// <param name="Reference">An asset reference (<c>sha256:…</c>) of a .wav file of the tone to match.</param>
/// <param name="Seconds">How long to listen to the rig, 3 to 60 seconds.</param>
/// <param name="Amount">How much of the correction to apply, 0 to 100 %.</param>
/// <param name="Smoothing">Smoothing width in octaves, 0.1 to 1.</param>
public sealed record ToneMatchRequest(string Reference, double Seconds = 10, double Amount = 100, double Smoothing = 1.0 / 3);

/// <summary>One impulse response in an IR mix.</summary>
/// <param name="Asset">An asset reference of a .wav IR.</param>
/// <param name="LevelDb">Gain applied before summing.</param>
/// <param name="DelayMs">Delay added before summing (after alignment).</param>
/// <param name="Invert">Flip the polarity.</param>
public sealed record IrMixSource(string Asset, double LevelDb = 0, double DelayMs = 0, bool Invert = false);

/// <summary>
///     Mixes impulse responses into a new one (a Cab-Lab-style blend of mics or cabs), stored as a new asset.
/// </summary>
/// <param name="Name">File name for the result (".wav" is added).</param>
/// <param name="Sources">Two or more IRs.</param>
/// <param name="Align">Shift each IR so its peak lands at the same sample before the delays apply.</param>
/// <param name="LengthMs">Length of the result.</param>
/// <param name="SampleRate">Rate of the result.</param>
/// <param name="Normalize">Scale the result to unit energy, like the Cab block does.</param>
public sealed record IrMixRequest(
    string Name,
    IReadOnlyList<IrMixSource> Sources,
    bool Align = true,
    double LengthMs = 170,
    int SampleRate = 48_000,
    bool Normalize = true);
