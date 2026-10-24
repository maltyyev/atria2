using System.Buffers.Binary;
using Atria.Game.Combat;
using Atria.Resources;

namespace Atria.Tests.Combat;

// Existing byte-level parity fixtures address the historical image. All encoding work stays in tests.
internal sealed record ActorImageSlice(object Source, int Offset, int Length)
{
    private byte[] Image => Source switch
    {
        CombatActorState actor => CombatActorCodec.Encode(actor),
        byte[] bytes => bytes,
        _ => throw new ArgumentException("Expected an actor or a fixture byte buffer.")
    };

    public byte[] ToArray() => Image.AsSpan(Offset, Length).ToArray();
    public bool SequenceEqual(ReadOnlySpan<byte> other) => Image.AsSpan(Offset, Length).SequenceEqual(other);
    public bool SequenceEqual(ActorImageSlice other) => SequenceEqual(other.ToArray());
    public bool SequenceEqual(CombatActorState other) => SequenceEqual(CombatActorCodec.Encode(other));
    public ActorImageSlice Slice(int offset, int length) => new(Source, checked(Offset + offset), length);
    public void Clear() => Change(image => image.AsSpan(Offset, Length).Clear());
    public void Fill(byte value) => Change(image => image.AsSpan(Offset, Length).Fill(value));
    public void CopyTo(Span<byte> target) => Image.AsSpan(Offset, Length).CopyTo(target);
    public int Read32() => BinaryPrimitives.ReadInt32LittleEndian(Image.AsSpan(Offset, Length));
    public short Read16() => BinaryPrimitives.ReadInt16LittleEndian(Image.AsSpan(Offset, Length));
    public void Write32(int value) => Change(image => BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(Offset, Length), value));
    public void Write16(short value) => Change(image => BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(Offset, Length), value));

    private void Change(Action<byte[]> change)
    {
        byte[] image = Image;
        change(image);
        if (Source is CombatActorState actor)
        {
            CombatActorCodec.DecodeInto(image, actor);
        }
    }
}

internal static class LegacyActorFixture
{
    public static bool SequenceEqual(this Span<byte> expected, CombatActorState actual) => expected.SequenceEqual(CombatActorCodec.Encode(actual));
    public static bool SequenceEqual(this ReadOnlySpan<byte> expected, CombatActorState actual) => expected.SequenceEqual(CombatActorCodec.Encode(actual));
    public static ActorImageSlice AsSpan(this CombatActorState actor, int start = 0) => new(actor, start, CombatActorCodec.ImageSize - start);
    public static ActorImageSlice AsSpan(this CombatActorState actor, int start, int length) => new(actor, start, length);
    public static ActorImageSlice FixtureSpan(this object source, int start = 0) => new(source, start, (source is byte[] bytes ? bytes.Length : CombatActorCodec.ImageSize) - start);
    public static ActorImageSlice FixtureSpan(this object source, int start, int length) => new(source, start, length);
    public static byte[] ToArray(this CombatActorState actor) => CombatActorCodec.Encode(actor);
    public static int ReadInt32LittleEndian(ActorImageSlice slice) => slice.Read32();
    public static int ReadInt32LittleEndian(ReadOnlySpan<byte> slice) => BinaryPrimitives.ReadInt32LittleEndian(slice);
    public static short ReadInt16LittleEndian(ActorImageSlice slice) => slice.Read16();
    public static short ReadInt16LittleEndian(ReadOnlySpan<byte> slice) => BinaryPrimitives.ReadInt16LittleEndian(slice);
    public static uint ReadUInt32LittleEndian(ActorImageSlice slice) => unchecked((uint)slice.Read32());
    public static uint ReadUInt32LittleEndian(ReadOnlySpan<byte> slice) => BinaryPrimitives.ReadUInt32LittleEndian(slice);
    public static ushort ReadUInt16LittleEndian(ReadOnlySpan<byte> slice) => BinaryPrimitives.ReadUInt16LittleEndian(slice);
    public static void WriteInt32LittleEndian(ActorImageSlice slice, int value) => slice.Write32(value);
    public static void WriteInt32LittleEndian(Span<byte> slice, int value) => BinaryPrimitives.WriteInt32LittleEndian(slice, value);
    public static void WriteUInt32LittleEndian(ActorImageSlice slice, uint value) => slice.Write32(unchecked((int)value));
    public static void WriteUInt32LittleEndian(Span<byte> slice, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(slice, value);
    public static void WriteInt16LittleEndian(ActorImageSlice slice, short value) => slice.Write16(value);
    public static void WriteInt16LittleEndian(Span<byte> slice, short value) => BinaryPrimitives.WriteInt16LittleEndian(slice, value);
    public static void WriteUInt16LittleEndian(Span<byte> slice, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(slice, value);
}
