using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Circuit Amp's per-model panels and Axe-Fx parity parameters (docs/06 §1.4): labels per type, each expert
///     parameter stock at its default and audible away from it, and the power-section behaviours they expose (rectifier
///     and ripple, variac, transformer saturation, the speaker presets, the graphic EQ slot).
/// </summary>
public class CircuitAmpExpertTests(ITestOutputHelper output) {
    const double Rate = 48_000;
    const double InnerRate = 96_000;

    /// <summary>
    ///     Each expert parameter, away from its default, on each model: what to set, and whether it must be audible.
    /// </summary>
    public static TheoryData<int, int, float, bool> ExpertSettings {
        get {
            (int Index, float Value)[] settings = [
                (CircuitAmpBlock.InputTrim, 10),
                (CircuitAmpBlock.LowCut, 300),
                (CircuitAmpBlock.PreampTube, 2),
                (CircuitAmpBlock.PreampTube, 3),
                (CircuitAmpBlock.PreampTube, 4),
                (CircuitAmpBlock.PreampTube, 5),
                (CircuitAmpBlock.PreampBias, 100),
                (CircuitAmpBlock.PreampBias, -100),
                (CircuitAmpBlock.PreampSag, 0),
                (CircuitAmpBlock.StackFrequency, 180),
                (CircuitAmpBlock.MasterTrim, 30),
                (CircuitAmpBlock.PowerAmpModel, 0),
                (CircuitAmpBlock.PowerTube, 2),
                (CircuitAmpBlock.PowerTube, 3),
                (CircuitAmpBlock.PowerTube, 4),
                (CircuitAmpBlock.PowerTube, 5),
                (CircuitAmpBlock.PowerTube, 6),
                (CircuitAmpBlock.PowerTube, 7),
                (CircuitAmpBlock.TransformerMatch, 200),
                (CircuitAmpBlock.TransformerLow, 400),
                (CircuitAmpBlock.TransformerHigh, 25),
                (CircuitAmpBlock.TransformerDrive, 10),
                (CircuitAmpBlock.SpeakerCurve, 1),
                (CircuitAmpBlock.SpeakerCurve, 4),
                (CircuitAmpBlock.SpeakerCurve, 5),
                (CircuitAmpBlock.SpeakerResonance, 250),
                (CircuitAmpBlock.SpeakerQ, 300),
                (CircuitAmpBlock.SpeakerPeak, 300),
                (CircuitAmpBlock.SpeakerHigh, 300),
                (CircuitAmpBlock.SpeakerImpedance, 50),
                (CircuitAmpBlock.PresenceFrequency, 300),
                (CircuitAmpBlock.DepthFrequency, 300),
                (CircuitAmpBlock.Variac, 70),
                (CircuitAmpBlock.SupplyTime, 25),
                (CircuitAmpBlock.SupplyType, 2),
                (CircuitAmpBlock.Rectifier, 4),
                (CircuitAmpBlock.CathodeTime, 25),
                (CircuitAmpBlock.BrightCap, 300)
            ];
            var data = new TheoryData<int, int, float, bool>();
            for (var type = 0; type < AmpDesign.All.Count; type++) {
                var design = AmpDesign.All[type];
                foreach (var (index, value) in settings) {
                    // Known no-ops: the cathode time constant of a fixed-bias amp, a 6L6GC or EL34
                    // or EL84 in place of itself, the presence and depth frequencies of an amp without feedback; the
                    // master trim of an amp without a master, the preamp tube and stack frequency of a (stock) channel
                    // without a 12AX7 or a tone stack (the AC15's EF86 channel), the speaker preset or rectifier the
                    // amp has anyway; and where the panel says the amp lacks the control, the bright cap (no bright
                    // switch: a bright cap a channel switch engages, or none at all) and the presence frequency (no
                    // presence).
                    var tube = new[] { null, "6L6GC", "EL34", "EL84", "6V6GT", "KT66", "KT88", "6550" }[
                        index == CircuitAmpBlock.PowerTube ? (int)value : 0];
                    // And the bright cap of an amp without one (the Laney's drive channel).
                    var audible = !(index == CircuitAmpBlock.CathodeTime && !design.Power.CathodeBias)
                        && !(index == CircuitAmpBlock.BrightCap && design == LaneyGH100L.Design)
                        // And the supply's time constants on an amp still clean at gain 7 (the Princeton AA964's two
                        // stages, the second inside the feedback loop; the Scout's two, with no feedback, into a
                        // class-A pair on one cathode): its output tubes draw too little extra current to sag.
                        && !(index is CircuitAmpBlock.PreampSag or CircuitAmpBlock.SupplyTime
                            && (design == PrincetonAA964.Design || design == GibsonScout.Design))
                        // Nor does the B+ time constant on the Supro 1624T: its 6973 pair idles at 44 mA a tube on one
                        // cathode resistor, and RCA's sheet has such a pair drawing only a fifth more at full power
                        // (about -66 dB).
                        && !(index == CircuitAmpBlock.SupplyTime && design == Supro1624T.Design)
                        && !(index == CircuitAmpBlock.PowerTube
                            && design.Power.Name.Contains(tube ?? "-", StringComparison.Ordinal))
                        && !(index is CircuitAmpBlock.PresenceFrequency or CircuitAmpBlock.DepthFrequency
                            && !design.Power.HasFeedback)
                        // The Mark V's channel 3 at Gain 7, behind the loop's recovery stage with Output at 10, holds
                        // its power amp so far into clipping that the depth corner moves the render by less (−65 dB).
                        // Its two cleaner channels, on the same power amp, stay in the list.
                        && !(index == CircuitAmpBlock.DepthFrequency && design == MarkV.Design)
                        && !(index == CircuitAmpBlock.MasterTrim && design.Panel.Master is null)
                        && !(index == CircuitAmpBlock.PreampTube && !StockPath(design).Any(HasTwelveAX7))
                        && !(index == CircuitAmpBlock.StackFrequency && !StockPath(design).Any(HasStack))
                        && !(index == CircuitAmpBlock.BrightCap && design.Panel.Bright is null)
                        && !(index == CircuitAmpBlock.PresenceFrequency && design.Panel.Presence is null)
                        // Nor has a presence without a cap a corner to move (the Fireball's rheostat in series with
                        // the feedback): Presence Freq is hidden there.
                        && !(index == CircuitAmpBlock.PresenceFrequency && !design.Power.Feedback.HasPresenceCap)
                        && !(index == CircuitAmpBlock.SpeakerCurve
                            && SpeakerDesign.Presets[(int)value - 1] == design.Power.Speaker)
                        && !(index == CircuitAmpBlock.Rectifier
                            && RectifierModel.All[(int)value - 1] == design.Power.PowerSupply.Rectifier)
                        && !(index == CircuitAmpBlock.BrightCap && !HasBrightCap(design))
                        && !(design.SolidState is not null && !SolidStateAudible(design, index))
                        // A preamp fed from a separate screen winding (the SVT) sees no sag to scale.
                        && !(index == CircuitAmpBlock.PreampSag && design.Power.ScreenSupply > 0)
                        // The Major's preamp node sits behind 25k and 50 µF (1.25 s): it does not move within the
                        // render, and its only preamp stage before the loop is V1 at small signal.
                        && !(index == CircuitAmpBlock.PreampSag
                            && design.Power.PowerSupply.PreampResistance * design.Power.PowerSupply.PreampCap > 1)
                        // A rack preamp on its own supply (the TriAxis) sees no sag to scale.
                        && !(index == CircuitAmpBlock.PreampSag && design.PreampSupply is { Separate: true })
                        // Nor does the B+ time constant reach far on the TriAxis leads: with the Dynamic Voice flat
                        // at its default their lows no longer drive the generic power amp hard enough to sag much
                        // (about −69 dB, against −50 dB with the old scooped default).
                        && !(index == CircuitAmpBlock.SupplyTime
                            && (design == TriAxis.Lead1 || design == TriAxis.Lead2))
                        // The Bass 400's core at this level is far below its knee (−65 dB at drive 10 when it
                        // was sized for 400 W; about as far at its 300 W).
                        && !(index == CircuitAmpBlock.TransformerDrive && design == MesaBass400.Design);
                    data.Add(type, index, value, audible);
                }
            }

            return data;
        }
    }

