using Cathode.Contracts;
using Cathode.Dsp.Parameters;
using Cathode.Server.Rig;
using System.Globalization;

namespace Cathode.Server.Stores;

/// <summary>
///     A factory preset as a recipe: the blocks of its main row in signal order, from Input 1 to Output 1, each with
///     its channels' settings, and its scenes as changes from the blocks' starting state. Settings are written
///     <c>"id=value id=value"</c>; a value that is not a number names one of the parameter's options, with
///     underscores for spaces (<c>type=Classic_Plate</c>), matched exactly or by a part of the name only one option
///     has (<c>m1speaker=Greenback</c>). Anything not set takes the block's default.
/// </summary>
/// <remarks>
///     The amp's Level and the scene levels are not part of the recipe: Preset Leveling sets them
///     (<see cref="FactoryLevels" />), so that every factory preset plays at the bank's loudness.
/// </remarks>
public sealed class FactoryRecipe(string name, string description, params string[] tags) {
    /// <summary>The row the Input-to-Output path runs on.</summary>
    public const int MainRow = Preset.DefaultRow;

    /// <summary>The Input and Output take the first and last column; the blocks go in between.</summary>
    public const int MaxBlocks = 12;

    const string InputId = "in1";
    const string OutputId = "out1";

    /// <summary>The noise gate a driven preset gets when its recipe sets none.</summary>
    const string DrivenGate = "type=Intelligent threshold=-62";

    /// <summary>
    ///     The studio compressor after the cab of a preset with a clean scene: it leaves the playing alone and only
    ///     catches a hot pickup's peaks (above -12 dBFS, 20:1 on a peak detector, 1 ms look-ahead so the first instant
    ///     of a pick attack does not slip through), which a clean amp passes on undamped and which would otherwise clip
    ///     the output. Driven scenes, already compressed by their amps, peak below it.
    /// </summary>
    const string PeakCompressor =
        "type=Studio detector=Peak lookAhead=1_ms threshold=-12 ratio=20 attack=0.1 release=80 knee=3";

    static readonly string[] DrivenTags = ["Crunch", "Lead", "Fuzz", "High Gain"];

    readonly List<Part> parts = [];
    readonly List<SceneSpec> scenes = [];
    string inputSettings = string.Empty;
    double tempo = 120;

    public string Name => name;

    public string Id => FactoryPresets.IdPrefix + Slug(name);

    /// <summary>The scenes' extra loudness over the bank's target, in dB (a lead scene's lift), in order.</summary>
    public IReadOnlyList<float> Lifts => [..scenes.Select(static s => s.Lift)];

    /// <summary>The preset's tempo (for tempo-synced delays and modulation).</summary>
    public FactoryRecipe Tempo(double bpm) {
        tempo = bpm;
        return this;
    }

    /// <summary>Input 1's settings: its noise gate, mostly.</summary>
    public FactoryRecipe Input(string settings) {
        inputSettings = settings;
        return this;
    }

    /// <summary>
    ///     A Circuit Amp of <paramref name="model" /> (a type name), its speaker load following the Cab when the model
    ///     has one (a solid-state amp does not).
    /// </summary>
    public FactoryRecipe Amp(string model, params string[] channels) =>
        Add("circuitamp", $"type={Option(model)}", channels);

    /// <summary>
    ///     A Cab block whose four mics all point at <paramref name="speaker" /> in <paramref name="cabinet" />, mic 1 a
    ///     <paramref name="mic" /> (the others muted unless <paramref name="settings" /> unmutes them).
    /// </summary>
    public FactoryRecipe Cab(string cabinet, string speaker, string mic, string settings = "") {
        var box = string.Join(
            ' ',
            Enumerable.Range(1, 4).Select(m => $"m{m}cab={Option(cabinet)} m{m}speaker={Option(speaker)}")
        );
        return Add("cab", $"{box} m1mic={Option(mic)}", [settings]);
    }

    /// <summary>A Drive block of <paramref name="circuit" /> (its type name).</summary>
    public FactoryRecipe Drive(string circuit, string settings) => Add("drive", $"type={Option(circuit)}", [settings]);

    /// <summary>A block of <paramref name="type" /> with up to four channels' settings (A first).</summary>
    public FactoryRecipe Add(string type, params string[] channels) => Add(type, string.Empty, channels);

    /// <summary>The block added last starts bypassed (scenes engage it with <c>+id</c>).</summary>
    public FactoryRecipe Off() {
        parts[^1] = parts[^1] with { Bypassed = true };
        return this;
    }

