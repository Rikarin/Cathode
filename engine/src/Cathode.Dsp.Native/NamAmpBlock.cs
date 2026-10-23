using Cathode.Dsp.Blocks;
using Cathode.Dsp.Capture;

namespace Cathode.Dsp.Native;

/// <summary>
///     The capture amp with Neural Amp Modeler support: .nam files run on the reference C++ core, AIDA-X files
///     (.aidax, .json) in managed code.
/// </summary>
public sealed class NamAmpBlock() : CaptureAmpBlock(LoadModel) {
    /// <summary>Metadata of the loaded model when it is a NAM model.</summary>
    public NamModelInfo? Model => (CaptureModel as NamModel)?.Info;

    /// <summary>Control thread, before the block is published: resize a slimmable model (0 = Lite, 1 = Full).</summary>
    public void SetModelSize(double size) => (CaptureModel as NamModel)?.SetSize(size);

    public static ICaptureModel LoadModel(string path) =>
        AidaModel.IsAidaFile(path) ? AidaModel.Load(path) : NamModel.Load(path);
}
