namespace Cathode.Dsp.Reverb;

/// <summary>
///     Jon Dattorro's plate reverb ("Effect Design, Part 1", JAES 1997): a bandwidth filter and four input diffusers
///     feed a figure-eight tank of two modulated allpasses, four delays, two more allpasses and two damping filters;
///     the stereo output is a fixed set of taps into the tank.
/// </summary>
/// <remarks>
///     Lengths are the paper's, at its 29 761 Hz rate, scaled to the engine rate and (in the tank) by the Size control.
///     The decay coefficient is derived from the decay time, so the T60 follows the setting: the signal is multiplied
///     by it twice per half-tank, so one half-tank of L samples attenuates by decay², giving
///     decay = 10^(-3 L / (2 T60 fs)).
/// </remarks>
sealed class DattorroPlate : IDisposable {
    const double PaperRate = 29_761;
    const float Bandwidth = 0.9995f;
    const float InputDiffusion1 = 0.75f;
    const float InputDiffusion2 = 0.625f;
    const float DecayDiffusion1 = 0.70f;
    const float ExcursionSamples = 16f;
    const double ModulationHz = 1.0;
    const float OutputScale = 0.6f;

    static readonly int[] InputLengths = [142, 107, 379, 277];

    /// <summary>Output taps (left then right), in the order <see cref="Tick" /> reads them.</summary>
    static readonly int[] PaperTaps = [266, 2974, 1913, 1996, 1990, 187, 1066, 353, 3627, 1228, 2673, 2111, 335, 121];

    // Tank, in signal order: left half then right half.
    const int LeftAllpass1 = 672, LeftDelay1 = 4453, LeftAllpass2 = 1800, LeftDelay2 = 3720;
    const int RightAllpass1 = 908, RightDelay1 = 4217, RightAllpass2 = 2656, RightDelay2 = 3163;

    readonly ReverbLine[] input = [new(), new(), new(), new()];
    readonly ReverbLine leftAllpass1 = new(), leftDelay1 = new(), leftAllpass2 = new(), leftDelay2 = new();
    readonly ReverbLine rightAllpass1 = new(), rightDelay1 = new(), rightAllpass2 = new(), rightDelay2 = new();
    readonly ReverbLine[] all;
    readonly float[] inputLengths = new float[4];
    readonly int[] taps = new int[14];
    double rate;
    float scale;
    float bandwidthState;
    float leftDamping;
    float rightDamping;
    float leftFeedback; // output of the left half, entering the right half on the next sample
    float rightFeedback;
    float decay;
    float decayDiffusion2;
    float damping;
    double phase;
    float excursion;

    int lAp1, lD1, lAp2, lD2, rAp1, rD1, rAp2, rD2;

    public DattorroPlate() {
        all = [
            ..input, leftAllpass1, leftDelay1, leftAllpass2, leftDelay2, rightAllpass1, rightDelay1, rightAllpass2,
            rightDelay2
        ];
    }

    public void Allocate(double sampleRate, float maxScale) {
        rate = sampleRate;
        var r = sampleRate / PaperRate;
        excursion = (float)(ExcursionSamples * r);
        for (var i = 0; i < input.Length; i++) {
            inputLengths[i] = (float)(InputLengths[i] * r);
            input[i].Allocate((int)inputLengths[i] + 2);
        }

        var tank = r * maxScale;
        int Size(int length, float extra = 0) => (int)(length * tank + extra) + 4;
        leftAllpass1.Allocate(Size(LeftAllpass1, 2 * excursion));
        leftDelay1.Allocate(Size(LeftDelay1));
        leftAllpass2.Allocate(Size(LeftAllpass2));
        leftDelay2.Allocate(Size(LeftDelay2));
        rightAllpass1.Allocate(Size(RightAllpass1, 2 * excursion));
        rightDelay1.Allocate(Size(RightDelay1));
        rightAllpass2.Allocate(Size(RightAllpass2));
        rightDelay2.Allocate(Size(RightDelay2));
        scale = -1;
    }

    public void Clear() {
        foreach (var line in all) {
            line.Clear();
        }

        bandwidthState = leftDamping = rightDamping = leftFeedback = rightFeedback = 0;
        phase = 0;
    }