    /// <summary>
    ///     Puts the block added last's <paramref name="param" /> on an expression pedal: External 1 by default
    ///     (<paramref name="source" />, ext1-ext8).
    /// </summary>
    public FactoryRecipe Pedal(string param, float min, float max, string source = "ext1") {
        parts[^1]
            .Modifiers.Add(
                new() {
                    Block = parts[^1].Id,
                    Param = param,
                    Source = source,
                    Min = min,
                    Max = max,
                    Curve = "linear"
                }
            );
        return this;
    }

    /// <summary>
    ///     A scene: <paramref name="changes" /> lists <c>+id</c> to engage a block, <c>-id</c> to bypass it and
    ///     <c>id=B</c> for its channel, where id is the block's (<c>drive1</c>) or its type's for the first of a type
    ///     (<c>drive</c>; <c>amp</c> for the Circuit Amp). <paramref name="lift" /> is how much louder than the bank's
    ///     target the scene should play (a lead scene's, in dB).
    /// </summary>
    public FactoryRecipe Scene(string sceneName, string changes = "", float lift = 0) {
        scenes.Add(new(sceneName, changes, lift));
        return this;
    }

    FactoryRecipe Add(string type, string common, string[] channels) {
        var number = parts.Count(p => p.Type == type) + 1;
        parts.Add(new(type, $"{type}{number}", common, channels.Length == 0 ? [string.Empty] : channels, false, []));
        return this;
    }

    /// <summary>
    ///     Builds the preset with <paramref name="level" />'s trims (the amps' Level, added to every channel, and each
    ///     scene's level). Every unknown id, bad option and out-of-range value goes into <paramref name="problems" />;
    ///     the values are then sanitised, so the preset always loads.
    /// </summary>
    public Preset Build(BlockCatalog catalog, FactoryLevels.Trim? level, List<string> problems) {
        void Problem(string text) => problems.Add($"{name}: {text}");

        if (scenes.Count is 0 or > Preset.SceneCount) {
            Problem($"a preset needs 1 to {Preset.SceneCount} scenes, not {scenes.Count}.");
        }

        var preset = new Preset {
            Id = Id,
            Name = name,
            Author = FactoryPresets.Author,
            Description = description,
            Tags = [..tags],
            Factory = true,
            Tempo = tempo,
            Blocks = [],
            Cables = [],
            Scenes = []
        };
        var input = InputBlock(catalog, Problem);
        var blocks = WithPeakCompressor();
        if (blocks.Count > MaxBlocks) {
            Problem($"{blocks.Count} blocks do not fit between Input and Output ({MaxBlocks} cells).");
        }

        preset.Blocks.Add(input);
        var previous = AddChain(preset, blocks, catalog, level, Problem);
        preset.Blocks.Add(new() { Id = OutputId, Type = BuiltInTypes.Output, Row = MainRow, Column = MaxBlocks + 1 });
        preset.Cables.Add(new() { From = previous, To = OutputId });

        // The scenes a recipe leaves out repeat its first, so an unplanned scene switch keeps the sound.
        for (var s = 0; s < Preset.SceneCount; s++) {
            preset.Scenes.Add(BuildScene(s, blocks, level, Problem));
        }

        return preset;
    }

    /// <summary>The Input block, with the recipe's gate settings (or the driven tags' default gate).</summary>
    PresetBlock InputBlock(BlockCatalog catalog, Action<string> problem) {
        var input = new PresetBlock { Id = InputId, Type = BuiltInTypes.Input, Row = MainRow, Column = 0 };
        var gate = inputSettings.Length > 0 ? inputSettings
            : tags.Any(DrivenTags.Contains) ? DrivenGate
            : string.Empty;
        if (gate.Length > 0) {
            input.Channels["A"] = Settings(catalog, BuiltInTypes.Input, gate, problem);
        }

        return input;
    }

    /// <summary>
    ///     The blocks in series on the main row after the Input, shunts filling the cells up to the Output, with
    ///     their pedal modifiers; returns the id of the last, which feeds the Output.
    /// </summary>
    static string AddChain(
        Preset preset,
        List<Part> blocks,
        BlockCatalog catalog,
        FactoryLevels.Trim? level,
        Action<string> problem
    ) {
        var previous = InputId;
        for (var column = 1; column <= MaxBlocks; column++) {
            var part = column - 1 < blocks.Count ? blocks[column - 1] : null;
            var block = part is null
                ? new PresetBlock { Id = $"shunt-r{MainRow}c{column}", Type = BuiltInTypes.Shunt }
                : Block(catalog, part, level, problem);
            block.Row = MainRow;
            block.Column = column;
            preset.Blocks.Add(block);
            preset.Cables.Add(new() { From = previous, To = block.Id });
            previous = block.Id;
            if (part is not null) {
                preset.Modifiers.AddRange(part.Modifiers);
            }
        }

        return previous;
    }

