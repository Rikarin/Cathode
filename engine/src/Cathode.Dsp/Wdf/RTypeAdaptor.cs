using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Wdf;

/// <summary>
///     An R-type adaptor inside a tree: any linear topology (bridges, tone stacks, feedback networks) joining its child
///     ports, described by a <see cref="WdfNetlist" /> with one <see cref="WdfNetlist.Up" /> port towards its parent, where
///     it is adapted (reflection-free). The scattering matrix comes from <see cref="RTypeScattering" />.
/// </summary>
/// <remarks>
///     <para>
///         Per sample: <see cref="Reflect" /> gathers the children's waves and returns the parent wave
///         b_up = Σ S_up,j a_j; <see cref="Incident" /> takes the parent's wave and hands each child
///         b_k = Σ S_k,j a_j. That is one row of the matrix, then N rows: O(N²) multiply-adds.
///     </para>
///     <para>
///         Changing a child's component or an internal resistor (<see cref="SetResistance" />) takes effect at the next
///         <see cref="Propagate" /> of the tree, which refactors the matrix without allocating, so a pot can move every
///         audio block.
///     </para>
/// </remarks>
public struct RTypeAdaptor<TPorts> : IWdfPort
    where TPorts : struct, IWdfPortList {
    readonly RTypeScattering scattering;
    readonly double[] inputs; // u: child waves, the parent wave, source values
    readonly double[] outputs;
    double a;
    double b;

    /// <summary>The children, in netlist port order.</summary>
    public TPorts Ports;

    /// <summary>The matrix and its structure, for inspection.</summary>
    public readonly RTypeScattering Scattering => scattering;

    public readonly double Resistance => scattering.UpResistance;

    public readonly double Reflected => b;

    public readonly double Voltage => 0.5 * (a + b);

    public readonly double Current => 0.5 * (a - b) / scattering.UpResistance;

    public RTypeAdaptor(WdfNetlist netlist, TPorts ports) {
        scattering = new(netlist);
        if (!scattering.HasUp) {
            throw new ArgumentException("An internal R-type adaptor needs an Up port.", nameof(netlist));
        }

        if (scattering.DeviceCount > 0) {
            throw new ArgumentException("Nonlinear devices belong to an RTypeRoot.", nameof(netlist));
        }

        if (scattering.PortCount != ports.Count) {
            throw new ArgumentException(
                $"The netlist has {scattering.PortCount} ports, the port list {ports.Count}.",
                nameof(ports)
            );
        }

        Ports = ports;
        inputs = new double[scattering.Inputs];
        outputs = new double[scattering.PortCount];
    }

    /// <summary>Sets ideal voltage source <paramref name="index" /> (in volts) for the coming samples.</summary>
    public readonly void SetVoltage(int index, double volts) => inputs[scattering.VoltageSourceColumn + index] = volts;

    /// <summary>Sets ideal current source <paramref name="index" /> (in amperes) for the coming samples.</summary>
    public readonly void SetCurrent(int index, double amps) => inputs[scattering.CurrentSourceColumn + index] = amps;

    /// <summary>Changes internal resistor <paramref name="index" />. Propagate the tree afterwards.</summary>
    public readonly void SetResistance(int index, double ohms) => scattering.SetResistance(index, ohms);

    /// <summary>Changes the gain of VCVS <paramref name="index" />. Propagate the tree afterwards.</summary>
    public readonly void SetGain(int index, double gain) => scattering.SetGain(index, gain);

    public double Propagate() {
        Ports.Propagate(scattering.PortResistances);
        scattering.Recompute();
        return scattering.UpResistance;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Reflect() {
        var u = inputs.AsSpan();
        Ports.Reflect(u);
        b = RTypeScattering.Dot(scattering.Row(scattering.UpRow), u);
        return b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Incident(double a) {
        var u = inputs.AsSpan();
        u[scattering.UpColumn] = a;
        var waves = outputs.AsSpan();
        for (var k = 0; k < waves.Length; k++) {
            waves[k] = RTypeScattering.Dot(scattering.Row(k), u);
        }

        Ports.Incident(waves);
        this.a = a;
    }

    public void Reset() {
        a = 0;
        b = 0;
        inputs.AsSpan(0, scattering.VoltageSourceColumn).Clear(); // keep the source values
        Ports.Reset();
    }
}
