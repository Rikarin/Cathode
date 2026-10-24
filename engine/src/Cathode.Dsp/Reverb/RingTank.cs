namespace Cathode.Dsp.Reverb;

/// <summary>
///     The classic hall and plate of the late-1970s/1980s studio reverbs (the Lexicon 224/480 family, as Griesinger
///     described them and as Dattorro's "Effect Design, Part 1" (JAES 1997) publishes a tank "in the style of
///     Griesinger"): input diffusers, then a closed ring of four sections, each a modulated allpass, a delay, the loss
///     filter, a second allpass and a delay, with the stereo output a set of taps into the ring.
/// </summary>
/// <remarks>
///     <para>
///         Dattorro's plate is the two-section case of this ring (a figure eight). Four sections give a longer loop with
///         the same density, the shape of the larger halls; the mono input enters at two opposite sections, and the left
///         output is tapped mostly from sections 1 and 3's first halves, the right from their other points with the signs
///         turned, so the two sides decorrelate. Every section's first allpass is modulated: a sine "spin" (two LFOs, the
///         second half of the ring at LFO Phase from the first) plus a slow random "wander" (a new random target every
///         half period, reached by a straight ramp), the two kinds of modulation the 224XL and 480L manuals name. The
///         wander is our reading; the exact random process of the hardware is not published.
///     </para>
///     <para>
///         Each section loses <c>10^(-3 L / (T60 fs))</c> over its own length L (the allpasses are lossless), so the whole
///         ring decays at the set T60, and the bass and treble multipliers of the 224 (its crossover and bass decay)
///         are the network's Low/High Freq Time splits in every section. Echo Density scales the in-loop allpass gains
///         (how fast the echoes multiply), Input Diffusion and its Time the four input allpasses. The section lengths
///         are ours, mutually prime-ish, not the hardware's (which are not published).
///     </para>
/// </remarks>
sealed class RingTank : IDisposable {
    public const int Sections = 4;
    const int InputStages = 4;
    const int TapsPerSide = 7;
    const float SpinShare = 0.75f;
    const float ExcursionMs = 0.5f;
    const float MaxDecayDiffusion = 0.85f;

    /// <summary>Lines in the pool: four input diffusers, then per section allpass 1, delay 1, allpass 2, delay 2.</summary>
    const int Ap1 = 0, D1 = 1, Ap2 = 2, D2 = 3;

    /// <summary>The concert hall: sections of about 125 ms, a 0.5 s ring.</summary>
    public static readonly Voicing Hall = new(
        [22.6f, 47.3f, 13.1f, 39.7f, 30.4f, 43.9f, 17.9f, 34.1f, 25.1f, 51.7f, 11.3f, 37.3f, 33.7f, 41.3f, 15.7f, 31.9f],
        [4.77f, 3.59f, 12.73f, 9.30f],
        0.75f,
        0.625f,
        0.70f,
        0.50f,
        0.55f
    );

    /// <summary>The plate: sections of about 55 ms, denser input and loop diffusion, a bright fast build.</summary>
    public static readonly Voicing Plate = new(
        [8.9f, 23.3f, 6.1f, 19.7f, 11.3f, 21.1f, 7.7f, 17.3f, 9.7f, 25.9f, 5.3f, 18.9f, 12.7f, 20.3f, 6.9f, 16.1f],
        [3.1f, 2.3f, 8.3f, 6.1f],
        0.78f,
        0.70f,
        0.75f,
        0.60f,
        0.45f
    );

    /// <summary>Output taps per side: the line in its section (delay 1, allpass 2, delay 2), section, position, sign.</summary>
    static readonly (int Line, int Section, float Position, float Sign)[] LeftTaps = [
        (D1, 1, 0.13f, 1f), (D1, 1, 0.71f, 1f), (Ap2, 1, 0.47f, -1f), (D2, 1, 0.38f, 1f), (D1, 3, 0.29f, -1f),
        (Ap2, 3, 0.61f, -1f), (D2, 3, 0.83f, -1f)
    ];

