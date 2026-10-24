using Cathode.Dsp.Cabinets;

namespace Cathode.Dsp.Amp;

/// <summary>
///     Which of the Circuit Amp's advanced controls mean something on a model (docs/08 §4): most act on a component the
///     model may not have (a plate-bypass cap, a cathode follower, the phase inverter's input network, a cap across the
///     master), and a generic switch stands in only where the model has no circuit of its own for it. A control a model
///     lacks is hidden on its panel (<see cref="AmpPanel.Label" />) and does nothing.
/// </summary>
public static class AmpFeatures {
    /// <summary>Whether the Circuit Amp parameter <paramref name="id" /> acts on <paramref name="design" />.</summary>
    public static bool Has(AmpDesign design, string id) {
        var preamp = design.Preamp;
        var panel = design.Panel;
        return id switch {
            "stacktype" => preamp.Any(static s => s is ToneStackStageDesign),
            "stackloc" => preamp.Any(AmpChannel.IsStack),
            "t1plate" => HasPlateCap(design, 2),
            "t2plate" => HasPlateCap(design, 1),
            "cfcomp" or "cfharm" => preamp.Any(static s => s is ToneStackStageDesign {
                    Driver: StackDriver.CathodeFollower
                }
            ),
            "pibiasx" => design.Power.InverterInput is not null,
            "mvloc" => HasMaster(design) && design.Power.ShuntMaster is null
                && design.Power.Topology == PowerTopology.PushPull,
            "mvcap" => preamp.Any(HasMasterCap),
            "fatsw" => !panel.HasSwitch("Fat"),
            "presshift" => !panel.HasSwitch("Shift") && !panel.HasSwitch("Presence Shift"),
            "overdrive" => !panel.HasSwitch("Overdrive")
                && panel.Control1 is not ("Overdrive" or "Drive" or "Lead Drive")
                && panel.Control2 is not ("Overdrive" or "Drive" or "Lead Drive"),
            "cutsw" => !panel.HasSwitch("Bass Cut"),
            _ => true
        };
    }

    /// <summary>
    ///     Whether <paramref name="design" /> has at least <paramref name="count" /> top-level stages with a
    ///     plate-bypass cap (<see cref="HasPlateCap(AmpStageDesign)" />).
    /// </summary>
    static bool HasPlateCap(AmpDesign design, int count) => design.Preamp.Count(HasPlateCap) >= count;

    /// <summary>
    ///     Whether a stage holds a plate-bypass cap (a triode stage's <see cref="TriodeStageDesign.PlateBypass" />, a
    ///     network's <see cref="CapacitorRole.Plate" /> cap, or either in a channel-switched branch). Triode 2 Plate Freq
    ///     scales the last such stage's, Triode 1 the one before it.
    /// </summary>
    public static bool HasPlateCap(AmpStageDesign stage) =>
        stage switch {
            TriodeStageDesign triode => triode.PlateBypass > 0,
            NetworkStageDesign network => network.Netlist.Elements.Any(static e => e.CapacitorRole == CapacitorRole.Plate),
            ChannelSwitchStageDesign channels => channels.Off.Any(HasPlateCap) || channels.On.Any(HasPlateCap),
            _ => false
        };

    static bool HasMaster(AmpDesign design) =>
        design.Preamp.Any(static s => s switch {
                TriodeStageDesign t => t.Volume is not null && t.VolumeRole == PotRole.Master,
                ToneStackStageDesign t => t.LoadRole == PotRole.Master,
                CouplingStageDesign c => c.Role == PotRole.Master,
                NetworkStageDesign n => n.Netlist.Elements.Any(static e =>
                    e.Part != PotPart.None && e.Role == PotRole.Master
                ),
                _ => false
            }
        );

    static bool HasMasterCap(AmpStageDesign stage) =>
        stage switch {
            TriodeStageDesign t => t.VolumeRole == PotRole.Master && t.Volume is not null && t.BrightCap > 0,
            ToneStackStageDesign t => t.LoadRole == PotRole.Master && t.BrightCap > 0,
            NetworkStageDesign n => n.Netlist.Elements.Any(cap => cap.Kind == ElementKind.Capacitor
                && n.Netlist.Elements.Any(pot => pot is { Role: PotRole.Master, Part: PotPart.Upper }
                    && ((pot.A == cap.A && pot.B == cap.B) || (pot.A == cap.B && pot.B == cap.A))
                )
            ),
            _ => false
        };
}

