using Cathode.Contracts;
using Cathode.Dsp.Blocks;

namespace Cathode.Server.Rig;

/// <summary>Audio I/O helpers (docs/08-blocks-parity.md §2): the cab resonance helper's checks and readings.</summary>
public sealed partial class LiveRig {
    /// <summary>Throws 404 unless the working copy has block <paramref name="id" />.</summary>
    public Task CheckBlockAsync(string id) =>
        Locked(() => {
                Find(id);
                return Task.FromResult(State());
            }
        );

    /// <summary>
    ///     A Circuit Amp's speaker resonance in its active channel: the frequency at LF Resonance 100 % for its model and
    ///     speaker, the LF Resonance setting, and the frequency that gives.
    /// </summary>
    public async Task<SpeakerResonanceInfo> SpeakerResonanceAsync(string id) {
        var state = await StateAsync();
        var block = state.Preset.Blocks.FirstOrDefault(b => b.Id == id)
            ?? throw new RigException(404, $"No block '{id}'.");
        if (block.Type != CircuitAmpBlock.Info.TypeId) {
            throw new RigException(400, $"'{id}' is not a Circuit Amp.");
        }

        var channel = state.Preset.Scenes[state.ActiveScene].StateOf(id).Channel;
        var values = block.Channels.GetValueOrDefault(channel);
        float Value(int index) {
            var param = CircuitAmpBlock.Info.Params[index];
            return values?.GetValueOrDefault(param.Id, param.Default) ?? param.Default;
        }

        var baseHz = CircuitAmpBlock.SpeakerResonanceHz(
            (int)Value(CircuitAmpBlock.Type),
            (int)Value(CircuitAmpBlock.SpeakerCurve)
        );
        var percent = Value(CircuitAmpBlock.SpeakerResonance);
        return new(baseHz, percent, baseHz * percent / 100);
    }
}
