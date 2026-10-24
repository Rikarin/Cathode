using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
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

    [Fact]
    public void Panel_labels_name_and_hide_controls_per_model() {
        var parameters = CircuitAmpBlock.Info.Params;
        IReadOnlyList<string>? Labels(string id) => parameters.Single(p => p.Id == id).TypeLabels;
        string Label(AmpDesign design, string id) => Labels(id)![AmpDesign.All.ToList().IndexOf(design)];

        Assert.Equal(["Gain", "Gain", "Gain"], Labels("gain")!.Take(3));
        Assert.Equal(["", "", "Cut"], Labels("cut")!.Take(3));
        Assert.Equal(["Middle", "Middle", ""], Labels("middle")!.Take(3));
        Assert.All(Labels("switch1")!.Take(2), static l => Assert.Empty(l));
        Assert.All(Labels("eq1")!.Take(3), static l => Assert.Empty(l));
        Assert.Null(Labels("variac"));
        Assert.Equal(AmpDesign.All.Count, Labels("control1")!.Count);

        // Channel switches and the class-A family's own controls.
        Assert.Equal("Normal Ch", Label(AmpDesign.AC30, "switch1"));
        Assert.Equal("", Label(AmpDesign.AC30, "master"));
        Assert.Equal("Brilliance", Label(Amp.Models.AC15.Design, "bright"));
        Assert.Equal("", Label(Amp.Models.AC15.Design, "treble"));
        Assert.Equal("Volume", Label(Amp.Models.DC30.Design, "gain"));
        Assert.Equal("Tone (Ch 2)", Label(Amp.Models.DC30.Design, "control1"));
        Assert.Equal("Master", Label(Amp.Models.DC30.Design, "master"));

        // The Marshall family's.
        Assert.Equal("Preamp", Label(Amp.Models.JCM2203.Design, "gain"));
        Assert.Equal("Low Input", Label(Amp.Models.JCM2203.Design, "switch1"));
        Assert.Equal("Jumper", Label(Amp.Models.JTM45.Design, "switch1"));
        Assert.Equal("", Label(Amp.Models.JTM45.Design, "master"));
        Assert.Equal("Drive", Label(Amp.Models.LaneyGH100L.Design, "control1"));
    }

    [Fact]
    public void Parameters_after_level_are_appended_with_stock_defaults() {
        var parameters = CircuitAmpBlock.Info.Params;
        string[] first = ["type", "gain", "bass", "middle", "treble", "presence", "depth", "cut", "master", "sag", "bias",
            "feedback", "bright", "level"];
        Assert.Equal(first, parameters.Take(first.Length).Select(static p => p.Id));
        Assert.Equal(52, parameters.Count);
        Assert.Equal(CircuitAmpBlock.CathodeTime, parameters.Count - 1);
        foreach (var p in parameters.Skip(first.Length)) {
            output.WriteLine($"{p.Id,-10} {p.Name,-20} {p.Min,8} .. {p.Max,-8} default {p.Default} {p.Unit}");
        }
    }

    /// <summary>Each expert parameter, away from its default, on each model: what to set, and whether it must be audible.</summary>
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
                    // Known no-ops: the cathode time constant of a fixed-bias amp, a 6L6GC (the 5881's curves) or EL34
                    // or EL84 in place of itself, the presence and depth frequencies of an amp without feedback; the
                    // master trim of an amp without a master, the preamp tube and stack frequency of a (stock) channel
                    // without a 12AX7 or a tone stack (the AC15's EF86 channel), the speaker preset or rectifier the amp
                    // has anyway; and where the panel says the amp lacks the control, the bright cap (no bright switch:
                    // a bright cap a channel switch engages, or none at all) and the presence frequency (no presence).
                    var tube = new[] { null, "6L6GC", "EL34", "EL84", "6V6GT", "KT66", "KT88", "6550" }[
                        index == CircuitAmpBlock.PowerTube ? (int)value : 0];
                    // And the bright cap of an amp without one (the Laney's drive channel).
                    var audible = !(index == CircuitAmpBlock.CathodeTime && !design.Power.CathodeBias)
                        && !(index == CircuitAmpBlock.BrightCap && design == Amp.Models.LaneyGH100L.Design)
                        // And the supply's time constants on an amp still clean at gain 7 (the Princeton AA964's two
                        // stages, the second inside the feedback loop): its 6V6s draw too little extra current to sag.
                        && !(index is CircuitAmpBlock.PreampSag or CircuitAmpBlock.SupplyTime
                            && design == Amp.Models.PrincetonAA964.Design)
                        && !(index == CircuitAmpBlock.PowerTube && design.Power.Name.Contains(tube ?? "-", StringComparison.Ordinal))
                        && !(index is CircuitAmpBlock.PresenceFrequency or CircuitAmpBlock.DepthFrequency && !design.Power.HasFeedback)
                        && !(index == CircuitAmpBlock.MasterTrim && design.Panel.Master is null)
                        && !(index == CircuitAmpBlock.PreampTube && !StockPath(design).Any(HasTwelveAX7))
                        && !(index == CircuitAmpBlock.StackFrequency && !StockPath(design).Any(HasStack))
                        && !(index == CircuitAmpBlock.BrightCap && design.Panel.Bright is null)
                        && !(index == CircuitAmpBlock.PresenceFrequency && design.Panel.Presence is null)
                        && !(index == CircuitAmpBlock.SpeakerCurve && SpeakerDesign.Presets[(int)value - 1] == design.Power.Speaker)
                        && !(index == CircuitAmpBlock.Rectifier
                            && RectifierModel.All[(int)value - 1] == design.Power.PowerSupply.Rectifier)
                        && !(index == CircuitAmpBlock.BrightCap && !HasBrightCap(design));
                    data.Add(type, index, value, audible);
                }
            }

            return data;
        }
    }

    /// <summary>Whether a model has a bright cap for the Bright Cap setting to scale (the Mark IV's lead channel has none).</summary>
    static bool HasBrightCap(AmpDesign design) =>
        design.Preamp.Any(static s => s switch {
                TriodeStageDesign t => t.BrightCap > 0,
                ToneStackStageDesign t => t.BrightCap > 0,
                NetworkStageDesign n => n.Netlist.Elements.Any(static e => e.CapacitorRole == CapacitorRole.Bright),
                _ => false
            }
        );

    [Theory]
    [MemberData(nameof(ExpertSettings))]
    public void Expert_parameters_are_audible_and_bounded(int type, int index, float value, bool audible) {
        float[] Render(params (int Index, float Value)[] settings) {
            using var block = new CircuitAmpBlock();
            block.Params.SetTarget(CircuitAmpBlock.Type, type);
            block.Params.SetTarget(CircuitAmpBlock.Gain, 7);
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
            $"{AmpDesign.All[type].Name} {CircuitAmpBlock.Info.Params[index].Id} = {value}: {difference:0.0} dB from stock, " +
            $"peak {peak:0.0} dBFS"
        );
        Assert.True(Analysis.AllFinite(changed));
        Assert.True(peak < 6, $"peak {peak:0.0} dBFS");
        if (audible) {
            // A class-A amp (cathode bias, EL84) draws nearly the same current loud or quiet: its preamp sees little sag.
            var limit = index == CircuitAmpBlock.PreampSag && AmpDesign.All[type].Power.CathodeBias ? -80 : -60;
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

    [Fact]
    public void Input_select_takes_the_chosen_input() {
        float[] Render(int select, bool silentLeft) {
            using var block = new CircuitAmpBlock();
            block.Params.SetTarget(CircuitAmpBlock.InputSelect, select);
            block.Prepare(new(Rate, 64));
            var chord = CircuitAmpBlockTests.Chord(4_800, 0.25f);
            var silence = new float[chord.Length];
            return BlockRender.Stereo(block, silentLeft ? silence : chord, silentLeft ? chord : silence, _ => 64).Left;
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
        var power = new PowerAmp(AmpDesign.Plexi.Power, InnerRate);
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
        output.WriteLine($"{mode}: mean B+ {mean:0.0} V (DC supply {dc:0.0} V), ripple {swing:0.00} V p-p at {peak:0.0} Hz");
        Assert.InRange(peak, ripple - 3, ripple + 3);
        Assert.InRange(mean, 0.97 * dc, 1.03 * dc);
        Assert.InRange(swing, 0.1, 20);
    }

    [Fact]
    public void Variac_and_rectifiers_set_the_supply() {
        double Settled(PowerSettings settings, bool loud) {
            var power = new PowerAmp(AmpDesign.AC30.Power, InnerRate);
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
            var power = new PowerAmp(AmpDesign.Plexi.Power, InnerRate);
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
            $"THD at 42 Hz: drive 0 {Analysis.ToDb(low0):0.0} dB, drive 10 {Analysis.ToDb(low10):0.0} dB; at 1 kHz " +
            $"{Analysis.ToDb(high0):0.0} dB, {Analysis.ToDb(high10):0.0} dB"
        );
        Assert.True(Analysis.ToDb(low10) - Analysis.ToDb(low0) > 6, "the core saturates on low notes");
        Assert.True(Math.Abs(Analysis.ToDb(high10) - Analysis.ToDb(high0)) < 2, "and leaves the mids alone");
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

    /// <summary>The preamp stages that play with every switch off: a channel switch's stock channel.</summary>
    static IEnumerable<AmpStageDesign> StockPath(AmpDesign design) =>
        design.Preamp.SelectMany(static s => s is ChannelSwitchStageDesign channels ? channels.Off : [s]);

    static bool HasTwelveAX7(AmpStageDesign stage) => stage switch {
        TriodeStageDesign triode => triode.Tube == Wdf.TriodeModel.Ecc83,
        ToneStackStageDesign stack => stack.Tube == Wdf.TriodeModel.Ecc83,
        NetworkStageDesign network => network.Netlist.Elements.Any(static e =>
            e.Kind == ElementKind.Triode && e.Triode == Wdf.TriodeModel.Ecc83
        ),
        _ => false
    };

    static bool HasStack(AmpStageDesign stage) => stage switch {
        ToneStackStageDesign => true,
        NetworkStageDesign network => network.Netlist.Elements.Any(static e => e.CapacitorRole == CapacitorRole.Stack),
        _ => false
    };
}
