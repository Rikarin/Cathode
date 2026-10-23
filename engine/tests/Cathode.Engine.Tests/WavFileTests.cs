using Cathode.Audio;
using Cathode.Dsp.IO;
using Cathode.Testing;

namespace Cathode.Engine.Tests;

public class WavFileTests
{
    [Theory]
    [InlineData(WavSampleFormat.Float32, -300)]
    [InlineData(WavSampleFormat.Pcm24, -140)]
    [InlineData(WavSampleFormat.Pcm16, -95)]
    public void Round_trips_within_format_precision(WavSampleFormat format, double maxResidualDb)
    {
        var left = Signals.Sine(440, 48_000, 4_800, 0.8f);
        var right = Signals.Noise(4_800, seed: 4, amplitude: 0.5f);
        using var stream = new MemoryStream();

        WavFile.Write(stream, new AudioData(48_000, [left, right]), format);
        stream.Position = 0;
        var read = WavFile.Read(stream);

        Assert.Equal(48_000, read.SampleRate);
        Assert.Equal(2, read.Channels.Length);
        Assert.True(Analysis.NullResidualDb(left, read.Channels[0]) <= maxResidualDb);
        Assert.True(Analysis.NullResidualDb(right, read.Channels[1]) <= maxResidualDb);
    }
}
