using System.Buffers.Binary;
using System.Text;

namespace Cathode.Dsp.IO;

/// <summary>Audio data as one float array per channel, nominally in [-1, 1].</summary>
public sealed record AudioData(int SampleRate, float[][] Channels) {
    public int Frames => Channels.Length == 0 ? 0 : Channels[0].Length;
}

/// <summary>
///     A WAVE file's format (<paramref name="Format" />: 1 PCM, 3 IEEE float) and the bytes of samples it holds.
/// </summary>
public sealed record WavHeader(int SampleRate, int Channels, int Bits, ushort Format, long DataBytes) {
    public long Frames => DataBytes / (Bits / 8 * Channels);
}

public enum WavSampleFormat {
    Pcm16,
    Pcm24,
    Float32
}

/// <summary>
///     Minimal RIFF/WAVE reader and writer: PCM 8/16/24/32-bit (8-bit read only), IEEE float 32/64, including
///     WAVE_FORMAT_EXTENSIBLE.
/// </summary>
public static class WavFile {
    const ushort FormatPcm = 1;
    const ushort FormatFloat = 3;
    const ushort FormatExtensible = 0xFFFE;
    const int MaxSampleRate = 768_000;

    public static AudioData Read(string path) {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>
    ///     Reads a seekable WAVE stream. The file is treated as untrusted: a chunk's declared size is never trusted
    ///     beyond what the stream holds, and a format this reader does not decode is an
    ///     <see cref="InvalidDataException" /> (as is a truncated header, an <see cref="EndOfStreamException" />).
    /// </summary>
    public static AudioData Read(Stream stream) {
        var header = ReadHeader(stream);
        using var reader = new BinaryReader(stream, Encoding.ASCII, true);
        var bytes = reader.ReadBytes((int)header.DataBytes);
        return new(header.SampleRate, Decode(bytes, header.Format, header.Channels, header.Bits));
    }

    /// <summary>
    ///     The format and length of a seekable WAVE stream, checked as <see cref="Read(Stream)" /> checks it, without
    ///     decoding the samples; the stream is left at the start of the samples.
    /// </summary>
    public static WavHeader ReadHeader(Stream stream) {
        using var reader = new BinaryReader(stream, Encoding.ASCII, true);
        if (ReadTag(reader) != "RIFF") {
            throw new InvalidDataException("Not a RIFF file.");
        }

        reader.ReadUInt32();
        if (ReadTag(reader) != "WAVE") {
            throw new InvalidDataException("Not a WAVE file.");
        }

        WavHeader? format = null;
        while (stream.Position + 8 <= stream.Length) {
            var tag = ReadTag(reader);
            var size = reader.ReadUInt32();
            var next = stream.Position + size + (size & 1);
            if (tag == "fmt ") {
                format = ReadFormat(reader, size);
            } else if (tag == "data") {
                // The declared size may lie (0xFFFFFFFF from a streaming writer, or hostile): only what is there
                // counts.
                var length = Math.Min(Math.Min(size, stream.Length - stream.Position), Array.MaxLength);
                return (format ?? throw new InvalidDataException("'data' chunk before 'fmt ' chunk.")) with {
                    DataBytes = length
                };
            }

            stream.Position = next;
        }

        throw new InvalidDataException("No 'data' chunk found.");
    }

    /// <summary>A <c>fmt </c> chunk of <paramref name="size" /> bytes, refused unless this reader decodes it.</summary>
    static WavHeader ReadFormat(BinaryReader reader, uint size) {
        if (size < 16) {
            throw new InvalidDataException("The 'fmt ' chunk is too short.");
        }

        var format = reader.ReadUInt16();
        var channels = reader.ReadUInt16();
        var sampleRate = reader.ReadInt32();
        reader.ReadUInt32(); // byte rate
        reader.ReadUInt16(); // block align
        var bits = reader.ReadUInt16();
        if (format == FormatExtensible && size >= 40) {
            reader.ReadUInt16(); // cbSize
            reader.ReadUInt16(); // valid bits
            reader.ReadUInt32(); // channel mask
            format = reader.ReadUInt16(); // first two bytes of the sub-format GUID carry the format tag
        }

        if (channels == 0) {
            throw new InvalidDataException("The 'fmt ' chunk declares no channels.");
        }

        if (sampleRate is <= 0 or > MaxSampleRate) {
            throw new InvalidDataException($"Unsupported sample rate {sampleRate} Hz.");
        }

        return IsSupported(format, bits)
            ? new(sampleRate, channels, bits, format, 0)
            : throw new InvalidDataException(
                $"Unsupported WAV format {format} with {bits} bits: use PCM 8, 16, 24 or 32-bit, or float 32 or 64-bit."
            );
    }

    public static void Write(string path, AudioData audio, WavSampleFormat format = WavSampleFormat.Float32) {
        using var stream = File.Create(path);
        Write(stream, audio, format);
    }

    public static void Write(Stream stream, AudioData audio, WavSampleFormat format = WavSampleFormat.Float32) {
        var channels = audio.Channels.Length;
        var bytesPerSample = format switch {
            WavSampleFormat.Pcm16 => 2,
            WavSampleFormat.Pcm24 => 3,
            _ => 4
        };
        var dataSize = audio.Frames * channels * bytesPerSample;

        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write(format == WavSampleFormat.Float32 ? FormatFloat : FormatPcm);
        writer.Write((ushort)channels);
        writer.Write(audio.SampleRate);
        writer.Write(audio.SampleRate * channels * bytesPerSample);
        writer.Write((ushort)(channels * bytesPerSample));
        writer.Write((ushort)(bytesPerSample * 8));
        writer.Write("data"u8);
        writer.Write(dataSize);

        Span<byte> sample = stackalloc byte[4];
        for (var i = 0; i < audio.Frames; i++) {
            for (var c = 0; c < channels; c++) {
                var value = audio.Channels[c][i];
                switch (format) {
                    case WavSampleFormat.Pcm16:
                        BinaryPrimitives.WriteInt16LittleEndian(sample, (short)Quantize(value, 16));
                        writer.Write(sample[..2]);
                        break;
                    case WavSampleFormat.Pcm24:
                        BinaryPrimitives.WriteInt32LittleEndian(sample, Quantize(value, 24));
                        writer.Write(sample[..3]);
                        break;
                    default:
                        BinaryPrimitives.WriteSingleLittleEndian(sample, value);
                        writer.Write(sample);
                        break;
                }
            }
        }
    }

    /// <summary>Scales by 2^(bits-1), matching the reader, and clamps to the representable range.</summary>
    static int Quantize(float value, int bits) {
        var scale = 1L << (bits - 1);
        return (int)Math.Clamp((long)Math.Round((double)value * scale), -scale, scale - 1);
    }

    static bool IsSupported(ushort format, int bits) =>
        (format, bits) is (FormatPcm, 8 or 16 or 24 or 32) or (FormatFloat, 32 or 64);

    static float[][] Decode(byte[] bytes, ushort format, int channels, int bits) {
        var bytesPerSample = bits / 8;
        var frames = bytes.Length / (bytesPerSample * channels);
        var result = new float[channels][];
        for (var c = 0; c < channels; c++) {
            result[c] = new float[frames];
        }

        var span = bytes.AsSpan();
        for (var i = 0; i < frames; i++) {
            for (var c = 0; c < channels; c++) {
                var s = span.Slice((i * channels + c) * bytesPerSample, bytesPerSample);
                result[c][i] = (format, bits) switch {
                    (FormatPcm, 8) => (s[0] - 128) / 128f, // 8-bit PCM is unsigned
                    (FormatPcm, 16) => BinaryPrimitives.ReadInt16LittleEndian(s) / 32768f,
                    (FormatPcm, 24) => ((s[2] << 24) | (s[1] << 16) | (s[0] << 8)) / 2147483648f,
                    (FormatPcm, 32) => BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648f,
                    (FormatFloat, 32) => BinaryPrimitives.ReadSingleLittleEndian(s),
                    (FormatFloat, 64) => (float)BinaryPrimitives.ReadDoubleLittleEndian(s),
                    _ => throw new NotSupportedException($"Unsupported WAV format {format} with {bits} bits.")
                };
            }
        }

        return result;
    }

    static string ReadTag(BinaryReader reader) => Encoding.ASCII.GetString(reader.ReadBytes(4));
}
