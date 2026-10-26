using Cathode.Dsp.Wdf;
using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Amp;

/// <summary>
///     Two preamp paths and a front-panel switch choosing between them: an amp's channels, of which the guitar is
///     plugged into one (the Vox AC30's Brilliant and Normal channels, the Matchless DC-30's top-boost and EF86
///     channels). <paramref name="Off" /> plays while <paramref name="Switch" /> is off (the stock channel),
///     <paramref name="On" /> while it is on. Each path is a chain of stages like a whole preamp, fed by the same source;
///     the idle channel's tubes do not run, and what of it loads the shared circuit (the other channel's mixing resistor,
///     its volume pot) belongs in the playing path's own netlist.
/// </summary>
/// <param name="Name">A label, e.g. "Channel".</param>
/// <param name="Switch">The switch that selects <paramref name="On" />.</param>
/// <param name="Off">The stages of the channel that plays with the switch off, in signal order (at least one).</param>
/// <param name="On">The stages of the channel that plays with the switch on (at least one).</param>
public sealed record ChannelSwitchStageDesign(
    string Name,
    SwitchRole Switch,
    IReadOnlyList<AmpStageDesign> Off,
    IReadOnlyList<AmpStageDesign> On
) : AmpStageDesign(Name) {
    public override bool HasTremolo => Off.Any(static s => s.HasTremolo) || On.Any(static s => s.HasTremolo);

    public override AmpStage Create(double sampleRate) => new ChannelSwitchStage(this, sampleRate);
}

/// <summary>
///     A <see cref="ChannelSwitchStageDesign" />: both channels' stages, of which only the selected chain is ticked.
///     Turning the switch puts the newly selected chain at its operating point (as plugging into the other input would
///     find it: idle, its caps charged), so the change is as quiet as moving the cable.
/// </summary>
public sealed class ChannelSwitchStage : AmpStage {
    readonly AmpStage[] off;
    readonly AmpStage[] on;
    readonly int offGainStage;
    readonly int onGainStage;
    AmpStage[] active;
    double sourceResistance;
    double restInput;
    double supply = 1;
    double[]? supplies; // the channel's per-node supplies, once it hands them over (SetSupplies)

    public ChannelSwitchStageDesign Design { get; }

    /// <summary>The playing channel's stages.</summary>
    public IReadOnlyList<AmpStage> Active => active;

    /// <summary>Whether the <see cref="ChannelSwitchStageDesign.On" /> channel plays.</summary>
    public bool IsOn => ReferenceEquals(active, on);

    public override double OutputResistance => active[^1].OutputResistance;

    public override double RestOutput => active[^1].RestOutput;

    /// <summary>The playing channel's first nonlinear stage's Newton statistics.</summary>
    public override NewtonSolver? Newton {
        get {
            foreach (var stage in active) {
                if (stage.Newton is { } newton) {
                    return newton;
                }
            }

            return null;
        }
    }

    public override bool IsGainStage => offGainStage >= 0 || onGainStage >= 0;

    /// <summary>The gain stages of the channel that has more of them.</summary>
    public int GainStages => Math.Max(off.Count(static s => s.IsGainStage), on.Count(static s => s.IsGainStage));

    public ChannelSwitchStage(ChannelSwitchStageDesign design, double sampleRate) {
        if (design.Off.Count == 0 || design.On.Count == 0) {
            throw new ArgumentException($"{design.Name}: both channels need a stage.", nameof(design));
        }

        Design = design;
        off = [..design.Off.Select(d => d.Create(sampleRate))];
        on = [..design.On.Select(d => d.Create(sampleRate))];
        offGainStage = Array.FindLastIndex(off, static s => s.IsGainStage);
        onGainStage = Array.FindLastIndex(on, static s => s.IsGainStage);
        active = off;
        Reset(0);
    }

    public override void SetSampleRate(double sampleRate) {
        foreach (var stage in off.Concat(on)) {
            stage.SetSampleRate(sampleRate);
        }
    }

    public override void SetSourceResistance(double ohms) {
        sourceResistance = ohms;
        off[0].SetSourceResistance(ohms);
        on[0].SetSourceResistance(ohms);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void SetSupply(double scale) => supply = scale;

    /// <summary>Each channel's stages on their designs' nodes, or on the switch's.</summary>
    public override void AssignSupplyNode(int node) {
        SupplyNode = node;
        for (var i = 0; i < off.Length; i++) {
            off[i].AssignSupplyNode(Design.Off[i].SupplyNode ?? node);
        }

        for (var i = 0; i < on.Length; i++) {
            on[i].AssignSupplyNode(Design.On[i].SupplyNode ?? node);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void SetSupplies(double[] scales) {
        supplies = scales;
        supply = scales[SupplyNode];
    }

    public override void Update(in AmpControls controls) {
        Configure(off, controls);
        Configure(on, controls);
        var selected = controls.IsOn(Design.Switch) ? on : off;
        if (!ReferenceEquals(selected, active)) {
            active = selected;
            Settle(active);
        }
    }

    /// <summary>The settings for each channel; the preamp bias goes to each channel's last gain stage.</summary>
    public override void Apply(in PreampSettings settings) {
        Apply(off, offGainStage, settings);
        Apply(on, onGainStage, settings);
    }

    public override void Reset(double restInput) {
        this.restInput = restInput;
        Settle(off);
        Settle(on);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override double Tick(double input) {
        var x = input;
        foreach (var stage in active) {
            Supply(stage);
            x = stage.Tick(x);
        }

        return x;
    }

    public override void FlushDenormals() {
        foreach (var stage in active) {
            stage.FlushDenormals();
        }
    }

    /// <summary>
    ///     A stage's supply: its own node's once the channel has handed the nodes over, else the switch's.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Supply(AmpStage stage) {
        if (supplies is { } scales) {
            stage.SetSupplies(scales);
        } else {
            stage.SetSupply(supply);
        }
    }

    static void Configure(AmpStage[] chain, in AmpControls controls) {
        for (var i = 0; i < chain.Length; i++) {
            chain[i].Update(controls);
            if (i > 0) {
                chain[i].SetSourceResistance(chain[i - 1].OutputResistance);
            }
        }
    }

    static void Apply(AmpStage[] chain, int gainStage, in PreampSettings settings) {
        for (var i = 0; i < chain.Length; i++) {
            chain[i].Apply(i == gainStage ? settings : settings with { CathodeResistor = 1 });
        }
    }

    /// <summary>A chain at its operating point, in signal order, as <see cref="AmpChannel.Reset" /> does the preamp.</summary>
    void Settle(AmpStage[] chain) {
        var rest = restInput;
        var resistance = sourceResistance;
        foreach (var stage in chain) {
            stage.SetSourceResistance(resistance);
            Supply(stage);
            stage.Reset(rest);
            rest = stage.RestOutput;
            resistance = stage.OutputResistance;
        }
    }
}
