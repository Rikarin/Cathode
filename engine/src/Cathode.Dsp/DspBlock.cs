using Cathode.Dsp.Parameters;

namespace Cathode.Dsp;

/// <summary>Convenience base class wiring the parameter bank into the block lifecycle.</summary>
public abstract class DspBlock : IDspBlock {
    public BlockDescriptor Descriptor { get; }

    public ParamBank Params { get; }

    public virtual int LatencySamples => 0;

    protected ProcessSpec Spec { get; private set; } = ProcessSpec.Default;

    protected DspBlock(BlockDescriptor descriptor) {
        Descriptor = descriptor;
        Params = new(descriptor.Params);
    }

    public void Prepare(in ProcessSpec spec) {
        Spec = spec;
        Params.Prepare(spec.SampleRate);
        OnPrepare(spec);
        Reset();
    }

    public void Reset() {
        Params.SnapToTargets();
        OnReset();
    }

    public void Process(in StereoBuffer io, in BlockContext context) {
        Params.BeginBlock();
        OnProcess(io, context);
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void OnPrepare(in ProcessSpec spec) { }

    protected abstract void OnReset();

    protected abstract void OnProcess(in StereoBuffer io, in BlockContext context);

    protected virtual void Dispose(bool disposing) { }
}
