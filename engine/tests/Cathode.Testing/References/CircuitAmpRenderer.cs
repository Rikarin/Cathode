using System.Globalization;
using Cathode.Dsp.Blocks;

namespace Cathode.Testing.References;

/// <summary>
///     Renders the Circuit Amp block offline through its public surface only: parameters by id, so expert parameters
///     added later work without changes here. The input is in volts (the block's 1.0 = 1 V).
/// </summary>
public sealed class CircuitAmpRenderer : IDisposable {
    const int Block = 64;

    readonly CircuitAmpBlock block = new();
    readonly double sampleRate;

    public CircuitAmpRenderer(double sampleRate) {
        this.sampleRate = sampleRate;
        block.Prepare(new(sampleRate, Block));
    }

    public IReadOnlyList<string> Types =>
        CircuitAmpBlock.Info.Params[CircuitAmpBlock.Info.IndexOf("type")].Options ?? [];

    /// <summary>The parameter ids the block has (for tolerance names and the CLI).</summary>
    public static IEnumerable<string> ParameterIds => CircuitAmpBlock.Info.Params.Select(static p => p.Id);

    /// <summary>
    ///     Renders <paramref name="volts" /> with <paramref name="settings" />, then <paramref name="overrides" /> (by
    ///     parameter id) on top. Every render starts from the amp's operating point.
    /// </summary>
    public float[] Render(
        ReadOnlySpan<float> volts,
        CircuitSettings settings,
        IReadOnlyDictionary<string, double>? overrides = null
    ) {
        Configure(settings, overrides);
        var left = volts.ToArray();
        var right = new float[left.Length];
        for (var offset = 0; offset < left.Length; offset += Block) {
            var frames = Math.Min(Block, left.Length - offset);
            block.Process(new(left.AsSpan(offset, frames), right.AsSpan(offset, frames)), new(sampleRate, 120, offset));
        }

        return left;
    }

    public void Dispose() => block.Dispose();

    void Configure(CircuitSettings settings, IReadOnlyDictionary<string, double>? overrides) {
        var info = CircuitAmpBlock.Info;
        for (var i = 0; i < info.Params.Count; i++) {
            block.Params.SetTarget(i, info.Params[i].Default);
        }

        var type = Types.ToList().FindIndex(t => string.Equals(t, settings.Type, StringComparison.OrdinalIgnoreCase));
        if (type < 0) {
            throw new ArgumentException(
                $"Unknown circuit amp type '{settings.Type}' (have {string.Join(", ", Types)}).",
                nameof(settings)
            );
        }

        block.Params.SetTarget(info.IndexOf("type"), type);
        foreach (var source in new[] { settings.Params, overrides }) {
            foreach (var (id, value) in source ?? new Dictionary<string, double>()) {
                var index = info.IndexOf(id);
                if (index < 0) {
                    throw new ArgumentException(
                        $"The circuit amp has no parameter '{id}' (have {string.Join(", ", ParameterIds)}).",
                        nameof(settings)
                    );
                }

                block.Params.SetTarget(index, info.Params[index].Sanitize((float)value));
            }
        }

        block.Reset();
    }

    /// <summary>Settings as text, e.g. <c>Plexi gain=6 bright=1</c>.</summary>
    public static string Describe(CircuitSettings settings, IReadOnlyDictionary<string, double>? overrides = null) {
        var all = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var source in new[] { settings.Params, overrides }) {
            foreach (var (id, value) in source ?? new Dictionary<string, double>()) {
                all[id] = value;
            }
        }

        return string.Join(
            ' ',
            all.Select(static p => $"{p.Key}={p.Value.ToString("0.##", CultureInfo.InvariantCulture)}")
                .Prepend(settings.Type)
        );
    }
}
