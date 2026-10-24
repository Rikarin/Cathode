using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Reverb;

// Fixed-size state stored inline in the tank objects rather than as separate arrays, so a preset load (which creates
// the Reverb block) allocates fewer small objects.

[InlineArray(2)]
struct Two<T> {
    T element;
}

[InlineArray(4)]
struct Four<T> {
    T element;
}

[InlineArray(7)]
struct Seven<T> {
    T element;
}

[InlineArray(12)]
struct Twelve<T> {
    T element;
}
