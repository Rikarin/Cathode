using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Amp;

/// <summary>
///     A stage built from two others that run side by side on the same input (the harmonic vibrato's bands, the
///     grid-drive tremolo's plain and modulated netlists): the sample rate, the source resistance, the supply, the
///     controls and the settings go to both, the first one first.
/// </summary>
public abstract class PairedAmpStage(AmpStage first, AmpStage second) : AmpStage {
    public sealed override void SetSampleRate(double sampleRate) {
        first.SetSampleRate(sampleRate);
        second.SetSampleRate(sampleRate);
        OnSampleRate(sampleRate);
    }

    public sealed override void SetSourceResistance(double ohms) {
        first.SetSourceResistance(ohms);
        second.SetSourceResistance(ohms);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override void SetSupply(double scale) {
        first.SetSupply(scale);
        second.SetSupply(scale);
    }

    public sealed override void Update(in AmpControls controls) {
        first.Update(controls);
        second.Update(controls);
        OnUpdate(controls);
    }

    public sealed override void Apply(in PreampSettings settings) {
        first.Apply(settings);
        second.Apply(settings);
    }

    /// <summary>The stage's own part of <see cref="SetSampleRate" />, after both halves have theirs.</summary>
    protected abstract void OnSampleRate(double sampleRate);

    /// <summary>The stage's own part of <see cref="Update" />, after both halves have theirs.</summary>
    protected abstract void OnUpdate(in AmpControls controls);
}
