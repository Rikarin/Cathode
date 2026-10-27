namespace Cathode.Dsp.Capture;

/// <summary>
///     Runs a capture model at the rate it was trained at inside an engine running at another rate: the input is
///     resampled to the model rate, the model runs there, and its output is resampled back. A neural capture's filters
///     and time constants are learnt in samples, so run at the wrong rate its whole response shifts in frequency (an
///     octave up at twice the rate). Every NAM host resamples for this reason.
/// </summary>
/// <remarks>
///     Both conversions are windowed-sinc interpolators at arbitrary ratio (any pair of integer rates: 44.1/48/88.2/96
///     kHz), band-limited to 90 % of the lower Nyquist frequency, with a Kaiser window. Engine sample k comes out
///     <see cref="Latency" /> samples later, a fixed delay the block reports so the graph aligns parallel paths to it.
///     Construction and <see cref="Reset" /> run on the control thread; <see cref="Process" /> is allocation-free.
/// </remarks>
public sealed class CaptureResampler {
    const int ZeroCrossings = 12;
    const double Bandwidth = 0.9; // of the lower Nyquist frequency
    const double KaiserBeta = 7.0; // about -70 dB stopband
    const int TableResolution = 256; // kernel points per input sample

    readonly long engineRate;
    readonly long modelRate;
    readonly Kernel toModel; // over engine samples
    readonly Kernel toEngine; // over model samples
    readonly float[] input; // engine-rate history, a ring
    readonly float[] modelOutput; // model-rate history, a ring
    readonly long inputMask;
    readonly long outputMask;
    readonly float[] modelIn;
    readonly float[] modelOut;
    long received; // engine samples pushed so far
    long produced; // model samples run so far

    /// <param name="engineRate">The engine's sample rate.</param>
    /// <param name="modelRate">The rate the model was trained at.</param>
    /// <param name="maxBlockSize">The largest engine block <see cref="Process" /> will be given.</param>
    public CaptureResampler(double engineRate, double modelRate, int maxBlockSize) {
        this.engineRate = (long)Math.Round(engineRate);
        this.modelRate = (long)Math.Round(modelRate);
        if (this.engineRate <= 0 || this.modelRate <= 0) {
            throw new ArgumentOutOfRangeException(nameof(modelRate), "Sample rates must be positive.");
        }

        var cutoff = Bandwidth * 0.5 * Math.Min(this.engineRate, this.modelRate); // Hz
        toModel = new(2 * cutoff / this.engineRate);
        toEngine = new(2 * cutoff / this.modelRate);
        // Output sample k needs model samples up to floor(k * fm / fe) + toEngine.Reach, and model sample j needs
        // engine samples up to floor(j * fe / fm) + toModel.Reach; this delay keeps both available (see Process).
        Latency = toModel.Reach + (int)Math.Ceiling((toEngine.Reach + 1.0) * this.engineRate / this.modelRate) + 1;
        MaxModelFrames = (int)Math.Ceiling(maxBlockSize * (double)this.modelRate / this.engineRate) + 2;
        input = new float[RingSize(maxBlockSize + 2 * toModel.Reach + 8)];
        modelOutput = new float[RingSize(
            (int)Math.Ceiling((maxBlockSize + Latency) * (double)this.modelRate / this.engineRate)
            + 2 * toEngine.Reach
            + 8
        )];
        inputMask = input.Length - 1;
        outputMask = modelOutput.Length - 1;
        modelIn = new float[MaxModelFrames];
        modelOut = new float[MaxModelFrames];
    }

    /// <summary>The rate the model runs at.</summary>
    public double ModelRate => modelRate;

    /// <summary>The largest block the model is given; reset the model for this many frames.</summary>
    public int MaxModelFrames { get; }

    /// <summary>The fixed delay from input to output, in engine samples.</summary>
    public int Latency { get; }

    /// <summary>
    ///     True when a model trained at <paramref name="modelRate" /> needs resampling at
    ///     <paramref name="engineRate" />.
    /// </summary>
    public static bool IsNeeded(double engineRate, double modelRate) => Math.Abs(engineRate - modelRate) >= 0.5;

