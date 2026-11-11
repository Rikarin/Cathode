using Cathode.Dsp.Amp;

namespace Cathode.Dsp.Modulation;

/// <summary>
///     The analogue path around the bucket brigade of the Roland Jazz Chorus's chorus, for the Chorus block's JC-120
///     type: the filter and pre-emphasis ahead of the delay line (<see cref="Drive" />) and the filter and
///     de-emphasis after it (<see cref="Recover" />), as two linear netlists with the drawn values. The delay line
///     itself is the block's.
/// </summary>
/// <remarks>
///     <para>
///         Source: the chorus on Roland's circuit diagram dated December 1984, the sheet the JC-120 amp type is built
///         from (schematicheaven.net/newamps/roland_jazz_chorus.pdf, read at the scan's own resolution; see
///         <see cref="Amp.Models.JazzChorus120" />). Ahead of the delay: C58 0.047 µF from the preamp's output with
///         R110 270k, R109 5.6k with C64 2.2 nF, R108 56k with C65 220 pF, into IC5a non-inverting, its feedback R123
///         22k ‖ C73 47 pF over R121 22k beside R122 5.6k + C71 0.01 µF (2× at low frequencies rising to 5.9×: the
///         pre-emphasis); R130 1k into IC6, an MN3007, clocked by IC7, an MN3101. After it: C79 0.047 µF with R132
///         100k; R133, R131 and R129, 8.2k each, with C76 2.2 nF to ground after the first, C74 8.2 nF from after the
///         second to Q27's emitter and C75 470 pF at its base (a third-order low-pass around the follower); R127 10k;
///         C72 0.047 µF and R120 100k into IC5b inverting, its feedback R107 47k ‖ (C70 4.7 nF + R119 8.2k) (the
///         de-emphasis); C63 0.047 µF with R102 270k. The op-amps and the follower are ideal here: the levels (under
///         a volt) are far from their rails.
///     </para>
///     <para>
///         The drawing marks the clock "80K-350KHz" and the sweep a triangle of "240ms-1sec". An MN3007 has 1024
///         stages and delays by stages / (2 × clock): 5.12 ms at 100 kHz in its distributors' data (cedist.com,
///         amplifiedparts.com), so 1.46 ms at 350 kHz and 6.4 ms at 80 kHz (<see cref="ShortestDelayMs" />,
///         <see cref="LongestDelayMs" />), swept at 1 to 4.2 Hz. Assumed: that the triangle sweeps the delay time
///         evenly (it sweeps the clock oscillator's control voltage; the oscillator's law is not on the sheet).
///     </para>
///     <para>
///         The level: Roland's signal voltages are 290 mV dry at IC3b's output and 300 mV at the chorus's output
///         (1 kHz). The two filters as read give 0.67 between them, so the delay line's gain is set to make up the
///         marked ratio (<see cref="MarkedGain" />): the marks, not the reading, set the level (the 850 mV marked at
///         Q27 against 300 mV after IC5b is a gain of 0.35 where the drawn feedback gives 0.25).
///     </para>
///     <para>
///         The mixing, which the block does: with the chorus on, Q17 takes the dry signal off the second power amp's
///         mixer (IC4a, R73 47k) and Q21 puts the chorus on it (R95 47k), while the first power amp (IC1a) stays dry.
///         So one speaker is dry and the other is all chorus, and the two mix in the air.
///     </para>
/// </remarks>
public sealed class JazzChorusPath {
    /// <summary>The MN3007's stages.</summary>
    public const int Stages = 1024;

    /// <summary>The clock's range as marked on the drawing, in hertz.</summary>
    public const double SlowClock = 80e3;

    public const double FastClock = 350e3;

    /// <summary>The chorus's output over the dry signal at 1 kHz, from the drawing's marked voltages.</summary>
    public const double MarkedGain = 300.0 / 290.0;

    readonly AmpStage input;
    readonly AmpStage output;
    readonly double gain;

    /// <summary>The delay at the fastest clock: 1.46 ms.</summary>
    public static double ShortestDelayMs => 1e3 * Stages / (2 * FastClock);