    [Fact]
    public void Panel_labels_name_and_hide_controls_per_model() {
        var parameters = CircuitAmpBlock.Info.Params;
        IReadOnlyList<string>? Labels(string id) => parameters.Single(p => p.Id == id).TypeLabels;
        string Label(AmpDesign design, string id) => Labels(id)![AmpDesign.All.ToList().IndexOf(design)];

        Assert.Equal(["Gain", "Gain", "Gain"], Labels("gain")!.Take(3));
        Assert.Equal([string.Empty, string.Empty, "Cut"], Labels("cut")!.Take(3));
        Assert.Equal(["Middle", "Middle", string.Empty], Labels("middle")!.Take(3));
        Assert.All(Labels("switch1")!.Take(2), static l => Assert.Empty(l));
        Assert.All(Labels("eq1")!.Take(3), static l => Assert.Empty(l));
        Assert.Null(Labels("variac"));
        Assert.Equal(AmpDesign.All.Count, Labels("control1")!.Count);

        // Channel switches and the class-A family's own controls.
        Assert.Equal("Normal Ch", Label(AC30.Design, "switch1"));
        Assert.Equal(string.Empty, Label(AC30.Design, "master"));
        Assert.Equal("Brilliance", Label(AC15.Design, "bright"));
        Assert.Equal(string.Empty, Label(AC15.Design, "treble"));
        Assert.Equal("Volume", Label(DC30.Design, "gain"));
        Assert.Equal("Tone (Ch 2)", Label(DC30.Design, "control1"));
        Assert.Equal("Master", Label(DC30.Design, "master"));

        // The Marshall family's.
        Assert.Equal("Preamp", Label(JCM2203.Design, "gain"));
        Assert.Equal("Low Input", Label(JCM2203.Design, "switch1"));
        Assert.Equal("Jumper", Label(JTM45.Design, "switch1"));
        Assert.Equal(string.Empty, Label(JTM45.Design, "master"));
        Assert.Equal("Drive", Label(LaneyGH100L.Design, "control1"));
    }

    [Fact]
    public void Parameters_after_level_are_appended_with_stock_defaults() {
        var parameters = CircuitAmpBlock.Info.Params;
        string[] first = [
            "type", "gain", "bass", "middle", "treble", "presence", "depth", "cut", "master", "sag", "bias",
            "feedback", "bright", "level"
        ];
        Assert.Equal(first, parameters.Take(first.Length).Select(static p => p.Id));
        Assert.Equal(113, parameters.Count);
        Assert.Equal(CircuitAmpBlock.PreampModel, parameters.Count - 1);
        foreach (var p in parameters.Skip(first.Length)) {
            output.WriteLine($"{p.Id,-10} {p.Name,-20} {p.Min,8} .. {p.Max,-8} default {p.Default} {p.Unit}");
        }
    }

