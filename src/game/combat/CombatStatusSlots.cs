using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>40b850 exact refresh/expiry/overwrite selection over3or10 native24-byte status slots.</summary>
public static class CombatStatusSlots
{
    public static int Insert(Span<byte> slots, int code, int bankFlag, int field3, int deadline, int sourceType, int milliseconds)
    {
        int count = bankFlag == 0 ? 3 : 10;
        if (slots.Length < count * 24) throw new ArgumentException("Incomplete native status-slot bank.");
        int storedCode = unchecked(code - 1), selected = -1;
        for (int i = 0; i < count; i++)
            if (I(slots, i * 24 + 4) == storedCode) { selected = i; break; }
        if (selected < 0)
            for (int i = 0; i < count; i++)
                if (I(slots, i * 24) < milliseconds) { selected = i; break; }
        if (selected < 0) selected = 0;
        int at = selected * 24;
        W(slots, at, deadline); W(slots, at + 4, storedCode); W(slots, at + 8, bankFlag);
        W(slots, at + 12, sourceType < 8 ? field3 : field3 == 0 ? 1 : 0); W(slots, at + 16, 0);
        // Original writes param_1[5], NOT selectedSlot[5]. Keep this observable field write.
        W(slots, 20, 0);
        return selected;
    }
    private static int I(ReadOnlySpan<byte> bytes,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
    private static void W(Span<byte> bytes,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(bytes[offset..],value);
}
