using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Wdf;

/// <summary>
///     The children of an R-type adaptor, in netlist port order. Implemented by <see cref="Ports{TA, TB}" /> and its
///     siblings of other arities, which hold each child as a public field (<c>A</c>, <c>B</c>, ...) so that sources and
///     components deep inside are set in place. Like the adaptors, they are structs composed through generics: the JIT
///     specialises and inlines every child's methods.
/// </summary>
public interface IWdfPortList {
    /// <summary>Number of ports.</summary>
    int Count { get; }

    /// <summary>Propagates every child and writes its port resistance.</summary>
    void Propagate(Span<double> resistances);

    /// <summary>Calls every child's <see cref="IWdfPort.Reflect" /> and writes the waves.</summary>
    void Reflect(Span<double> waves);

    /// <summary>Hands every child its incident wave.</summary>
    void Incident(ReadOnlySpan<double> waves);

    /// <summary>Resets every child.</summary>
    void Reset();
}

/// <summary>One adapted port of an R-type adaptor, in netlist port order.</summary>
public struct Ports<TA>(TA a) : IWdfPortList
    where TA : struct, IWdfPort {
    public TA A = a;

    public readonly int Count => 1;

    public void Propagate(Span<double> resistances) {
        resistances[0] = A.Propagate();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reflect(Span<double> waves) {
        waves[0] = A.Reflect();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Incident(ReadOnlySpan<double> waves) {
        A.Incident(waves[0]);
    }

    public void Reset() {
        A.Reset();
    }
}

/// <summary>Two adapted ports of an R-type adaptor, in netlist port order.</summary>
public struct Ports<TA, TB>(TA a, TB b) : IWdfPortList
    where TA : struct, IWdfPort
    where TB : struct, IWdfPort {
    public TA A = a;
    public TB B = b;

    public readonly int Count => 2;

    public void Propagate(Span<double> resistances) {
        resistances[0] = A.Propagate();
        resistances[1] = B.Propagate();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reflect(Span<double> waves) {
        waves[0] = A.Reflect();
        waves[1] = B.Reflect();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Incident(ReadOnlySpan<double> waves) {
        A.Incident(waves[0]);
        B.Incident(waves[1]);
    }

    public void Reset() {
        A.Reset();
        B.Reset();
    }
}

/// <summary>Three adapted ports of an R-type adaptor, in netlist port order.</summary>
public struct Ports<TA, TB, TC>(TA a, TB b, TC c) : IWdfPortList
    where TA : struct, IWdfPort
    where TB : struct, IWdfPort
    where TC : struct, IWdfPort {
    public TA A = a;
    public TB B = b;
    public TC C = c;

    public readonly int Count => 3;

    public void Propagate(Span<double> resistances) {
        resistances[0] = A.Propagate();
        resistances[1] = B.Propagate();
        resistances[2] = C.Propagate();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reflect(Span<double> waves) {
        waves[0] = A.Reflect();
        waves[1] = B.Reflect();
        waves[2] = C.Reflect();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Incident(ReadOnlySpan<double> waves) {
        A.Incident(waves[0]);
        B.Incident(waves[1]);
        C.Incident(waves[2]);
    }

    public void Reset() {
        A.Reset();
        B.Reset();
        C.Reset();
    }
}

/// <summary>Four adapted ports of an R-type adaptor, in netlist port order.</summary>
public struct Ports<TA, TB, TC, TD>(TA a, TB b, TC c, TD d) : IWdfPortList
    where TA : struct, IWdfPort
    where TB : struct, IWdfPort
    where TC : struct, IWdfPort
    where TD : struct, IWdfPort {
    public TA A = a;
    public TB B = b;
    public TC C = c;
    public TD D = d;

    public readonly int Count => 4;

    public void Propagate(Span<double> resistances) {
        resistances[0] = A.Propagate();
        resistances[1] = B.Propagate();
        resistances[2] = C.Propagate();
        resistances[3] = D.Propagate();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reflect(Span<double> waves) {
        waves[0] = A.Reflect();
        waves[1] = B.Reflect();
        waves[2] = C.Reflect();
        waves[3] = D.Reflect();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Incident(ReadOnlySpan<double> waves) {
        A.Incident(waves[0]);
        B.Incident(waves[1]);
        C.Incident(waves[2]);
        D.Incident(waves[3]);
    }

    public void Reset() {
        A.Reset();
        B.Reset();
        C.Reset();
        D.Reset();
    }
}

/// <summary>Five adapted ports of an R-type adaptor, in netlist port order.</summary>
public struct Ports<TA, TB, TC, TD, TE>(TA a, TB b, TC c, TD d, TE e) : IWdfPortList
    where TA : struct, IWdfPort
    where TB : struct, IWdfPort
    where TC : struct, IWdfPort
    where TD : struct, IWdfPort
    where TE : struct, IWdfPort {
    public TA A = a;
    public TB B = b;
    public TC C = c;
    public TD D = d;
    public TE E = e;

    public readonly int Count => 5;

    public void Propagate(Span<double> resistances) {
        resistances[0] = A.Propagate();
        resistances[1] = B.Propagate();
        resistances[2] = C.Propagate();
        resistances[3] = D.Propagate();
        resistances[4] = E.Propagate();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reflect(Span<double> waves) {
        waves[0] = A.Reflect();
        waves[1] = B.Reflect();
        waves[2] = C.Reflect();
        waves[3] = D.Reflect();
        waves[4] = E.Reflect();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Incident(ReadOnlySpan<double> waves) {
        A.Incident(waves[0]);
        B.Incident(waves[1]);
        C.Incident(waves[2]);
        D.Incident(waves[3]);
        E.Incident(waves[4]);
    }

    public void Reset() {
        A.Reset();
        B.Reset();
        C.Reset();
        D.Reset();
        E.Reset();
    }
}

/// <summary>Six adapted ports of an R-type adaptor, in netlist port order.</summary>
public struct Ports<TA, TB, TC, TD, TE, TF>(TA a, TB b, TC c, TD d, TE e, TF f) : IWdfPortList
    where TA : struct, IWdfPort
    where TB : struct, IWdfPort
    where TC : struct, IWdfPort
    where TD : struct, IWdfPort
    where TE : struct, IWdfPort
    where TF : struct, IWdfPort {
    public TA A = a;
    public TB B = b;
    public TC C = c;
    public TD D = d;
    public TE E = e;
    public TF F = f;

    public readonly int Count => 6;

    public void Propagate(Span<double> resistances) {
        resistances[0] = A.Propagate();
        resistances[1] = B.Propagate();
        resistances[2] = C.Propagate();
        resistances[3] = D.Propagate();
        resistances[4] = E.Propagate();
        resistances[5] = F.Propagate();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reflect(Span<double> waves) {
        waves[0] = A.Reflect();
        waves[1] = B.Reflect();
        waves[2] = C.Reflect();
        waves[3] = D.Reflect();
        waves[4] = E.Reflect();
        waves[5] = F.Reflect();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Incident(ReadOnlySpan<double> waves) {
        A.Incident(waves[0]);
        B.Incident(waves[1]);
        C.Incident(waves[2]);
        D.Incident(waves[3]);
        E.Incident(waves[4]);
        F.Incident(waves[5]);
    }

    public void Reset() {
        A.Reset();
        B.Reset();
        C.Reset();
        D.Reset();
        E.Reset();
        F.Reset();
    }
}

/// <summary>Seven adapted ports of an R-type adaptor, in netlist port order.</summary>
public struct Ports<TA, TB, TC, TD, TE, TF, TG>(TA a, TB b, TC c, TD d, TE e, TF f, TG g) : IWdfPortList
    where TA : struct, IWdfPort
    where TB : struct, IWdfPort
    where TC : struct, IWdfPort
    where TD : struct, IWdfPort
    where TE : struct, IWdfPort
    where TF : struct, IWdfPort
    where TG : struct, IWdfPort {
    public TA A = a;
    public TB B = b;
    public TC C = c;
    public TD D = d;
    public TE E = e;
    public TF F = f;
    public TG G = g;

    public readonly int Count => 7;

    public void Propagate(Span<double> resistances) {
        resistances[0] = A.Propagate();
        resistances[1] = B.Propagate();
        resistances[2] = C.Propagate();
        resistances[3] = D.Propagate();
        resistances[4] = E.Propagate();
        resistances[5] = F.Propagate();
        resistances[6] = G.Propagate();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reflect(Span<double> waves) {
        waves[0] = A.Reflect();
        waves[1] = B.Reflect();
        waves[2] = C.Reflect();
        waves[3] = D.Reflect();
        waves[4] = E.Reflect();
        waves[5] = F.Reflect();
        waves[6] = G.Reflect();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Incident(ReadOnlySpan<double> waves) {
        A.Incident(waves[0]);
        B.Incident(waves[1]);
        C.Incident(waves[2]);
        D.Incident(waves[3]);
        E.Incident(waves[4]);
        F.Incident(waves[5]);
        G.Incident(waves[6]);
    }

    public void Reset() {
        A.Reset();
        B.Reset();
        C.Reset();
        D.Reset();
        E.Reset();
        F.Reset();
        G.Reset();
    }
}

/// <summary>Eight adapted ports of an R-type adaptor, in netlist port order.</summary>
public struct Ports<TA, TB, TC, TD, TE, TF, TG, TH>(TA a, TB b, TC c, TD d, TE e, TF f, TG g, TH h) : IWdfPortList
    where TA : struct, IWdfPort
    where TB : struct, IWdfPort
    where TC : struct, IWdfPort
    where TD : struct, IWdfPort
    where TE : struct, IWdfPort
    where TF : struct, IWdfPort
    where TG : struct, IWdfPort
    where TH : struct, IWdfPort {
    public TA A = a;
    public TB B = b;
    public TC C = c;
    public TD D = d;
    public TE E = e;
    public TF F = f;
    public TG G = g;
    public TH H = h;

    public readonly int Count => 8;

    public void Propagate(Span<double> resistances) {
        resistances[0] = A.Propagate();
        resistances[1] = B.Propagate();
        resistances[2] = C.Propagate();
        resistances[3] = D.Propagate();
        resistances[4] = E.Propagate();
        resistances[5] = F.Propagate();
        resistances[6] = G.Propagate();
        resistances[7] = H.Propagate();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reflect(Span<double> waves) {
        waves[0] = A.Reflect();
        waves[1] = B.Reflect();
        waves[2] = C.Reflect();
        waves[3] = D.Reflect();
        waves[4] = E.Reflect();
        waves[5] = F.Reflect();
        waves[6] = G.Reflect();
        waves[7] = H.Reflect();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Incident(ReadOnlySpan<double> waves) {
        A.Incident(waves[0]);
        B.Incident(waves[1]);
        C.Incident(waves[2]);
        D.Incident(waves[3]);
        E.Incident(waves[4]);
        F.Incident(waves[5]);
        G.Incident(waves[6]);
        H.Incident(waves[7]);
    }

    public void Reset() {
        A.Reset();
        B.Reset();
        C.Reset();
        D.Reset();
        E.Reset();
        F.Reset();
        G.Reset();
        H.Reset();
    }
}
