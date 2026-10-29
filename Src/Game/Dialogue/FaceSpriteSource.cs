using System.Buffers.Binary;
using Atria.Render.Codecs;
using Microsoft.Win32.SafeHandles;

namespace Atria.Game.Dialogue;

/// <summary>Read-only face.SPR frame selection, 402510/4022c0. The selected disk frame becomes runtime frame0.</summary>
public sealed class FaceSpriteSource : IDisposable
{
    private readonly SafeFileHandle handle;
    private readonly byte[] header;
    private readonly (long Offset, int Length)[] frames;
    private readonly SpriteFile? additional;
    public int Count => frames.Length + (additional is null ? 0 : 2);

    public FaceSpriteSource(string path)
    {
        handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
        try
        {
            string authored = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "interface", "EirenFaces.spr"));
            if (File.Exists(authored))
            {
                additional = SpriteFile.Decode(File.ReadAllBytes(authored));
            }

            header = new byte[86];
            Read(header, 0);
            if (!header.AsSpan().StartsWith("JAMIE SYSTEM SPRITE TOOL"u8))
            {
                throw new InvalidDataException("Invalid face sprite header.");
            }

            int count = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(80));
            int rectangles = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(82));
            int points = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(84));
            frames = new (long, int)[count];
            long offset = 86, fileLength = RandomAccess.GetLength(handle);
            int prefix = checked(10 + rectangles * 8 + points * 4);
            Span<byte> size = stackalloc byte[4];
            for (int i = 0; i < count; i++)
            {
                if (offset + prefix + 4 > fileLength)
                {
                    throw new InvalidDataException("Truncated face frame header.");
                }

                Read(size, offset + prefix);
                uint streamBytes = BinaryPrimitives.ReadUInt32LittleEndian(size);
                long length = prefix + 4L + streamBytes;
                if (length > int.MaxValue || length > fileLength - offset)
                {
                    throw new InvalidDataException("Face frame exceeds file.");
                }

                frames[i] = (offset, (int)length);
                offset += length;
            }

            if (offset != fileLength)
            {
                throw new InvalidDataException("Unexpected face sprite tail.");
            }
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public SpriteFrame Load(int face)
    {
        if (additional is not null && face >= frames.Length && face < Count)
        {
            return additional.Frames[face - frames.Length];
        }

        if ((uint)face >= (uint)frames.Length)
        {
            throw new InvalidDataException("Face identifier is outside the original sprite.");
        }

        var frame = frames[face];
        byte[] selected = new byte[checked(86 + frame.Length)];
        header.CopyTo(selected, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(selected.AsSpan(80), 1);
        Read(selected.AsSpan(86), frame.Offset);
        return SpriteFile.Decode(selected).Frames[0];
    }

    private void Read(Span<byte> bytes, long offset)
    {
        while (!bytes.IsEmpty)
        {
            int count = RandomAccess.Read(handle, bytes, offset);
            if (count == 0)
            {
                throw new EndOfStreamException("Truncated face sprite.");
            }

            bytes = bytes[count..];
            offset += count;
        }
    }

    public void Dispose() => handle.Dispose();
}
