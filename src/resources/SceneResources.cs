using System;
using System.Buffers.Binary;
using System.IO;

namespace Atria.Resources;

/// <summary>Established v2 record boundaries only. Numeric fields remain named by index until semantics are recovered.</summary>
public sealed class SodV2
{
    public const int HeaderSize = 120;
    public const int RecordSize = 370;
    private readonly ReadOnlyMemory<byte> source;
    public int Count { get; }
    public ReadOnlyMemory<byte> Header => source[..HeaderSize];
    public ReadOnlyMemory<byte> BackgroundReference => StringField(34, 13);
    public ReadOnlyMemory<byte> SpriteReference => StringField(47, 13);
    public ReadOnlyMemory<byte> MapReference => StringField(60, 13);
    public ReadOnlyMemory<byte> ScriptReference => StringField(94, 13);

    private SodV2(ReadOnlyMemory<byte> source, int count) { this.source = source; Count = count; }
    public static bool HasV2Signature(ReadOnlySpan<byte> source) => source.StartsWith("Object Tool ver2.0"u8);

    public static SodV2 Parse(ReadOnlyMemory<byte> source)
    {
        if (!HasV2Signature(source.Span)) throw new NotSupportedException("Unsupported SOD variant; FOREST00 is not v2.");
        if (source.Length < HeaderSize) throw new InvalidDataException("Truncated SOD v2 header.");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(source.Span[30..]);
        if (HeaderSize + (long)count * RecordSize != source.Length)
            throw new InvalidDataException("SOD v2 record count does not match length.");
        return new SodV2(source, (int)count);
    }

    public SodObject GetObject(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        return new SodObject(source.Slice(HeaderSize + index * RecordSize, RecordSize));
    }

    private ReadOnlyMemory<byte> StringField(int offset, int size)
    {
        ReadOnlyMemory<byte> bytes = source.Slice(offset, size);
        return bytes[..RawBytes.NullTerminated(bytes.Span).Length];
    }
}

public readonly struct SodObject
{
    public ReadOnlyMemory<byte> Raw { get; }
    internal SodObject(ReadOnlyMemory<byte> raw) => Raw = raw;
    public int GetInteger(int field)
    {
        if ((uint)field >= 30) throw new ArgumentOutOfRangeException(nameof(field));
        return BinaryPrimitives.ReadInt32LittleEndian(Raw.Span.Slice(field * 4, 4));
    }
    public ReadOnlyMemory<byte> GetString(int field)
    {
        if ((uint)field >= 5) throw new ArgumentOutOfRangeException(nameof(field));
        ReadOnlyMemory<byte> bytes = Raw.Slice(120 + field * 50, 50);
        return bytes[..RawBytes.NullTerminated(bytes.Span).Length];
    }
}

/// <summary>0x41ad90: 313 bytes of disk header, width*height 328-byte tiles, then optional inline SOD.</summary>
public sealed class AmfMap
{
    public const int HeaderSize = 313;
    public const int TileSize = 328;
    private readonly ReadOnlyMemory<byte> source;
    public int Width { get; }
    public int Height { get; }
    public ReadOnlyMemory<byte> Header => source[..HeaderSize];
    public SodV2? EmbeddedScene { get; }

    private AmfMap(ReadOnlyMemory<byte> source, int width, int height, SodV2? scene)
    { this.source = source; Width = width; Height = height; EmbeddedScene = scene; }

    public static AmfMap Parse(ReadOnlyMemory<byte> source)
    {
        if (source.Length < HeaderSize) throw new InvalidDataException("Truncated AMF header.");
        int width = BinaryPrimitives.ReadInt32LittleEndian(source.Span[13..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(source.Span[17..]);
        if (width <= 0 || height <= 0) throw new InvalidDataException("Invalid AMF dimensions.");
        // Division avoids overflowing even a hostile int.MaxValue * int.MaxValue dimension pair.
        long count = (long)width * height;
        if (count > (source.Length - HeaderSize) / TileSize) throw new InvalidDataException("AMF tile range exceeds member.");
        int end = HeaderSize + (int)count * TileSize;
        SodV2? scene = null;
        if (source.Span[0] != 0) scene = SodV2.Parse(source[end..]);
        else if (end != source.Length) throw new InvalidDataException("AMF without scene has unexplained trailing bytes.");
        return new AmfMap(source, width, height, scene);
    }

    public AmfTile GetTile(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(x));
        return new AmfTile(source.Slice(HeaderSize + (y * Width + x) * TileSize, TileSize));
    }
}

public readonly record struct MapEventCondition(int EventId, int RequiredState);

public readonly struct AmfTile
{
    public ReadOnlyMemory<byte> Raw { get; }
    internal AmfTile(ReadOnlyMemory<byte> raw) => Raw = raw;
    public int SceneFlag => BinaryPrimitives.ReadInt32LittleEndian(Raw.Span[24..]);
    public MapEventCondition FirstEvent => ReadCondition(112);
    public MapEventCondition SecondEvent => ReadCondition(120);
    private MapEventCondition ReadCondition(int offset) => new(
        BinaryPrimitives.ReadInt32LittleEndian(Raw.Span[offset..]),
        BinaryPrimitives.ReadInt32LittleEndian(Raw.Span[(offset + 4)..]));
}