    [Theory]
    [MemberData(nameof(ExpertSettings))]
    public void Expert_parameters_are_audible_and_bounded(int type, int index, float value, bool audible) {
        float[] Render(params (int Index, float Value)[] settings) {
            using var block = new CircuitAmpBlock();
            block.Params.SetTarget(CircuitAmpBlock.Type, type);
            block.Params.SetTarget(CircuitAmpBlock.Gain, 7);
            block.Params.SetTarget(CircuitAmpBlock.Master, 10); // the power amp driven, so its settings are heard
            CircuitAmpBlockTests.SecondMasterFullUp(block, type);
            block.Params.SetTarget(CircuitAmpBlock.Depth, 5);
            block.Params.SetTarget(CircuitAmpBlock.Bright, 1);
            foreach (var (i, v) in settings) {
                block.Params.SetTarget(i, v);
            }

            block.Prepare(new(Rate, 64));
            return BlockRender.Mono(block, CircuitAmpBlockTests.Chord(14_400, 0.25f), 64);
        }

        var stock = Render();
        var changed = Render((index, value));
        var difference = Analysis.NullResidualDb(stock, changed) - Analysis.ToDb(Analysis.Rms(stock));
        var peak = Analysis.ToDb(Analysis.Peak(changed));
        output.WriteLine(
            $"{AmpDesign.All[type].Name} {CircuitAmpBlock.Info.Params[index].Id} = {value}: "
            + $"{difference:0.0} dB from stock, "
            + $"peak {peak:0.0} dBFS"
        );
        Assert.True(Analysis.AllFinite(changed));
        Assert.True(peak < 6, $"peak {peak:0.0} dBFS");
        if (audible) {
            // A class-A amp (cathode bias, EL84) draws nearly the same current loud or quiet: its preamp sees little
            // sag. Behind a slow preamp node (0.3 s or more) and the decoupling chain below it, little of the sag
            // reaches the stages within the render.
            var supply = AmpDesign.All[type].Power.PowerSupply;
            var limit = index != CircuitAmpBlock.PreampSag ? -60
                : AmpDesign.All[type].Power.CathodeBias ? -80
                : supply.PreampResistance * supply.PreampCap >= 0.3 ? -70
                : -60;
            Assert.True(difference > limit, $"{difference:0.0} dB from stock");
        }
    }

    [Fact]
    public void Expert_parameters_at_their_defaults_leave_the_amp_stock() {
        for (var type = 0; type < AmpDesign.All.Count; type++) {
            using var plain = new CircuitAmpBlock();
            using var explicitStock = new CircuitAmpBlock();
            plain.Params.SetTarget(CircuitAmpBlock.Type, type);
            explicitStock.Params.SetTarget(CircuitAmpBlock.Type, type);
            for (var i = CircuitAmpBlock.Switch1; i < CircuitAmpBlock.Info.Params.Count; i++) {
                // Move every parameter away and back before preparing: the block must not keep anything from it.
                explicitStock.Params.SetTarget(i, CircuitAmpBlock.Info.Params[i].Max);
                explicitStock.Params.SetTarget(i, CircuitAmpBlock.Info.Params[i].Default);
            }

            plain.Prepare(new(Rate, 64));
            explicitStock.Prepare(new(Rate, 64));
            var input = CircuitAmpBlockTests.Chord(9_600, 0.25f);
            Assert.Equal(BlockRender.Mono(plain, input, 64), BlockRender.Mono(explicitStock, input, 64));
        }
    }