    static readonly (int Line, int Section, float Position, float Sign)[] RightTaps = [
        (D1, 3, 0.17f, 1f), (D1, 3, 0.67f, 1f), (Ap2, 3, 0.41f, -1f), (D2, 3, 0.52f, 1f), (D1, 1, 0.23f, -1f),
        (Ap2, 1, 0.57f, -1f), (D2, 1, 0.79f, -1f)
    ];

    readonly LinePool lines = new(InputStages + 4 * Sections);
    Four<TankLoss> filters;
    Four<float> outputs;
    Four<float> inputLengths;
    Four<float> ap1;
    Four<int> d1;
    Four<float> ap2;
    Four<int> d2;
    Seven<int> leftTaps;
    Seven<int> rightTaps;
    Four<float> wander;
    Four<float> wanderStep;
    Four<int> wanderLeft;
    double rate;
    DecayShape shape;
    float inputGain1, inputGain2;
    float decayDiffusion1, decayDiffusion2;
    float outputScale;
    float excursion;
    double phase;
    double step;
    double offset;
    int wanderPeriod;
    uint seed;
    bool stack;

    public sealed record Voicing(
        float[] SectionMs,
        float[] InputMs,
        float InputGain1,
        float InputGain2,
        float DecayDiffusion1,
        float DecayDiffusion2,
        float OutputScale
    );

    static int Line(int section, int line) => InputStages + 4 * section + line;

    /// <summary>Control thread: room for both voicings at <paramref name="maxScale" />, the diffusers at twice their time.</summary>
    public void Allocate(double sampleRate, float maxScale, float maxInputTime, float maxModDepth) {
        rate = sampleRate;
        var ms = 0.001 * sampleRate;
        Span<int> sizes = stackalloc int[InputStages + 4 * Sections];
        for (var i = 0; i < InputStages; i++) {
            sizes[i] = (int)(Math.Max(Hall.InputMs[i], Plate.InputMs[i]) * maxInputTime * ms) + 4;
        }

        var maxExcursion = ExcursionMs * maxModDepth * ms;
        for (var k = 0; k < 4 * Sections; k++) {
            var longest = Math.Max(Hall.SectionMs[k], Plate.SectionMs[k]) * maxScale * ms;
            sizes[InputStages + k] = (int)(longest + (k % 4 == Ap1 ? 2 * maxExcursion : 0)) + 4;
        }

        lines.Allocate(sizes);
        Clear();
    }

    public void Clear() {
        lines.Clear();
        for (var s = 0; s < Sections; s++) {
            filters[s].Clear();
            outputs[s] = 0;
            wander[s] = 0;
            wanderStep[s] = 0;
            wanderLeft[s] = 0;
        }

        phase = 0;
        seed = 0x2545F491u;
    }

    public void Dispose() => lines.Dispose();

    public void FlushDenormals() {
        for (var s = 0; s < Sections; s++) {
            filters[s].FlushDenormals();
            if (MathF.Abs(outputs[s]) < 1e-20f) {
                outputs[s] = 0;
            }
        }
    }

    /// <summary>
    ///     Once per block. <paramref name="diffusion" /> and <paramref name="density" /> are fractions (1 = the voicing's
    ///     gains, Echo Density 50 %), <paramref name="modDepth" /> a multiple of the 0.5 ms excursion.
    /// </summary>
    public void Configure(
        Voicing voicing,
        in DecayShape decay,
        float scale,
        float diffusion,
        float inputTime,
        float density,
        float modDepth,
        float modRateHz,
        float modPhaseDegrees,
        bool stack
    ) {
        shape = decay;
        this.stack = stack;
        var ms = (float)(0.001 * rate);
        inputGain1 = Math.Min(0.9f, voicing.InputGain1 * diffusion);
        inputGain2 = Math.Min(0.9f, voicing.InputGain2 * diffusion);
        for (var i = 0; i < InputStages; i++) {
            inputLengths[i] = Math.Clamp(voicing.InputMs[i] * inputTime * ms, 1f, lines.Length(i) - 2);
        }

        decayDiffusion1 = Math.Min(MaxDecayDiffusion, voicing.DecayDiffusion1 * density);
        decayDiffusion2 = Math.Min(MaxDecayDiffusion, voicing.DecayDiffusion2 * density);
        outputScale = voicing.OutputScale;
        excursion = ExcursionMs * modDepth * ms;
        step = 2 * Math.PI * modRateHz / rate;
        offset = Math.PI * modPhaseDegrees / 180.0;
        wanderPeriod = Math.Max(1, (int)(rate / (2 * modRateHz)));

        for (var s = 0; s < Sections; s++) {
            var k = 4 * s;
            ap1[s] = Math.Max(1f, voicing.SectionMs[k + Ap1] * scale * ms);
            d1[s] = Math.Max(1, (int)MathF.Round(voicing.SectionMs[k + D1] * scale * ms));
            ap2[s] = Math.Max(1f, MathF.Round(voicing.SectionMs[k + Ap2] * scale * ms));
            d2[s] = Math.Max(1, (int)MathF.Round(voicing.SectionMs[k + D2] * scale * ms));
            // The modulated allpass sits at its mean length plus the excursion, which the loop length includes.
            filters[s].Configure(ap1[s] + excursion + d1[s] + ap2[s] + d2[s], shape);
        }

        for (var t = 0; t < TapsPerSide; t++) {
            leftTaps[t] = TapDelay(LeftTaps[t]);
            rightTaps[t] = TapDelay(RightTaps[t]);
        }
    }

