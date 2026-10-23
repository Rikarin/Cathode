namespace Cathode.Dsp.Wdf;

/// <summary>
///     An adapted wave digital one-port: a leaf element, or an adaptor together with its subtree. Waves use the voltage
///     definition a = v + R i, b = v - R i (Fettweis), so v = (a + b) / 2 and i = (a - b) / 2R, with i flowing into the
///     port. R is the port resistance, chosen so that the port's reflected wave never depends on its incident wave.
/// </summary>
/// <remarks>
///     Implementations are structs composed through generic adaptors (<see cref="Series{TA,TB}" />,
///     <see cref="Parallel{TA,TB}" />), so the JIT specialises every tree and inlines it: no virtual dispatch per sample
///     (after Werner's thesis, Stanford 2016, and chowdsp_wdf's compile-time trees). A sample is evaluated as
///     <see cref="Reflect" /> bottom-up, the root's scattering, then <see cref="Incident" /> top-down; see
///     <see cref="WdfCircuit{TTree,TRoot}" />. Store trees in mutable fields and never copy them by value mid-stream.
/// </remarks>
public interface IWdfPort {
    /// <summary>Port resistance, in ohms, as of the last <see cref="Propagate" />.</summary>
    double Resistance { get; }

    /// <summary>The wave this port last reflected towards its parent.</summary>
    double Reflected { get; }

    /// <summary>Port voltage at the last evaluated sample.</summary>
    double Voltage { get; }

    /// <summary>Current into the port at the last evaluated sample.</summary>
    double Current { get; }

    /// <summary>
    ///     Recomputes the port resistance (and every adaptor coefficient below) from the component values, and returns it.
    ///     Call after changing a component, then adapt the root to the result.
    /// </summary>
    double Propagate();

    /// <summary>Computes and returns the reflected wave b from the element state and the subtree.</summary>
    double Reflect();

    /// <summary>Accepts the incident wave a from the parent and updates the element state.</summary>
    void Incident(double a);

    /// <summary>Clears the element state (capacitor charge, inductor flux) without touching component values.</summary>
    void Reset();
}

/// <summary>
///     An unadapted element at the root of a tree: it sees the whole tree as one port of resistance R and maps the
///     tree's reflected wave to the wave it sends back. Nonlinear elements live here, where the scattering can be solved
///     explicitly instead of through a delay-free loop.
/// </summary>
public interface IWdfRoot {
    /// <summary>Port voltage at the last evaluated sample.</summary>
    double Voltage { get; }

    /// <summary>Tells the root the resistance of the tree it terminates. Called after every <see cref="IWdfPort.Propagate" />.</summary>
    void Adapt(double resistance);

    /// <summary>Returns the wave b sent back into the tree for the incident wave <paramref name="a" />.</summary>
    double Scatter(double a);

    /// <summary>Clears any state the root keeps between samples.</summary>
    void Reset();
}
