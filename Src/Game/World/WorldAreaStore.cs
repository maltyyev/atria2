using System.Buffers.Binary;
using Atria.Resources;

namespace Atria.Game.World;

/// <summary>Owned mutable AMF header/100-tile context required by native area synchronization.</summary>
public sealed class WorldMapContext
{
    private readonly byte[] header;
    private readonly byte[] tiles;
    public int Width => BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(13, 4));
    public int Height => BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(17, 4));
    public ReadOnlyMemory<byte> BackgroundReference => CString(header.AsSpan(53, 260));
    public int AreaIndex => BinaryPrimitives.ReadInt32LittleEndian(tiles.AsSpan(124, 4));

    public byte[] GetHeader() => (byte[])header.Clone();
    public byte[] GetTiles() => (byte[])tiles.Clone();
    internal Span<byte> HeaderBytes => header;
    internal Span<byte> TileBytes => tiles;

    private WorldMapContext(byte[] header, byte[] tiles)
    {
        this.header = header;
        this.tiles = tiles;
    }

    public static WorldMapContext Parse(ReadOnlyMemory<byte> bytes)
    {
        var map = AmfMap.Parse(bytes);
        if (map.Width != 10 || map.Height != 10)
        {
            throw new NotSupportedException("Native area synchronization requires its recovered10x10 tile storage.");
        }

        var tiles = new byte[32800];
        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 10; x++)
            {
                map.GetTile(x, y).Raw.Span.CopyTo(tiles.AsSpan((y * 10 + x) * 328, 328));
            }
        }

        return new(map.Header.ToArray(), tiles);
    }

    public WorldSceneReference SelectScene(int x, int y)
    {
        if ((uint)x >= 10 || (uint)y >= 10)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        var tile = tiles.AsSpan((y * 10 + x) * 328, 328);
        return new(
            BinaryPrimitives.ReadInt32LittleEndian(tile[88..]),
            CString(tile.Slice(128, 20)),
            CString(tile.Slice(148, 20)),
            BinaryPrimitives.ReadInt32LittleEndian(tiles.AsSpan(124, 4))
        );
    }

    internal static byte[] CString(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0);
        if (end < 0)
        {
            throw new InvalidDataException("Unterminated native world string.");
        }

        return bytes[..end].ToArray();
    }
}

public sealed record WorldSceneReference(int SceneIndex, ReadOnlyMemory<byte> SceneMember, ReadOnlyMemory<byte> ScriptMember, int AreaIndex);
/// <summary>Eight417-byte inline records and owned32800-byte payloads; disk pointers never become native pointers.</summary>
public sealed class WorldAreaStore
{
    private readonly byte[][] inline = Enumerable.Range(0, 8).Select(_ => new byte[417]).ToArray();
    private readonly byte[][] payload = Enumerable.Range(0, 8).Select(_ => new byte[32800]).ToArray();
    public byte[] GetInline(int index) => (byte[])inline[Check(index)].Clone();
    public byte[] GetPayload(int index) => (byte[])payload[Check(index)].Clone();
    public bool IsInitialized(int index) => BinaryPrimitives.ReadInt32LittleEndian(inline[Check(index)].AsSpan(100, 4)) != 0;
    public void Import(int index, ReadOnlySpan<byte> inlineBytes, ReadOnlySpan<byte> payloadBytes)
    {
        Check(index);
        if (inlineBytes.Length != 417 || payloadBytes.Length != 32800)
        {
            throw new InvalidDataException("Incorrect native area record size.");
        }

        inlineBytes.CopyTo(inline[index]);
        payloadBytes.CopyTo(payload[index]);
    }

    public void ResetNames(IReadOnlyList<ReadOnlyMemory<byte>> messages)
    {
        if (messages.Count < 306)
        {
            throw new InvalidDataException("TEXTmsg lacks eight native area names.");
        }

        var names = new byte[8][];
        for (int i = 0; i < 8; i++)
        {
            var name = messages[298 + i].Span;
            int end = name.IndexOf((byte)0);
            if (end >= 0)
            {
                name = name[..end];
            }

            if (name.Length >= 50)
            {
                throw new InvalidDataException("Area name exceeds native field.");
            }

            names[i] = name.ToArray();
        }

        for (int i = 0; i < 8; i++)
        {
            inline[i].AsSpan(0, 104).Clear();
            names[i].CopyTo(inline[i], 0);
            names[i].CopyTo(inline[i], 50);
            // Original allocates new uninitialized payload; owned deterministic storage uses zero bytes.
            payload[i] = new byte[32800];
        }
    }

    public void Capture(int index, WorldMapContext map)
    {
        Check(index);
        ArgumentNullException.ThrowIfNull(map);
        byte[] next = (byte[])inline[index].Clone(), tiles = (byte[])payload[index].Clone();
        BinaryPrimitives.WriteInt32LittleEndian(next.AsSpan(100, 4), 1);
        CopyCString(map.HeaderBytes[..13], next.AsSpan(104, 13));
        map.HeaderBytes.Slice(13, 24).CopyTo(next.AsSpan(117, 24));
        next.AsSpan(141, 16).Clear();
        CopyCString(map.HeaderBytes.Slice(53, 260), next.AsSpan(157, 260));
        CopyTiles(map.TileBytes, tiles);
        inline[index] = next;
        payload[index] = tiles;
    }

    public void Restore(int index, WorldMapContext map)
    {
        Check(index);
        ArgumentNullException.ThrowIfNull(map);
        if (!IsInitialized(index))
        {
            throw new InvalidOperationException("Area has not been captured.");
        }

        byte[] header = map.GetHeader(), tiles = map.GetTiles();
        CopyCString(inline[index].AsSpan(104, 13), header.AsSpan(0, 13));
        inline[index].AsSpan(117, 24).CopyTo(header.AsSpan(13, 24));
        CopyCString(inline[index].AsSpan(157, 260), header.AsSpan(53, 260));
        CopyTiles(payload[index], tiles);
        if (BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(13, 4)) != 10 || BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(17, 4)) != 10)
        {
            throw new InvalidDataException("Stored world dimensions exceed native tile contract.");
        }

        header.CopyTo(map.HeaderBytes);
        tiles.CopyTo(map.TileBytes);
        inline[index].AsSpan(141, 16).Clear();
    }

    private static void CopyTiles(ReadOnlySpan<byte> source, Span<byte> target)
    {
        for (int i = 0; i < 100; i++)
        {
            var a = source.Slice(i * 328, 328);
            var b = target.Slice(i * 328, 328);
            a[..128].CopyTo(b);
            for (int j = 0; j < 10; j++)
            {
                CopyCString(a.Slice(128 + j * 20, 20), b.Slice(128 + j * 20, 20));
            }
        }
    }

    private static void CopyCString(ReadOnlySpan<byte> source, Span<byte> target)
    {
        int end = source.IndexOf((byte)0);
        if (end < 0 || end >= target.Length)
        {
            throw new InvalidDataException("Unterminated native world string.");
        }

        source[..(end + 1)].CopyTo(target);
    }

    private static int Check(int index) => (uint)index < 8 ? index : throw new ArgumentOutOfRangeException(nameof(index));
}
