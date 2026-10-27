using Cathode.Dsp.IO;
using Cathode.Testing;

namespace Cathode.Engine.Tests;

public class WavFileTests {
    [Theory]
    [InlineData(WavSampleFormat.Float32, -300)]
    [InlineData(WavSampleFormat.Pcm24, -140)]
    [InlineData(WavSampleFormat.Pcm16, -95)]
    public void Round_trips_within_format_precision(WavSampleFormat format, double maxResidualDb) {
        var left = Signals.Sine(440, 48_000, 4_800, 0.8f);
        var right = Signals.Noise(4_800, 4, 0.5f);
        using var stream = new MemoryStream();

        WavFile.Write(stream, new(48_000, [left, right]), format);
        stream.Position = 0;
        var read = WavFile.Read(stream);

        Assert.Equal(48_000, read.SampleRate);
        Assert.Equal(2, read.Channels.Length);
        Assert.True(Analysis.NullResidualDb(left, read.Channels[0]) <= maxResidualDb);
        Assert.True(Analysis.NullResidualDb(right, read.Channels[1]) <= maxResidualDb);
    }

    [Theory]
    [InlineData(0x7FFF_FFF0u)]
    [InlineData(0xFFFF_FFFFu)] // a streaming writer's "unknown length"
    public void A_data_size_beyond_the_file_reads_what_is_there(uint declared) {
        var read = WavFile.Read(new MemoryStream(Header(1, 1, 48_000, 16, declared, new byte[100])));

        Assert.Equal(50, read.Frames);
    }

    [Theory]
    [InlineData(1, 0, 48_000, 16)] // no channels
    [InlineData(1, 1, 48_000, 0)] // no bits
    [InlineData(1, 1, 48_000, 12)]
    [InlineData(3, 1, 48_000, 16)]
    [InlineData(2, 1, 48_000, 16)] // ADPCM
    [InlineData(1, 1, 0, 16)]
    public void Formats_it_cannot_decode_are_invalid_data(int format, int channels, int rate, int bits) {
        var wav = Header((ushort)format, (ushort)channels, rate, (ushort)bits, 4, [0, 0, 0, 0]);

        Assert.Throws<InvalidDataException>(() => WavFile.Read(new MemoryStream(wav)));
    }

    [Fact]
    public void Eight_bit_pcm_is_unsigned() {
        var read = WavFile.Read(new MemoryStream(Header(1, 1, 8_000, 8, 3, [0, 128, 255])));

        Assert.Equal([-1f, 0f, 127 / 128f], read.Channels[0]);
    }

    /// <summary>A 44-byte canonical header with the given fields, then <paramref name="data" />.</summary>
    static byte[] Header(ushort format, ushort channels, int rate, ushort bits, uint dataSize, byte[] data) {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + data.Length);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write(format);
        writer.Write(channels);
        writer.Write(rate);
        writer.Write(rate * channels * bits / 8);
        writer.Write((ushort)(channels * bits / 8));
        writer.Write(bits);
        writer.Write("data"u8);
        writer.Write(dataSize);
        writer.Write(data);
        writer.Flush();
        return stream.ToArray();
    }
}
