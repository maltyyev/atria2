using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Atria.Resources;

/// <summary>Ordered INF tagged records recovered from0x4126e0. Does not replace actor-state construction.</summary>
public sealed class ActorInfo
{
    public IReadOnlyList<ActorInfoRecord> Records { get; }
    private ActorInfo(List<ActorInfoRecord> records) => Records = records.AsReadOnly();

    public static ActorInfo Parse(ReadOnlyMemory<byte> source)
    {
        List<ActorInfoRecord> records = new();
        int cursor = 0;
        bool hasAnimation = false, hasType = false;
        while (cursor < source.Length)
        {
            int tagOffset = cursor;
            byte tag = source.Span[cursor++];
            int length;
            switch (tag)
            {
                case (byte)'R': length = 13; hasAnimation = true; break;
                case (byte)'T':
                    if (!hasAnimation) throw new InvalidDataException("INF type precedes its animation reference.");
                    length = 24; hasType = true; break;
                case (byte)'A': case (byte)'M': case (byte)'S': case (byte)'J': case (byte)'P': case (byte)'V':
                    RequireType(hasType); length = 20; break;
                case (byte)'D': RequireType(hasType); length = 12; break;
                case (byte)'X':
                    RequireType(hasType);
                    if (source.Length - cursor < 4) throw new InvalidDataException("Truncated INF sound count.");
                    uint count = BinaryPrimitives.ReadUInt32LittleEndian(source.Span[cursor..]);
                    if (count > (source.Length - cursor - 4) / 4) throw new InvalidDataException("INF sound list exceeds member.");
                    length = 4 + (int)count * 4;
                    break;
                case (byte)'E':
                    if (cursor != source.Length) throw new InvalidDataException("Unexplained bytes after INF end marker.");
                    records.Add(new ActorInfoRecord(tagOffset, tag, ReadOnlyMemory<byte>.Empty));
                    return new ActorInfo(records);
                default: throw new InvalidDataException($"Unknown INF tag0x{tag:x2} at{tagOffset}.");
            }
            if (length > source.Length - cursor) throw new InvalidDataException("Truncated INF tagged record.");
            records.Add(new ActorInfoRecord(tagOffset, tag, source.Slice(cursor, length)));
            cursor += length;
        }
        throw new InvalidDataException("INF end marker is missing.");
    }

    private static void RequireType(bool hasType)
    { if (!hasType) throw new InvalidDataException("INF action precedes its type record."); }
}

public readonly record struct ActorInfoRecord(int Offset, byte Tag, ReadOnlyMemory<byte> Payload)
{
    public int GetInteger(int index)
    {
        if (Tag == (byte)'R' || Tag == (byte)'E') throw new InvalidOperationException("This INF record is not an integer payload.");
        if ((uint)index >= (uint)(Payload.Length / 4)) throw new ArgumentOutOfRangeException(nameof(index));
        return BinaryPrimitives.ReadInt32LittleEndian(Payload.Span.Slice(index * 4, 4));
    }
    public ReadOnlyMemory<byte> AnimationReference => Tag == (byte)'R'
        ? DatTables.Terminated(Payload)
        : throw new InvalidOperationException("This INF record is not an animation reference.");
}
