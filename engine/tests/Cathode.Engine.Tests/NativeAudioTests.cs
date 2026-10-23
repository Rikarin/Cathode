using Cathode.Audio.Native;

namespace Cathode.Engine.Tests;

public class NativeAudioTests {
    [Fact]
    public void Flush_to_zero_turns_subnormal_results_into_zero() {
        Assert.SkipUnless(NativeAudio.IsLibraryPresent, "native shim not built (run native/build.sh)");
        var result = float.NaN;
        var thread = new Thread(() => {
                NativeAudio.FlushDenormalsOnCurrentThread();
                result = Product(1e-38f, 1e-3f);
            }
        );
        thread.Start();
        thread.Join();

        Assert.True(float.IsSubnormal(Product(1e-38f, 1e-3f)), "baseline should be subnormal on a normal thread");
        Assert.Equal(0f, result);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static float Product(float a, float b) => a * b;
}
