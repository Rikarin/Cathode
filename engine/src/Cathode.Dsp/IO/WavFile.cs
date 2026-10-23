using System.Buffers.Binary;
using System.Text;

namespace Cathode.Dsp.IO;

/// <summary>Audio data as one float array per channel, nominally in [-1, 1].</summary>
public sealed record AudioData(int SampleRate, float[][] Channels) {
    public int Frames => Channels.Length == 0 ? 0 : Channels[0].Length;
}

public enum WavSampleFormat {
    Pcm16,
    Pcm24,
    Float32,
}

/// <summary>Minimal RIFF/WAVE reader and writer: PCM 16/24/32-bit, IEEE float 32/64, including WAVE_FORMAT_EXTENSIBLE.</summary>
public static class WavFile {
    private const ushort FormatPcm = 1;
    private const ushort FormatFloat = 3;
    private const ushort FormatExtensible = 0xFFFE;

    public static AudioData Read(string path) {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static AudioData Read(Stream stream) {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (ReadTag(reader) != "RIFF") {
            throw new InvalidDataException("Not a RIFF file.");
        }

        reader.ReadUInt32();
        if (ReadTag(reader) != "WAVE") {
            throw new InvalidDataException("Not a WAVE file.");
        }

        ushort format = 0, channels = 0, bits = 0;
        var sampleRate = 0;
        while (stream.Position + 8 <= stream.Length) {
            var tag = ReadTag(reader);
            var size = reader.ReadUInt32();
            var next = stream.Position + size + (size & 1);
            if (tag == "fmt ") {
                format = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadUInt32(); // byte rate
                reader.ReadUInt16(); // block align
                bits = reader.ReadUInt16();
                if (format == FormatExtensible && size >= 40) {
                    reader.ReadUInt16(); // cbSize
                    reader.ReadUInt16(); // valid bits
                    reader.ReadUInt32(); // channel mask
                    format = reader.ReadUInt16(); // first two bytes of the sub-format GUID carry the format tag
                }
            } else if (tag == "data") {
                if (channels == 0) {
                    throw new InvalidDataException("'data' chunk before 'fmt ' chunk.");
                }

                var bytes = reader.ReadBytes((int)size);
                return new AudioData(sampleRate, Decode(bytes, format, channels, bits));
            }

            stream.Position = next;
        }

        throw new InvalidDataException("No 'data' chunk found.");
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
            _ => 4,
        };
        var dataSize = audio.Frames * channels * bytesPerSample;

        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
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
    private static int Quantize(float value, int bits) {
        var scale = 1L << (bits - 1);
        return (int)Math.Clamp((long)Math.Round((double)value * scale), -scale, scale - 1);
    }

    private static float[][] Decode(byte[] bytes, ushort format, int channels, int bits) {
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
                    (FormatPcm, 16) => BinaryPrimitives.ReadInt16LittleEndian(s) / 32768f,
                    (FormatPcm, 24) => ((s[2] << 24) | (s[1] << 16) | (s[0] << 8)) / 2147483648f,
                    (FormatPcm, 32) => BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648f,
                    (FormatFloat, 32) => BinaryPrimitives.ReadSingleLittleEndian(s),
                    (FormatFloat, 64) => (float)BinaryPrimitives.ReadDoubleLittleEndian(s),
                    _ => throw new NotSupportedException($"Unsupported WAV format {format} with {bits} bits."),
                };
            }
        }

        return result;
    }

    private static string ReadTag(BinaryReader reader) => Encoding.ASCII.GetString(reader.ReadBytes(4));
}