    /// <summary>Scene <paramref name="s" />: the recipe's scene of that number, or else its last.</summary>
    Scene BuildScene(int s, List<Part> blocks, FactoryLevels.Trim? level, Action<string> problem) {
        var planned = Math.Min(s, scenes.Count - 1);
        var spec = planned >= 0 ? scenes[planned] : null;
        var scene = new Scene {
            Name = s < scenes.Count ? spec!.Name : $"Scene {s + 1}",
            Level = level?.Scenes is { } levels && planned >= 0 && planned < levels.Length ? levels[planned] : 0
        };
        foreach (var part in blocks) {
            scene.Blocks[part.Id] = new(part.Bypassed);
        }

        foreach (var change in spec?.Changes.Split(' ', Split) ?? []) {
            Apply(scene, change, problem);
        }

        return scene;
    }

    /// <summary>The recipe's blocks, with <see cref="PeakCompressor" /> after the cab when a scene plays clean.</summary>
    List<Part> WithPeakCompressor() {
        var clean = tags.Contains("Clean")
            || tags.Contains("Ambient")
            || scenes.Exists(static s => s.Name.StartsWith("Clean", StringComparison.Ordinal));
        var cab = parts.FindIndex(static p => p.Type == "cab");
        if (!clean || cab < 0) {
            return parts;
        }

        var number = parts.Count(static p => p.Type == "compressor") + 1;
        return [
            ..parts.Take(cab + 1),
            new("compressor", $"compressor{number}", string.Empty, [PeakCompressor], false, []),
            ..parts.Skip(cab + 1)
        ];
    }

    /// <summary>
    ///     An Amp channel as the recipes were voiced: the speaker load following the Cab on a model that has one (a
    ///     solid-state amp has none, and keeps Stock), Master 10 unless the recipe sets it (the default up to
    ///     1.0.0-rc.3), and the level trimmed by <paramref name="trimDb" /> when given (<see cref="FactoryLevels" />).
    /// </summary>
    static void Voice(BlockDescriptor descriptor, Dictionary<string, float> values, float? trimDb) {
        var speaker = descriptor.Params[descriptor.IndexOf("speaker")];
        var model = (int)values.GetValueOrDefault("type");
        if (!values.ContainsKey("speaker")) {
            values["speaker"] = speaker.TypeLabels?[model] is { Length: > 0 }
                ? Value(speaker, "Follow_Cab") ?? speaker.Default
                : 0;
        }

        values.TryAdd("master", 10);
        if (trimDb is { } trim) {
            var output = descriptor.Params[descriptor.IndexOf("level")];
            values["level"] = output.Sanitize(values.GetValueOrDefault("level", output.Default) + trim);
        }
    }

    static PresetBlock Block(BlockCatalog catalog, Part part, FactoryLevels.Trim? level, Action<string> problem) {
        var block = new PresetBlock { Id = part.Id, Type = part.Type };
        if (catalog.Descriptor(part.Type) is not { } descriptor) {
            problem($"no block type '{part.Type}'.");
            return block;
        }

        if (part.Channels.Length > descriptor.Channels) {
            problem($"{descriptor.Name} has {descriptor.Channels} channel(s), not {part.Channels.Length}.");
        }

        for (var c = 0; c < Math.Min(part.Channels.Length, descriptor.Channels); c++) {
            var values = Settings(catalog, part.Type, $"{part.Common} {part.Channels[c]}", problem);
            if (part.Type == "circuitamp") {
                Voice(descriptor, values, level is not null && c < level.Amp.Length ? level.Amp[c] : null);
            }

            block.Channels[Preset.ChannelNames[c]] = values;
        }

        foreach (var modifier in part.Modifiers.Where(m => descriptor.IndexOf(m.Param) < 0)) {
            problem($"{part.Id} has no parameter '{modifier.Param}' to put on a pedal.");
        }

        return block;
    }

    void Apply(Scene scene, string change, Action<string> problem) {
        if (change[0] is '+' or '-') {
            if (Resolve(change[1..]) is { } id) {
                scene.Blocks[id] = scene.Blocks[id] with { Bypassed = change[0] == '-' };
            } else {
                problem($"scene '{scene.Name}' names no block '{change[1..]}'.");
            }

            return;
        }

        var equals = change.IndexOf('=', StringComparison.Ordinal);
        var target = equals > 0 ? Resolve(change[..equals]) : null;
        var channel = equals > 0 ? change[(equals + 1)..] : string.Empty;
        var index = Preset.ChannelNames.IndexOf(channel);
        if (target is null || index < 0) {
            problem($"scene '{scene.Name}' has a change '{change}' that is not +block, -block or block=A-D.");
        } else if (index >= parts.First(p => p.Id == target).Channels.Length) {
            problem($"scene '{scene.Name}' picks channel {channel} of {target}, which the recipe does not set.");
        } else {
            scene.Blocks[target] = scene.Blocks[target] with { Channel = channel };
        }
    }

