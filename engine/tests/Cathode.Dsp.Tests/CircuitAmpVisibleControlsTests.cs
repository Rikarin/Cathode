using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Parameters;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     A control the Circuit Amp shows on a model does something there, and one it hides does nothing (issue #276).
///     Every parameter shown on some models and hidden on others (<see cref="ParamDescriptor.TypeLabels" />) changes
///     the render of each model that shows it, in some context the editor can reach; one that cannot is hidden by
///     <see cref="AmpFeatures" /> or the model's <see cref="AmpPanel" />, or is listed in <see cref="Known" /> with
///     the reason. The controls shown on every model (the inserts, the speaker stages, the output) have their own
///     tests.
/// </summary>
public class CircuitAmpVisibleControlsTests(ITestOutputHelper output) {
    /// <summary>
    ///     A change below this, relative to the render's level, counts as none (float rounding is far lower).
    /// </summary>
    const double AudibleDb = -90;

    /// <summary>
    ///     The inserts and tone controls engaged, by parameter id: what a dependent control needs before it can act (a
    ///     tremolo's rate needs its depth, Presence Freq on an amp without feedback needs the Feedback knob up).
    /// </summary>
    static readonly (string Id, float Value)[] Engaged = [
        ("gain", 7), ("bass", 6), ("middle", 6), ("treble", 6), ("presence", 7), ("depth", 5), ("cut", 5),
        ("master", 8), ("bright", 1), ("feedback", 10), ("control1", 7), ("control2", 7), ("tremdepth", 60),
        ("screenq", 2), ("overdrive", 5), ("xfmrdrive", 5)
    ];

    /// <summary>
    ///     On top of <see cref="Engaged" />, the amp driven as hard as its panel allows: grid current, and plates
    ///     swinging below their cathodes, for the bias-excursion controls and the suppressor diodes.
    /// </summary>
    static readonly (string Id, float Value)[] Driven = [
        ("gain", 10), ("master", 10), ("control1", 10), ("control2", 10), ("intrim", 20), ("bias", 0),
        ("xfmrhf", 25)
    ];

    /// <summary>
    ///     The advanced controls <see cref="AmpFeatures" /> hides where a model lacks the part: hidden, each does
    ///     nothing at all. (A hidden panel knob is another matter: the Dual Rectifier's power amp keeps a fixed
    ///     presence network at the hidden knob's setting.)
    /// </summary>
    static readonly string[] HiddenIsInert = [
        "katc", "mvtrim", "brightcap", "stackfreq", "fatsw", "presag", "pretube", "overdrive", "presshift",
        "presfreq", "depthfreq", "t2plate", "mvloc", "cfcomp", "cfharm", "pibiasx", "cutsw", "hitreble"
    ];

    /// <summary>
    ///     Visible controls known to do nothing on a model in any context this test reaches, as "model: id", each with
    ///     its reason. An entry is a written-down exception, not a rule: the test fails once it starts to act.
    /// </summary>
    static readonly Dictionary<string, string> Known = new() {
        // 300 W of 6550s behind direct-coupled followers: the test's drive never pulls grid current through the
        // coupling caps Excursion Time scales.
        ["SVT: ptbiastime"] = "the power grids do not conduct at any drive the test reaches",
        // The same power amp behind channel two, which drives it about 4 dB less than channel one. Its screens are on
        // a winding of their own (a stiff stand-in in the model), so the node Screen Q acts on barely moves: on
        // channel one the change just clears the test's −90 dB, on channel two it does not.
        ["SVT Channel 2: ptbiastime"] = "the power grids do not conduct at any drive the test reaches",
        ["SVT Channel 2: screenq"] = "the screens' own winding holds their node at channel two's drive",
        ["SVT-CL: ptbiastime"] = "the power grids do not conduct at any drive the test reaches",
        // A cathode-biased class-A pair on one cathode: its plates stay above it even driven flat out.
        ["Scout GA-17RVT: suppressor"] = "the plates never swing below the cathode",
        // No master, and the loop return's divider (10k and 33k into 15k + 22k) ahead of the inverter: its grid
        // stays out of conduction.
        ["Subway Blues: pibiasx"] = "the inverter's grid does not conduct at any drive the test reaches"
    };

    public static TheoryData<int> Types => [..Enumerable.Range(0, AmpDesign.All.Count)];

    [Theory]
    [MemberData(nameof(Types))]
    public void A_shown_control_changes_the_render_and_a_hidden_one_does_not(int type) {
        var design = AmpDesign.All[type];
        var parameters = CircuitAmpBlock.Info.Params;
        bool Visible(int index) => parameters[index].TypeLabels is not { } labels || labels[type] != string.Empty;
        int Index(string id) => parameters.ToList().FindIndex(p => p.Id == id);

        // The model as it ships: its own defaults.
        var defaults = new Dictionary<int, float> { [CircuitAmpBlock.Type] = type };
        for (var i = 0; i < parameters.Count; i++) {
            if (parameters[i].TypeDefaults is { } own) {
                defaults[i] = own[type];
            }
        }

        // Only what the panel shows is set: the editor cannot reach the rest.
        Dictionary<int, float> With(Dictionary<int, float> basis, (string Id, float Value)[] settings) {
            var context = new Dictionary<int, float>(basis);
            foreach (var (id, value) in settings) {
                if (Visible(Index(id))) {
                    context[Index(id)] = value;
                }
            }

            return context;
        }

        // The contexts, plainest first: defaults, the tone controls engaged, the amp driven hard, then each with
        // every combination of the model's switches.
        var engaged = With(defaults, Engaged);
        var driven = With(engaged, Driven);
        var contexts = new List<Dictionary<int, float>> { defaults, engaged, driven };
        int[] switches = [CircuitAmpBlock.Switch1, CircuitAmpBlock.Switch2, CircuitAmpBlock.Switch3];
        var plain = contexts.Count;
        foreach (var basis in new[] { engaged, defaults, driven }) {
            for (var combination = 1; combination < 8; combination++) {
                if (Enumerable.Range(0, 3).Any(k => (combination >> k & 1) != 0 && !Visible(switches[k]))) {
                    continue;
                }

                var context = new Dictionary<int, float>(basis);
                for (var k = 0; k < 3; k++) {
                    context[switches[k]] = combination >> k & 1;
                }

                contexts.Add(context);
            }
        }

        var input = Stimulus();
        var baselines = new float[contexts.Count][];

        float[] Render(Dictionary<int, float> settings) {
            using var block = TestBlocks.Create<CircuitAmpBlock>([..settings.Select(static s => (s.Key, s.Value))]);
            return BlockRender.Stereo(block, input, input, static _ => 64).Left;
        }

        // Whether parameter `index`, moved away from where context `c` has it, changes that context's render.
        bool ActsIn(int c, int index, bool everyValue) {
            var p = parameters[index];
            var current = contexts[c].GetValueOrDefault(index, p.Default);
            float[] values = p.Kind switch {
                ParamKind.Float when everyValue => [p.Max, p.Min, 0.5f * (p.Min + p.Max)],
                ParamKind.Float => [p.Max, p.Min],
                _ => [..Enumerable.Range((int)p.Min, (int)(p.Max - p.Min) + 1).Select(static v => (float)v)]
            };
            foreach (var value in values.Where(v => Math.Abs(v - current) > 1e-3f * (p.Max - p.Min))) {
                var stock = baselines[c] ??= Render(contexts[c]);
                var changed = Render(new(contexts[c]) { [index] = value });
                if (Analysis.NullResidualDb(stock, changed) - Analysis.ToDb(Analysis.Rms(stock)) > AudibleDb) {
                    return true;
                }
            }

            return false;
        }

        var inert = new List<string>();
        var acting = new List<string>();
        for (var index = 0; index < parameters.Count; index++) {
            var id = parameters[index].Id;
            if (parameters[index].TypeLabels is null) {
                continue;
            }

            if (!Visible(index)) {
                // Hidden: nothing in the plain contexts, nor with all the model's switches on.
                if (HiddenIsInert.Contains(id)
                    && Enumerable.Range(0, plain).Append(contexts.Count - 1).Any(c => ActsIn(c, index, false))) {
                    acting.Add(id);
                }

                continue;
            }

            var key = $"{design.Name}: {id}";
            var acts = Enumerable.Range(0, contexts.Count).Any(c => ActsIn(c, index, true));
            if (!acts && !Known.ContainsKey(key)) {
                inert.Add(id);
            }

            Assert.False(acts && Known.ContainsKey(key), $"{key} acts: take it out of the known list");
        }

        output.WriteLine(
            $"{design.Name}: inert [{string.Join(", ", inert)}], hidden but acting [{string.Join(", ", acting)}]"
        );
        Assert.True(inert.Count == 0, $"{design.Name} shows controls that do nothing: {string.Join(", ", inert)}");
        Assert.True(acting.Count == 0, $"{design.Name} hides controls that act: {string.Join(", ", acting)}");
    }

    /// <summary>A loud chord, then its quiet tail: 0.1 s, the same on both sides.</summary>
    static float[] Stimulus() {
        var chord = CircuitAmpBlockTests.Chord(4_800, 0.5f);
        for (var n = chord.Length / 2; n < chord.Length; n++) {
            chord[n] *= 0.1f;
        }

        return chord;
    }
}
