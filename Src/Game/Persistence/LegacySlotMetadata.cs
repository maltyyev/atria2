using System.Buffers.Binary;

namespace Atria.Game.Persistence;

/// <summary>133-byte slot record42ac30/42b0d0. Thirteen leading bytes and DWORD113 retain opaque identity.</summary>
public sealed class LegacySlotMetadata
{
    public const int Size = 133;
    private readonly byte[] bytes;
    private LegacySlotMetadata(byte[] bytes) => this.bytes = bytes;
    public static LegacySlotMetadata Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Size)
        {
            throw new InvalidDataException("Slot metadata must contain133 bytes.");
        }

        return new(bytes.ToArray());
    }

    public static LegacySlotMetadata Empty(ReadOnlySpan<byte> emptyLabel)
    {
        var data = new byte[Size];
        WriteString(data.AsSpan(13, 50), emptyLabel);
        WriteString(data.AsSpan(63, 50), emptyLabel);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(121), -1);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(125), -1);
        return new(data);
    }

    public byte[] Encode() => (byte[])bytes.Clone();
    public ReadOnlyMemory<byte> PrimaryName => ReadString(bytes.AsMemory(13, 50));
    public ReadOnlyMemory<byte> SecondaryName => ReadString(bytes.AsMemory(63, 50));
    public int Opaque113 => Read(113);
    public int Present => Read(117);
    public int AreaIndex => Read(121);
    public int TownId => Read(125);
    public int ActorZeroLevel => Read(129);

    private int Read(int at) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at, 4));
    public LegacySlotMetadata WithCurrent(
        ReadOnlySpan<byte> primary,
        ReadOnlySpan<byte> secondary,
        int savedMode,
        int area,
        int town,
        int actorZeroLevel,
        ReadOnlySpan<byte> worldSuffix
    )
    {
        var copy = Encode();
        byte[] first = Trim(primary).ToArray(), second = Trim(secondary).ToArray();
        if (savedMode == 300)
        {
            first = Join(first, Trim(worldSuffix));
            second = Join(second, Trim(worldSuffix));
        }

        WriteString(copy.AsSpan(13, 50), first);
        WriteString(copy.AsSpan(63, 50), second);
        void Put(int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(at, 4), value);
        Put(117, 1);
        Put(121, area);
        Put(125, town);
        Put(129, actorZeroLevel);
        return new(copy);
    }

    private static ReadOnlyMemory<byte> ReadString(ReadOnlyMemory<byte> field)
    {
        int end = field.Span.IndexOf((byte)0);
        if (end < 0)
        {
            throw new InvalidDataException("Slot label lacks its bounded terminator.");
        }

        return field[..end];
    }

    private static void WriteString(Span<byte> target, ReadOnlySpan<byte> value)
    {
        int end = value.IndexOf((byte)0);
        if (end >= 0)
        {
            value = value[..end];
        }

        if (value.Length >= target.Length)
        {
            throw new InvalidDataException("Slot label exceeds50-byte native field.");
        }

        value.CopyTo(target);
        target[value.Length] = 0;
    }

    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> value)
    {
        int end = value.IndexOf((byte)0);
        return end < 0 ? value : value[..end];
    }

    private static byte[] Join(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var result = new byte[checked(a.Length + b.Length)];
        a.CopyTo(result);
        b.CopyTo(result.AsSpan(a.Length));
        return result;
    }
}
