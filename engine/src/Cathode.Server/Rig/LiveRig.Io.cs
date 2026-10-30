using Cathode.Contracts;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Parameters;

namespace Cathode.Server.Rig;

/// <summary>
///     Audio I/O helpers (docs/08-blocks-parity.md §2): the cab resonance helper's checks and readings; and the
///     graphic EQs' frequency responses for their charts.
/// </summary>
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
        var speaker = (int)ValueIn(scene, block, info, CircuitAmpBlock.SpeakerCurve);
        var cab = speaker == CircuitAmpBlock.SpeakerAuto ? FollowedCab(scene, state.Preset, id) : null;
        var model = (int)ValueIn(scene, block, info, CircuitAmpBlock.Type);
        var baseHz = CircuitAmpBlock.SpeakerResonanceHz(model, speaker, cab);
        var percent = ValueIn(scene, block, info, CircuitAmpBlock.SpeakerResonance);
        return new(baseHz, percent, baseHz * percent / 100);
    }

    /// <summary>
    ///     The frequency response of a block's graphic EQ in its active channel, measured on the engine's own
    ///     equaliser at <see cref="ResponseFrequencies" />, with its layout's band centres: a Graphic EQ's
    ///     (<see cref="GraphicEqResponse" />), a Drive's EQ 2 (<see cref="DriveEqResponse" />) or an Amp's Output EQ
    ///     (<see cref="AmpEqResponse" />). 400 for a block without one.
    /// </summary>
    public async Task<FrequencyResponse> EqResponseAsync(string id) {
        var state = await StateAsync();
        var block = state.Preset.Blocks.Find(b => b.Id == id)
            ?? throw new RigException(404, $"No block '{id}'.");
        (BlockDescriptor Info, int Type, Func<ParamBank, float[]> Of, Func<ParamBank, float[]> Centers) kind =
            block.Type switch {
                _ when block.Type == GraphicEqBlock.Info.TypeId => (
                    GraphicEqBlock.Info,
                    GraphicEqBlock.Type,
                    static v => GraphicEqResponse.Of(v, ResponseFrequencies),
                    GraphicEqResponse.Centers
                ),
                _ when block.Type == DriveBlock.Info.TypeId => (
                    DriveBlock.Info,
                    DriveBlock.Type,
                    static v => DriveEqResponse.Of(v, ResponseFrequencies),
                    DriveEqResponse.Centers
                ),
                _ when block.Type == CircuitAmpBlock.Info.TypeId => (
                    CircuitAmpBlock.Info,
                    CircuitAmpBlock.Type,
                    static v => AmpEqResponse.Of(v, ResponseFrequencies),
                    AmpEqResponse.Centers
                ),
                _ => throw new RigException(400, $"'{id}' has no graphic EQ.")
            };

        var scene = state.Preset.Scenes[state.ActiveScene];
        var values = new ParamBank(kind.Info.Params);
        values.SetTarget(kind.Type, ValueIn(scene, block, kind.Info, kind.Type)); // the type first
        for (var p = 0; p < kind.Info.Params.Count; p++) {
            values.SetTarget(p, ValueIn(scene, block, kind.Info, p));
        }

        values.SnapToTargets();
        return new(ResponseFrequencies, kind.Of(values), kind.Centers(values));
    }

    /// <summary>The frequencies a block's response is given at: 256, log-spaced from 20 Hz to 20 kHz.</summary>
    static readonly float[] ResponseFrequencies = EqResponse.Frequencies();

    /// <summary>A parameter of a block in the channel <paramref name="scene" /> gives it, or the default.</summary>
    static float ValueIn(Scene scene, PresetBlock of, BlockDescriptor descriptor, int index) {
        return descriptor.ValueOf(index, of.Channels.GetValueOrDefault(scene.StateOf(of.Id).Channel));
    }

    /// <summary>
    ///     As the graph compiler links it (ICabFollower): the nearest Cab block downstream through the cables, breadth
    ///     first; in it the first unmuted DynaCab slot.
    /// </summary>
    static (int Enclosure, int Speaker)? FollowedCab(Scene scene, Preset preset, string from) {
        var queue = new Queue<string>([from]);
        var seen = new HashSet<string> { from };
        while (queue.Count > 0) {
            var node = queue.Dequeue();
            foreach (var cable in preset.Cables) {
                if (cable.From != node || !seen.Add(cable.To)) {
                    continue;
                }

                var next = BlockById(preset, cable.To);
                if (next?.Type == CabBlock.Info.TypeId) {
                    return DynaCabOf(scene, next);
                }

                queue.Enqueue(cable.To);
            }
        }

        return null;
    }

    static PresetBlock? BlockById(Preset preset, string id) => preset.Blocks.Find(b => b.Id == id);

    /// <summary>A Cab block's first unmuted DynaCab slot: its enclosure and speaker; null for none.</summary>
    static (int Enclosure, int Speaker)? DynaCabOf(Scene scene, PresetBlock cab) {
        for (var slot = 0; slot < CabBlock.Slots; slot++) {
            if ((int)ValueIn(scene, cab, CabBlock.Info, CabBlock.SlotParam(slot, CabBlock.Source))
                == CabBlock.SourceDynaCab
                && ValueIn(scene, cab, CabBlock.Info, CabBlock.SlotParam(slot, CabBlock.Mute)) < 0.5f) {
                return (
                    (int)ValueIn(scene, cab, CabBlock.Info, CabBlock.SlotParam(slot, CabBlock.Cab)),
                    (int)ValueIn(scene, cab, CabBlock.Info, CabBlock.SlotParam(slot, CabBlock.Speaker))
                );
            }
        }

        return null;
    }
}