    /// <summary>A block by its id, or the first of a type by the type's id (<c>amp</c> is the Circuit Amp).</summary>
    string? Resolve(string token) {
        var type = token == "amp"
            ? "circuitamp"
            : token.StartsWith("amp", StringComparison.Ordinal)
                ? "circuitamp" + token[3..]
                : token;
        return parts.Find(p => p.Id == type)?.Id ?? parts.Find(p => p.Id == type + "1")?.Id;
    }

    static Dictionary<string, float> Settings(BlockCatalog catalog, string type, string text, Action<string> problem) {
        var values = new Dictionary<string, float>(StringComparer.Ordinal);
        var descriptor = catalog.Descriptor(type);
        foreach (var setting in text.Split(' ', Split)) {
            var equals = setting.IndexOf('=', StringComparison.Ordinal);
            var id = equals > 0 ? setting[..equals] : setting;
            var index = descriptor?.IndexOf(id) ?? -1;
            if (equals <= 0 || index < 0) {
                problem($"{type} has no parameter '{id}' (in '{setting}').");
                continue;
            }

            var param = descriptor!.Params[index];
            if (Value(param, setting[(equals + 1)..]) is not { } value) {
                problem($"{type}.{id}: '{setting[(equals + 1)..]}' is not a number or one option of {param.Name}.");
                continue;
            }

            if (param.Sanitize(value) != value) {
                problem($"{type}.{id} = {value} is outside {param.Min}..{param.Max}.");
            }

            values[id] = param.Sanitize(value);
        }

        if (descriptor is not null) {
            CheckTypeControls(descriptor, type, values, problem);
        }

        return values;
    }

    /// <summary>
    ///     A control the chosen type does not have (an amp model's panel, a delay type's page) does nothing there.
    /// </summary>
    static void CheckTypeControls(
        BlockDescriptor descriptor,
        string type,
        Dictionary<string, float> values,
        Action<string> problem
    ) {
        if (descriptor.IndexOf("type") is not (>= 0 and var typeIndex)) {
            return;
        }

        var model = (int)values.GetValueOrDefault("type", descriptor.Params[typeIndex].Default);
        foreach (var id in values.Keys) {
            if (descriptor.Params[descriptor.IndexOf(id)].TypeLabels is { } labels
                && model < labels.Count
                && string.IsNullOrEmpty(labels[model])) {
                problem($"{type} type {descriptor.Params[typeIndex].Options?[model]} has no control '{id}'.");
            }
        }
    }

    /// <summary>A number, or the option named exactly, or else the only option whose name has it.</summary>
    static float? Value(ParamDescriptor param, string text) {
        // A type is always named ("5150" is an amp, not the 5151st option).
        if (param.Id == "type" && param.Options is { } types && Exact(types, text.Replace('_', ' ')) is { } named) {
            return named;
        }

        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) {
            return number;
        }

        if (param.Options is not { } options) {
            return null;
        }

        var wanted = text.Replace('_', ' ');
        if (Exact(options, wanted) is { } exact) {
            return exact;
        }

        var matches = Enumerable.Range(0, options.Count)
            .Where(i => options[i].Contains(wanted, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    static int? Exact(IReadOnlyList<string> options, string name) {
        for (var i = 0; i < options.Count; i++) {
            if (string.Equals(options[i], name, StringComparison.OrdinalIgnoreCase)) {
                return i;
            }
        }

        return null;
    }

    /// <summary>An option name as a setting value: spaces become underscores.</summary>
    static string Option(string name) => name.Replace(' ', '_');

    static string Slug(string text) {
        var slug = string.Concat(text.ToLowerInvariant().Select(static c => char.IsAsciiLetterOrDigit(c) ? c : '-'));
        while (slug.Contains("--", StringComparison.Ordinal)) {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-');
    }

    const StringSplitOptions Split = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;

    sealed record Part(
        string Type,
        string Id,
        string Common,
        string[] Channels,
        bool Bypassed,
        List<PresetModifier> Modifiers
    );

    sealed record SceneSpec(string Name, string Changes, float Lift);
}