    /// <summary>Clears the histories (the model is reset separately).</summary>
    public void Reset() {
        Array.Clear(input);
        Array.Clear(modelOutput);
        received = 0;
        produced = 0;
    }

    /// <summary>
    ///     Audio thread: <paramref name="samples" /> (engine rate) through <paramref name="model" /> at its own rate,
    ///     in place, delayed by <see cref="Latency" />.
    /// </summary>
    public void Process(Span<float> samples, ICaptureModel model) {
        var n = samples.Length;
        for (var i = 0; i < n; i++) {
            input[(received + i) & inputMask] = samples[i];
        }

        received += n;

        // Every model sample whose interpolation window is complete: floor(j * fe / fm) + reach <= received - 1.
        var ready = received - toModel.Reach;
        var end = ready > 0 ? (ready * modelRate - 1) / engineRate + 1 : 0;
        var count = (int)(end - produced);
        for (var m = 0; m < count; m++) {
            var position = (produced + m) * engineRate; // engine time of model sample j, times fm
            modelIn[m] = toModel.Interpolate(
                input,
                inputMask,
                position / modelRate,
                (double)(position % modelRate) / modelRate
            );
        }

        if (count > 0) {
            model.Process(modelIn.AsSpan(0, count), modelOut.AsSpan(0, count));
            for (var m = 0; m < count; m++) {
                modelOutput[(produced + m) & outputMask] = modelOut[m];
            }

            produced += count;
        }

        var first = received - n - Latency; // the engine sample that comes out first in this block
        for (var i = 0; i < n; i++) {
            var k = first + i;
            if (k < 0) {
                samples[i] = 0f;
                continue;
            }

            var position = k * modelRate; // model time of engine sample k, times fe
            samples[i] = toEngine.Interpolate(
                modelOutput,
                outputMask,
                position / engineRate,
                (double)(position % engineRate) / engineRate
            );
        }
    }

    static int RingSize(int atLeast) {
        var size = 16;
        while (size < 2 * atLeast) {
            size <<= 1;
        }

        return size;
    }

    /// <summary>A Kaiser-windowed sinc low-pass at <c>omega</c> (cutoff over half the input rate), tabulated.</summary>
    sealed class Kernel {
        readonly float[] table; // h(u) for u = -(Reach + 1) .. Reach + 1 in steps of 1 / TableResolution
        readonly double offset;

        public Kernel(double omega) {
            var halfWidth = ZeroCrossings / omega; // input samples
            Reach = (int)Math.Ceiling(halfWidth);
            offset = (Reach + 1) * TableResolution;
            table = new float[2 * (Reach + 1) * TableResolution + 2];
            var norm = BesselI0(KaiserBeta);
            for (var p = 0; p < table.Length; p++) {
                var u = (p - offset) / TableResolution;
                if (Math.Abs(u) >= halfWidth) {
                    continue;
                }

                var x = Math.PI * omega * u;
                var sinc = x == 0 ? 1 : Math.Sin(x) / x;
                var r = u / halfWidth;
                table[p] = (float)(omega * sinc * BesselI0(KaiserBeta * Math.Sqrt(1 - r * r)) / norm);
            }
        }

        /// <summary>How many input samples either side of a point the kernel reaches.</summary>
        public int Reach { get; }

        /// <summary>
        ///     The signal in <paramref name="ring" /> at input time <paramref name="whole" /> +
        ///     <paramref name="fraction" />.
        /// </summary>
        public float Interpolate(float[] ring, long mask, long whole, double fraction) {
            var sum = 0f;
            for (var d = -Reach; d <= Reach; d++) {
                // Input sample whole + d sits at distance u = fraction - d from the point.
                var at = (fraction - d) * TableResolution + offset;
                var index = (int)at;
                var weight = (float)(at - index);
                var h = table[index] + (table[index + 1] - table[index]) * weight;
                sum += ring[(whole + d) & mask] * h;
            }

            return sum;
        }

        static double BesselI0(double x) {
            double sum = 1, term = 1;
            for (var k = 1; k < 50; k++) {
                term *= x * x / (4.0 * k * k);
                sum += term;
                if (term < 1e-12 * sum) {
                    break;
                }
            }

            return sum;
        }
    }
}
