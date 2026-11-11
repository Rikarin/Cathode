using Cathode.Dsp.Amp;
using Cathode.Dsp.Parameters;
using Cathode.Testing;
using System.Globalization;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The two sweeps over every parameter of every type (docs/03-testing-strategy.md §3, items 10 and 11): a shown
///     parameter changes what the block plays, and no single parameter at an end of its range keeps the block
///     sounding after its input stops.
/// </summary>
public partial class BlockConformanceTests {
    /// <summary>
    ///     Settings a parameter is tried in: the type's defaults, then this many minus one seeded random settings of
    ///     the other parameters. A knob is dead when it changes nothing in any of them.
    /// </summary>
    const int Contexts = 8;

    /// <summary>Set to <c>1</c> (the nightly workflow does) to sweep every type of the blocks with many.</summary>
    const string FullVariable = "CATHODE_SWEEPS_FULL";

    /// <summary>Above this many types a build sweeps a sample of them.</summary>
    const int ManyTypes = 32;

    /// <summary>The docs/03 bound: a tail under this has decayed.</summary>
    const double DecayedDb = -90;

    /// <summary>
    ///     Loud noise, quiet noise and silence, a tenth of a second each, a low tone for three tenths, silence for as
    ///     long, a second note a fifth up and silence to 1.2 s, different on the two sides: enough for a detector to
    ///     open and close, a pitch tracker to lock, an onset detector to hear a new note, a pan to show and the second
    ///     repeat of a delay at its default time to come back.
    /// </summary>
    static readonly (float[] Left, float[] Right) Probe = (ProbeSide(31, 196), ProbeSide(37, 247));

    /// <summary>A tenth of a second of full-scale noise, as the decay test at the defaults uses.</summary>
    static readonly float[] Burst = Signals.Noise(4_800, 3);

    /// <summary>
    ///     A parameter shown on a type that changes nothing the type plays is a dead knob (the #4 / #31 class). So
    ///     each one, on each type, must change the output at its lowest, highest or middle value (a choice: at any
    ///     other option), from the type's defaults or from one of a few random settings of the other parameters.
    ///     A parameter hidden behind a switch is tried with the switch where it shows, and one that acts only with
    ///     another at some setting (an LFO's duty cycle, with a wave it shapes) is tried with that set
    ///     (<see cref="Needs" />). The few that rightly change no audio of the block on its own are named in
    ///     <see cref="ChangesNoAudio" />. A block with a model per type is sampled (<see cref="SweptTypes" />).
    /// </summary>
    [Theory]
    [MemberData(nameof(BlockTypes))]
    public void A_parameter_shown_on_a_type_changes_what_it_plays(string typeId) {
        var descriptor = Registry.GetDescriptor(typeId);
        var dead = new List<string>();
        foreach (var type in SweptTypes(descriptor, 8)) {
            var tried = Enumerable.Range(0, descriptor.Params.Count)
                .Where(i => i != descriptor.TypeIndex
                    && OnType(descriptor.Params[i], type)
                    && !ChangesNoAudio(typeId, descriptor.Params[i].Id, type)
                )
                .ToList();
            var live = new bool[descriptor.Params.Count];
            for (var context = 0; context < Contexts && tried.Exists(i => !live[i]); context++) {
                var baselines = new Dictionary<string, (float[] Left, float[] Right)?>(StringComparer.Ordinal);
                foreach (var i in tried.Where(i => !live[i])) {
                    live[i] = Live(descriptor, type, context, i, baselines);
                }
            }

            dead.AddRange(tried.Where(i => !live[i]).Select(i => $"{descriptor.Params[i].Id} (type {type})"));
        }

        Assert.True(dead.Count == 0, $"{typeId}: changes nothing: {string.Join(", ", dead)}");
    }

    /// <summary>
    ///     Whether parameter <paramref name="index" /> changes what the type plays in setting
    ///     <paramref name="context" />. <paramref name="baselines" /> holds the renders without the change, by
    ///     settings.
    /// </summary>
    static bool Live(
        BlockDescriptor descriptor,
        int type,
        int context,
        int index,
        Dictionary<string, (float[] Left, float[] Right)?> baselines
    ) {
        var p = descriptor.Params[index];
        using var block = Configured(descriptor, type, context, p);
        var settings = Settings(block);
        if (!baselines.TryGetValue(settings, out var baseline)) {
            block.Prepare(Spec);
            var rendered = Render(block, Probe.Left, Probe.Right);

            // Random settings that play nothing at all (a level at its minimum) can show no knob working.
            baseline = context > 0 && Analysis.Peak(rendered.Left) == 0 && Analysis.Peak(rendered.Right) == 0
                ? null
                : rendered;
            baselines[settings] = baseline;
        }

        return baseline is { } reference
            && Probes(p, block.Params.GetTarget(index))
                .Any(value => Changes(descriptor, type, context, index, value, reference));
    }

