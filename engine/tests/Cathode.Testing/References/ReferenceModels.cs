using Cathode.Dsp.Native;

namespace Cathode.Testing.References;

/// <summary>A reference amp that renders a whole signal offline, at its own <see cref="SampleRate" />.</summary>
public interface IReferenceModel : IDisposable {
    string Architecture { get; }

    double SampleRate { get; }

    /// <summary>Renders <paramref name="input" /> (digital full scale) from a cleared state.</summary>
    float[] Render(ReadOnlySpan<float> input);
}

/// <summary>
///     A NAM capture run by the reference C++ core through <c>cth_nam</c> (needs <c>native/build.sh</c>).
/// </summary>
public sealed class NamReferenceModel(NamModel model, double sampleRate) : IReferenceModel {
    const int Block = 4_096;

    public string Architecture => "NAM " + model.Architecture;

    public double SampleRate { get; } = sampleRate;

    /// <summary>True when the native library is built and loadable.</summary>
    public static bool IsAvailable => NamModel.IsLibraryPresent;

    public NamModelInfo Info => model.Info;

    public static NamReferenceModel Load(string path, double? sampleRate = null) {
        NamModel.SetFastTanh(false);
        var model = NamModel.Load(path);
        return new(model, sampleRate ?? (model.ExpectedSampleRate > 0 ? model.ExpectedSampleRate : 48_000));
    }

    public float[] Render(ReadOnlySpan<float> input) {
        model.Reset(SampleRate, Block);
        var output = new float[input.Length];
        for (var start = 0; start < input.Length; start += Block) {
            var length = Math.Min(Block, input.Length - start);
            model.Process(input.Slice(start, length), output.AsSpan(start, length));
        }

        return output;
    }

    public void Dispose() => model.Dispose();
}

/// <summary>Opens the runner for a manifest entry's file.</summary>
public static class ReferenceModels {
    /// <summary>Why <paramref name="entry" /> cannot be rendered on this machine, or null if it can.</summary>
    public static string? Unsupported(ReferenceEntry entry) =>
        entry.Format switch {
            "nam" when !NamReferenceModel.IsAvailable => "cth_nam is not built (run engine/native/build.sh)",
            "nam" or "guitarml-lstm" => null,
            "measured-response" =>
                "a measured response has no model to render (only its frequency response is compared)",
            _ => $"unknown format '{entry.Format}'"
        };

    public static IReferenceModel Open(ReferenceEntry entry, string path) {
        switch (entry.Format) {
            case "nam":
                return NamReferenceModel.Load(path, entry.SampleRate);
            case "guitarml-lstm":
                var model = GuitarMlLstmModel.Load(path, entry.EffectiveSampleRate);
                model.SetConditioning(entry.Conditioning ?? []);
                return model;
            default:
                throw new NotSupportedException(Unsupported(entry));
        }
    }
}
