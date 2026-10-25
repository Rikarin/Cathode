namespace Cathode.Server.Stores;

/// <summary>
///     Preset Leveling for the factory bank: how much each factory preset's amp Level (per channel) and scene levels
///     move so that every scene plays at <see cref="TargetLufs" /> plus its lift (a lead scene's few dB more). The
///     measurement is the editor's Preset Leveling's, BS.1770 momentary loudness gated and integrated, of a
///     guitar-like test phrase with any expression pedal half way (FactoryPresetLevelTests). Each amp channel takes the
///     error of the first scene that uses it, the way a player levels an amp channel; the scene levels take the rest
///     (a drive or an effect switched on by the scene). The table (<c>FactoryLevels.Table.cs</c>) is generated: after
///     changing a recipe, run <c>CATHODE_UPDATE_FACTORY_LEVELS=1 dotnet test --filter FactoryPresetLevelTests</c>.
/// </summary>
public static partial class FactoryLevels {
    /// <summary>The bank's loudness, in LUFS: the editor's Preset Leveling's default target.</summary>
    public const float TargetLufs = -18f;

    /// <summary>
    ///     The level changes of one preset: <see cref="Amp" />[c] dB on the Level of the Circuit Amp's channel c (A
    ///     first), and each planned scene's level in dB.
    /// </summary>
    public sealed record Trim(float[] Amp, float[] Scenes);

    public static Trim? Of(string id) => Table.GetValueOrDefault(id);
}
