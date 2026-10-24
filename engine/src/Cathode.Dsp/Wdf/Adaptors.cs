using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Wdf;

/// <summary>
///     Three-port series adaptor, adapted at its parent port: R = R_A + R_B. The current into the parent port flows
///     through A and then B, so the children carry the same current and their voltages add up to the parent's.
/// </summary>
public struct Series<TA, TB>(TA first, TB second) : IWdfPort
    where TA : struct, IWdfPort
    where TB : struct, IWdfPort {
    public TA A = first;
    public TB B = second;
    double a;
    double conductance;
    double ratio; // R_A / R

    public double Resistance { get; private set; }
    public double Reflected { get; private set; }
    public readonly double Voltage => 0.5 * (a + Reflected);
    public readonly double Current => 0.5 * (a - Reflected) * conductance;

    public double Propagate() {
        var ra = A.Propagate();
        Resistance = ra + B.Propagate();
        conductance = 1 / Resistance;
        ratio = ra / Resistance;
        return Resistance;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Reflect() {
        Reflected = A.Reflect() + B.Reflect();
        return Reflected;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Incident(double a) {
        var ba = A.Reflected;
        var toA = ba + ratio * (a - ba - B.Reflected);
        A.Incident(toA);
        B.Incident(a - toA);
        this.a = a;
    }

    public void Reset() {
        a = 0;
        Reflected = 0;
        A.Reset();
        B.Reset();
    }
}

/// <summary>
///     Three-port parallel adaptor, adapted at its parent port: G = G_A + G_B. The two children share one voltage.
/// </summary>
public struct Parallel<TA, TB>(TA first, TB second) : IWdfPort
    where TA : struct, IWdfPort
    where TB : struct, IWdfPort {
    public TA A = first;
    public TB B = second;
    double a;
    double difference; // b_B - b_A from the last Reflect
    double conductance;
    double ratio; // G_A / G

    public double Resistance { get; private set; }
    public double Reflected { get; private set; }
    public readonly double Voltage => 0.5 * (a + Reflected);
    public readonly double Current => 0.5 * (a - Reflected) * conductance;

    public double Propagate() {
        var ga = 1 / A.Propagate();
        var g = ga + 1 / B.Propagate();
        Resistance = 1 / g;
        conductance = g;
        ratio = ga / g;
        return Resistance;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Reflect() {
        var ba = A.Reflect();
        var bb = B.Reflect();
        difference = bb - ba;
        Reflected = bb - ratio * difference;
        return Reflected;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Incident(double a) {
        var toB = a + Reflected - B.Reflected;
        A.Incident(toB + difference);
        B.Incident(toB);
        this.a = a;
    }

    public void Reset() {
        a = 0;
        Reflected = 0;
        difference = 0;
        A.Reset();
        B.Reset();
    }
}
