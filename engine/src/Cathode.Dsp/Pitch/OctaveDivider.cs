using Cathode.Dsp.Filters;

namespace Cathode.Dsp.Pitch;

/// <summary>
///     Analog-style octave divider after the Boss OC-2: the input is low-passed to its fundamental, a Schmitt trigger
///     squares it, and two flip-flops divide that square by two and four. Each sub voice is the low-passed input
///     multiplied by its flip-flop (+1/-1), so it follows the playing dynamics, and since the flips happen at zero
///     crossings of the carrier the product is nearly continuous.
/// </summary>
/// <remarks>
///     No pitch tracker and no latency. Like the original it wants single notes: chords and the top of the neck (whose
///     fundamental the tracking filter attenuates) make it glitch, which is the character of the effect.
/// </remarks>
public struct OctaveDivider {
    const double TrackingCutoff = 900;
    const double ToneCutoff = 2_000;
    const float Hysteresis = 0.1f;
    const float Floor = 1e-4f; // -80 dBFS: below this the trigger never fires

    SvfCoefficients trackingA;
    SvfCoefficients trackingB;
    SvfCoefficients tone;
    SvfState stageA;
    SvfState stageB;
    SvfState toneOne;
    SvfState toneTwo;
    float release;
    float envelope;
    bool high;
    float divideByTwo;
    float divideByFour;

    public void Prepare(double sampleRate) {
        trackingA = SvfCoefficients.Design(BiquadType.LowPass, sampleRate, TrackingCutoff, 0.5412);
        trackingB = SvfCoefficients.Design(BiquadType.LowPass, sampleRate, TrackingCutoff, 1.3066);
        tone = SvfCoefficients.Design(BiquadType.LowPass, sampleRate, ToneCutoff, 0.7071);
        release = (float)Math.Exp(-1 / (0.05 * sampleRate));
        Reset();
    }

    public void Reset() {
        stageA.Reset();
        stageB.Reset();
        toneOne.Reset();
        toneTwo.Reset();
        envelope = 0;
        high = false;
        divideByTwo = 1;
        divideByFour = 1;
    }

    /// <summary>Returns the octave-down and two-octaves-down voices for one input sample.</summary>
    public (float OctaveDown, float TwoOctavesDown) Process(float input) {
        var carrier = (float)stageB.Tick(trackingB, stageA.Tick(trackingA, input));
        var magnitude = MathF.Abs(carrier);
        envelope = magnitude > envelope ? magnitude : envelope * release;

        var threshold = Hysteresis * envelope + Floor;
        if (!high && carrier > threshold) {
            high = true;
            divideByTwo = -divideByTwo;
            if (divideByTwo > 0) {
                divideByFour = -divideByFour;
            }
        } else if (high && carrier < -threshold) {
            high = false;
        }

        var one = (float)toneOne.Tick(tone, carrier * divideByTwo);
        var two = (float)toneTwo.Tick(tone, carrier * divideByFour);
        return (one, two);
    }

    /// <summary>Call once per block.</summary>
    public void FlushDenormals() {
        stageA.FlushDenormals();
        stageB.FlushDenormals();
        toneOne.FlushDenormals();
        toneTwo.FlushDenormals();
        if (envelope < 1e-20f) {
            envelope = 0;
        }
    }
}
