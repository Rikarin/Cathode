using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Wdf;

/// <summary>
///     An R-type adaptor inside a tree: any linear topology (bridges, tone stacks, feedback networks) joining its child
///     ports, described by a <see cref="WdfNetlist" /> with one <see cref="WdfNetlist.Up" /> port towards its parent,
///     where
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
    /// <summary>The children, in netlist port order.</summary>
    public TPorts Ports;

    readonly double[] inputs; // u: child waves, the parent wave, source values
    readonly double[] outputs;
    double a;

    /// <summary>The matrix and its structure, for inspection.</summary>
    public RTypeScattering Scattering { get; }

    public readonly double Resistance => Scattering.UpResistance;

    public double Reflected { get; private set; }

    public readonly double Voltage => 0.5 * (a + Reflected);

    public readonly double Current => 0.5 * (a - Reflected) / Scattering.UpResistance;

    public RTypeAdaptor(WdfNetlist netlist, TPorts ports) {
        Scattering = new(netlist);
        if (!Scattering.HasUp) {
            throw new ArgumentException("An internal R-type adaptor needs an Up port.", nameof(netlist));
        }

        if (Scattering.DeviceCount > 0) {
            throw new ArgumentException("Nonlinear devices belong to an RTypeRoot.", nameof(netlist));
        }

        if (Scattering.PortCount != ports.Count) {
            throw new ArgumentException(
                $"The netlist has {Scattering.PortCount} ports, the port list {ports.Count}.",
                nameof(ports)
            );
        }

        Ports = ports;
        inputs = new double[Scattering.Inputs];
        outputs = new double[Scattering.PortCount];
    }

    /// <summary>Sets ideal voltage source <paramref name="index" /> (in volts) for the coming samples.</summary>
    public readonly void SetVoltage(int index, double volts) => inputs[Scattering.VoltageSourceColumn + index] = volts;

    /// <summary>Sets ideal current source <paramref name="index" /> (in amperes) for the coming samples.</summary>
    public readonly void SetCurrent(int index, double amps) => inputs[Scattering.CurrentSourceColumn + index] = amps;

    /// <summary>Changes internal resistor <paramref name="index" />. Propagate the tree afterwards.</summary>
    public readonly void SetResistance(int index, double ohms) => Scattering.SetResistance(index, ohms);

    /// <summary>Changes the gain of VCVS <paramref name="index" />. Propagate the tree afterwards.</summary>
    public readonly void SetGain(int index, double gain) => Scattering.SetGain(index, gain);

    /// <summary>
    ///     Recomputes the children's resistances and the matrix, and returns the adapted parent resistance. A network that
    ///     has become singular keeps its previous matrix (counted in <see cref="RTypeScattering.Failures" />); one that has
    ///     never had a valid matrix throws, since its parent would otherwise divide by a zero resistance. The first
    ///     propagation is the root's, at construction, so that throw never reaches the audio thread.
    /// </summary>
    public double Propagate() {
        Ports.Propagate(Scattering.PortResistances);
        if (!Scattering.Recompute() && !Scattering.HasMatrix) {
            throw new InvalidOperationException(
                "The R-type adaptor's network is singular at its initial component values (a floating node, a source "
                + "loop, or no path to its Up port)."
            );
        }

        return Scattering.UpResistance;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Reflect() {
        var u = inputs.AsSpan();
        Ports.Reflect(u);
        Reflected = RTypeScattering.Dot(Scattering.Row(Scattering.UpRow), u);
        return Reflected;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Incident(double a) {
        var u = inputs.AsSpan();
        u[Scattering.UpColumn] = a;
        var waves = outputs.AsSpan();
        for (var k = 0; k < waves.Length; k++) {
            waves[k] = RTypeScattering.Dot(Scattering.Row(k), u);
        }

        Ports.Incident(waves);
        this.a = a;
    }

    public void Reset() {
        a = 0;
        Reflected = 0;
        inputs.AsSpan(0, Scattering.VoltageSourceColumn).Clear(); // keep the source values
        Ports.Reset();
    }
}