    [Fact]
    public void Power_amp_off_outputs_the_preamp_and_leaves_the_power_stage_idle() {
        using var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, 1);
        block.Params.SetTarget(CircuitAmpBlock.PowerAmpModel, 0);
        block.Prepare(new(Rate, 64));
        var idle = block.Channel.Power.PlateCurrents;
        var rendered = BlockRender.Mono(block, CircuitAmpBlockTests.Chord(9_600, 0.25f), 64);
        output.WriteLine($"preamp out: peak {Analysis.ToDb(Analysis.Peak(rendered)):0.0} dBFS");
        Assert.InRange(Analysis.ToDb(Analysis.Peak(rendered)), -40, 0);
        Assert.Equal(idle, block.Channel.Power.PlateCurrents);
    }

    /// <summary>
    ///     With the power amp off nothing steps its supply, which the preamp still reads. Switched off while playing,
    ///     the supply must not stay where the playing had sagged it: the preamp runs on the voltage at rest, as it
    ///     does when the switch is thrown in silence, and the power amp comes back from rest too.
    /// </summary>
    [Fact]
    public void Power_amp_switched_off_while_playing_leaves_the_preamp_on_the_supply_at_rest() {
        using var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, AmpDesign.All.ToList().IndexOf(Plexi.Design));
        block.Params.SetTarget(CircuitAmpBlock.Gain, 10);
        block.Params.SetTarget(CircuitAmpBlock.Master, 10);
        block.Params.SetTarget(CircuitAmpBlock.Sag, 10);
        block.Prepare(new(Rate, 64));
        var rest = (Node: block.Channel.Supplies[0], block.Channel.Power.Supply);
        var sine = Signals.Sine(110, Rate, 96_000, 0.5f);
        BlockRender.Mono(block, sine, 64);
        var sagged = (Node: block.Channel.Supplies[0], block.Channel.Power.Supply);
        Assert.True(sagged.Node < rest.Node - 0.02, $"the supply did not sag: {sagged.Node:0.0000}");

        block.Params.SetTarget(CircuitAmpBlock.PowerAmpModel, 0);
        BlockRender.Mono(block, [..sine.AsSpan(0, 24_000)], 64);
        var off = (Node: block.Channel.Supplies[0], block.Channel.Power.Supply);
        output.WriteLine(
            $"preamp node at rest {rest.Node:0.0000} (B+ {rest.Supply:0.0} V), playing {sagged.Node:0.0000} "
            + $"({sagged.Supply:0.0} V), power amp off {off.Node:0.0000} ({off.Supply:0.0} V)"
        );
        Assert.Equal(rest.Node, off.Node, 1e-9);
        Assert.Equal(rest.Supply, off.Supply, 1e-6);

        // And a setting the supply follows still reaches the preamp while the power amp is off.
        block.Params.SetTarget(CircuitAmpBlock.Variac, 70);
        BlockRender.Mono(block, [..sine.AsSpan(0, 24_000)], 64);
        using var brown = new CircuitAmpBlock();
        brown.Params.SetTarget(CircuitAmpBlock.Type, AmpDesign.All.ToList().IndexOf(Plexi.Design));
        brown.Params.SetTarget(CircuitAmpBlock.Sag, 10);
        brown.Params.SetTarget(CircuitAmpBlock.Variac, 70);
        brown.Prepare(new(Rate, 64));
        output.WriteLine($"Variac 70 % with the power amp off: B+ {block.Channel.Power.Supply:0.0} V");
        Assert.Equal(brown.Channel.Power.Supply, block.Channel.Power.Supply, 1e-6);
        Assert.True(block.Channel.Supplies[0] < 0.8 * rest.Node, "the preamp does not follow the Variac");
    }

    [Fact]
    public void Input_select_takes_the_chosen_input() {
        static float[] Render(int select, bool silentLeft) {
            using var block = new CircuitAmpBlock();
            block.Params.SetTarget(CircuitAmpBlock.InputSelect, select);
            block.Prepare(new(Rate, 64));
            var chord = CircuitAmpBlockTests.Chord(4_800, 0.25f);
            var silence = new float[chord.Length];
            return BlockRender.Stereo(block, silentLeft ? silence : chord, silentLeft ? chord : silence, static _ => 64)
                .Left;
        }

        Assert.True(Analysis.Rms(Render(0, true)) < 1e-6);
        Assert.True(Analysis.Rms(Render(1, true)) > 1e-3);
        Assert.True(Analysis.Rms(Render(1, false)) < 1e-6);
        Assert.True(Analysis.Rms(Render(2, true)) > 1e-3);
    }

    [Theory]
    [InlineData(SupplyMode.Ac60, 120)]
    [InlineData(SupplyMode.Ac50, 100)]
    public void Ac_supply_ripples_at_twice_the_line_frequency(SupplyMode mode, double ripple) {
        var power = new PowerAmp(Plexi.Design.Power, InnerRate);
        var dc = power.Supply;
        power.Apply(PowerSettings.Stock with { Supply = mode });
        power.Reset();
        const int Length = 65_536;
        var supply = new float[Length];
        for (var i = 0; i < 4 * Length; i++) {
            power.Tick(0);
            if (i >= 3 * Length) {
                supply[i - 3 * Length] = (float)power.Supply;
            }
        }

        var mean = supply.Average(static v => (double)v);
        var magnitudes = Fft.Magnitudes(supply.Select(v => (float)(v - mean)).ToArray());
        var peak = Array.IndexOf(magnitudes, magnitudes.Max()) * InnerRate / Length;
        var swing = supply.Max() - supply.Min();
        output.WriteLine(
            $"{mode}: mean B+ {mean:0.0} V (DC supply {dc:0.0} V), ripple {swing:0.00} V p-p at {peak:0.0} Hz"
        );
        Assert.InRange(peak, ripple - 3, ripple + 3);
        Assert.InRange(mean, 0.97 * dc, 1.03 * dc);
        Assert.InRange(swing, 0.1, 20);
    }

    /// <summary>
    ///     A stiff silicon supply on AC (the JCM900 4100's 25 Ω into 100 µF: R C = 2.5 ms, under the 4.2 ms at which
    ///     a source-resistance calibration still reaches the design's B+) charges its reservoir smoothly in each
    ///     conduction window, even at Sag 0 with B+ Time Const 25 % (dt / R C about 1.6 at the inner rate), instead of
    ///     a sawtooth at the inner Nyquist rate; its mean is the design's B+; and Sag, which scales the source
    ///     resistance, still moves it.
    /// </summary>
    [Fact]
    public void Ac_supply_on_a_stiff_silicon_supply_charges_smoothly_and_follows_sag() {
        var design = JCM900.Head100.Power;

        (double Mean, double Kink) Run(double sag, double timeConstant) {
            var power = new PowerAmp(design, InnerRate);
            power.Configure(new(0.5, 0, 0, sag, 0.5, 0.5));
            power.Apply(PowerSettings.Stock with { Supply = SupplyMode.Ac60, TimeConstant = timeConstant });
            power.Reset();
            var (mean, kink, previous, before) = (0.0, 0.0, 0.0, 0.0);
            const int Settle = 48_000;
            const int Length = 19_200; // 0.2 s: twelve cycles
            for (var i = 0; i < Settle + Length; i++) {
                power.Tick(0);
                var v = power.Supply;
                if (i >= Settle) {
                    mean += v / Length;
                    kink = Math.Max(kink, Math.Abs(v - 2 * previous + before)); // a sawtooth's second difference
                }

                (before, previous) = (previous, v);
            }

            return (mean, kink);
        }

        var stock = Run(0.5, 1);
        var hard = Run(0, 0.25);
        var stiff = Run(0, 1);
        var soft = Run(1, 1);
        output.WriteLine(
            $"mean B+ {stock.Mean:0.0} V (design {design.Supply:0.0} V), "
            + $"Sag 0 {stiff.Mean:0.0} V, Sag 10 {soft.Mean:0.0} V; "
            + $"largest second difference {stock.Kink:0.000} V stock, {hard.Kink:0.000} V at Sag 0 and 25 %"
        );
        Assert.InRange(stock.Mean, 0.985 * design.Supply, 1.015 * design.Supply);
        Assert.True(stock.Kink < 0.5 && hard.Kink < 0.5, "B+ jumps from sample to sample");
        Assert.True(stiff.Mean > soft.Mean + 5, "Sag does not move the AC supply");
    }

    [Fact]
    public void Variac_and_rectifiers_set_the_supply() {
        static double Settled(PowerSettings settings, bool loud) {
            var power = new PowerAmp(AC30.Design.Power, InnerRate);
            power.Apply(settings);
            power.Reset();
            for (var i = 0; i < 48_000; i++) {
                power.Tick(loud ? 0.8 * Math.Sin(2 * Math.PI * 220 * i / InnerRate) : 0);
            }

            return power.Supply;
        }

        var stock = Settled(PowerSettings.Stock, false);
        var brown = Settled(PowerSettings.Stock with { Variac = 0.8 }, false);
        output.WriteLine($"AC30 B+: stock {stock:0.0} V, variac 80 % {brown:0.0} V");
        Assert.InRange(brown / stock, 0.75, 0.85);

        var silicon = stock - Settled(PowerSettings.Stock with { Rectifier = RectifierModel.Silicon }, true);
        var gz34 = stock - Settled(PowerSettings.Stock with { Rectifier = RectifierModel.GZ34 }, true);
        var fiveY3 = stock - Settled(PowerSettings.Stock with { Rectifier = RectifierModel.FiveY3 }, true);
        output.WriteLine($"B+ drop at full drive: silicon {silicon:0.0} V, GZ34 {gz34:0.0} V, 5Y3 {fiveY3:0.0} V");
        Assert.True(silicon < gz34 && gz34 < fiveY3, "a softer rectifier sags more");
    }

    [Fact]
    public void Transformer_drive_saturates_low_notes() {
        const int Length = 65_536;

        double Thd(double drive, int bin) {
            var power = new PowerAmp(Plexi.Design.Power, InnerRate);
            power.Apply(PowerSettings.Stock with { Drive = drive });
            power.Reset();
            var y = new float[Length];
            for (var i = 0; i < 2 * Length; i++) {
                var x = 0.5 * Math.Sin(2 * Math.PI * bin * i / Length);
                var s = power.Tick(x);
                if (i >= Length) {
                    y[i - Length] = (float)s;
                }
            }

            return Analysis.Thd(y, bin);
        }

        // About 42 Hz and 1.03 kHz, below the output tubes' clipping.
        var (low0, low10) = (Thd(0, 29), Thd(10, 29));
        var (high0, high10) = (Thd(0, 703), Thd(10, 703));
        output.WriteLine(
            $"THD at 42 Hz: drive 0 {Analysis.ToDb(low0):0.0} dB, drive 10 {Analysis.ToDb(low10):0.0} dB; at 1 kHz "
            + $"{Analysis.ToDb(high0):0.0} dB, {Analysis.ToDb(high10):0.0} dB"
        );
        Assert.True(Analysis.ToDb(low10) - Analysis.ToDb(low0) > 6, "the core saturates on low notes");
        Assert.True(Math.Abs(Analysis.ToDb(high10) - Analysis.ToDb(high0)) < 2, "and leaves the mids alone");
    }

    /// <summary>
    ///     On a single-ended amp the idle current's flux sits in the core, and Xfmr Drive brings the knee towards it.
    ///     That flux follows the inductance (Xfmr Low Freq) and the idle current (Bias, Variac, the tube), so the knee
    ///     must follow them too: placed from the design's values it fell below the flux at rest, the core sat
    ///     saturated and every cut-off of the tube spiked the output to 15-70 times full scale.
    /// </summary>
    [Theory]
    [InlineData(false, 25f, 50f, 100f, 0)]
    [InlineData(false, 25f, 100f, 130f, 0)]
    [InlineData(false, 100f, 100f, 130f, 0)]
    [InlineData(false, 100f, 50f, 100f, 7)]
    [InlineData(true, 25f, 50f, 100f, 0)]
    [InlineData(true, 25f, 100f, 130f, 0)]
    [InlineData(true, 100f, 100f, 130f, 0)]
    public void Transformer_drive_on_a_single_ended_amp_keeps_the_knee_above_the_flux_at_rest(
        bool princeton,
        float lowFrequency,
        float bias,
        float variac,
        int tube
    ) {
        var design = princeton ? Princeton5F2.Design : Champ.Design;
        var input = new float[48_000];
        var chord = CircuitAmpBlockTests.Chord(12_000, 1.2f);
        var noise = new Random(7);
        for (var i = 0; i < input.Length; i++) {
            input[i] = chord[i % chord.Length] + 0.01f * (float)(2 * noise.NextDouble() - 1);
        }

        (double Peak, double Flux, double Drift) Run(float drive) {
            using var block = new CircuitAmpBlock();
            block.Params.SetTarget(CircuitAmpBlock.Type, AmpDesign.All.ToList().IndexOf(design));
            block.Params.SetTarget(CircuitAmpBlock.TransformerLow, lowFrequency);
            block.Params.SetTarget(CircuitAmpBlock.Bias, bias);
            block.Params.SetTarget(CircuitAmpBlock.Variac, variac);
            block.Params.SetTarget(CircuitAmpBlock.PowerTube, tube);
            block.Params.SetTarget(CircuitAmpBlock.TransformerDrive, drive);
            block.Prepare(new(Rate, 64));
            var rest = block.Channel.Power.Flux;
            BlockRender.Mono(block, new float[24_000], 64);
            var drift = Math.Abs(block.Channel.Power.Flux - rest);
            return (Analysis.Peak(BlockRender.Mono(block, input, 64)), rest, drift);
        }

        // The same settings reached while playing: the knee follows each of them without a reset.
        double Live(float drive) {
            using var block = new CircuitAmpBlock();
            block.Params.SetTarget(CircuitAmpBlock.Type, AmpDesign.All.ToList().IndexOf(design));
            block.Prepare(new(Rate, 64));
            BlockRender.Mono(block, [..input.AsSpan(0, 12_000)], 64);
            block.Params.SetTarget(CircuitAmpBlock.TransformerLow, lowFrequency);
            block.Params.SetTarget(CircuitAmpBlock.Bias, bias);
            block.Params.SetTarget(CircuitAmpBlock.Variac, variac);
            block.Params.SetTarget(CircuitAmpBlock.TransformerDrive, drive);
            return Analysis.Peak(BlockRender.Mono(block, input, 64));
        }

        var linear = Run(0);
        for (var drive = 2; drive <= 10; drive += 2) {
            var (peak, rest, drift) = Run(drive);
            output.WriteLine(
                $"{design.Name} Xfmr Drive {drive}: peak {peak:0.00} (linear core {linear.Peak:0.00}), "
                + $"flux at rest {rest:0.000} Vs, drift in silence {drift:0.0000} Vs"
            );
            Assert.True(peak < 2 * linear.Peak + 0.1, $"Xfmr Drive {drive}: peak {peak:0.00}");
            Assert.True(drift < 0.01 * Math.Abs(rest), $"Xfmr Drive {drive}: the core does not start at rest");
            Assert.True(
                Math.Abs(rest) > 0.5 * Math.Abs(linear.Flux),
                $"Xfmr Drive {drive}: the core is saturated at rest ({rest:0.000} Vs of {linear.Flux:0.000} Vs)"
            );
            var live = Live(drive);
            output.WriteLine($"  set while playing: peak {live:0.00}");
            Assert.True(live < 2 * linear.Peak + 0.1, $"Xfmr Drive {drive} set while playing: peak {live:0.00}");
        }
    }

    [Fact]
    public void Speaker_presets_peak_at_their_resonance() {
        foreach (var speaker in SpeakerDesign.Presets) {
            var (r, l, c) = speaker.Motional;
            var omega = 2 * Math.PI * speaker.Resonance;
            var motional = 1 / (1 / r + 1 / (omega * l) - omega * c); // real at resonance
            output.WriteLine($"{speaker.Resonance} Hz: motional {motional:0.0} Ω + {speaker.Resistance} Ω");
            Assert.Equal(speaker.PeakImpedance, speaker.Resistance + motional, 1e-6);
        }
    }

    [Fact]
    public void Equalizer_bands_boost_their_centres_and_are_exact_when_flat() {
        var design = new EqualizerStageDesign("EQ", EqualizerStageDesign.MarkBands);
        var stage = design.Create(InnerRate);
        stage.Update(new(0.5, 0.5, 0.5, 0.5, 1, false));
        stage.Reset(0);
        Assert.Equal(0.123, stage.Tick(0.123));

        stage.Update(new(0.5, 0.5, 0.5, 0.5, 1, false, Equalizer: new(0, 0, 12, 0, -12)));
        var impulse = new float[16_384];
        for (var i = 0; i < impulse.Length; i++) {
            impulse[i] = (float)stage.Tick(i == 0 ? 1 : 0);
        }

        var boost = Analysis.ToDb(Analysis.MagnitudeAt(impulse, 750, InnerRate));
        var cut = Analysis.ToDb(Analysis.MagnitudeAt(impulse, 6_600, InnerRate));
        var between = Analysis.ToDb(Analysis.MagnitudeAt(impulse, 30, InnerRate));
        output.WriteLine($"750 Hz {boost:0.00} dB, 6.6 kHz {cut:0.00} dB, 30 Hz {between:0.00} dB");
        Assert.InRange(boost, 11.5, 12.5);
        Assert.InRange(cut, -12.5, -11.5);
        Assert.InRange(between, -0.5, 0.5);
    }

    [Fact]
    public void Depth_and_presence_frequency_hide_where_there_is_nothing_for_them_to_move() {
        var parameters = CircuitAmpBlock.Info.Params;
        var depth = parameters[CircuitAmpBlock.DepthFrequency].TypeLabels!;
        var presence = parameters[CircuitAmpBlock.PresenceFrequency].TypeLabels!;
        for (var type = 0; type < AmpDesign.All.Count; type++) {
            var design = AmpDesign.All[type];
            var feedback = design.Power.Feedback;
            // Depth Freq: a Depth knob, a fixed low shelf in the feedback network (the BE-100), or a Deep switch whose
            // resonance it moves (the DSL100).
            var hasDepth = design.Panel.HasTubes
                && (design.Panel.Depth is not null || feedback.LowFeedback != 1 || feedback.Deep is not null);
            Assert.True(hasDepth == (depth[type] == "Depth Freq"), $"{design.Name}: Depth Freq '{depth[type]}'");
            // Presence Freq: a presence cap in a tube power amp's feedback network, with a knob on it or not.
            var hasPresence = design.Panel.HasTubes && feedback.PresenceCap >= 1e-9;
            Assert.True(
                hasPresence == (presence[type] == "Presence Freq"),
                $"{design.Name}: Presence Freq '{presence[type]}'"
            );
            // A Presence knob turns a network with a cap, or a rheostat in series with the feedback (the Fireball's has
            // no cap: the knob is the amount of feedback, and there is no corner for Presence Freq to move).
            if (design.Panel.Presence is not null && !design.Power.Brilliance && design.Panel.HasTubes) {
                Assert.True(hasPresence || feedback.Series is not null, $"{design.Name} has a Presence knob");
            }
        }

        static string Label(string model, IReadOnlyList<string> labels) =>
            labels[AmpDesign.All.ToList().FindIndex(a => a.Name == model)];

        Assert.Equal(string.Empty, Label("Mark IIC+", depth));
        Assert.Equal(string.Empty, Label("SVT", depth));
        Assert.Equal("Depth Freq", Label("SLO-100", depth));
        Assert.Equal(string.Empty, Label("SVT", presence));
        Assert.Equal(string.Empty, Label("Portaflex B-15N", presence));
        Assert.Equal(string.Empty, Label("Bass 400", presence));
        Assert.Equal(string.Empty, Label("Legacy VL100", presence));
        Assert.Equal("Presence Freq", Label("Dual Rectifier", presence));
    }

    [Fact]
    public void Expert_controls_hide_on_models_without_the_part_they_act_on() {
        var parameters = CircuitAmpBlock.Info.Params;

        string[] Hidden(string id) {
            var labels = parameters.Single(p => p.Id == id).TypeLabels!;
            return [..AmpDesign.All.Where((_, type) => labels[type] == string.Empty).Select(static a => a.Name)];
        }

        // Cathode Time Const: the bypass cap of a cathode-biased output stage.
        Assert.Equal(
            AmpDesign.All.Where(static a => !a.Power.CathodeBias || !a.Panel.HasTubes).Select(static a => a.Name),
            Hidden("katc")
        );
        // Master Trim: a master to trim.
        Assert.Equal(
            AmpDesign.All.Where(static a => a.Panel.Master is null).Select(static a => a.Name),
            Hidden("mvtrim")
        );
        // Preamp Sag: not a rack preamp on its own supply, nor a solid-state amp's regulated rails.
        Assert.Equal(
            [
                "JC-120", "TriAxis Rhythm", "TriAxis Lead 1", "TriAxis Lead 2", "Valvestate 8080", "JMP-1", "GK 800RB",
                "RG100ES"
            ],
            Hidden("presag")
        );
        // Preamp Tube: a 12AX7 to replace (the B-15N runs 6SL7s on the 5751's curves, the Scout 6EU7s).
        Assert.Equal(
            ["Portaflex B-15N", "JC-120", "Scout GA-17RVT", "Portaflex B-15N Jumped", "GK 800RB", "RG100ES"],
            Hidden("pretube")
        );
        // Bright Cap: a bright cap that is not across a master.
        Assert.Contains("Champ 5F1", Hidden("brightcap"));
        Assert.Contains("Hot Rod Deluxe", Hidden("brightcap"));
        Assert.Contains("Mark IV", Hidden("brightcap"));
        Assert.DoesNotContain("Plexi", Hidden("brightcap"));
        Assert.DoesNotContain("Blackface", Hidden("brightcap"));
        // The generic Overdrive: a gain stage to drive.
        Assert.Contains("JC-120", Hidden("overdrive"));
        // The B-15N's Baxandall has no middle control.
        Assert.Contains("Portaflex B-15N", Hidden("middle"));
    }

    [Fact]
    public void Preamp_tube_replaces_paralleled_twelve_ax7s_with_both_halves_of_the_chosen_tube() {
        var paralleled = TriodeModel.Ecc83.Paralleled(2);
        Assert.Equal(2, paralleled.SectionsOf(TriodeModel.Ecc83));
        Assert.Equal(1, TriodeModel.Ecc83.SectionsOf(TriodeModel.Ecc83));
        Assert.Equal(0, TriodeModel.Ecc81.SectionsOf(TriodeModel.Ecc83));
        Assert.Equal(0, TriodeModel.Ecc81.Paralleled(2).SectionsOf(TriodeModel.Ecc83));

        var swap = new PreampSettings(TriodeModel.TwelveAY7);
        Assert.Equal(TriodeModel.TwelveAY7, swap.TubeFor(TriodeModel.Ecc83));
        Assert.Equal(TriodeModel.TwelveAY7.Paralleled(2), swap.TubeFor(paralleled));
        Assert.Equal(TriodeModel.Ecc81, swap.TubeFor(TriodeModel.Ecc81));
        Assert.Equal(paralleled, PreampSettings.Stock.TubeFor(paralleled));
    }

    [Theory]
    [InlineData("Chieftain")] // both preamp triodes are paralleled 12AX7s
    [InlineData("18 W 1974X")] // the Normal channel's only preamp stage is one
    public void Preamp_tube_reaches_a_stage_of_paralleled_twelve_ax7s(string model) {
        var type = AmpDesign.All.ToList().FindIndex(a => a.Name == model);
        Assert.True(type >= 0, model);

        float[] Render(float tube) {
            using var block = TestBlocks.Create<CircuitAmpBlock>(
                (CircuitAmpBlock.Type, type),
                (CircuitAmpBlock.PreampTube, tube)
            );
            return BlockRender.Mono(block, CircuitAmpBlockTests.Chord(9_600, 0.1f), 64);
        }

        var stock = Render(0);
        Assert.Equal(stock, Render(1)); // a 12AX7 for a 12AX7
        for (var tube = 2; tube <= 5; tube++) {
            var changed = Render(tube);
            var difference = Analysis.NullResidualDb(stock, changed) - Analysis.ToDb(Analysis.Rms(stock));
            output.WriteLine($"{model}, tube {tube}: {difference:0.0} dB from stock");
            Assert.True(Analysis.AllFinite(changed));
            Assert.True(difference > -30, $"tube {tube}: {difference:0.0} dB");
        }
    }

    /// <summary>
    ///     Whether an expert parameter reaches a solid-state amp: not where its panel hides it (the tube parameters);
    ///     the supply's (Variac, B+ Time Const) only once the rails are loaded hard, which this test's chord does not
    ///     do.
    /// </summary>
    static bool SolidStateAudible(AmpDesign design, int index) =>
        design.Panel.Label(CircuitAmpBlock.Info.Params[index].Id, "-") is not null
        && index is not (CircuitAmpBlock.Variac or CircuitAmpBlock.SupplyTime);

    /// <summary>
    ///     Preamp Bias, Preamp and Power Tube Hardness and CF Harmonics are continuous knobs: swept a little every
    ///     block, the channel retunes and slews instead of resetting to its rest (which cut the signal to DC at every
    ///     step).
    /// </summary>
    [Theory]
    [InlineData("bias")]
    [InlineData("hardness")]
    [InlineData("follower")]
    [InlineData("power hardness")]
    public void Sweeping_a_continuous_operating_point_setting_does_not_cut_the_signal(string setting) {
        var controls = new AmpControls(0.6, 0.5, 0.5, 0.5, 0.6, false);
        var power = new PowerControls(0.5, 0.5, 0.5, 0.5, 0.5, 0.5);
        var channel = new AmpChannel(JCM2203.Design, InnerRate);
        channel.Configure(controls, power);
        channel.Reset();
        var (largestStep, peak, previous) = (0.0, 0.0, 0.0);
        var steps = 0;
        for (var n = 0; n < 48_000; n++) {
            if (n >= 24_000 && n % 64 == 0) {
                // Up to 10 % away over the second half, in blocks of 64 samples as the Circuit Amp applies it.
                var amount = 1 + 0.1 * (n - 24_000) / 24_000;
                var preamp = setting switch {
                    "bias" => PreampSettings.Stock with { CathodeResistor = amount },
                    "hardness" => PreampSettings.Stock with { Hardness = amount },
                    "follower" => PreampSettings.Stock with { FollowerBias = amount },
                    _ => PreampSettings.Stock
                };
                var settings = setting == "power hardness"
                    ? PowerSettings.Stock with { Hardness = amount }
                    : PowerSettings.Stock;
                channel.Apply(preamp, settings, ChannelSettings.Stock);
                channel.Configure(controls, power);
                steps++;
            }

            var y = channel.Tick(0.3 * Math.Sin(2 * Math.PI * 220 * n / InnerRate));
            if (n >= 12_000) {
                var step = Math.Abs(y - previous);
                if (n < 24_000) {
                    peak = Math.Max(peak, step); // the signal's own steepest sample-to-sample step
                } else {
                    largestStep = Math.Max(largestStep, step);
                }
            }

            previous = y;
        }

        output.WriteLine(
            $"{setting}: {steps} steps, steepest {largestStep:0.000} V against the signal's {peak:0.000} V"
        );
        Assert.True(largestStep < 1.5 * peak, $"{largestStep:0.000} V step against the signal's {peak:0.000} V");
    }

    /// <summary>
    ///     Whether a model has a bright cap for the Bright Cap setting to scale (the Mark IV's lead channel has none).
    /// </summary>
    static bool HasBrightCap(AmpDesign design) =>
        design.Preamp.Any(static s => s switch {
                TriodeStageDesign t => t.BrightCap > 0,
                ToneStackStageDesign t => t.BrightCap > 0,
                NetworkStageDesign n => n.Netlist.Elements.Any(static e => e.CapacitorRole == CapacitorRole.Bright),
                _ => false
            }
        );

    /// <summary>The preamp stages that play with every switch off: a channel switch's stock channel.</summary>
    static IEnumerable<AmpStageDesign> StockPath(AmpDesign design) =>
        design.Preamp.SelectMany(static s => s is ChannelSwitchStageDesign channels ? channels.Off : [s]);

    static bool HasTwelveAX7(AmpStageDesign stage) =>
        stage switch {
            TriodeStageDesign triode => triode.Tube.SectionsOf(TriodeModel.Ecc83) > 0,
            ToneStackStageDesign stack => stack.Tube.SectionsOf(TriodeModel.Ecc83) > 0,
            NetworkStageDesign network => network.Netlist.Elements.Any(static e =>
                e.Kind == ElementKind.Triode && e.Triode.SectionsOf(TriodeModel.Ecc83) > 0
            ),
            _ => false
        };

    static bool HasStack(AmpStageDesign stage) =>
        stage switch {
            ToneStackStageDesign => true,
            NetworkStageDesign network => network.Netlist.Elements.Any(static e => e.CapacitorRole
                == CapacitorRole.Stack
            ),
            _ => false
        };
}
