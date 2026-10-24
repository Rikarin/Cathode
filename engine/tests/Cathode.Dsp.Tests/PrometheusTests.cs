using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Lichtlaerm Prometheus models (an inferred circuit, docs/07): the real amp's panel, every switch and the Cut
///     knob audible, the gain and Cut knobs monotonic, and every switch combination stable under full drive.
/// </summary>
public class PrometheusTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    static int Lead => AmpDesign.All.ToList().IndexOf(Prometheus.Design);

    static int Clean => AmpDesign.All.ToList().IndexOf(PrometheusClean.Design);

    [Fact]
    public void Panels_name_the_real_amps_controls() {
        Assert.True(Lead >= 0 && Clean >= 0);
        var lead = Prometheus.Design.Panel;
        Assert.Equal(
            ("Modern", "Bite Up", "Bite Down", "Cut"),
            (lead.Switch1, lead.Switch2, lead.Switch3, lead.Control1)
        );
        Assert.Null(lead.Cut);
        Assert.Null(lead.Bright);
        var clean = PrometheusClean.Design.Panel;
        Assert.Equal(("Volume", "Brilliant"), (clean.Gain, clean.Bright));
        Assert.Null(clean.Switch1);
        Assert.Null(clean.Control1);
    }

    [Theory]
    [InlineData(CircuitAmpBlock.Switch1, 1f)]
    [InlineData(CircuitAmpBlock.Switch2, 1f)]
    [InlineData(CircuitAmpBlock.Switch3, 1f)]
    [InlineData(CircuitAmpBlock.Control1, 10f)]
    public void Lead_switches_and_cut_are_audible(int index, float value) {
        var stock = Render(Lead, 0.05f, (CircuitAmpBlock.Gain, 5f));
        var changed = Render(Lead, 0.05f, (CircuitAmpBlock.Gain, 5f), (index, value));
        var difference = Analysis.NullResidualDb(stock, changed) - Analysis.ToDb(Analysis.Rms(stock));
        output.WriteLine($"{CircuitAmpBlock.Info.Params[index].Id} = {value}: {difference:0.0} dB from stock");
        Assert.True(difference > -30, $"{difference:0.0} dB");
    }

    [Fact]
    public void Cut_removes_bass_and_gain_adds_drive() {
        // Below clipping, so the knobs' linear effect shows: Cut lowers 82 Hz against 1 kHz, gain raises the level.
        double Ratio(float cut) {
            var low = Analysis.Rms(
                Render(Lead, 0.0005f, 82.4, (CircuitAmpBlock.Gain, 1f), (CircuitAmpBlock.Control1, cut))
            );
            var high = Analysis.Rms(
                Render(Lead, 0.0005f, 1_000, (CircuitAmpBlock.Gain, 1f), (CircuitAmpBlock.Control1, cut))
            );
            return Analysis.ToDb(low / high);
        }

        var ratios = new[] { 0f, 5f, 10f }.Select(Ratio).ToArray();
        output.WriteLine(
            $"82 Hz against 1 kHz at Cut 0/5/10: {string.Join(" / ", ratios.Select(static r => $"{r:0.0}"))} dB"
        );
        Assert.True(ratios[0] > ratios[1] && ratios[1] > ratios[2] && ratios[0] - ratios[2] > 6);

        var levels = new[] { 0f, 3f, 6f }
            .Select(g => Analysis.ToDb(Analysis.Rms(Render(Lead, 0.0005f, 1_000, (CircuitAmpBlock.Gain, g)))))
            .ToArray();
        output.WriteLine($"1 kHz level at gain 0/3/6: {string.Join(" / ", levels.Select(static l => $"{l:0.0}"))} dB");
        Assert.True(levels[0] < levels[1] && levels[1] < levels[2]);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void Lead_is_stable_under_full_drive(bool modern, bool biteUp, bool biteDown) {
        using var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, Lead);
        foreach (var (index, value) in new[] {
                     (CircuitAmpBlock.Gain, 10f), (CircuitAmpBlock.Bass, 10f), (CircuitAmpBlock.Treble, 10f),
                     (CircuitAmpBlock.Presence, 10f), (CircuitAmpBlock.Depth, 10f),
                     (CircuitAmpBlock.Switch1, modern ? 1f : 0f), (CircuitAmpBlock.Switch2, biteUp ? 1f : 0f),
                     (CircuitAmpBlock.Switch3, biteDown ? 1f : 0f)
                 }) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        var square = Signals.Sine(110, Rate, 24_000).Select(static s => s >= 0 ? 1f : -1f).ToArray();
        var rendered = BlockRender.Mono(block, Signals.Concat(square, Signals.Noise(24_000, 4)), 64);
        Assert.True(Analysis.AllFinite(rendered));
        foreach (var newton in block.Channel.Stages.Where(static s => s.Newton is not null)
                     .Select(static s => s.Newton!)) {
            Assert.Equal(0, newton.NonFinite);
            Assert.True(newton.Unconverged <= newton.Samples / 10_000, $"{newton.Unconverged} unconverged samples");
        }

        Assert.True(Analysis.ToDb(Analysis.Peak(rendered)) < 6);
    }

    static float[] Render(int type, float peak, params (int Index, float Value)[] settings) =>
        Render(type, peak, 0, settings);

    /// <summary>A tone at <paramref name="frequency" /> (0: the strummed chord) through the amp.</summary>
    static float[] Render(int type, float peak, double frequency, params (int Index, float Value)[] settings) {
        using var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, type);
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        var input = frequency > 0
            ? Signals.Sine(frequency, Rate, 24_000, peak)
            : CircuitAmpBlockTests.Chord(14_400, peak);
        return BlockRender.Mono(block, input, 64).AsSpan(input.Length / 2).ToArray();
    }
}
