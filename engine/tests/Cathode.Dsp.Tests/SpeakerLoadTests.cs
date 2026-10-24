using System.Numerics;
using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Cabinets;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Circuit Amp's speaker load (issue #1): its names, the Cab block's speakers as electrical loads (their
///     Thiele-Small data against the textbook impedance), the cabinets' wiring and boxes, and Follow Cab, which hands
///     the amp the load of the speaker the Cab synthesises.
/// </summary>
public class SpeakerLoadTests(ITestOutputHelper output) {
    public static TheoryData<int> Speakers() => [..Enumerable.Range(0, CabCatalogue.Speakers.Count)];

    [Fact]
    public void The_speaker_parameters_are_named_as_the_load_on_the_power_amp() {
        (int Index, string Id, string Name)[] expected = [
            (CircuitAmpBlock.SpeakerCurve, "speaker", "Speaker load"),
            (CircuitAmpBlock.SpeakerResonance, "spkres", "Load resonance"),
            (CircuitAmpBlock.SpeakerQ, "spkq", "Load Q"),
            (CircuitAmpBlock.SpeakerPeak, "spkpeak", "Load peak"),
            (CircuitAmpBlock.SpeakerHigh, "spkhf", "Load HF"),
            (CircuitAmpBlock.SpeakerImpedance, "spkimp", "Load impedance")
        ];
        foreach (var (index, id, name) in expected) {
            var param = CircuitAmpBlock.Info.Params[index];
            Assert.Equal((id, name), (param.Id, param.Name));
        }

        var options = CircuitAmpBlock.Info.Params[CircuitAmpBlock.SpeakerCurve].Options!;
        Assert.Equal("Stock", options[0]);
        Assert.Equal("Follow Cab", options[CircuitAmpBlock.SpeakerAuto]);
    }

    /// <summary>
    ///     Each speaker's impedance is the textbook Thiele-Small one, <c>Re + Res / (1 + j Qms (f/Fs − Fs/f))</c> with
    ///     <c>Res = Re Qms / Qes</c>, plus the voice coil's semi-inductance: the peak sits at Fs, reaches Re + Res, and
    ///     the top rises with Le.
    /// </summary>
    [Theory]
    [MemberData(nameof(Speakers))]
    public void A_speakers_impedance_peaks_at_its_resonance_and_rises_with_its_voice_coil(int index) {
        var speaker = CabCatalogue.Speakers[index];
        var e = speaker.Electrical;
        Assert.True(e.Qes > 0 && e.Qms > e.Qes, $"{speaker.Name}: Qms {e.Qms}, Qes {e.Qes}");
        Assert.Equal(e.Qms * e.Qes / (e.Qms + e.Qes), speaker.Qts, 1e-12);

        // The textbook motional impedance, independently.
        Complex Textbook(double hz) =>
            e.Re + e.Re * e.Qms / e.Qes / new Complex(1, e.Qms * (hz / speaker.Fs - speaker.Fs / hz));

        var (peakHz, peak) = Peak(hz => speaker.Impedance(hz).Magnitude, 20, 500);
        var expected = e.Re * (1 + e.Qms / e.Qes);
        output.WriteLine(
            $"{speaker.Name}: Re {e.Re:0.00} Ω, Fs {speaker.Fs:0} Hz, Qms {e.Qms:0.00}, Qes {e.Qes:0.000}; peak "
            + $"{peak:0.0} Ω at {peakHz:0.0} Hz (textbook {expected:0.0} Ω); |Z| 1 kHz "
            + $"{speaker.Impedance(1_000).Magnitude:0.0} Ω, 5 kHz {speaker.Impedance(5_000).Magnitude:0.0} Ω"
        );
        Assert.InRange(peakHz, speaker.Fs * 0.98, speaker.Fs * 1.02);
        // The voice coil adds a fraction of an ohm at Fs.
        Assert.InRange(peak, expected, expected * 1.02);
        for (var hz = 20.0; hz < 400; hz *= 1.2) {
            Assert.True(
                (speaker.Impedance(hz) - Textbook(hz) - e.Coil(hz)).Magnitude < 1e-9,
                $"{speaker.Name} at {hz:0} Hz"
            );
        }

        // Le as the datasheet defines it: the reactance at 1 kHz is 2π f Le.
        Assert.Equal(2 * Math.PI * 1_000 * e.Le, e.Coil(1_000).Imaginary, 1e-9);
        // The semi-inductance rises as f^n: 5^0.7 ≈ 3.1 times from 1 to 5 kHz, well above Re at the top.
        Assert.Equal(Math.Pow(5, e.SemiExponent), e.Coil(5_000).Magnitude / e.Coil(1_000).Magnitude, 1e-9);
        Assert.True(speaker.Impedance(5_000).Magnitude > speaker.Impedance(1_000).Magnitude * 1.5);
        Assert.True(speaker.Impedance(1_000).Magnitude > e.Re);
    }

    /// <summary>
    ///     The power amp's load (the lumped <see cref="SpeakerDesign" />) for each speaker in an open back matches the
    ///     speaker's own impedance, normalised to 8 Ω: through the resonance and at the lossy coil's fit points (1 and
    ///     5 kHz) within 2 %. Elsewhere a first-order coil cannot follow the semi-inductance's f^0.7 exactly (it rises
    ///     faster around 2-3 kHz and has less of its resistive part in the low mids): from 20 Hz to 5 kHz within 20 %.
    /// </summary>
    [Theory]
    [MemberData(nameof(Speakers))]
    public void The_amps_load_is_the_speakers_own_impedance(int index) {
        var speaker = CabCatalogue.Speakers[index];
        var open = Enclosure("1x12 Open Back");
        var load = AutoSpeaker.Design(open, speaker);
        var scale = SpeakerDesign.Nominal / speaker.Electrical.Nominal;

        void Check(double hz, double tolerance) {
            var want = open.Impedance(speaker, hz) * scale / open.WiringFactor;
            var got = load.Impedance(hz);
            Assert.True(
                Math.Abs(got.Magnitude / want.Magnitude - 1) < tolerance,
                $"{speaker.Name} at {hz:0} Hz: load {got.Magnitude:0.00} Ω, speaker {want.Magnitude:0.00} Ω"
            );
        }

        output.WriteLine(
            $"{speaker.Name}: Re {load.Resistance:0.00} Ω, L {load.Inductance * 1e3:0.000} mH, eddy "
            + $"{load.EddyResistance:0.0} Ω, {load.Resonance:0.0} Hz, peak {load.PeakImpedance:0.0} Ω, Q {load.Q:0.00}"
        );
        foreach (var hz in (double[]) [speaker.Fs, 1_000, 5_000]) {
            Check(hz, 0.02);
        }

        for (var hz = 20.0; hz <= 5_000; hz *= 1.1) {
            Check(hz, 0.2);
        }

        Assert.Equal(speaker.Fs, load.Resonance, 1e-9);
        Assert.Equal(speaker.Electrical.Qms, load.Q, 1e-9);
    }

    /// <summary>
    ///     A closed box raises the resonance to Fs √(1 + Vas / Vb) per speaker, and Qms with it, but leaves the peak's
    ///     height (Re + Res): Small's closed-box relations.
    /// </summary>
    [Fact]
    public void A_closed_box_raises_the_resonance_but_not_the_peak() {
        var v30 = CabCatalogue.Speakers[3];
        var closed = Enclosure("4x12 Closed Back");
        var open = Enclosure("1x12 Open Back");
        var stiffening = Math.Sqrt(1 + v30.Vas / closed.LitresPerSpeaker);
        var (closedHz, closedPeak) = Peak(hz => closed.Impedance(v30, hz).Magnitude / closed.WiringFactor, 20, 500);
        var (openHz, openPeak) = Peak(hz => open.Impedance(v30, hz).Magnitude, 20, 500);
        output.WriteLine($"V30 open {openHz:0.0} Hz {openPeak:0.0} Ω; 4x12 closed {closedHz:0.0} Hz {closedPeak:0.0} Ω");
        Assert.InRange(closedHz, v30.Fs * stiffening * 0.98, v30.Fs * stiffening * 1.02);
        Assert.InRange(openHz, v30.Fs * 0.98, v30.Fs * 1.02);
        Assert.Equal(openPeak, closedPeak, openPeak * 0.01);
        var load = AutoSpeaker.Design(closed, v30);
        Assert.Equal(v30.Fs * stiffening, load.Resonance, 1e-9);
        Assert.Equal(v30.Electrical.Qms * stiffening, load.Q, 1e-9);
    }

    /// <summary>
    ///     A cabinet's load is one speaker's times series over parallel: a 4x12 series-parallel at the speakers'
    ///     nominal, a 2x12 combo at half, a 4x10 at a quarter. Normalised to the tap, the 4x12 and a 1x12 differ only
    ///     by their boxes.
    /// </summary>
    [Fact]
    public void A_4x12_and_a_1x12_combine_their_speakers_by_wiring_and_box() {
        var v30 = CabCatalogue.Speakers[3];
        var single = Enclosure("1x12 Closed Back");
        var quad = Enclosure("4x12 Closed Back");
        var twin = Enclosure("2x12 Open Back");
        var four = Enclosure("4x10 Open Back");
        Assert.Equal((2, 2, 1.0), (quad.Series, quad.Parallel, quad.WiringFactor));
        Assert.Equal(8, quad.Nominal(v30), 1e-12);
        Assert.Equal(8, single.Nominal(v30), 1e-12);
        Assert.Equal(4, twin.Nominal(v30), 1e-12);
        Assert.Equal(2, four.Nominal(v30), 1e-12);

        // The same speaker in the same air would be the same load whatever the wiring: only scaled.
        var openOne = Enclosure("1x12 Open Back");
        foreach (var hz in (double[]) [50, 75, 200, 1_000, 5_000]) {
            Assert.Equal(0.5, (twin.Impedance(v30, hz) / openOne.Impedance(v30, hz)).Real, 1e-12);
            Assert.Equal(0.25, (four.Impedance(v30, hz) / openOne.Impedance(v30, hz)).Real, 1e-12);
        }

        // Each speaker in a 4x12 has 36 l of air, in the 1x12 45 l: the 4x12 resonates higher.
        var quadLoad = AutoSpeaker.Design(quad, v30);
        var singleLoad = AutoSpeaker.Design(single, v30);
        output.WriteLine($"V30 4x12 {quadLoad.Resonance:0.0} Hz, 1x12 closed {singleLoad.Resonance:0.0} Hz");
        Assert.True(quadLoad.Resonance > singleLoad.Resonance * 1.03);
        Assert.Equal(quadLoad.PeakImpedance, singleLoad.PeakImpedance, 1e-9);
        Assert.Equal(quadLoad.Resistance, singleLoad.Resistance, 1e-9);
        // Open backs are free air whatever the count.
        Assert.Equal(AutoSpeaker.Design(openOne, v30), AutoSpeaker.Design(twin, v30));
    }

    [Fact]
    public void Follow_cab_resolves_the_resonance_helpers_base_frequency() {
        var closed = Index(Enclosure("4x12 Closed Back"));
        var stock = CircuitAmpBlock.SpeakerResonanceHz(0, 0);
        Assert.Equal(stock, CircuitAmpBlock.SpeakerResonanceHz(0, CircuitAmpBlock.SpeakerAuto));
        Assert.Equal(stock, CircuitAmpBlock.SpeakerResonanceHz(0, CircuitAmpBlock.SpeakerAuto, (-1, -1)));
        Assert.Equal(
            AutoSpeaker.For(closed, 3)!.Resonance,
            CircuitAmpBlock.SpeakerResonanceHz(0, CircuitAmpBlock.SpeakerAuto, (closed, 3))
        );
        Assert.Equal(
            SpeakerDesign.Closed8x10.Resonance,
            CircuitAmpBlock.SpeakerResonanceHz(0, 5, (closed, 3))
        );
    }

    /// <summary>
    ///     Following a Cab, the amp's load is that Cab's speaker's: changing the speaker in the Cab changes what the
    ///     amp sounds like, without allocating on the audio thread.
    /// </summary>
    [Fact]
    public void Follow_cab_tracks_a_change_of_the_cabs_speaker_without_allocating() {
        var type = AmpDesign.All.ToList().IndexOf(Blackface.Design);
        var closed = Index(Enclosure("4x12 Closed Back"));
        var input = CircuitAmpBlockTests.Chord(9_600, 0.3f);
        using var cab = new CabBlock();
        cab.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Source), CabBlock.SourceDynaCab);
        cab.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Cab), closed);
        cab.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Mute), 0);

        float[] Render(int speaker) {
            cab.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Speaker), speaker);
            // As the Cab block running on the audio thread would.
            cab.Params.SnapToTargets();
            using var amp = TestBlocks.Create<CircuitAmpBlock>(
                (CircuitAmpBlock.Type, type),
                (CircuitAmpBlock.SpeakerCurve, CircuitAmpBlock.SpeakerAuto)
            );
            amp.FollowCab(cab);
            return BlockRender.Stereo(amp, input, input, static _ => 64).Left;
        }

        var v30 = Render(3);
        var c12n = Render(7);
        var residual = Analysis.NullResidualDb(v30, c12n) - Analysis.ToDb(Analysis.Rms(v30));
        output.WriteLine($"V30 against C12N in the 4x12: residual {residual:0.0} dB");
        Assert.True(residual > -40, $"{residual:0.0} dB");

        // The change itself, live: the amp picks the new load up on the audio thread with no allocation.
        using var live = TestBlocks.Create<CircuitAmpBlock>(
            (CircuitAmpBlock.Type, type),
            (CircuitAmpBlock.SpeakerCurve, CircuitAmpBlock.SpeakerAuto)
        );
        live.FollowCab(cab);
        var left = Signals.Noise(64, 1);
        var right = Signals.Noise(64, 2);
        var context = new BlockContext(48_000, 120, 0);
        var speaker = 0;
        var allocated = AllocationProbe.Measure(() => {
                for (var i = 0; i < 400; i++) {
                    if (i % 40 == 0) {
                        speaker = (speaker + 1) % CabCatalogue.Speakers.Count;
                        cab.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Speaker), speaker);
                        cab.Params.SnapToTargets();
                    }

                    live.Process(new(left, right), context);
                }
            }
        );
        Assert.Equal(0, allocated);
    }

    static EnclosureModel Enclosure(string name) => CabCatalogue.Enclosures.Single(e => e.Name == name);

    static int Index(EnclosureModel enclosure) => CabCatalogue.Enclosures.ToList().IndexOf(enclosure);

    /// <summary>The largest of <paramref name="magnitude" /> on a fine log grid, with its frequency.</summary>
    static (double Hz, double Value) Peak(Func<double, double> magnitude, double from, double to) {
        var best = (Hz: from, Value: magnitude(from));
        for (var hz = from; hz <= to; hz *= 1.0005) {
            var value = magnitude(hz);
            if (value > best.Value) {
                best = (hz, value);
            }
        }

        return best;
    }
}
