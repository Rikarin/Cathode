using Cathode.Dsp.Blocks;
using Cathode.Dsp.Cabinets;
using Cathode.Dsp.IO;
using Cathode.Testing;
using System.IO.Compression;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Cab block's DynaCab source: physical sanity of the model, level normalisation, the IR swap and user grids.
/// </summary>
public sealed class DynaCabTests : IDisposable {
    const double Rate = 48_000;
    const double Inch = 0.0254;

    static readonly ProcessSpec Spec = new(Rate, 64);
    static readonly SpeakerModel V30 = CabCatalogue.Speakers[3];
    static readonly EnclosureModel Closed1x12 = CabCatalogue.Enclosures[1];
    static readonly EnclosureModel Open1x12 = CabCatalogue.Enclosures[0];
    static readonly EnclosureModel Closed4x12 = CabCatalogue.Enclosures[5];

    readonly string folder = Directory.CreateTempSubdirectory("cathode-dynacab-").FullName;

    public static TheoryData<int> DirectionalMics => [0, 1, 2, 5, 7, 8];

    public void Dispose() => Directory.Delete(folder, true);

    [Theory]
    [MemberData(nameof(DirectionalMics))]
    public void Proximity_boost_grows_as_a_directional_mic_closes_in(int mic) {
        double[] distances = [12, 8, 4, 2, 1, 0];
        var boosts = distances.Select(d => BassBoost(
                    Synthesize(new(V30, Closed1x12, CabCatalogue.Mics[mic], 0.25, d * Inch, 0, true, 0))
                )
            )
            .ToArray();

        for (var i = 1; i < boosts.Length; i++) {
            Assert.True(
                boosts[i] > boosts[i - 1],
                $"{CabCatalogue.Mics[mic].Name}: boost {boosts[i]:0.0} dB at {distances[i]} in, {boosts[i - 1]:0.0} dB at {distances[i - 1]} in"
            );
        }

        // Cardioids gain about 6-9 dB from a foot to the grille, figure-8 ribbons more.
        var minimum = CabCatalogue.Mics[mic].Omni == 0 ? 9 : 4;
        Assert.InRange(boosts[^1] - boosts[0], minimum, 20);
    }

    [Fact]
    public void An_omni_has_no_proximity_effect() {
        var omni = CabCatalogue.Mics[10];
        var near = BassBoost(Synthesize(new(V30, Closed1x12, omni, 0.25, 0, 0, true, 0)));
        var far = BassBoost(Synthesize(new(V30, Closed1x12, omni, 0.25, 12 * Inch, 0, true, 0)));

        Assert.InRange(near - far, -1, 1);
    }

    [Fact]
    public void Proximity_off_removes_the_boost() {
        var on = BassBoost(Synthesize(new(V30, Closed1x12, CabCatalogue.Mics[5], 0.25, 0, 0, true, 0)));
        var off = BassBoost(Synthesize(new(V30, Closed1x12, CabCatalogue.Mics[5], 0.25, 0, 0, false, 0)));

        Assert.True(on - off > 9, $"ribbon at the grille: {on - off:0.0} dB from proximity");
    }

    [Fact]
    public void Proximity_amount_scales_the_boost_and_its_frequency_moves_it_up() {
        static double Boost(double amount, double frequency) =>
            BassBoost(
                Synthesize(new(V30, Closed1x12, CabCatalogue.Mics[0], 0.25, Inch, 0, amount > 0, 0, amount, frequency))
            );

        var none = Boost(0, 1);
        var stock = Boost(1, 1);
        var twice = Boost(2, 1);
        Assert.True(stock - none > 3, $"stock proximity adds {stock - none:0.0} dB");
        Assert.True(twice - stock > 2, $"twice the amount adds {twice - stock:0.0} dB more");
        Assert.Equal(
            stock,
            BassBoost(Synthesize(new(V30, Closed1x12, CabCatalogue.Mics[0], 0.25, Inch, 0, true, 0))),
            1e-9
        );

        // A higher corner lifts the low mids too, not only the lows.
        static double LowMids(double frequency) {
            var ir = Synthesize(new(V30, Closed1x12, CabCatalogue.Mics[0], 0.25, Inch, 0, true, 0, 1, frequency));
            return BandDb(ir, 300, 600) - BandDb(ir, 2_000, 4_000);
        }

        Assert.True(LowMids(4) > LowMids(1) + 1.5, $"x4: {LowMids(4):0.0} dB, stock {LowMids(1):0.0} dB");
    }

