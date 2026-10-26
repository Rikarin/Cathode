using Cathode.Contracts;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Parameters;

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
    ///     A Circuit Amp's speaker load resonance in its active channel: the frequency at Load resonance 100 % for its
    ///     model and Speaker load (with Follow Cab, the speaker and cabinet of the Cab block it follows), the Load
    ///     resonance setting, and the frequency that gives.
    /// </summary>
    public async Task<SpeakerResonanceInfo> SpeakerResonanceAsync(string id) {
        var state = await StateAsync();
        var block = state.Preset.Blocks.Find(b => b.Id == id)
            ?? throw new RigException(404, $"No block '{id}'.");
        if (block.Type != CircuitAmpBlock.Info.TypeId) {
            throw new RigException(400, $"'{id}' is not a Circuit Amp.");
        }

        var scene = state.Preset.Scenes[state.ActiveScene];
        var info = CircuitAmpBlock.Info;
        var speaker = (int)Value(block, info, CircuitAmpBlock.SpeakerCurve);
        var cab = speaker == CircuitAmpBlock.SpeakerAuto ? FollowedCab(state.Preset, id) : null;
        var baseHz = CircuitAmpBlock.SpeakerResonanceHz((int)Value(block, info, CircuitAmpBlock.Type), speaker, cab);
        var percent = Value(block, info, CircuitAmpBlock.SpeakerResonance);
        return new(baseHz, percent, baseHz * percent / 100);

        float Value(PresetBlock of, BlockDescriptor descriptor, int index) {
            var param = descriptor.Params[index];
            var values = of.Channels.GetValueOrDefault(scene.StateOf(of.Id).Channel);
            return values?.GetValueOrDefault(param.Id, param.Default) ?? param.Default;
        }

        // As the graph compiler links it (ICabFollower): the nearest Cab block downstream through the cables, breadth
        // first; in it the first unmuted DynaCab slot.
        (int Enclosure, int Speaker)? FollowedCab(Preset preset, string from) {
            var queue = new Queue<string>([from]);
            var seen = new HashSet<string> { from };
            while (queue.Count > 0) {
                var node = queue.Dequeue();
                foreach (var cable in preset.Cables) {
                    if (cable.From != node || !seen.Add(cable.To)) {
                        continue;
                    }

                    var next = preset.Blocks.Find(b => b.Id == cable.To);
                    if (next?.Type == CabBlock.Info.TypeId) {
                        for (var slot = 0; slot < CabBlock.Slots; slot++) {
                            if ((int)Value(next, CabBlock.Info, CabBlock.SlotParam(slot, CabBlock.Source))
                                == CabBlock.SourceDynaCab
                                && Value(next, CabBlock.Info, CabBlock.SlotParam(slot, CabBlock.Mute)) < 0.5f) {
                                return (
                                    (int)Value(next, CabBlock.Info, CabBlock.SlotParam(slot, CabBlock.Cab)),
                                    (int)Value(next, CabBlock.Info, CabBlock.SlotParam(slot, CabBlock.Speaker))
                                );
                            }
                        }

                        return null;
                    }

                    queue.Enqueue(cable.To);
                }
            }

            return null;
        }
    }
}
