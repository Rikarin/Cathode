using Cathode.Contracts;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Convolution;
using Cathode.Dsp.IO;
using Cathode.Dsp.Spectrum;
using Cathode.Server.Engine;
using Cathode.Server.Stores;

namespace Cathode.Server.Rig;

/// <summary>
///     Impulse response tools (docs/04 Phase 6 items 6 and 8): Tone Match learning and the IR mixer. Results are new
///     assets, so presets keep referring to exactly the filter or IR they were made with.
/// </summary>
public sealed class IrTools(LiveRig rig, AssetStore assets, EngineConnection engine) {
    /// <summary>
    ///     Listens to the rig, designs the correction towards the reference recording and sets it as the Tone Match
    ///     block's <c>match</c>. If the block already has a match, the rig was heard through it, so the new filter
    ///     replaces it instead of stacking on it.
    /// </summary>
    public async Task<LiveState> ToneMatchAsync(string blockId, ToneMatchRequest request, CancellationToken cancellationToken) {
        var block = (await rig.StateAsync()).Preset.Blocks.FirstOrDefault(b => b.Id == blockId)
            ?? throw RigException.NotFound($"No block '{blockId}'.");
        if (block.Type != ToneMatchBlock.Info.TypeId) {
            throw new RigException(400, $"'{blockId}' is not a Tone Match block.");
        }

        if (request.Seconds is < 3 or > 60 || request.Amount is < 0 or > 100 || request.Smoothing is < 0.1 or > 1) {
            throw new RigException(400, "Listen for 3 to 60 seconds, with an amount of 0 to 100 % and smoothing of 0.1 to 1 octave.");
        }

        var referenceAudio = ReadWav(request.Reference);
        var (rigSpectrum, frames, rate) =
            await engine.CaptureSpectrumAsync(TimeSpan.FromSeconds(request.Seconds), cancellationToken);
        if (frames < 8) {
            throw new RigException(409, "Nothing was heard from the rig: play while Tone Match listens.");
        }

        var reference = new PowerSpectrum(ToneMatchDesign.GridSize);
        reference.AddSignal(Resampler.Resample(Mono(referenceAudio), referenceAudio.SampleRate, rate));
        if (reference.Frames == 0) {
            throw new RigException(400, "The reference recording is silent or shorter than a tenth of a second.");
        }

        float[]? previous = null;
        if (block.Assets.TryGetValue("match", out var current) && assets.PathOf(current) is { } path) {
            var old = WavFile.Read(path);
            previous = Resampler.Resample(old.Channels[0], old.SampleRate, rate);
        }

        var filter = ToneMatchDesign.Design(
            reference.Mean(),
            rigSpectrum,
            rate,
            previous,
            request.Amount,
            request.Smoothing
        );
        var name = $"Tone Match {DateTime.Now:yyyy-MM-dd HH.mm.ss}.wav";
        var asset = await StoreAsync(new((int)rate, [filter]), name, cancellationToken);
        return await rig.SetAssetAsync(blockId, "match", new(asset.Ref), null);
    }

    /// <summary>Blends IRs into a new IR asset.</summary>
    public async Task<AssetInfo> MixAsync(IrMixRequest request, CancellationToken cancellationToken) {
        if (request.Sources.Count is < 1 or > 8) {
            throw new RigException(400, "Mix 1 to 8 impulse responses.");
        }

        if (request.LengthMs is < 5 or > 2_000 || request.SampleRate is < 22_050 or > 192_000) {
            throw new RigException(400, "The result must be 5 ms to 2 s long, at 22.05 to 192 kHz.");
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) {
            throw new RigException(400, "Give the result a file name.");
        }

        var inputs = request.Sources.Select(source => {
                var audio = ReadWav(source.Asset);
                return new IrMixInput(Mono(audio), audio.SampleRate, source.LevelDb, source.DelayMs, source.Invert);
            }
        ).ToList();
        var length = (int)Math.Round(request.LengthMs * 0.001 * request.SampleRate);
        var mixed = IrMix.Mix(inputs, request.SampleRate, length, request.Align, request.Normalize);
        var name = request.Name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? request.Name : request.Name + ".wav";
        return await StoreAsync(new(request.SampleRate, [mixed]), name, cancellationToken);
    }

    AudioData ReadWav(string reference) {
        var info = assets.Get(reference) ?? throw RigException.NotFound($"No asset {reference}.");
        if (info.Kind != "ir") {
            throw new RigException(400, $"{info.Name} is not a .wav file.");
        }

        try {
            return WavFile.Read(assets.PathOf(reference)!);
        } catch (InvalidDataException e) {
            throw new RigException(400, $"{info.Name}: {e.Message}");
        }
    }

    async Task<AssetInfo> StoreAsync(AudioData audio, string name, CancellationToken cancellationToken) {
        using var stream = new MemoryStream();
        WavFile.Write(stream, audio);
        stream.Position = 0;
        return await assets.AddAsync(stream, name, cancellationToken);
    }

    /// <summary>The average of all channels: the analysis is of the tone, not the stereo image.</summary>
    static float[] Mono(AudioData audio) {
        var mono = new float[audio.Frames];
        foreach (var channel in audio.Channels) {
            for (var i = 0; i < mono.Length; i++) {
                mono[i] += channel[i] / audio.Channels.Length;
            }
        }

        return mono;
    }
}