    [Fact]
    public void Room_size_moves_the_reflections_later() {
        // What the room adds, as an IR: its energy arrives later in a bigger room.
        double Centroid(double size) {
            var dry = Synthesize(new(V30, Closed1x12, CabCatalogue.Mics[10], 0.25, Inch, 0, true, 0));
            var wet = Synthesize(
                new(
                    V30,
                    Closed1x12,
                    CabCatalogue.Mics[10],
                    0.25,
                    Inch,
                    0,
                    true,
                    1,
                    RoomShape: new CabRoom(size, Floor: 0)
                )
            );
            double moment = 0, energy = 0;
            for (var i = 0; i < dry.Length; i++) {
                var d = wet[i] - dry[i] * (Rms(wet) / Rms(dry));
                moment += i * d * d;
                energy += d * d;
            }

            return moment / energy / Rate;
        }

        var small = Centroid(1);
        var big = Centroid(2);
        Assert.True(
            big > small + 0.002,
            $"room energy centre {small * 1_000:0.0} ms at 1x, {big * 1_000:0.0} ms at 2x"
        );

        static double Rms(float[] x) => Math.Sqrt(x.Sum(static v => (double)v * v));
    }

    [Fact]
    public void Room_damping_and_diffusion_tame_the_room_comb() {
        // The room's comb, as the spread of the levels against the anechoic IR over a band (finely sampled).
        static double Ripple(CabRoom room, double low, double high) {
            var dry = Synthesize(new(V30, Closed1x12, CabCatalogue.Mics[10], 0.25, 6 * Inch, 0, true, 0));
            var wet = Synthesize(
                new(V30, Closed1x12, CabCatalogue.Mics[10], 0.25, 6 * Inch, 0, true, 1, RoomShape: room)
            );
            var levels = new List<double>();
            for (var hz = low; hz <= high; hz *= 1.01) {
                levels.Add(20 * Math.Log10(Analysis.MagnitudeAt(wet, hz, Rate) / Analysis.MagnitudeAt(dry, hz, Rate)));
            }

            var mean = levels.Average();
            return Math.Sqrt(levels.Average(l => (l - mean) * (l - mean)));
        }

        var stock = Ripple(CabRoom.Default, 2_000, 8_000);
        var damped = Ripple(CabRoom.Default with { HighDamping = 4 }, 2_000, 8_000);
        Assert.True(damped < 0.8 * stock, $"damping: {damped:0.00} dB against {stock:0.00} dB");

        // The walls alone (no floor), where their comb is strongest.
        var walls = CabRoom.Default with { Floor = 0 };
        var specular = Ripple(walls, 1_000, 4_000);
        var diffuse = Ripple(walls with { Diffusion = 0.2e-3 }, 1_000, 4_000);
        Assert.True(diffuse < 0.5 * specular, $"diffusion: {diffuse:0.00} dB against {specular:0.00} dB");
    }

    [Fact]
    public void Moving_from_the_cap_to_the_edge_darkens() {
        double[] positions = [0, 0.25, 0.5, 0.75, 1];
        var treble = positions.Select(static p => Brightness(
                    Synthesize(new(V30, Closed4x12, CabCatalogue.Mics[0], p, Inch, 0, true, 0))
                )
            )
            .ToArray();

        for (var i = 1; i < treble.Length; i++) {
            Assert.True(
                treble[i] < treble[i - 1] + 0.5,
                $"treble {treble[i]:0.0} dB at {positions[i]}, {treble[i - 1]:0.0} dB at {positions[i - 1]}"
            );
        }

        Assert.True(treble[0] - treble[^1] > 6, $"edge only {treble[0] - treble[^1]:0.0} dB darker than the cap");
    }

    [Fact]
    public void Moving_away_softens_the_position_effect() {
        static double Spread(double distance) =>
            Brightness(Synthesize(new(V30, Closed1x12, CabCatalogue.Mics[0], 0, distance, 0, true, 0)))
            - Brightness(Synthesize(new(V30, Closed1x12, CabCatalogue.Mics[0], 1, distance, 0, true, 0)));

        Assert.True(
            Spread(0) > 2 * Spread(12 * Inch),
            $"cap-edge {Spread(0):0.0} dB at the grille, {Spread(12 * Inch):0.0} dB at a foot"
        );
    }

