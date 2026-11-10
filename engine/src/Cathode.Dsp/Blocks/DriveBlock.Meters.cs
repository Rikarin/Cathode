using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Blocks;

/// <summary>The Drive block's own meters (<see cref="IBlockMeters" />): its circuit solvers' fault counts.</summary>
public sealed partial class DriveBlock : IBlockMeters {
    /// <summary>
    ///     What the circuit types' solvers gave up on since the block was made, as counts (not dB), 0 on a healthy rig
    ///     and with a type that is not a circuit: the Amp block's three meters (<see cref="SolverFaults.MeterIds" />),
    ///     the numbers to quote in a bug report.
    /// </summary>
    public IReadOnlyList<string> MeterIds => SolverFaults.MeterIds;

    /// <summary>Both channels' <see cref="SolverFaults" />, over every circuit type played. Allocation-free.</summary>
    public SolverFaults Faults => left.Circuit.Faults + right.Circuit.Faults;

    public void ReadMeters(Span<float> values) => Faults.Write(values);

    /// <summary>The counts stay while the block is bypassed.</summary>
    public void ReadRestingMeters(Span<float> values) => ReadMeters(values);
}
