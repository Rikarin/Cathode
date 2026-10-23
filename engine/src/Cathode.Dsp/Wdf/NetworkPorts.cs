using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Wdf;

/// <summary>
///     The adapted ports of an R-type root whose netlist decides how many there are (the amp's general network stage):
///     a source (a resistive voltage source) and any number of capacitors and inductors, held in arrays, discretised with
///     the trapezoidal rule like <see cref="Capacitor" /> and <see cref="Inductor" />.
/// </summary>
public struct NetworkPorts : IWdfPortList {
    /// <summary>The source: the previous stage's open-circuit voltage behind its output resistance.</summary>
    public ResistiveVoltageSource Source;

    readonly double[] sign; // +1 capacitor (b = state), -1 inductor (b = -state)
    readonly double[] resistance;
    readonly double[] state;

    public readonly int Count => 1 + state.Length;

    /// <param name="sourceResistance">The source's resistance.</param>
    /// <param name="inductors">For each reactive port, whether it is an inductor.</param>
    public NetworkPorts(double sourceResistance, ReadOnlySpan<bool> inductors) {
        Source = new(sourceResistance);
        sign = new double[inductors.Length];
        resistance = new double[inductors.Length];
        state = new double[inductors.Length];
        for (var i = 0; i < inductors.Length; i++) {
            sign[i] = inductors[i] ? -1 : 1;
            resistance[i] = 1;
        }
    }

    /// <summary>Reactive port <paramref name="index" /> (0-based, after the source) at <paramref name="value" /> farads or henries.</summary>
    public readonly void Configure(int index, double value, double sampleRate) =>
        resistance[index] = sign[index] > 0 ? 0.5 / (value * sampleRate) : 2 * value * sampleRate;

    /// <summary>
    ///     Starts reactive port <paramref name="index" /> at rest: a capacitor charged to <paramref name="value" /> volts,
    ///     an inductor carrying <paramref name="value" /> amperes. Call after <see cref="Configure" />.
    /// </summary>
    public readonly void Precharge(int index, double value) =>
        state[index] = sign[index] > 0 ? value : value * resistance[index];

    public readonly void Propagate(Span<double> resistances) {
        resistances[0] = Source.Propagate();
        for (var i = 0; i < state.Length; i++) {
            resistances[i + 1] = resistance[i];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Reflect(Span<double> waves) {
        waves[0] = Source.Reflect();
        for (var i = 0; i < state.Length; i++) {
            waves[i + 1] = sign[i] * state[i];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Incident(ReadOnlySpan<double> waves) {
        Source.Incident(waves[0]);
        waves.Slice(1, state.Length).CopyTo(state);
    }

    public void Reset() {
        Source.Reset();
        Array.Clear(state);
    }

    /// <summary>Clears state that has decayed into the subnormal range.</summary>
    public readonly void FlushDenormals() {
        for (var i = 0; i < state.Length; i++) {
            if (Math.Abs(state[i]) < 1e-30) {
                state[i] = 0;
            }
        }
    }
}
