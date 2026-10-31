namespace Cathode.Server.Stores;

/// <summary>
///     Preset Leveling for the factory bank: every factory preset's amp Level (per channel) and scene levels are set so
///     that each planned scene plays at <see cref="TargetLufs" /> plus its lift (a lead scene's few dB more), as
///     <c>Factory/bank.json</c> lists them. The measurement is the editor's Preset Leveling's, BS.1770 momentary
///     loudness gated and integrated, of a guitar-like test phrase with any expression pedal half way
///     (FactoryPresetLevelTests). Each amp channel takes the error of the first scene that uses it, the way a player
///     levels an amp channel; the scene levels take the rest. After changing a preset file, run
///     <c>CATHODE_UPDATE_FACTORY_LEVELS=1 dotnet test --filter FactoryPresetLevelTests</c>: it rewrites the levels in
///     the files.
/// </summary>
public static class FactoryLevels {
    /// <summary>The bank's loudness, in LUFS: the editor's Preset Leveling's default target.</summary>
    public const float TargetLufs = -18f;
}