    [Fact]
    public void Ribbons_are_darker_than_dynamics() {
        var dynamics = new[] { 0, 1, 2, 3, 4 }.Select(Brightness).ToArray();
        var ribbons = new[] { 5, 6 }.Select(Brightness).ToArray();

        Assert.True(
            ribbons.Max() < dynamics.Min() - 3,
            $"ribbons up to {ribbons.Max():0.0} dB, dynamics from {dynamics.Min():0.0} dB"
        );

        static double Brightness(int mic) =>
            DynaCabTests.Brightness(Synthesize(new(V30, Closed4x12, CabCatalogue.Mics[mic], 0.25, Inch, 0, true, 0)));
    }

    [Fact]
    public void A_closed_4x12_has_a_tighter_low_end_than_an_open_1x12() {
        // Below its (higher) box resonance the closed cab falls away at 12 dB/octave; the open-back combo rings at the
        // speaker's free-air resonance and reaches lower, looser.
        static double Tightness(EnclosureModel cab) {
            var ir = Synthesize(new(V30, cab, CabCatalogue.Mics[0], 0.25, Inch, 0, true, 0));
            return BandDb(ir, 100, 200) - BandDb(ir, 40, 70);
        }

        Assert.True(
            Tightness(Closed4x12) > Tightness(Open1x12) + 3,
            $"closed {Tightness(Closed4x12):0.0} dB, open {Tightness(Open1x12):0.0} dB"
        );
        Assert.InRange(
            new CabAcoustics(new(V30, Closed4x12, CabCatalogue.Mics[0], 0, 0, 0, true, 0)).SystemResonance,
            90,
            130
        );
    }

    [Fact]
    public void Open_backs_lose_a_little_more_bass_with_distance_than_closed_cabs() {
        // With an omni (no proximity effect) the only difference is the open back's inverted back wave, whose share
        // grows as the mic leaves the cone's near field. It is a modest effect a foot out; directional mics lose much
        // more bass with distance from proximity, in both cabs alike.
        static double Loss(EnclosureModel cab) {
            var omni = CabCatalogue.Mics[10];
            return BandDb(Synthesize(new(V30, cab, omni, 0.25, 0, 0, true, 0)), 60, 150)
                - BandDb(Synthesize(new(V30, cab, omni, 0.25, 12 * Inch, 0, true, 0)), 60, 150)
                - (BandDb(Synthesize(new(V30, cab, omni, 0.25, 0, 0, true, 0)), 800, 1_600)
                    - BandDb(Synthesize(new(V30, cab, omni, 0.25, 12 * Inch, 0, true, 0)), 800, 1_600));
        }

        Assert.True(
            Loss(Open1x12) > Loss(Closed1x12) + 0.75,
            $"open {Loss(Open1x12):0.0} dB, closed {Loss(Closed1x12):0.0} dB"
        );
    }

    [Fact]
    public void Every_speaker_mic_and_cab_plays_at_about_the_same_loudness() {
        // Pink noise through each choice (one at a time from the default), measured with BS.1770's K filter.
        var input = PinkNoise(48_000, 3);
        var levels = new List<(string Name, double Db)>();
        for (var i = 0; i < CabCatalogue.Speakers.Count; i++) {
            levels.Add((CabCatalogue.Speakers[i].Name, Loudness(CabBlock.SlotParam(0, CabBlock.Speaker), i)));
        }

        for (var i = 0; i < CabCatalogue.Mics.Count; i++) {
            levels.Add((CabCatalogue.Mics[i].Name, Loudness(CabBlock.SlotParam(0, CabBlock.Mic), i)));
        }

        for (var i = 0; i < CabCatalogue.Enclosures.Count; i++) {
            levels.Add((CabCatalogue.Enclosures[i].Name, Loudness(CabBlock.SlotParam(0, CabBlock.Cab), i)));
        }

        levels.Add(("edge", Loudness(CabBlock.SlotParam(0, CabBlock.Position), 100)));
        levels.Add(("12 in", Loudness(CabBlock.SlotParam(0, CabBlock.Distance), 12)));
        levels.Add(("room", Loudness(CabBlock.Room, 100)));
        var spread = levels.Max(static l => l.Db) - levels.Min(static l => l.Db);
        Assert.True(spread < 2, string.Join(", ", levels.Select(static l => $"{l.Name} {l.Db:0.0}")));

        double Loudness(int param, float value) {
            using var block = new CabBlock();
            block.Params.SetTarget(param, value);
            block.Prepare(Spec);
            var output = BlockRender.Mono(block, input, 64);
            return KWeightedDb(output);
        }
    }

