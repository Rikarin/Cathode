namespace Cathode.Dsp;

/// <summary>
///     A block that needs a file (a NAM model, an impulse response) besides its parameters. The graph compiler calls
///     <see cref="LoadAsset" /> on the control thread, before <see cref="IDspBlock.Prepare" />.
/// </summary>
public interface IAssetConsumer {
    /// <summary>Names of the asset slots this block accepts, e.g. <c>model</c>.</summary>
    IReadOnlyList<string> AssetSlots { get; }

    /// <summary>
    ///     Loads <paramref name="path" /> into <paramref name="slot" />. Throws on unreadable or unsupported files.
    /// </summary>
    void LoadAsset(string slot, string path);
}
