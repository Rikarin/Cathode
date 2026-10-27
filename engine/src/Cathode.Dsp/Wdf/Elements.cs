using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Wdf;

/// <summary>A resistor: b = 0 when the port resistance equals the resistance.</summary>
public struct Resistor(double resistance) : IWdfPort {
    /// <summary>Resistance in ohms. <see cref="IWdfPort.Propagate" /> the tree after changing it.</summary>
    public double Value = resistance;

    double a;

    public readonly double Resistance => Value;

    public readonly double Reflected => 0;

    public readonly double Voltage => 0.5 * a;

    public readonly double Current => 0.5 * a / Value;

    public readonly double Propagate() => Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly double Reflect() => 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Incident(double a) => this.a = a;

    public void Reset() => a = 0;
}

/// <summary>
///     A capacitor discretised with the trapezoidal rule (bilinear transform): R = T / 2C and b[n] = a[n - 1]. Linear
///     WDF networks of these are exactly the bilinear transforms of their analog prototypes.
/// </summary>
public struct Capacitor(double capacitance, double sampleRate) : IWdfPort {
    double a;
    double b;

    public double Resistance { get; private set; } = 0.5 / (capacitance * sampleRate);

    public double Reflected { get; private set; }

    /// <summary>
    ///     The port voltage this sample, (a + b) / 2: b is the wave the capacitor reflected before its incident one.
    /// </summary>
    public readonly double Voltage => 0.5 * (a + b);

    public readonly double Current => 0.5 * (a - b) / Resistance;

    /// <summary>
    ///     Changes the capacitance or sample rate, keeping the stored wave. <see cref="IWdfPort.Propagate" /> after.
    /// </summary>
    public void Configure(double capacitance, double sampleRate) => Resistance = 0.5 / (capacitance * sampleRate);

    /// <summary>
    ///     Charges the capacitor to <paramref name="volts" /> at rest (no current), e.g. to start a circuit at its DC
    ///     operating point instead of letting the coupling and bypass capacitors charge up from zero.
    /// </summary>
    public void Precharge(double volts) {
        a = volts;
        b = volts;
        Reflected = volts;
    }

    public readonly double Propagate() => Resistance;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly double Reflect() => Reflected;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Incident(double a) {
        this.a = a;
        b = Reflected;
        Reflected = a;
    }

    public void Reset() {
        a = 0;
        b = 0;
        Reflected = 0;
    }
}

/// <summary>An inductor discretised with the trapezoidal rule: R = 2L / T and b[n] = -a[n - 1].</summary>
public struct Inductor(double inductance, double sampleRate) : IWdfPort {
    double a;
    double b;
    double state;

    public double Resistance { get; private set; } = 2 * inductance * sampleRate;

    public readonly double Reflected => -state;

    /// <summary>
    ///     The port voltage this sample, (a + b) / 2: b is the wave the inductor reflected before its incident one.
    /// </summary>
    public readonly double Voltage => 0.5 * (a + b);

    public readonly double Current => 0.5 * (a - b) / Resistance;

    /// <summary>
    ///     Changes the inductance or sample rate, keeping the stored wave. <see cref="IWdfPort.Propagate" /> after.
    /// </summary>
    public void Configure(double inductance, double sampleRate) => Resistance = 2 * inductance * sampleRate;

    /// <summary>
    ///     Starts the inductor carrying <paramref name="amps" /> at rest (no voltage across it), e.g. a bias current at
    ///     its DC operating point. Call after <see cref="Configure" />: the stored wave depends on the port resistance.
    /// </summary>
    public void Precharge(double amps) {
        a = amps * Resistance;
        b = -a;
        state = a;
    }

    public readonly double Propagate() => Resistance;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly double Reflect() => -state;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Incident(double a) {
        this.a = a;
        b = -state;
        state = a;
    }

    public void Reset() {
        a = 0;
        b = 0;
        state = 0;
    }
}

/// <summary>A voltage source with a series resistance (v = V + R i): b = V.</summary>
public struct ResistiveVoltageSource(double resistance) : IWdfPort {
    /// <summary>Source resistance in ohms. <see cref="IWdfPort.Propagate" /> the tree after changing it.</summary>
    public double Value = resistance;

    /// <summary>Source voltage; set it before each <see cref="WdfCircuit{TTree,TRoot}.Tick" />.</summary>
    public double Volts;

    double a;

    public readonly double Resistance => Value;

    public readonly double Reflected => Volts;

    public readonly double Voltage => 0.5 * (a + Volts);

    public readonly double Current => 0.5 * (a - Volts) / Value;

    public readonly double Propagate() => Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly double Reflect() => Volts;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Incident(double a) => this.a = a;

    public void Reset() => a = 0;
}

/// <summary>
///     A current source with a parallel resistance, pushing <see cref="Amps" /> out of its positive terminal
///     (v = R (i + I)): b = R I.
/// </summary>
public struct ResistiveCurrentSource(double resistance) : IWdfPort {
    /// <summary>Parallel resistance in ohms. <see cref="IWdfPort.Propagate" /> the tree after changing it.</summary>
    public double Value = resistance;

    /// <summary>Source current; set it before each <see cref="WdfCircuit{TTree,TRoot}.Tick" />.</summary>
    public double Amps;

    double a;

    public readonly double Resistance => Value;

    public readonly double Reflected => Value * Amps;

    public readonly double Voltage => 0.5 * (a + Value * Amps);

    public readonly double Current => 0.5 * (a - Value * Amps) / Value;

    public readonly double Propagate() => Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly double Reflect() => Value * Amps;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Incident(double a) => this.a = a;

    public void Reset() => a = 0;
}