    /// <summary>The delay at the slowest clock: 6.4 ms.</summary>
    public static double LongestDelayMs => 1e3 * Stages / (2 * SlowClock);

    public JazzChorusPath(double sampleRate) {
        input = InputFilter().Create(sampleRate);
        output = OutputFilter().Create(sampleRate);
        Reset();
        gain = 1;
        gain = MarkedGain / GainAt(1_000, sampleRate);
        Reset();
    }

    /// <summary>What goes into the delay line for <paramref name="dry" />.</summary>
    public double Drive(double dry) => gain * input.Tick(dry);

    /// <summary>The chorus's output for what comes out of the delay line.</summary>
    public double Recover(double delayed) => output.Tick(delayed);

    public void Reset() {
        input.Reset(0);
        output.Reset(0);
    }

    public void FlushDenormals() {
        input.FlushDenormals();
        output.FlushDenormals();
    }

    /// <summary>
    ///     The path's gain at <paramref name="frequency" /> with no delay between its halves: a sine through both, its
    ///     level taken over the last of forty cycles (construction: the control thread).
    /// </summary>
    public double GainAt(double frequency, double sampleRate) {
        var period = sampleRate / frequency;
        var samples = (int)(40 * period);
        var from = samples - (int)(8 * period);
        var (sum, driven) = (0.0, 0.0);
        for (var n = 0; n < samples; n++) {
            var x = Math.Sin(2 * Math.PI * n / period);
            var y = Recover(Drive(x));
            if (n >= from) {
                sum += y * y;
                driven += x * x;
            }
        }

        return Math.Sqrt(sum / driven);
    }

    /// <summary>The input filter and IC5a with its pre-emphasis; out at IC5a's output.</summary>
    static NetworkStageDesign InputFilter() {
        var net = new StageNetlist();
        var (a, b, plus, minus, leg, o) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, a, 0.047e-6);
        net.Resistor(a, StageNetlist.Ground, 270e3);
        net.Resistor(a, b, 5.6e3);
        net.Capacitor(b, StageNetlist.Ground, 2.2e-9);
        net.Resistor(b, plus, 56e3);
        net.Capacitor(plus, StageNetlist.Ground, 220e-12);
        net.Resistor(o, minus, 22e3);
        net.Capacitor(o, minus, 47e-12);
        net.Resistor(minus, StageNetlist.Ground, 22e3);
        net.Resistor(minus, leg, 5.6e3);
        net.Capacitor(leg, StageNetlist.Ground, 0.01e-6);
        net.Amplifier(plus, minus, o);
        net.Output = o;
        return new("IC5a", net);
    }

    /// <summary>
    ///     The delay line's output filter around Q27 (an ideal follower) and IC5b with its de-emphasis; out after
    ///     C63.
    /// </summary>
    static NetworkStageDesign OutputFilter() {
        var net = new StageNetlist();
        var (p, first, second, third, emitter) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, p, 0.047e-6);
        net.Resistor(p, StageNetlist.Ground, 100e3);
        net.Resistor(p, first, 8.2e3);
        net.Capacitor(first, StageNetlist.Ground, 2.2e-9);
        net.Resistor(first, second, 8.2e3);
        net.Capacitor(second, emitter, 8.2e-9);
        net.Resistor(second, third, 8.2e3);
        net.Capacitor(third, StageNetlist.Ground, 470e-12);
        net.Amplifier(third, emitter, emitter);
        net.Resistor(emitter, StageNetlist.Ground, 10e3);

        var (q, minus, series, o, coupled) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(emitter, q, 0.047e-6);
        net.Resistor(q, minus, 100e3);
        net.Resistor(o, minus, 47e3);
        net.Capacitor(o, series, 4.7e-9);
        net.Resistor(series, minus, 8.2e3);
        net.Amplifier(StageNetlist.Ground, minus, o);
        net.Capacitor(o, coupled, 0.047e-6);
        net.Resistor(coupled, StageNetlist.Ground, 270e3);
        net.Output = coupled;
        return new("IC5b", net);
    }
}