    public void Dispose() {
        foreach (var line in all) {
            line.Dispose();
        }
    }

    /// <summary>Once per block: decay time in seconds, tank size scale, damping cut-off in Hz.</summary>
    public void Configure(float decaySeconds, float sizeScale, float dampingHz) {
        if (sizeScale != scale) {
            scale = sizeScale;
            var r = rate / PaperRate * sizeScale;
            int Length(int paper) => Math.Max(1, (int)Math.Round(paper * r));
            (lAp1, lD1, lAp2, lD2) = (Length(LeftAllpass1), Length(LeftDelay1), Length(LeftAllpass2), Length(LeftDelay2));
            (rAp1, rD1, rAp2, rD2) =
                (Length(RightAllpass1), Length(RightDelay1), Length(RightAllpass2), Length(RightDelay2));
            for (var i = 0; i < taps.Length; i++) {
                taps[i] = Length(PaperTaps[i]);
            }
        }

        var halfTank = (lAp1 + lD1 + lAp2 + lD2 + rAp1 + rD1 + rAp2 + rD2) / 2.0;
        decay = (float)Math.Pow(10, -3 * halfTank / (2 * decaySeconds * rate));
        decayDiffusion2 = Math.Clamp(decay + 0.15f, 0.25f, 0.5f);
        damping = MathF.Exp(-2f * MathF.PI * dampingHz / (float)rate);
    }

    public (float Left, float Right) Tick(float x) {
        bandwidthState += Bandwidth * (x - bandwidthState);
        var diffused = bandwidthState;
        diffused = input[0].Allpass(diffused, inputLengths[0], InputDiffusion1);
        diffused = input[1].Allpass(diffused, inputLengths[1], InputDiffusion1);
        diffused = input[2].Allpass(diffused, inputLengths[2], InputDiffusion2);
        diffused = input[3].Allpass(diffused, inputLengths[3], InputDiffusion2);

        var lfo = (float)Math.Sin(phase);
        phase += 2 * Math.PI * ModulationHz / rate;
        if (phase >= 2 * Math.PI) {
            phase -= 2 * Math.PI;
        }

        // Left half, fed by the right half's previous output.
        var left = leftAllpass1.Allpass(diffused + decay * rightFeedback, lAp1 + excursion * (1 + lfo), -DecayDiffusion1);
        left = leftDelay1.Delay(left, lD1);
        leftDamping = left + (leftDamping - left) * damping;
        left = leftAllpass2.Allpass(leftDamping * decay, lAp2, decayDiffusion2);
        var leftOut = leftDelay2.Delay(left, lD2);

        // Right half, fed by the left half's previous output.
        var right = rightAllpass1.Allpass(
            diffused + decay * leftFeedback,
            rAp1 + excursion * (1 - lfo),
            -DecayDiffusion1
        );
        right = rightDelay1.Delay(right, rD1);
        rightDamping = right + (rightDamping - right) * damping;
        right = rightAllpass2.Allpass(rightDamping * decay, rAp2, decayDiffusion2);
        var rightOut = rightDelay2.Delay(right, rD2);

        leftFeedback = MathF.Abs(leftOut) < 1e-20f ? 0f : leftOut;
        rightFeedback = MathF.Abs(rightOut) < 1e-20f ? 0f : rightOut;

        var outLeft = rightDelay1.Tap(taps[0])
            + rightDelay1.Tap(taps[1])
            - rightAllpass2.Tap(taps[2])
            + rightDelay2.Tap(taps[3])
            - leftDelay1.Tap(taps[4])
            - leftAllpass2.Tap(taps[5])
            - leftDelay2.Tap(taps[6]);
        var outRight = leftDelay1.Tap(taps[7])
            + leftDelay1.Tap(taps[8])
            - leftAllpass2.Tap(taps[9])
            + leftDelay2.Tap(taps[10])
            - rightDelay1.Tap(taps[11])
            - rightAllpass2.Tap(taps[12])
            - rightDelay2.Tap(taps[13]);
        return (outLeft * OutputScale, outRight * OutputScale);
    }
}
