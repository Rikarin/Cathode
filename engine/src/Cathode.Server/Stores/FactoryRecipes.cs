namespace Cathode.Server.Stores;

/// <summary>
///     The factory bank's recipes (<see cref="FactoryRecipe" />), one file per group of genres. Each recreates the rig
///     of a well-known record or player as closely as the blocks allow: the amp model that is, or is closest to, the
///     one played, its usual cab, and the pedals and studio effects of that song or era. Names are the song's or the
///     tone's; the player is named only in the description. Descriptions are Cathode's own words.
/// </summary>
public static partial class FactoryRecipes {
    /// <summary>Every recipe, in bank order.</summary>
    public static IEnumerable<FactoryRecipe> All => [
        ..Blues(),
        ..ClassicRock(),
        ..HardRock(),
        ..Metal(),
        ..Funk(),
        ..Roots(),
        ..Jazz(),
        ..Alternative(),
        ..Atmospheric()
    ];

    static FactoryRecipe Preset(string name, string description, params string[] tags) => new(name, description, tags);
}