/// <summary>The Circuit Amp's Tonestack Type choices: every distinct stack the models' tone-stack stages use.</summary>
public static class AmpStacks {
    /// <summary>The stacks after Stock, in the order of the choice.</summary>
    public static IReadOnlyList<ToneStackDesign> All { get; } = Collect();

    /// <summary>The choice's options: Stock, then each stack's name (with its first model's where names repeat).</summary>
    public static IReadOnlyList<string> Names { get; } = ["Stock", ..All.Select(Name)];

    static ToneStackDesign[] Collect() {
        var stacks = new List<ToneStackDesign>(ToneStackDesign.All);
        foreach (var design in AmpDesign.All) {
            foreach (var stage in design.Preamp) {
                if (stage is ToneStackStageDesign { Stack: var stack } && !stacks.Contains(stack)) {
                    stacks.Add(stack);
                }
            }
        }

        return [..stacks];
    }

    static string Name(ToneStackDesign stack, int index) {
        var first = AmpDesign.All.FirstOrDefault(a =>
            a.Preamp.Any(s => s is ToneStackStageDesign t && t.Stack == stack)
        );
        var repeated = All.Take(index).Any(s => s.Name == stack.Name);
        return repeated && first is not null ? $"{stack.Name} ({first.Name})" : stack.Name;
    }
}

/// <summary>
///     The Auto speaker load (Speaker: Auto): the impedance curve of the cabinet and speaker the next Cab block
///     synthesises (<see cref="ICabFollower" />). The curve is the closest parametric preset for the enclosure (open or
///     closed, by its number of speakers) with its resonance moved to the speaker's in that box: its free-air Fs, raised
///     by a closed box's air spring to Fs √(1 + V_as / V_b) per speaker.
/// </summary>
public static class AutoSpeaker {
    static readonly SpeakerDesign[][] Table = [
        ..CabCatalogue.Enclosures.Select(static e => CabCatalogue.Speakers.Select(s => Design(e, s)).ToArray())
    ];

    /// <summary>The load for DynaCab enclosure <paramref name="enclosure" /> and speaker <paramref name="speaker" />, or null.</summary>
    public static SpeakerDesign? For(int enclosure, int speaker) =>
        (uint)enclosure < (uint)Table.Length && (uint)speaker < (uint)Table[enclosure].Length
            ? Table[enclosure][speaker]
            : null;

    static SpeakerDesign Design(EnclosureModel enclosure, SpeakerModel speaker) {
        var count = enclosure.Others.Length + 1;
        var preset = enclosure.Open
            ? count switch {
                1 => SpeakerDesign.OpenBack1x12,
                2 => SpeakerDesign.OpenBack2x12,
                _ => SpeakerDesign.OpenBack4x10
            }
            : count > 4 ? SpeakerDesign.Closed8x10 : SpeakerDesign.Closed4x12;
        var resonance = enclosure.Open || enclosure.LitresPerSpeaker <= 0
            ? speaker.Fs
            : speaker.Fs * Math.Sqrt(1 + speaker.Vas / enclosure.LitresPerSpeaker);
        return preset with { Resonance = resonance };
    }
}

/// <summary>
///     The High Treble control: a first-order high shelf at <see cref="Frequency" /> between the preamp and the power
///     amp, ±12 dB over the knob (5 flat, and then skipped).
/// </summary>
public struct HighTreble {
    public const double Frequency = 3_000;
    public const double RangeDb = 12;

    FirstOrder shelf;
    double knob;

    /// <summary>Whether the shelf is in (the knob off its flat centre).</summary>
    public readonly bool IsActive => knob != 0.5;

    /// <summary>The knob as a rotation 0..1 at <paramref name="rate" />; recomputes only when it moved.</summary>
    public void Design(double rotation, double rate) {
        if (rotation == knob && shelf.B0 != 0) {
            return;
        }

        knob = rotation;
        var gain = Math.Pow(10, (2 * rotation - 1) * RangeDb / 20);
        var omega = 2 * Math.PI * Frequency;
        // Boost: (ω + G s) / (ω + s); cut, its mirror G (ω + s) / (G ω + s): flat below, G above.
        if (gain >= 1) {
            shelf.Design(omega, gain, omega, 1, rate);
        } else {
            shelf.Design(gain * omega, gain, gain * omega, 1, rate);
        }
    }

    public double Tick(double x) => shelf.Process(x);

    public void Reset() => shelf.Reset();

    public void FlushDenormals() => shelf.FlushDenormals();
}
