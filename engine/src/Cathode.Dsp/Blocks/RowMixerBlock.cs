using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     A row mixer (the Mixer, the Outputs): the graph compiler hands it each cabled input through
///     <see cref="MixInput" /> with the grid row it comes from, instead of summing them, and each is added at its row's
///     left and right gains; inputs from rows below the last use the last row's. The gains glide to new targets at a
///     fixed rate (full scale in 20 ms), so scene changes never click. The first parameters are each row's gain (or
///     level), then each row's balance.
/// </summary>
public abstract class RowMixerBlock : DspBlock, ISideInputBlock {
    const double GlideMs = 20;

    readonly float[] gainLeft;
    readonly float[] gainRight;
    float glideStep = 1f;

    private protected RowMixerBlock(BlockDescriptor descriptor, int rows) : base(descriptor) {
        gainLeft = new float[rows];
        gainRight = new float[rows];
    }

    /// <summary>Its cabled inputs by row (<see cref="MixInput" />).</summary>
    public SideInputs SideInputs => SideInputs.Cables;

    /// <summary>Audio thread: a cabled input, through <see cref="MixInput" />.</summary>
    public void SideInput(SideInputKind kind, int row, in StereoBuffer signal, in StereoBuffer destination) =>
        MixInput(row, signal, destination);

    /// <summary>Adds <paramref name="source" />, from grid row <paramref name="row" />, at its row's gains.</summary>
    public void MixInput(int row, in StereoBuffer source, in StereoBuffer destination) {
        var slot = Slot(row);
        var (targetLeft, targetRight) = Target(slot);
        var left = gainLeft[slot];
        var right = gainRight[slot];
        if (left == targetLeft && right == targetRight) {
            if (left == 1f && right == 1f) {
                for (var i = 0; i < source.Length; i++) {
                    destination.Left[i] += source.Left[i];
                    destination.Right[i] += source.Right[i];
                }
            } else {
                for (var i = 0; i < source.Length; i++) {
                    destination.Left[i] += source.Left[i] * left;
                    destination.Right[i] += source.Right[i] * right;
                }
            }

            return;
        }

        for (var i = 0; i < source.Length; i++) {
            left = Glide(left, targetLeft);
            right = Glide(right, targetRight);
            destination.Left[i] += source.Left[i] * left;
            destination.Right[i] += source.Right[i] * right;
        }

        gainLeft[slot] = left;
        gainRight[slot] = right;
    }

    /// <summary>Row <paramref name="row" />'s left and right gains for its parameters' targets.</summary>
    protected abstract (float Left, float Right) Target(int row);

    /// <summary>Scales <paramref name="io" /> in place by grid row <paramref name="row" />'s gains.</summary>
    protected void ScaleRow(int row, in StereoBuffer io) {
        var slot = Slot(row);
        var (targetLeft, targetRight) = Target(slot);
        var left = gainLeft[slot];
        var right = gainRight[slot];
        if (left == targetLeft && right == targetRight) {
            if (left != 1f || right != 1f) {
                for (var i = 0; i < io.Length; i++) {
                    io.Left[i] *= left;
                    io.Right[i] *= right;
                }
            }

            return;
        }

        for (var i = 0; i < io.Length; i++) {
            left = Glide(left, targetLeft);
            right = Glide(right, targetRight);
            io.Left[i] *= left;
            io.Right[i] *= right;
        }

        gainLeft[slot] = left;
        gainRight[slot] = right;
    }

    protected override void OnPrepare(in ProcessSpec spec) =>
        glideStep = (float)(1.0 / Math.Max(1.0, GlideMs * 0.001 * spec.SampleRate));

    /// <summary>Jumps every row's gains to their targets, without gliding (on a reset).</summary>
    protected void ResetRows() {
        for (var row = 0; row < gainLeft.Length; row++) {
            (gainLeft[row], gainRight[row]) = Target(row);
        }
    }

    /// <summary>
    ///     Advances the smoothers of the rows' parameters over <paramref name="frames" />: the rows glide in
    ///     <see cref="MixInput" /> instead, but their smoothers must keep in step.
    /// </summary>
    protected void AdvanceRows(int frames) {
        for (var p = 0; p < 2 * gainLeft.Length; p++) {
            Params.Advance(p, frames);
        }
    }

    int Slot(int row) => Math.Clamp(row, 0, gainLeft.Length - 1);

    float Glide(float gain, float target) =>
        target > gain ? Math.Min(target, gain + glideStep) : Math.Max(target, gain - glideStep);
}
