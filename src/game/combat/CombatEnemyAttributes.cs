using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>413110 exact initial attribute projection; requires the caller's ordered native RNG.</summary>
public static class CombatEnemyAttributes
{
    // The shared INF byte image is mutable: each actor reduces the SAME b08 timer.
    public static void Initialize(byte[] actor, byte[] sharedInfo, int regionIndex, int variant,
        Func<int> nextNativeRandom, ref int accumulatedAward)
    {
        if (actor.Length != CombatSceneDefinition.RuntimeRecordSize || sharedInfo.Length != CombatInfoCharacter.RuntimeSize)
            throw new InvalidDataException("Unexpected combat runtime record dimensions.");
        int R(int offset) => BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(offset, 4));
        void W(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(actor.AsSpan(offset, 4), value);
        int kind = BinaryPrimitives.ReadInt32LittleEndian(sharedInfo.AsSpan(0xafc, 4));
        //413110 returns before any table access for scripted high-HP enemies, even for an otherwise invalid type.
        if(kind>7 && R(0x170)>9999) return;
        if ((uint)kind >= 52) throw new NotSupportedException("Combat attribute type lies outside byte-established rows0..51.");
        ReadOnlySpan<int> row = CombatAttributeTable.Values.Slice(kind * 32, 32);
        if (kind > 7)
        {
            if ((uint)regionIndex > 9) throw new NotSupportedException("Combat region exceeds the recovered eight incremental attribute columns.");
            unchecked
            {
                int hp = row[0], attack = row[1] << 2, field1f0 = row[2];
                for (int i = 1; i < regionIndex; i++)
                { hp += row[7 + i] * 2; attack += row[15 + i]; field1f0 += row[23 + i]; }
                if (unchecked((sbyte)actor[0xa6]) > 99) { hp <<= 1; attack >>= 1; field1f0 <<= 2; }
                W(0x170, hp); W(0x16c, hp); W(0x1f4, attack); W(0x1f0, field1f0);
                W(0x1e0, row[3]);
                int random = nextNativeRandom();
                if ((uint)random > 32767) throw new InvalidDataException("Combat RNG did not return a native15-bit sample.");
                W(0x1ec, random % (regionIndex + 1) + 1);
                W(0x17c, row[6]); W(0x14, hp); W(0x28, R(0x174));
                accumulatedAward += hp / 4 + row[31];
                int delay = BinaryPrimitives.ReadInt32LittleEndian(sharedInfo.AsSpan(0xb08, 4));
                if (delay > 0)
                    BinaryPrimitives.WriteInt32LittleEndian(sharedInfo.AsSpan(0xb08, 4), Math.Max(0, delay - regionIndex * 200));
                if (variant == 0) { hp += hp >> 1; attack += regionIndex / 2; }
                else if (variant == 1) { hp += hp >> 2; attack += regionIndex / 3; }
                W(0x170, hp); W(0x16c, hp); W(0x14, hp); W(0x1f4, attack);
            }
        }
        W(0x180, row[4]);
        (int code, int delay, int rank) special = kind switch
        {
            38 => (40, 20000, 11), 39 => (7, 40000, 12), 40 => (25, 20000, regionIndex),
            41 => (25, 30000, unchecked(regionIndex + 1)), 42 => (25, 30000, unchecked(regionIndex + 1)),
            43 => (25, 20000, unchecked(regionIndex + 2)), 44 => (3, 70000, 12), _ => (0, 0, 0)
        };
        if (kind is >=38 and <=44) { W(0x355, special.code); W(0x35d, special.delay); W(0x1ec, special.rank); }
    }
}
