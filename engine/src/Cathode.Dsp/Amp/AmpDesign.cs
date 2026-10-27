using Cathode.Dsp.Amp.Models;

namespace Cathode.Dsp.Amp;

/// <summary>
///     A complete amp model as a configuration of the stage library: the preamp stages in signal order, then the power
///     amp, and its front panel. <paramref name="OutputTrimDb" /> evens out the models' loudness at the block's output.
///     Each model lives in its own file under <c>Amp/Models</c> and is registered in <see cref="All" />
///     (<c>Models/AmpModels.cs</c>).
/// </summary>
/// <remarks>
///     <para>Adding an amp:</para>
///     <list type="number">
///         <item>
///             Add <c>Amp/Models/MyAmp.cs</c> with
///             <c>
///                 public static class MyAmp { public static AmpDesign Design { get; } =
///                 new(...); }
///             </c>
///             , built from <see cref="TriodeStageDesign" />, <see cref="ToneStackStageDesign" />,
///             <see cref="CouplingStageDesign" />, <see cref="NetworkStageDesign" /> (any netlist, with pots, switches
///             and devices) and <see cref="EqualizerStageDesign" />, and a <see cref="PowerAmpDesign" />.
///         </item>
///         <item>
///             Name its model-specific controls in <see cref="Panel" /> (and hide standard knobs it lacks), and wire
///             them in its stages with <see cref="PotRole.Control1" />, <see cref="SwitchRole.Switch1" /> and so on.
///         </item>
///         <item>Append <c>MyAmp.Design</c> to <see cref="All" />: the list order is the Type parameter's value.</item>
///     </list>
/// </remarks>
public sealed partial record AmpDesign(
    string Name,
    IReadOnlyList<AmpStageDesign> Preamp,
    PowerAmpDesign Power,
    double OutputTrimDb
) {
    /// <summary>What the block's generic controls are called on this model, and which it lacks.</summary>
    public AmpPanel Panel { get; init; } = AmpPanel.Standard;

    /// <summary>
    ///     The preamp supply's decoupling chain as the schematic draws it, with each stage's node on its design
    ///     (<see cref="AmpStageDesign.SupplyNode" />); null for the generic chain (<see cref="AmpChannel" />).
    /// </summary>
    public PreampSupplyDesign? PreampSupply { get; init; }

    /// <summary>
    ///     A solid-state power amp in place of the tube one (the Roland JC-120): the channel runs it instead of
    ///     <see cref="Power" />, which such a model sets to <see cref="SolidStateTubePlaceholder" /> and never runs.
    ///     Its tube expert parameters then do nothing (see <see cref="SolidStatePowerAmp" /> for those that apply).
    ///     Null for a tube amp.
    /// </summary>
    public SolidStatePowerDesign? SolidState { get; init; }

    /// <summary>
    ///     The <see cref="Power" /> of a model with a <see cref="SolidState" /> power amp: a tube power amp the channel
    ///     builds (so that every model has one to inspect) but never runs.
    /// </summary>
    public static PowerAmpDesign SolidStateTubePlaceholder => Blackface.Design.Power;
}
