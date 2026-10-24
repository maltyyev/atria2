using System.Buffers.Binary;
using Atria.Game.Combat;

namespace Atria.Resources;

/// <summary>Original six-word status record. Runtime logic uses CombatStatusSlot.</summary>
public static class CombatStatusCodec
{
    public const int RecordSize = 6 * sizeof(int);

    public static void Decode(ReadOnlySpan<byte> bytes, Span<CombatStatusSlot> slots)
    {
        if (bytes.Length != checked(slots.Length * RecordSize))
        {
            throw new InvalidDataException("Invalid combat status bank length.");
        }

        for (int i = 0; i < slots.Length; i++)
        {
            var record = bytes.Slice(i * RecordSize, RecordSize);
            slots[i] = new CombatStatusSlot
            {
                Deadline = BinaryPrimitives.ReadInt32LittleEndian(record),
                Code = BinaryPrimitives.ReadInt32LittleEndian(record[4..]),
                BankFlag = BinaryPrimitives.ReadInt32LittleEndian(record[8..]),
                TeamSide = BinaryPrimitives.ReadInt32LittleEndian(record[12..]),
                BlinkDeadline = BinaryPrimitives.ReadInt32LittleEndian(record[16..]),
                BlinkVisible = BinaryPrimitives.ReadInt32LittleEndian(record[20..])
            };
        }
    }

    public static byte[] Encode(ReadOnlySpan<CombatStatusSlot> slots)
    {
        var bytes = new byte[checked(slots.Length * RecordSize)];
        for (int i = 0; i < slots.Length; i++)
        {
            var record = bytes.AsSpan(i * RecordSize, RecordSize);
            var slot = slots[i];
            BinaryPrimitives.WriteInt32LittleEndian(record, slot.Deadline);
            BinaryPrimitives.WriteInt32LittleEndian(record[4..], slot.Code);
            BinaryPrimitives.WriteInt32LittleEndian(record[8..], slot.BankFlag);
            BinaryPrimitives.WriteInt32LittleEndian(record[12..], slot.TeamSide);
            BinaryPrimitives.WriteInt32LittleEndian(record[16..], slot.BlinkDeadline);
            BinaryPrimitives.WriteInt32LittleEndian(record[20..], slot.BlinkVisible);
        }

        return bytes;
    }
}
