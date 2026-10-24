using System;
using System.Buffers.Binary;
using System.IO;

namespace Atria.Resources;

/// <summary>PCM format and file-relative sample range. Read metadata without buffering music.</summary>
public sealed record PcmWaveInfo(ushort Channels, uint SampleRate, ushort BitsPerSample, ushort BlockAlign, uint ByteRate, long DataOffset, uint DataLength)
{
    public long FrameCount => DataLength / BlockAlign;
    public double DurationSeconds => (double)FrameCount / SampleRate;

    public static PcmWaveInfo Read(Stream stream)
    {
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("PCM metadata requires a seekable readable stream.", nameof(stream));
        }

        long start = stream.Position;
        Span<byte> header = stackalloc byte[12];
        stream.ReadExactly(header);
        long end = ValidateHeader(header, stream.Length - start) + start;
        PcmFormat? format = null;
        long dataOffset = -1;
        uint dataSize = 0;
        Span<byte> chunk = stackalloc byte[8];
        Span<byte> fmt = stackalloc byte[16];
        while (stream.Position < end)
        {
            if (end - stream.Position < 8)
            {
                throw new InvalidDataException("Incomplete RIFF chunk header.");
            }

            stream.ReadExactly(chunk);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            long payload = stream.Position;
            long next = ValidateChunk(payload, size, end);
            if (chunk[..4].SequenceEqual("fmt "u8))
            {
                if (format is not null || size < 16)
                {
                    throw new InvalidDataException("Invalid or duplicate WAVE format chunk.");
                }

                stream.ReadExactly(fmt);
                format = ReadFormat(fmt);
            }
            else if (chunk[..4].SequenceEqual("data"u8))
            {
                if (dataOffset >= 0)
                {
                    throw new InvalidDataException("Multiple WAVE data chunks require an explicit concatenation contract.");
                }

                dataOffset = payload;
                dataSize = size;
            }

            stream.Position = next;
        }

        return Finish(format, dataOffset, dataSize);
    }

    public static PcmWaveInfo Parse(ReadOnlySpan<byte> source)
    {
        if (source.Length < 12)
        {
            throw new InvalidDataException("Truncated WAVE header.");
        }

        long end = ValidateHeader(source[..12], source.Length);
        PcmFormat? format = null;
        long dataOffset = -1;
        uint dataSize = 0;
        long cursor = 12;
        while (cursor < end)
        {
            if (end - cursor < 8)
            {
                throw new InvalidDataException("Incomplete RIFF chunk header.");
            }

            ReadOnlySpan<byte> chunk = source.Slice((int)cursor, 8);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            long payload = cursor + 8;
            long next = ValidateChunk(payload, size, end);
            if (chunk[..4].SequenceEqual("fmt "u8))
            {
                if (format is not null || size < 16)
                {
                    throw new InvalidDataException("Invalid or duplicate WAVE format chunk.");
                }

                format = ReadFormat(source.Slice((int)payload, 16));
            }
            else if (chunk[..4].SequenceEqual("data"u8))
            {
                if (dataOffset >= 0)
                {
                    throw new InvalidDataException("Multiple WAVE data chunks require an explicit concatenation contract.");
                }

                dataOffset = payload;
                dataSize = size;
            }

            cursor = next;
        }

        return Finish(format, dataOffset, dataSize);
    }

    private static long ValidateHeader(ReadOnlySpan<byte> header, long available)
    {
        if (!header[..4].SequenceEqual("RIFF"u8) || !header[8..12].SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("Expected RIFF WAVE.");
        }

        long end = 8L + BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        if (end < 12 || end > available)
        {
            throw new InvalidDataException("RIFF length exceeds source.");
        }

        return end;
    }

    private static long ValidateChunk(long payload, uint size, long end)
    {
        long next = payload + size + (size & 1);
        if (next > end)
        {
            throw new InvalidDataException("RIFF chunk exceeds its container.");
        }

        return next;
    }

    private static PcmFormat ReadFormat(ReadOnlySpan<byte> bytes)
    {
        ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
        if (tag != 1)
        {
            throw new NotSupportedException($"WAVE format tag {tag} is not integer PCM.");
        }

        ushort channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..]);
        uint rate = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        uint byteRate = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
        ushort align = BinaryPrimitives.ReadUInt16LittleEndian(bytes[12..]);
        ushort bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes[14..]);
        if (channels == 0 || rate == 0 || bits is not (8 or 16 or 24 or 32) || (uint)channels * bits / 8 != align || (ulong)rate * align != byteRate)
        {
            throw new InvalidDataException("Inconsistent integer PCM format.");
        }

        return new PcmFormat(channels, rate, bits, align, byteRate);
    }

    private static PcmWaveInfo Finish(PcmFormat? format, long offset, uint size)
    {
        if (format is not PcmFormat f || offset < 0)
        {
            throw new InvalidDataException("WAVE requires format and data chunks.");
        }

        if (size % f.Align != 0)
        {
            throw new InvalidDataException("WAVE data ends inside a sample frame.");
        }

        return new PcmWaveInfo(f.Channels, f.Rate, f.Bits, f.Align, f.ByteRate, offset, size);
    }

    private readonly record struct PcmFormat(ushort Channels, uint Rate, ushort Bits, ushort Align, uint ByteRate);
}

public sealed class PcmWave
{
    public PcmWaveInfo Info
    {
        get;
    }
    public ReadOnlyMemory<byte> Samples
    {
        get;
    }

    private PcmWave(PcmWaveInfo info, ReadOnlyMemory<byte> source)
    {
        Info = info;
        Samples = source.Slice(checked((int)info.DataOffset), checked((int)info.DataLength));
    }

    public static PcmWave Parse(ReadOnlyMemory<byte> source) => new(PcmWaveInfo.Parse(source.Span), source);
}