    [Fact]
    public void Default_is_mono_slot_one_at_unity_loudness() {
        using var block = Prepared();
        var (left, right) = block.ImpulseResponse();

        Assert.Equal(left, right);
        Assert.True(left.Length >= CabBlock.IrCapacity(Rate) + (int)(CabBlock.MaxDelayMs / 1_000 * Rate));
        Assert.All(left[(2 * block.Taps)..], static v => Assert.Equal(0f, v)); // a DynaCab is 2,048 taps
        Assert.InRange(Analysis.PeakIndex(left), 0, 8); // aligned: no distance delay with Auto-Align on
        Assert.Equal(IrFeed.Left, block.Feed); // Stereo input, slot 1 alone: it hears the left input
    }

    [Fact]
    public void Pan_invert_delay_and_mute_are_mixed_into_the_ir() {
        using var block = Prepared();
        var (reference, _) = block.ImpulseResponse();

        block.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Pan), -100f);
        var (left, right) = block.ImpulseResponse();
        Assert.Equal(reference, left);
        Assert.All(right, static v => Assert.Equal(0f, v));

        block.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Pan), 0f);
        block.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.SlotLevel), -6.0206f);
        block.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Invert), 1f);
        (left, _) = block.ImpulseResponse();
        for (var i = 0; i < reference.Length; i++) {
            Assert.Equal(-reference[i], left[i], 1e-6f); // alone, a slot's level is relative: it does nothing
        }

        block.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Invert), 0f);
        block.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.SlotLevel), 0f);
        block.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Delay), 1f);
        (left, _) = block.ImpulseResponse();
        Assert.Equal(Analysis.PeakIndex(reference) + 48, Analysis.PeakIndex(left));

        block.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Mute), 1f);
        (left, _) = block.ImpulseResponse();
        Assert.All(left, static v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Solo_plays_only_the_soloed_slots() {
        using var block = Prepared();
        var (one, _) = block.ImpulseResponse();
        block.Params.SetTarget(CabBlock.SlotParam(1, CabBlock.Mute), 0f);
        var (both, _) = block.ImpulseResponse();
        block.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Solo), 1f);
        var (soloed, _) = block.ImpulseResponse();

        Assert.NotEqual(one, both);
        Assert.Equal(one, soloed);
    }

    [Fact]
    public void Auto_align_off_delays_a_mic_by_its_distance() {
        using var block = Prepared();
        block.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Distance), 0f);
        block.Params.SetTarget(CabBlock.SlotParam(1, CabBlock.Mic), 0f);
        block.Params.SetTarget(CabBlock.SlotParam(1, CabBlock.Position), 25f);
        block.Params.SetTarget(CabBlock.SlotParam(1, CabBlock.Distance), 12f);
        block.Params.SetTarget(CabBlock.SlotParam(1, CabBlock.Mute), 0f);
        block.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Mute), 1f);
        var (alone, _) = block.ImpulseResponse(); // slot 2 alone: nothing earlier, so not delayed
        block.Params.SetTarget(CabBlock.SlotParam(1, CabBlock.AutoAlign), 0f);
        block.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Mute), 0f);
        var (both, _) = block.ImpulseResponse();
        block.Params.SetTarget(CabBlock.SlotParam(1, CabBlock.Mute), 1f);
        var (first, _) = block.ImpulseResponse();

        // The mix is linear, so both minus slot 1 alone is slot 2 as delayed behind slot 1 at the grille: 12 in more
        // path, 12 * 25.4 mm / 343 m/s.
        var second = both.Select((v, i) => 2 * v - first[i]).ToArray(); // two slots at 0 dB: each at half
        var expected = 12 * Inch / CabAcoustics.SpeedOfSound * Rate;
        Assert.Equal(expected, Analysis.PeakIndex(second) - Analysis.PeakIndex(alone), 1.5);
    }

    [Fact]
    public void A_parameter_change_is_crossfaded_in_without_a_click_or_allocation() {
        var input = Signals.Sine(220, Rate, 24_000, 0.5f);
        using var before = Prepared();
        using var after = Prepared(CabBlock.SlotParam(0, CabBlock.Mic), 5); // R-121
        var a = BlockRender.Mono(before, input, 64);
        var b = BlockRender.Mono(after, input, 64);

        const int SwapAt = 12_032;
        var context = new BlockContext(Rate, 120, 0);

        // One run: a fresh block, the change on the control thread at SwapAt, every callback's allocation counted.
        (float[] Output, long Allocated) Run() {
            using var switched = Prepared();
            var output = (float[])input.Clone();
            long allocated = 0;
            for (var offset = 0; offset < output.Length; offset += 64) {
                if (offset == SwapAt) {
                    switched.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Mic), 5);
                    switched.Refresh(); // control thread
                }

                var io = new StereoBuffer(output.AsSpan(offset, 64), new float[64]);
                var bytes = GC.GetAllocatedBytesForCurrentThread();
                switched.Process(io, context);
                allocated += GC.GetAllocatedBytesForCurrentThread() - bytes;
            }

            Assert.Equal(1, switched.IrSwaps);
            Assert.False(switched.IsFading);
            return (output, allocated);
        }

        // As AllocationProbe does, the least of up to three runs: the runtime now and then does one-off work on the
        // measuring thread (a tier-1 rejit, on-stack replacement, a cold type load), more often on a busy machine,
        // where the background compiler runs late; an allocation in the swap or the fade shows up in every run.
        var (output, allocated) = Run();
        for (var retry = 0; retry < 2 && allocated > 0; retry++) {
            allocated = Math.Min(allocated, Run().Allocated);
        }

        // Before the swap: exactly A; after the fade: B (the new IR has heard everything since the swap); during it, an
        // equal-gain blend of the two.
        var fade = (int)(0.02 * Rate);
        Assert.True(Analysis.NullResidualDb(a.AsSpan(0, SwapAt), output.AsSpan(0, SwapAt)) < -140);
        var end = SwapAt + fade + before.Taps + 512;
        Assert.True(Analysis.NullResidualDb(b.AsSpan(end), output.AsSpan(end)) < -100);
        var ideal = new float[fade];
        for (var i = 0; i < fade; i++) {
            var w = (i + 1f) / fade;
            ideal[i] = a[SwapAt + i] + (b[SwapAt + i] - a[SwapAt + i]) * w;
        }

        Assert.True(
            Analysis.NullResidualDb(ideal, output.AsSpan(SwapAt, fade)) < -30,
            $"fade residual {Analysis.NullResidualDb(ideal, output.AsSpan(SwapAt, fade)):0.0} dB"
        );

        // No step: the largest sample-to-sample change around the swap is no bigger than the steady signal's.
        static double MaxStep(ReadOnlySpan<float> x) {
            var max = 0.0;
            for (var i = 1; i < x.Length; i++) {
                max = Math.Max(max, Math.Abs(x[i] - x[i - 1]));
            }

            return max;
        }

        var steady = Math.Max(MaxStep(a.AsSpan(SwapAt - 2_000, 2_000)), MaxStep(b.AsSpan(end, 2_000)));
        Assert.True(MaxStep(output.AsSpan(SwapAt - 64, fade + 128)) < 1.05 * steady);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Refresh_without_changes_builds_nothing() {
        using var block = Prepared();
        block.Refresh();
        BlockRender.Mono(block, Signals.Noise(512, 1), 64);
        block.Params.SetTarget(CabBlock.LowCut, 200f); // filtered on the audio thread: no rebuild
        block.Params.SetTarget(CabBlock.Level, -3f);
        block.Refresh();
        BlockRender.Mono(block, Signals.Noise(512, 1), 64);

        Assert.Equal(0, block.IrSwaps);
    }

    [Theory]
    [InlineData(44_100)]
    [InlineData(96_000)]
    public void Irs_scale_with_the_sample_rate(double rate) {
        using var block = new CabBlock();
        block.Prepare(new(rate, 128));

        Assert.Equal((int)Math.Round(2_048 * rate / 48_000), block.Taps);
        var (left, _) = block.ImpulseResponse();
        Assert.True(Analysis.AllFinite(left));
    }

    [Fact]
    public void Grid_points_reproduce_their_irs_and_interpolate_between_them() {
        // A 3 × 2 grid made from the model itself, then loaded as a user pack.
        double[] positions = [0, 50, 100];
        double[] distances = [0, 6];
        var irs = new Dictionary<(double, double), float[]>();
        var entries = new List<string>();
        foreach (var p in positions) {
            foreach (var d in distances) {
                var ir = Synthesize(new(V30, Closed4x12, CabCatalogue.Mics[0], p / 100, d * Inch, 0, true, 0));
                var shifted = Signals.Concat(new float[10], ir); // a little pre-delay, as captures have
                var file = $"p{p}_d{d}.wav";
                WavFile.Write(Path.Combine(folder, file), new((int)Rate, [shifted]));
                irs[(p, d)] = ir;
                entries.Add($$"""{ "position": {{p}}, "distance": {{d}}, "file": "{{file}}" }""");
            }
        }

        File.WriteAllText(
            Path.Combine(folder, "manifest.json"),
            $$"""{ "format": "cathode-cabgrid", "version": 1, "name": "test", "irs": [{{string.Join(",", entries)}}] }"""
        );

        using var block = new CabBlock();
        block.LoadAsset("slot1", folder);
        block.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Source), CabBlock.SourcePack);
        block.Prepare(Spec);
        Assert.Equal([0, 0.5, 1], block.Grid(0)!.Positions);

        Assert.True(MaxDifferenceDb(irs[(50, 6)], IrAt(block, 50, 6)) < 0.5);
        Assert.True(MaxDifferenceDb(irs[(0, 0)], IrAt(block, 0, 0)) < 0.5);

        // Halfway between two positions: every band between its neighbours' (bilinear in log magnitude).
        var between = IrAt(block, 25, 0);
        foreach (var hz in new[] { 200.0, 1_000, 3_000, 5_000 }) {
            var low = BandDb(irs[(0, 0)], hz / 1.1, hz * 1.1);
            var high = BandDb(irs[(50, 0)], hz / 1.1, hz * 1.1);
            Assert.InRange(BandDb(between, hz / 1.1, hz * 1.1), Math.Min(low, high) - 0.5, Math.Max(low, high) + 0.5);
        }
    }

    [Fact]
    public void Grid_packs_load_from_an_archive_and_a_single_wav() {
        var ir = Synthesize(new(V30, Closed4x12, CabCatalogue.Mics[0], 0.25, Inch, 0, true, 0));
        WavFile.Write(Path.Combine(folder, "a.wav"), new((int)Rate, [ir]));
        var pack = Path.Combine(folder, "pack.cabgrid");
        using (var zip = ZipFile.Open(pack, ZipArchiveMode.Create)) {
            zip.CreateEntryFromFile(Path.Combine(folder, "a.wav"), "pack/a.wav");
            using var manifest = new StreamWriter(zip.CreateEntry("pack/manifest.json").Open());
            manifest.Write("""{ "irs": [{ "position": 0, "distance": 1, "file": "a.wav" }] }""");
        }

        using var archive = CabGrid.Load(pack);
        using var single = CabGrid.Load(Path.Combine(folder, "a.wav"));

        Assert.Equal([1.0], archive.Distances);
        Assert.Single(single.Positions);
    }

    [Fact]
    public void An_incomplete_grid_is_rejected() {
        WavFile.Write(Path.Combine(folder, "a.wav"), new((int)Rate, [Signals.Impulse(64)]));
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"),
            """{ "irs": [ { "position": 0, "distance": 0, "file": "a.wav" }, { "position": 50, "distance": 1, "file": "a.wav" } ] }"""
        );

        Assert.Throws<InvalidDataException>(() => CabGrid.Load(folder));
    }

    [Fact]
    public void Piston_and_bessel_helpers_match_their_definitions() {
        Assert.Equal(1, CabAcoustics.BesselJ0(0), 12);
        Assert.Equal(0, CabAcoustics.BesselJ0(CabCatalogue.RadialModes[0]), 4);
        Assert.Equal(0, CabAcoustics.BesselJ0(CabCatalogue.RadialModes[2]), 4);
        Assert.Equal(-3.01, CabAcoustics.PistonDb(1.62), 2);
        Assert.Equal(6, CabAcoustics.PeakDb(1_000, new(1_000, 6, 2)), 6);
        Assert.Equal(-3.01, CabAcoustics.HighPassDb(100, 100, 0.7071), 2);
    }

    static CabBlock Prepared(int param = -1, float value = 0) {
        var block = new CabBlock();
        if (param >= 0) {
            block.Params.SetTarget(param, value);
        }

        block.Prepare(Spec);
        return block;
    }

    static float[] IrAt(CabBlock block, float position, float distance) {
        block.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Position), position);
        block.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Distance), distance);
        return block.ImpulseResponse().Left;
    }

    static float[] Synthesize(in MicPlacement placement) {
        using var synthesizer = new CabIrSynthesizer(Rate);
        var ir = new float[synthesizer.Taps];
        synthesizer.Synthesize(placement, ir);
        return ir;
    }

    /// <summary>Low end (80-160 Hz) relative to the mids (0.8-1.6 kHz), dB.</summary>
    static double BassBoost(float[] ir) => BandDb(ir, 80, 160) - BandDb(ir, 800, 1_600);

    /// <summary>Treble (3-8 kHz) relative to the mids (300 Hz-1 kHz), dB.</summary>
    static double Brightness(float[] ir) => BandDb(ir, 3_000, 8_000) - BandDb(ir, 300, 1_000);

    /// <summary>Mean power over a band on a fine log grid, dB.</summary>
    static double BandDb(float[] ir, double low, double high) {
        double sum = 0;
        var n = 0;
        for (var hz = low; hz <= high; hz *= 1.02) {
            var m = Analysis.MagnitudeAt(ir, hz, Rate);
            sum += m * m;
            n++;
        }

        return 10 * Math.Log10(sum / n + 1e-30);
    }

    /// <summary>Largest magnitude difference over 100 Hz-8 kHz in sixth-octave bands, dB.</summary>
    static double MaxDifferenceDb(float[] a, float[] b) {
        var max = 0.0;
        for (var hz = 100.0; hz <= 8_000; hz *= 1.12) {
            max = Math.Max(max, Math.Abs(BandDb(a, hz / 1.06, hz * 1.06) - BandDb(b, hz / 1.06, hz * 1.06)));
        }

        return max;
    }

    /// <summary>Paul Kellet's pink-noise filter on white noise.</summary>
    static float[] PinkNoise(int length, int seed) {
        var white = Signals.Noise(length, seed, 0.25f);
        double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
        var pink = new float[length];
        for (var i = 0; i < length; i++) {
            var w = white[i];
            b0 = 0.99886 * b0 + w * 0.0555179;
            b1 = 0.99332 * b1 + w * 0.0750759;
            b2 = 0.96900 * b2 + w * 0.1538520;
            b3 = 0.86650 * b3 + w * 0.3104856;
            b4 = 0.55000 * b4 + w * 0.5329522;
            b5 = -0.7616 * b5 - w * 0.0168980;
            pink[i] = (float)((b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362) * 0.11);
            b6 = w * 0.115926;
        }

        return pink;
    }

    /// <summary>
    ///     RMS through ITU-R BS.1770-4's K filter (its published 48 kHz coefficients), skipping the first 0.25 s.
    /// </summary>
    static double KWeightedDb(float[] signal) {
        var shelf = new BiquadCoefficients(
            1.53512485958697,
            -2.69169618940638,
            1.19839281085285,
            -1.69065929318241,
            0.73248077421585
        );
        var highPass = new BiquadCoefficients(1.0, -2.0, 1.0, -1.99004745483398, 0.99007225036621);
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0, u1 = 0, u2 = 0, z1 = 0, z2 = 0, sum = 0;
        var n = 0;
        for (var i = 0; i < signal.Length; i++) {
            double x = signal[i];
            var y = shelf.B0 * x + shelf.B1 * x1 + shelf.B2 * x2 - shelf.A1 * y1 - shelf.A2 * y2;
            (x2, x1, y2, y1) = (x1, x, y1, y);
            var z = highPass.B0 * y + highPass.B1 * u1 + highPass.B2 * u2 - highPass.A1 * z1 - highPass.A2 * z2;
            (u2, u1, z2, z1) = (u1, y, z1, z);
            if (i >= 12_000) {
                sum += z * z;
                n++;
            }
        }

        return 10 * Math.Log10(sum / n);
    }

    readonly record struct BiquadCoefficients(double B0, double B1, double B2, double A1, double A2);
}
