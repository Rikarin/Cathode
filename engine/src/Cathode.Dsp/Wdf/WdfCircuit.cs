using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Wdf;

/// <summary>
///     A complete wave digital filter: an adapted tree terminated by an unadapted root. Set the sources, call
///     <see cref="Tick" /> once per sample, and read any element's <see cref="IWdfPort.Voltage" /> or
///     <see cref="IWdfPort.Current" /> afterwards.
/// </summary>
public struct WdfCircuit<TTree, TRoot>(TTree tree, TRoot root)
    where TTree : struct, IWdfPort
    where TRoot : struct, IWdfRoot {
    public TTree Tree = tree;
    public TRoot Root = root;

    /// <summary>Re-adapts the tree and the root after a component change. Cheap enough to call once per block.</summary>
    public void Propagate() => Root.Adapt(Tree.Propagate());

    /// <summary>Evaluates one sample and returns the voltage across the root.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick() {
        Tree.Incident(Root.Scatter(Tree.Reflect()));
        return Root.Voltage;
    }

    public void Reset() {
        Tree.Reset();
        Root.Reset();
    }
}

/// <summary>
///     A first-order RC low-pass as a wave digital filter (a resistive source across a capacitor, open at the root):
///     exactly the bilinear transform of 1 / (1 + sRC). The high-pass output is the input minus the low-pass.
/// </summary>
public struct RcLowPass() {
    const double Resistance = 10e3;

    WdfCircuit<Parallel<ResistiveVoltageSource, Capacitor>, OpenCircuit> circuit = new(
        new(new(Resistance), new(1e-9, 48_000)),
        default
    );

    /// <summary>Sets the -3 dB frequency (prewarped by the bilinear transform like any analog prototype).</summary>
    public void SetCutoff(double frequency, double sampleRate) {
        var hz = Math.Clamp(frequency, 1, sampleRate * 0.49);
        circuit.Tree.B.Configure(1 / (2 * Math.PI * hz * Resistance), sampleRate);
        circuit.Propagate();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick(double x) {
        circuit.Tree.A.Volts = x;
        return circuit.Tick();
    }

    public void Reset() => circuit.Reset();

    /// <summary>Zeroes a capacitor charge that has decayed into the subnormal range.</summary>
    public void FlushDenormals() {
        if (Math.Abs(circuit.Tree.B.Reflected) < 1e-30) {
            circuit.Reset();
        }
    }
}
