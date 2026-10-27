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
            "t2plate" => HasPlateCap(design, 1),
            "cfcomp" or "cfharm" => preamp.Any(static s => s is ToneStackStageDesign {
                    Driver: StackDriver.CathodeFollower
                }
            ),
            "pibiasx" => design.Power.InverterInput is not null,
            "mvloc" => HasMaster(design)
                && design.Power.ShuntMaster is null
                && design.Power.Topology == PowerTopology.PushPull,
            "fatsw" => !panel.HasSwitch("Fat"),
            "presshift" => !panel.HasSwitch("Shift") && !panel.HasSwitch("Presence Shift"),
            "overdrive" => !panel.HasSwitch("Overdrive")
                && panel.Control1 is not ("Overdrive" or "Drive" or "Lead Drive")
                && panel.Control2 is not ("Overdrive" or "Drive" or "Lead Drive"),
            "cutsw" => !panel.HasSwitch("Bass Cut"),
            "hitreble" => panel.Control1 is not "Hi Treble" && panel.Control2 is not "Hi Treble",
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
    ///     network's <see cref="CapacitorRole.Plate" /> cap, or either in a channel-switched branch). Triode 2 Plate
    ///     Freq scales the last such stage's, Triode 1 the one before it.
    /// </summary>
    public static bool HasPlateCap(AmpStageDesign stage) =>
        stage switch {
            TriodeStageDesign triode => triode.PlateBypass > 0,
            NetworkStageDesign network => network.Netlist.Elements.Any(static e => e.CapacitorRole
                == CapacitorRole.Plate
            ),
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
}

/// <summary>The Circuit Amp's Tonestack Type choices: every distinct stack the models' tone-stack stages use.</summary>
public static class AmpStacks {
    /// <summary>The stacks after Stock, in the order of the choice.</summary>
    public static IReadOnlyList<ToneStackDesign> All { get; } = Collect();

    /// <summary>
    ///     The choice's options: Stock, then each stack's name (with its first model's where names repeat).
    /// </summary>
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
///     The Follow Cab speaker load (Speaker load: Follow Cab): the impedance curve of the cabinet and speaker the next
///     Cab block synthesises (<see cref="ICabFollower" />), derived from that speaker's own electrical data
///     (<see cref="SpeakerElectrical" />) in that enclosure (<see cref="EnclosureModel.Impedance" />):
///     <list type="bullet">
///         <item>
///             the voice coil's DC resistance Re, and its semi-inductance as the power amp's lumped lossy coil
///             (<see cref="SpeakerElectrical.LossyCoil" />: an inductance across an eddy-current resistance, matched to
///             the semi-inductance at 1 and 5 kHz);
///         </item>
///         <item>
///             the motional resonance at the system resonance: free-air Fs in an open back, Fs √(1 + Vas / Vb) in a
///             closed box of Vb per speaker, with Qms raised by the same factor; the peak at Re + Re Qms / Qes, which
///             the box leaves alone;
///         </item>
///         <item>
///             the cabinet's wiring (<see cref="EnclosureModel.Series" />), which scales the curve by series over
///             parallel. The amp is taken to be on the matching tap, as the presets are: the curve is normalised to
///             the cabinet's nominal impedance (a Super Reverb's 2 Ω load on its 2 Ω tap is the 8 Ω reference's
///             shape), and Load impedance sets a mismatch on top.
///         </item>
///     </list>
///     Approximations: identical speakers, each in its own share of the box; an open back's speakers in free air
///     (the baffle's and the neighbouring cones' air mass, which lower Fs a few per cent, are left out); the power
///     amp's voice coil is first order (one inductance across one resistance), so the load matches the speaker's
///     semi-inductance at 1 and 5 kHz and is within about 20 % of it between 20 Hz and 5 kHz. The table is built once
///     (control thread); the audio thread only looks it up.
/// </summary>
public static class AutoSpeaker {
    static readonly SpeakerDesign[][] Table = [
        ..CabCatalogue.Enclosures.Select(static e => CabCatalogue.Speakers.Select(s => Design(e, s)).ToArray())
    ];

    /// <summary>
    ///     The load for DynaCab enclosure <paramref name="enclosure" /> and speaker <paramref name="speaker" />, or
    ///     null.
    /// </summary>
    public static SpeakerDesign? For(int enclosure, int speaker) =>
        (uint)enclosure < (uint)Table.Length && (uint)speaker < (uint)Table[enclosure].Length
            ? Table[enclosure][speaker]
            : null;

    /// <summary>
    ///     <paramref name="speaker" /> in <paramref name="enclosure" /> as the power amp's load, normalised to
    ///     <see cref="SpeakerDesign.Nominal" />.
    /// </summary>
    public static SpeakerDesign Design(EnclosureModel enclosure, SpeakerModel speaker) {
        var electrical = speaker.Electrical;
        var stiffening = enclosure.Stiffening(speaker);
        // Series over parallel scales every element alike, and normalising to the cabinet's nominal undoes it.
        var scale = SpeakerDesign.Nominal / electrical.Nominal;
        var (inductance, eddy) = electrical.LossyCoil();
        return new(
            electrical.Re * scale,
            inductance * scale,
            eddy * scale,
            speaker.Fs * stiffening,
            (electrical.Re + electrical.Res) * scale,
            electrical.Qms * stiffening
        );
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
