using System.Reflection;
using System.Security.Cryptography;

namespace Cathode.Server.Stores;

/// <summary>
///     The factory assets: files built into the Server (<c>src/Cathode.Server/Factory/assets</c>, embedded), which
///     <see cref="AssetStore" /> puts into the store at start-up, read-only. A new library then has something to put
///     in an IR Player, or in a Cab slot whose Source is IR, before the user has uploaded anything.
/// </summary>
/// <remarks>
///     They are impulse responses rendered from Cathode's own cab model (<c>Cathode.Dsp.Cabinets.CabIrSynthesizer</c>,
///     what the Cab block's DynaCab source plays), not recordings of real cabinets: <c>FactoryAssetTests</c> names the
///     speaker, cabinet, microphone and placement of each and writes the files. A file's name is the asset's name.
///     No amp capture is among them: the folder takes <c>.nam</c> and <c>.aidax</c> files as well, but a capture has
///     to be one the project made or may redistribute (see <c>Factory/README.md</c>).
/// </remarks>
public static class FactoryAssets {
    /// <summary>The embedded files' prefix (the csproj's LogicalName).</summary>
    const string Resource = "factory/assets/";

    /// <summary>One factory asset: its content-addressed reference, its name (a file name), kind and bytes.</summary>
    public sealed record Entry(string Ref, string Name, string Kind, byte[] Content);

    /// <summary>Every factory asset, by name.</summary>
    public static IReadOnlyList<Entry> All { get; } = Load();

    static List<Entry> Load() {
        var assembly = typeof(FactoryAssets).Assembly;
        return [
            ..assembly.GetManifestResourceNames()
                .Where(static name => name.StartsWith(Resource, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .Select(name => Read(assembly, name))
        ];
    }

    static Entry Read(Assembly assembly, string resource) {
        var name = resource[Resource.Length..];
        var kind = AssetStore.KindOf(name)
            ?? throw new InvalidOperationException($"The factory asset '{name}' is not a kind of asset.");
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var content = new MemoryStream();
        stream.CopyTo(content);
        var bytes = content.ToArray();
        return new("sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes)), name, kind, bytes);
    }
}