    int TapDelay((int Line, int Section, float Position, float Sign) tap) {
        var length = tap.Line switch {
            D1 => d1[tap.Section],
            Ap2 => (int)ap2[tap.Section],
            _ => d2[tap.Section]
        };
        return Math.Max(1, (int)(tap.Position * length));
    }

    /// <summary>A soft limit near ±4 (12 dBFS) for stacked tails, as the network's.</summary>
    static float Limit(float x) => 4f * MathF.Tanh(0.25f * x);

    float Wander(int s) {
        if (--wanderLeft[s] <= 0) {
            seed = seed * 1_664_525u + 1_013_904_223u;
            var target = (seed >> 8) * (2f / 16_777_216f) - 1f;
            wanderLeft[s] = wanderPeriod;
            wanderStep[s] = (target - wander[s]) / wanderPeriod;
        }

        return wander[s] += wanderStep[s];
    }

    public (float Left, float Right) Tick(float x) {
        x = lines.Allpass(0, x, inputLengths[0], inputGain1);
        x = lines.Allpass(1, x, inputLengths[1], inputGain1);
        x = lines.Allpass(2, x, inputLengths[2], inputGain2);
        x = lines.Allpass(3, x, inputLengths[3], inputGain2);

        float spinA = 0, spinB = 0;
        if (excursion > 0) {
            spinA = (float)Math.Sin(phase);
            spinB = (float)Math.Sin(phase + offset);
            phase += step;
            if (phase >= 2 * Math.PI) {
                phase -= 2 * Math.PI;
            }
        }

        // Every section reads the previous section's output from the last sample, so the ring runs in parallel.
        var carry = outputs[Sections - 1];
        for (var s = 0; s < Sections; s++) {
            var input = carry + (s % 2 == 0 ? x : 0f);
            carry = outputs[s];
            var length = ap1[s];
            if (excursion > 0) {
                var spin = s switch {
                    0 => spinA,
                    1 => -spinA,
                    2 => spinB,
                    _ => -spinB
                };
                length += excursion * (1f + SpinShare * spin + (1f - SpinShare) * Wander(s));
            }

            var v = lines.Allpass(Line(s, Ap1), input, length, -decayDiffusion1);
            v = lines.Delay(Line(s, D1), v, d1[s]);
            v = filters[s].Tick(v, shape);
            v = lines.Allpass(Line(s, Ap2), v, ap2[s], decayDiffusion2);
            v = lines.Delay(Line(s, D2), v, d2[s]);
            outputs[s] = stack ? Limit(v) : v;
        }

        float left = 0, right = 0;
        for (var t = 0; t < TapsPerSide; t++) {
            var (lineL, sectionL, _, signL) = LeftTaps[t];
            left += signL * lines.Tap(Line(sectionL, lineL), leftTaps[t]);
            var (lineR, sectionR, _, signR) = RightTaps[t];
            right += signR * lines.Tap(Line(sectionR, lineR), rightTaps[t]);
        }

        return (left * outputScale, right * outputScale);
    }
}
