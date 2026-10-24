namespace Cathode.Dsp.Reverb;

/// <summary>
///     A small room or ambience from nested allpasses, after Gardner's room designs ("The Virtual Acoustic Room", MIT
///     1992; "Reverberation algorithms" in Kahrs and Brandenburg, 1998): one loop through a double-nested allpass and a
///     nested allpass, an output tap after each, and the loop closed through a low-pass and a gain.
/// </summary>
/// <remarks>
///     <para>
///         The small-room layout: a short delay, a 35 ms allpass (gain 0.3) with a 22 ms (0.4) and an 8.3 ms (0.6)
///         allpass nested in series inside it, then a 66 ms allpass (0.1) with a 30 ms (0.4) one inside, each followed by
///         an output tap at half level. Lengths and gains are our reading of the published figure. The nested allpasses
///         thicken the echoes quickly without colouring them, which is why the design suits short, dense ambiences.
///     </para>
///     <para>
///         A nested allpass of total order N delays a signal by N samples on average over frequency, so the loop's gain
///         is <c>10^(-3 L / (T60 fs))</c> with L the sum of every delay in it; the decay follows the setting down to
///         about 0.3 s, below which the allpasses' own ringing (about 0.2 s) sets it. High Cut is the loop low-pass and
///         the Low/High Freq Time splits sit beside it. Gardner's room is mono; here a second copy 7 % longer makes the
///         right side, so the two sides decorrelate.
///     </para>
/// </remarks>
sealed class NestedRoom : IDisposable {
    const float RightStretch = 1.0713f;
    const float OutputScale = 0.5f;

    /// <summary>Delays (ms at size 50 %): loop delay, outer 1, inner 1a, inner 1b, outer 2, inner 2.</summary>
    static readonly float[] Ms = [4.7f, 35f, 22f, 8.3f, 66f, 30f];

    const float Outer1Gain = 0.3f, Inner1aGain = 0.4f, Inner1bGain = 0.6f, Outer2Gain = 0.1f, Inner2Gain = 0.4f;
    const int Stages = 6;

    readonly LinePool lines = new(2 * Stages);
    Twelve<int> lengths;
    Two<TankLoss> filters;
    Two<float> feedback;
    double rate;
    DecayShape shape;
    bool stack;

    public void Allocate(double sampleRate, float maxScale) {
        rate = sampleRate;
        Span<int> sizes = stackalloc int[2 * Stages];
        for (var i = 0; i < 2 * Stages; i++) {
            sizes[i] = (int)(Ms[i % Stages] * RightStretch * maxScale * 0.001 * sampleRate) + 4;
        }

        lines.Allocate(sizes);
        Clear();
    }

    public void Clear() {
        lines.Clear();
        filters[0].Clear();
        filters[1].Clear();
        feedback[0] = feedback[1] = 0;
    }

    public void Dispose() => lines.Dispose();

    public void FlushDenormals() {
        filters[0].FlushDenormals();
        filters[1].FlushDenormals();
        for (var i = 0; i < 2; i++) {
            if (MathF.Abs(feedback[i]) < 1e-20f) {
                feedback[i] = 0;
            }
        }
    }

    public void Configure(in DecayShape decay, float scale, bool stack) {
        shape = decay;
        this.stack = stack;
        var ms = (float)(0.001 * rate);
        for (var side = 0; side < 2; side++) {
            var loop = 0;
            for (var k = 0; k < Stages; k++) {
                var length = Ms[k] * scale * ms * (side == 1 ? RightStretch : 1f);
                lengths[side * Stages + k] = Math.Max(1, (int)MathF.Round(length));
                loop += lengths[side * Stages + k];
            }

            filters[side].Configure(loop, shape);
        }
    }

    static float Limit(float x) => 4f * MathF.Tanh(0.25f * x);

    float Side(int side, float x) {
        var b = side * Stages;
        var v = lines.Delay(b, x + feedback[side], lengths[b]);

        // Outer allpass 1, with allpasses 1a and 1b in series in its delay path.
        var delayed = lines.Tap(b + 1, lengths[b + 1]);
        delayed = lines.Allpass(b + 2, delayed, lengths[b + 2], Inner1aGain);
        delayed = lines.Allpass(b + 3, delayed, lengths[b + 3], Inner1bGain);
        var node = v + Outer1Gain * delayed;
        lines.Write(b + 1, node);
        var first = delayed - Outer1Gain * node;

        // Outer allpass 2, with allpass 2a in its delay path.
        delayed = lines.Tap(b + 4, lengths[b + 4]);
        delayed = lines.Allpass(b + 5, delayed, lengths[b + 5], Inner2Gain);
        node = first + Outer2Gain * delayed;
        lines.Write(b + 4, node);
        var second = delayed - Outer2Gain * node;

        var back = filters[side].Tick(second, shape);
        feedback[side] = stack ? Limit(back) : back;
        return 0.5f * (first + second);
    }

    public (float Left, float Right) Tick(float x) => (Side(0, x) * OutputScale, Side(1, x) * OutputScale);
}