    /// <summary>
    ///     The decay test away from the defaults: with any one shown parameter at an end of its range (a choice: at
    ///     each option, <see cref="Extremes" />), the output after a burst still dies away. A feedback path that
    ///     sustains or grows at a setting the editor offers fails it. A tail passes when it is under
    ///     <see cref="DecayedDb" /> or still falling; the settings that hold or make a sound on purpose
    ///     (<see cref="SustainsByDesign" />) must not grow instead.
    /// </summary>
    [Theory]
    [MemberData(nameof(BlockTypes))]
    public void Silence_after_a_burst_decays_with_any_one_parameter_at_an_extreme(string typeId) {
        var descriptor = Registry.GetDescriptor(typeId);
        var failures = new List<string>();
        foreach (var type in SweptTypes(descriptor, 16)) {
            for (var i = 0; i < descriptor.Params.Count; i++) {
                var p = descriptor.Params[i];
                if (i == descriptor.TypeIndex || !OnType(p, type)) {
                    continue;
                }

                using var probe = Configured(descriptor, type, 0, p);
                foreach (var value in Extremes(p, probe.Params.GetTarget(i))) {
                    // An open defect must still fail: once it decays, its entry goes.
                    var failure = Tail(descriptor, type, p, i, value);
                    if (KnownToRing(typeId, type, p.Id, value)) {
                        failure = failure is null ? "decays now: take it off KnownToRing" : null;
                    }

                    if (failure is not null) {
                        failures.Add(Invariant($"{p.Id}={value} (type {type}): {failure}"));
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, $"{typeId}: {string.Join("; ", failures)}");
    }

    /// <summary>
    ///     Settings at which an amp model sounds on after its input stops and should not: defects this sweep found
    ///     that are still open. They are listed so that the sweep holds every other setting meanwhile, and each must
    ///     still fail: one that decays fails the test until its entry is taken off, so the list cannot outlive a fix.
    /// </summary>
    /// <remarks>
    ///     Found in November 2026, sweeping all 92 models. The tweed Bassman motorboats (about 22 Hz at -25 dBFS RMS,
    ///     peaks of -15 dBFS) for as long as the block runs with its Normal Vol or the Overdrive at 10, or its tone
    ///     stack moved to Pre; its Jumped variant, the Tweed Twin 5F8-A (the same preamp) and the Powerball do with
    ///     the Overdrive at 10 (-16, -12 and -14 dBFS); the Deluxe 6G3 rings at -18 dBFS on EL34s; the Sunn Model T
    ///     holds -24 dBFS with its feedback at 10 and -28 dBFS on EL84s.
    /// </remarks>
    static bool KnownToRing(string typeId, int type, string param, float value) =>
        typeId == "circuitamp"
        && (AmpDesign.All[type].Name, param, value) switch {
            ("Bassman 5F6-A", "control1" or "overdrive", 10) => true,
            ("Bassman 5F6-A", "stackloc", 1) => true,
            ("Bassman 5F6-A Jumped" or "Tweed Twin 5F8-A" or "Powerball", "overdrive", 10) => true,
            ("Deluxe 6G3", "powertube", 2) => true,
            ("Sunn Model T", "feedback", 10) => true,
            ("Sunn Model T", "powertube", 3) => true,
            _ => false
        };

    /// <summary>
    ///     What is wrong with the tail after a burst at this setting, or null. The render stops as soon as the tail
    ///     has decayed, so only the settings that ring cost their full length.
    /// </summary>
    static string? Tail(BlockDescriptor descriptor, int type, ParamDescriptor p, int index, float value) {
        const int rate = 48_000;
        const int longest = 12;
        using var block = Configured(descriptor, type, 0, p);
        block.Params.SetTarget(index, value);
        block.Prepare(Spec);
        var held = SustainsByDesign(descriptor.TypeId, p.Id, value);
        Span<float> left = stackalloc float[MaxBlock];
        Span<float> right = stackalloc float[MaxBlock];
        Span<double> halves = stackalloc double[2 * longest]; // the peak of each half second after the burst
        halves.Clear();
        var loudest = 0.0;
        var quiet = 0;
        for (var offset = 0; offset < Burst.Length + longest * rate; offset += MaxBlock) {
            left.Clear();
            if (offset < Burst.Length) {
                Burst.AsSpan(offset, MaxBlock).CopyTo(left);
            }

            left.CopyTo(right);
            block.Process(new(left, right), new(Spec.SampleRate, 120, offset));
            if (!Analysis.AllFinite(left) || !Analysis.AllFinite(right)) {
                return "non-finite output";
            }

            var peak = Math.Max(Analysis.Peak(left), Analysis.Peak(right));
            loudest = Math.Max(loudest, peak);
            var after = offset - Burst.Length;
            if (after < 0) {
                continue;
            }

            var half = after / (rate / 2);
            halves[half] = Math.Max(halves[half], peak);
            quiet = Analysis.ToDb(peak) < DecayedDb ? quiet + MaxBlock : 0;
            if (!held && quiet >= rate / 2) {
                return null; // half a second under the bound: longer than the default time of any delay here
            }

            // A second and a half is enough for most: the third half second against the second.
            if (!held && after + MaxBlock == 3 * rate / 2) {
                var (early, late) = (Analysis.ToDb(halves[1]), Analysis.ToDb(halves[2]));
                if (late < DecayedDb || late <= early - 1) {
                    return null;
                }
            }

            // A held sound that is steady shows it in three seconds: the third second against the second.
            if (held && after + MaxBlock == 3 * rate) {
                var (early, late) = (Peak(halves[2..4]), Peak(halves[4..6]));
                if (Analysis.ToDb(late) <= Analysis.ToDb(early) + 0.1 && late <= loudest) {
                    return null;
                }
            }
        }

        // Neither by then: twelve seconds, in windows long enough to hold a repeat of a long delay or an LFO's cycle.
        var first = Analysis.ToDb(Peak(halves[8..16]));
        var last = Analysis.ToDb(Peak(halves[16..24]));
        if (held) {
            return last <= first + 0.5 && Peak(halves[16..24]) <= loudest
                ? null
                : Invariant($"a held sound grows: {first:0.0} dBFS, then {last:0.0} dBFS four seconds later");
        }

        return last < DecayedDb || last <= first - 1
            ? null
            : Invariant($"the tail is at {last:0.0} dBFS after 12 s ({first:0.0} dBFS four seconds before)");
    }

    static double Peak(ReadOnlySpan<double> peaks) {
        var peak = 0.0;
        foreach (var value in peaks) {
            peak = Math.Max(peak, value);
        }

        return peak;
    }

    static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>Shown parameters that rightly change no audio of the block itself.</summary>
    static bool ChangesNoAudio(string typeId, string param, int type) =>
        (typeId, param, type) switch {
            // No capture, impulse response or match is loaded here, and without one these blocks pass their input.
            ("nam" or "irplayer" or "tonematch", _, _) => true,
            // The looper's settings act on a loop, and none is recorded here (LooperBlockTests cover each).
            ("looper", _, _) => true,
            // The send's level goes to the feedback bus, and the return's comes from it: the graph joins the two.
            ("fbsend" or "fbreturn", "level", _) => true,
            // Its levels are those of the interface's send and return channels, which a block alone does not have.
            ("fxloop", "send" or "return", _) => true,
            // Picks what the analyser shows; the audio passes untouched.
            ("rta", "inputSelect", _) => true,
            // LFO Bypass Reset restarts the LFO when the block is engaged after a bypass, which the graph does.
            ("phaser" or "flanger", "lfoReset", _) => true,
            // Learn takes a rhythm from the notes of a phrase, more playing than the probe holds (TenTapBlockTests).
            ("tentap", "learn", _) => true,
            // The Custom Shifter has a shift for each note that can be played, and the probe plays two.
            ("pitch", _, 8) when param.StartsWith("custom", StringComparison.Ordinal) => true,
            _ => false
        };

    /// <summary>
    ///     Settings of other parameters without which <paramref name="param" /> rightly does nothing, and which the
    ///     random settings would rarely hit: it is tried with them.
    /// </summary>
    static (string Id, float Value)[] Needs(string typeId, string param, int type) =>
        (typeId, param) switch {
            // An LFO's duty cycle shapes its triangle, square, trapezoid and astable waves, not the default sine.
            ("phaser", "lfoDuty") => [("lfoType", 1)],
            ("filter", "lfoDuty") => [("lfoOn", 1), ("lfoType", 1)],
            ("tremolo", "duty") => [("shape", 1)],
            // A synth voice's duty is the width of its square wave.
            ("synth", "voice1Duty" or "voice2Duty" or "voice3Duty") => [($"{param[..6]}Shape", 3)],
            // The filter's LFO has a switch of its own, and its Rate counts off the tempo.
            ("filter", "lfoRate") => [("lfoOn", 1), ("lfoTempo", 0)],
            ("filter", "lfoType" or "lfoTempo" or "modFreq" or "lfoQuantize") => [("lfoOn", 1)],
            // The photocell's attack and release, with the photocell as the phaser's variable resistor.
            ("phaser", "photoAttack" or "photoRelease") => [("vcrType", 3)],
            // The resonators' own frequencies, with Tuning on Frequency instead of Chord.
            ("resonator", "freq1" or "freq2" or "freq3" or "freq4") => [("tuning", 1)],
            // Bands 21 to 24 exist with 24 bands.
            ("vocoder", _) when param.StartsWith("band", StringComparison.Ordinal) => [("bands", 5)],
            // A band's slope is that of its Blocking type (a cut filter).
            ("peq", "lowslope") => [("lowtype", 2)],
            ("peq", "highslope") => [("hightype", 2)],
            // The dry signal's delay is set by hand with Thru-Zero on Manual.
            ("flanger", "dryDelay") => [("thruZero", 2)],
            // The pickup's own values belong to the Custom pickup; the cable loads any pickup but Off.
            ("input", "pickupL" or "pickupR" or "pickupC") => [("pickup", 4)],
            ("input", "cable" or "impedance" or "buffer") => [("pickup", 1)],
            // A filter's gain is that of its Peak type.
            ("plex", "filterGain") => [("filterType", 4)],
            ("multidelay", "filter1Gain" or "filter2Gain" or "filter3Gain" or "filter4Gain") =>
                [($"{param[..7]}Type", 4)],
            // A voice's feedback is heard from its second repeat on: a short master time brings it into the probe.
            ("multidelay", "tap1Feedback" or "tap2Feedback" or "tap3Feedback" or "tap4Feedback") => [("time", 50)],
            // A voice's filter is off by default.
            ("multidelay", _) when param.StartsWith("filter", StringComparison.Ordinal)
                && (param.EndsWith("Frequency", StringComparison.Ordinal) || param.EndsWith('Q')) =>
                [($"{param[..7]}Type", 1)],
            // A voice's chorus, comb and ring modulator are at zero by default, and so are the LFOs' depths.
            ("multidelay", _) when param.StartsWith("chorus", StringComparison.Ordinal)
                && param.EndsWith("Rate", StringComparison.Ordinal) => [($"{param[..7]}Depth", 5)],
            ("multidelay", _) when param.StartsWith("comb", StringComparison.Ordinal)
                && param.EndsWith("Time", StringComparison.Ordinal) => [($"{param[..5]}Feedback", 50)],
            ("multidelay", _) when param.StartsWith("ring", StringComparison.Ordinal)
                && param.EndsWith("Frequency", StringComparison.Ordinal) => [($"{param[..5]}Mix", 50)],
            ("multidelay", "lfo1Rate" or "lfo1Phase") => [("lfo1Depth", 5), ("lfo1Tempo", 0)],
            ("multidelay", "lfo2Rate" or "lfo2Phase") => [("lfo2Depth", 5), ("lfo2Tempo", 0)],
            // The input swell's release fades what is played under the threshold after a note that opened it.
            ("plex", "envelopeRelease") => [("envelopeAttack", 50), ("envelopeThreshold", -12)],
            // An LFO's rate and phase show with a depth, and the rate counts off the tempo.
            ("delay", "lfo1Rate" or "lfo1Phase") => [("lfo1Depth", 5), ("lfo1Tempo", 0)],
            ("delay", "lfo2Rate" or "lfo2Phase") => [("lfo2Depth", 5), ("lfo2Tempo", 0)],
            ("delay", "lfo3Rate") => [("lfo3Depth", 50), ("lfo3Tempo", 0)],
            ("plex", "filterLfoRate") => [("filterType", 1), ("modFrequency", 2), ("filterLfoTempo", 0)],
            // The notes of the Custom scale, the formant shifts with Formant Correction on, and all sixteen steps.
            ("pitch", _) when param.StartsWith("customNote", StringComparison.Ordinal) =>
                [("scale", 12), ("customNotes", 4)],
            ("pitch", _) when param.StartsWith("formantShift", StringComparison.Ordinal) => [("formant", 1)],
            ("pitch", _) when param.StartsWith("arpStep", StringComparison.Ordinal) && param != "arpSteps" =>
                [("arpSteps", 15), ("arpTempo", 5)],
            ("cab", _) when param.Length > 2 && param[0] == 'm' && char.IsAsciiDigit(param[1]) => CabSlot(param),
            // A model's controls that belong to one of its channels, with that channel chosen.
            ("circuitamp", _) when ModelNeeds(AmpDesign.All[type].Name, param) is { Length: > 0 } needs => needs,
            // The amp's inserts and options, each off or at zero by default: a knob of one is tried with it on.
            ("circuitamp", "boostdrive" or "boosttone" or "boostlevel") => [("boost", 1)],
            ("circuitamp", "satdrive") => [("saturation", 1)],
            ("circuitamp", "inffreq" or "infq") => [("infilter", 3), ("infgain", 6)],
            ("circuitamp", "infgain") => [("infilter", 3)],
            ("circuitamp", "outeq" or "outeqloc") => [("outeq", 1), ("outeq1", 6)],
            ("circuitamp", _) when param.StartsWith("outeq", StringComparison.Ordinal) => [("outeq", 1)],
            ("circuitamp", "compamount" or "compthresh" or "compclarity") => [("comptype", 1)],
            ("circuitamp", "spkcomptc") => [("spkcomp", 5)],
            ("circuitamp", "tremrate") => [("tremdepth", 50)],
            ("circuitamp", "brightcap") => [("bright", 1)],
            // The cab's resonance is that of the thump, and the output mode takes the speaker stages out.
            ("circuitamp", "cabres") => [("thump", 5)],
            ("circuitamp", "outmode") => [("spkdrive", 5), ("thump", 5)],
            // Bias excursion and the plate suppressor diodes act on a power amp driven hard.
            ("circuitamp", "prebiasx" or "pibiasx" or "ptbiasx" or "suppressor") => [("gain", 10), ("master", 10)],
            // The presence circuit's frequency and shift, with the Presence knob up.
            ("circuitamp", "presfreq" or "presshift") => [("presence", 10)],
            // The Drive's second EQ is off by default, and its Mid Freq is that of a Mid away from 0 dB.
            ("drive", "eq2") => [("geq1", 6)],
            ("drive", _) when param.StartsWith("geq", StringComparison.Ordinal) => [("eq2", 1)],
            ("drive", "midFreq") => [("mid", 6)],
            _ => []
        };

    /// <summary>
    ///     Controls of an amp model that sit on one of its channels, the other being the default: the SLO-100's Bright
    ///     and Crunch are the Normal channel's, the Lone Star's Drive is Channel 2's with its Drive switch on, the
    ///     Rect-o-Verb's Pushed is the Rhythm channel's, and the Tremolux's, Concert's and Super's vibrato is on their
    ///     Vibrato channel (its Speed showing with an Intensity; the two brownface amps' behind their Vibrato pedal,
    ///     Switch 2, as well). Switch 1 picks the channel on each.
    /// </summary>
    static (string Id, float Value)[] ModelNeeds(string model, string param) =>
        (model, param) switch {
            ("SLO-100", "bright" or "switch2") => [("switch1", 1)],
            ("Lone Star", "control1") => [("switch1", 1), ("switch2", 1)],
            ("Rect-o-Verb", "switch2") => [("switch1", 1)],
            ("Tremolux AA763", "tremrate") => [("switch1", 1), ("tremdepth", 50)],
            ("Tremolux AA763", "tremdepth") => [("switch1", 1)],
            ("Concert 6G12" or "Super 6G4", "tremrate") => [("switch1", 1), ("switch2", 1), ("tremdepth", 50)],
            ("Concert 6G12" or "Super 6G4", "tremdepth") => [("switch1", 1), ("switch2", 1)],
            _ => []
        };

    /// <summary>
    ///     What a Cab mic slot's parameter needs to be heard: its slot playing (slots 2 to 4 are muted by default, and
    ///     a solo elsewhere silences it) from a source that has the control. Proximity belongs to the IR and Flat
    ///     sources (a DynaCab's is its Distance), the rest is tried on a DynaCab, as no IR file is loaded here, and
    ///     Auto-Align shows against a second modelled slot at another distance.
    /// </summary>
    static (string Id, float Value)[] CabSlot(string param) {
        var slot = param[..2];
        var control = param[2..];
        var needs = new List<(string Id, float Value)>();
        needs.AddRange(Enumerable.Range(1, 4).Select(static n => ($"m{n}solo", 0f)).Where(solo => solo.Item1 != param));
        if (control != "mute") {
            needs.Add((slot + "mute", 0));
        }

        if (control != "source") {
            needs.Add((slot + "source", control is "proximity" or "proxfreq" ? 3 : 1));
        }

        if (control == "align") {
            var other = slot == "m1" ? "m2" : "m1";
            needs.AddRange([(other + "mute", 0), (other + "source", 1), (other + "align", 1)]);
        }

        return [..needs];
    }

    /// <summary>Settings that hold or make a sound on purpose: the tail may stay, and must not grow.</summary>
    static bool SustainsByDesign(string typeId, string param, float value) =>
        (typeId, param) switch {
            // A feedback at 100 % of either polarity repeats for ever.
            ("delay" or "megatap" or "tentap" or "multidelay" or "plex", _)
                when param.Contains("eedback", StringComparison.Ordinal) => Math.Abs(value) >= 100,
            // Hold and Stack loop the repeats at unity.
            ("delay" or "plex", "hold") => value > 0,
            // A loop EQ band at +12 dB takes the loop over unity at the default feedback; the delay's limiter holds it.
            ("delay", "eq1Gain" or "eq2Gain") => value >= 12,
            // With Track on Off a voice is a free-running oscillator.
            ("synth", "voice1Track" or "voice2Track" or "voice3Track") => value == 2,
            // An amp makes sound of its own on an AC supply (hum) and with the bias tremolo running (its tick).
            ("circuitamp", "supply") => value >= 2,
            ("circuitamp", "tremrate") => true,
            ("circuitamp", "tremdepth") => value > 0,
            _ => false
        };

    static float[] ProbeSide(int seed, double tone) =>
        Signals.Concat(
            Signals.Noise(4_800, seed, 0.5f),
            Signals.Noise(4_800, seed + 1, 0.005f),
            new float[4_800],
            Signals.Sine(tone, 48_000, 14_400, 0.25f),
            new float[14_400],
            Signals.Sine(1.5 * tone, 48_000, 4_800, 0.25f),
            new float[9_600]
        );

    /// <summary>
    ///     The types a sweep covers. A block with a model per type (the Circuit Amp, the Drive) has dozens, each slow
    ///     to prepare, so a build sweeps one in <paramref name="sample" /> of them and the nightly run all
    ///     (<see cref="FullVariable" />).
    /// </summary>
    static IEnumerable<int> SweptTypes(BlockDescriptor descriptor, int sample) {
        var count = TypeCount(descriptor);
        var step = count > ManyTypes && Environment.GetEnvironmentVariable(FullVariable) != "1" ? sample : 1;
        return Enumerable.Range(0, count).Where(type => type % step == 0);
    }

    static int TypeCount(BlockDescriptor descriptor) =>
        descriptor.TypeIndex < 0 ? 1 : (int)descriptor.Params[descriptor.TypeIndex].Max + 1;

    /// <summary>Whether type <paramref name="type" /> has the parameter at all.</summary>
    static bool OnType(ParamDescriptor p, int type) => p.TypeLabels is not { } labels || labels[type].Length > 0;

    /// <summary>
    ///     The values to try a parameter at, other than the one it has: both ends and the middle, or every option.
    /// </summary>
    static IEnumerable<float> Probes(ParamDescriptor p, float current) {
        var values = p.Kind == ParamKind.Float
            ? [p.Min, p.Max, (p.Min + p.Max) / 2]
            : Enumerable.Range(0, (int)p.Max + 1).Select(static option => (float)option);
        return values.Where(value => value != current);
    }

    /// <summary>
    ///     The values the decay sweep sets a parameter to, other than the one it has: both ends of a knob and every
    ///     option of a choice. A choice of more than twelve options (note values, notes) is tried at its first, middle
    ///     and last.
    /// </summary>
    static IEnumerable<float> Extremes(ParamDescriptor p, float current) {
        IEnumerable<float> values = p.Kind == ParamKind.Float || p.Max > 12
            ? [p.Min, p.Max, MathF.Round((p.Min + p.Max) / 2)]
            : Enumerable.Range(0, (int)p.Max + 1).Select(static option => (float)option);
        return values.Take(p.Kind == ParamKind.Float ? 2 : int.MaxValue).Where(value => value != current);
    }

    /// <summary>
    ///     A block of type <paramref name="type" />, not yet prepared, set up to try <paramref name="shown" /> in: at
    ///     the type's defaults (context 0), or with every other knob at a seeded random value and every other choice
    ///     or switch at one half of the time; then what <paramref name="shown" /> needs (<see cref="Needs" />), and
    ///     the switches that hide it set where it shows.
    /// </summary>
    static IDspBlock Configured(BlockDescriptor descriptor, int type, int context, ParamDescriptor shown) {
        var block = OfType(descriptor, type);
        for (var i = 0; context > 0 && i < descriptor.Params.Count; i++) {
            var p = descriptor.Params[i];
            if (i == descriptor.TypeIndex) {
                continue;
            }

            // Seeded by the parameter's id, so a parameter added to the block leaves the others' values alone.
            var random = new Random(Seed(p.Id, context, type));
            if (p.Kind == ParamKind.Float || random.Next(2) == 0) {
                block.Params.SetTarget(i, p.Min + random.NextSingle() * (p.Max - p.Min));
            }
        }

        foreach (var (id, value) in Needs(descriptor.TypeId, shown.Id, type)) {
            block.Params.SetTarget(descriptor.IndexOf(id), value);
        }

        Show(block, descriptor, shown, type, 0);
        return block;
    }

    /// <summary>FNV-1a over the id and the two numbers: the same on every run and machine.</summary>
    static int Seed(string id, int context, int type) {
        var hash = 2_166_136_261;
        foreach (var c in $"{id}/{context}/{type}") {
            hash = (hash ^ c) * 16_777_619;
        }

        return (int)(hash & 0x7FFF_FFFF);
    }

    /// <summary>A block of type <paramref name="type" /> at that type's defaults, not yet prepared.</summary>
    static IDspBlock OfType(BlockDescriptor descriptor, int type) {
        var block = Registry.Create(descriptor.TypeId);
        if (descriptor.TypeIndex >= 0) {
            block.Params.SetTarget(descriptor.TypeIndex, type);
            block.Params.ResetToDefaults();
        }

        return block;
    }

    /// <summary>
    ///     Sets the parameters <paramref name="p" />'s visibility hangs on, and theirs, so that it shows.
    /// </summary>
    static void Show(IDspBlock block, BlockDescriptor descriptor, ParamDescriptor p, int type, int depth) {
        foreach (var condition in Conditions(p, type)) {
            var index = descriptor.IndexOf(condition.Param);
            if (depth < 4) {
                Show(block, descriptor, descriptor.Params[index], type, depth + 1);
            }

            if (!condition.Values.Contains(block.Params.GetTarget(index))) {
                block.Params.SetTarget(index, condition.Values[0]);
            }
        }
    }

    static IEnumerable<ParamCondition> Conditions(ParamDescriptor p, int type) {
        if (p.VisibleWhen is { } always) {
            yield return always;
        }

        if (p.TypeVisibleWhen is { } byType && byType.TryGetValue(type, out var onType)) {
            yield return onType;
        }
    }

    static string Settings(IDspBlock block) =>
        string.Join(
            ' ',
            Enumerable.Range(0, block.Params.Count)
                .Select(i => block.Params.GetTarget(i).ToString("R", CultureInfo.InvariantCulture))
        );

    /// <summary>
    ///     Whether parameter <paramref name="index" /> at <paramref name="value" /> makes the block play the probe
    ///     differently from <paramref name="baseline" />: set before the block is prepared, or else (a parameter that
    ///     acts when it is moved, as an LFO reset does; tried at the defaults only) a quarter of the way through.
    /// </summary>
    static bool Changes(
        BlockDescriptor descriptor,
        int type,
        int context,
        int index,
        float value,
        (float[] Left, float[] Right) baseline
    ) {
        foreach (var moveAt in new[] { 0, Probe.Left.Length / 4 }.Take(context == 0 ? 2 : 1)) {
            using var block = Configured(descriptor, type, context, descriptor.Params[index]);
            if (moveAt == 0) {
                block.Params.SetTarget(index, value);
            }

            block.Prepare(Spec);
            var differs = false;
            Render(
                block,
                Probe.Left,
                Probe.Right,
                (offset, left, right) => {
                    if (moveAt > 0 && offset + left.Length == moveAt) {
                        block.Params.SetTarget(index, value);
                    }

                    differs = !left.SequenceEqual(baseline.Left.AsSpan(offset, left.Length))
                        || !right.SequenceEqual(baseline.Right.AsSpan(offset, right.Length));
                    return differs;
                }
            );
            if (differs) {
                return true;
            }
        }

        return false;
    }

    delegate bool Callback(int offset, ReadOnlySpan<float> left, ReadOnlySpan<float> right);

    /// <summary>The grid's rows: a side-input block is handed a signal from each.</summary>
    const int GridRows = 6;

    /// <summary>
    ///     The signal on grid row <paramref name="row" /> (-1: the grid input): the input from another moment on each
    ///     row, with the sides swapped on every second row and quieter row by row, so no two rows carry the same.
    /// </summary>
    static void Row(float[] left, float[] right, int row, int offset, Span<float> rowLeft, Span<float> rowRight) {
        var shift = (row + 1) * 1_009;
        var gain = 1f - 0.1f * (row + 1);
        var (first, second) = (row & 1) == 0 ? (left, right) : (right, left);
        for (var i = 0; i < rowLeft.Length; i++) {
            var at = (offset + i + shift) % left.Length;
            rowLeft[i] = first[at] * gain;
            rowRight[i] = second[at] * gain;
        }
    }

    /// <summary>
    ///     Renders through <paramref name="block" /> in <see cref="MaxBlock" /> callbacks with a held note and the
    ///     global mix and gate offsets off zero, so the parameters that follow those show. <paramref name="stop" />
    ///     sees each callback's output and ends the render by returning true.
    /// </summary>
    static (float[] Left, float[] Right) Render(IDspBlock block, float[] left, float[] right, Callback? stop = null) {
        var outLeft = (float[])left.Clone();
        var outRight = (float[])right.Clone();
        var side = block as ISideInputBlock;
        var cables = side?.SideInputs.HasFlag(SideInputs.Cables) == true;
        var taps = side?.SideInputs.HasFlag(SideInputs.Taps) == true;
        Span<float> rowLeft = stackalloc float[MaxBlock];
        Span<float> rowRight = stackalloc float[MaxBlock];
        for (var offset = 0; offset < left.Length; offset += MaxBlock) {
            var frames = Math.Min(MaxBlock, left.Length - offset);
            var io = new StereoBuffer(outLeft.AsSpan(offset, frames), outRight.AsSpan(offset, frames));

            // What the graph does for a block that takes its inputs one by one: a cable from each grid row, added
            // into its buffer by the block itself, and the taps a sidechain can pick from (row -1 is the grid input).
            if (cables) {
                io.Left.Clear();
                io.Right.Clear();
            }

            for (var row = taps ? -1 : 0; side is not null && row < GridRows; row++) {
                Row(left, right, row, offset, rowLeft[..frames], rowRight[..frames]);
                var signal = new StereoBuffer(rowLeft[..frames], rowRight[..frames]);
                if (cables && row >= 0) {
                    side.SideInput(SideInputKind.Cable, row, signal, io);
                }

                if (taps) {
                    side.SideInput(SideInputKind.Tap, row, signal, default);
                }
            }

            // A second note is pressed half way through: some settings act on a new note.
            var notes = offset < left.Length / 2 ? HeldNotes.Of(57) : HeldNotes.Of(57, 64);
            block.Process(io, new(Spec.SampleRate, 120, offset, notes, 10f, 6f));
            if (stop?.Invoke(offset, outLeft.AsSpan(offset, frames), outRight.AsSpan(offset, frames)) == true) {
                break;
            }
        }

        return (outLeft, outRight);
    }
}
