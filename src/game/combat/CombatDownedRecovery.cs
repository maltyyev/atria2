using System.Buffers.Binary;
using Atria.Game.State;

namespace Atria.Game.Combat;

/// <summary>405b90: actual downed/recovery sequence transitions and ordered revival item consumption.</summary>
public static class CombatDownedRecovery
{
    // True means the caller may perform normal animation completion cleanup.
    public static bool Complete(byte[] actor, CombatInfoCharacter info, CombatAnimationCursor cursor,
        GameState state, Func<int> nextNativeRandom, Action<int> playSound,
        Action<int> showConsumedItemNotice, Action cycleControlledActor)
    {
        if (actor.Length != CombatSceneDefinition.RuntimeRecordSize) throw new ArgumentException("Invalid combat actor image.");
        W(actor, 0xc1, 30);
        if (I(actor, 0xb1) != 10)
        {
            if (I(actor, 0xb1) == 11)
            {
                if (I(actor, 0x1ec) == 0) return true;
                W(actor, 0xf6, unchecked(I(actor, 0xf6) + 1)); W(actor, 0xb1, 13); W(actor, 0xad, 0x840);
                cursor.BindReactionSequence();
                if (info.Type is >= 0 and <= 5) W(actor, 0x240, 80);
                else if (info.Type is 31 or 35 or 36 or 37 or 40 or 41 or 42 or 43 or 46) W(actor, 0x240, 100);
                return false;
            }
            if ((Next(nextNativeRandom) & 3) == 0) W(actor, 0x240, 40);
        }
        W(actor, 0xe6, I(actor, 0xe6) >> 2); W(actor, 0xb1, 11); W(actor, 0xf6, 17); W(actor, 0xad, 0x840);
        W(actor, 0xc1, CombatActorMaintenance.ModifyActionInterval(actor, (Next(nextNativeRandom) & 15) + 50));
        if (I(actor, 0x170) < 1)
        {
            int item = 61, slot = Find(actor, item);
            if (slot < 0) { item = 62; slot = Find(actor, item); }
            if (slot < 0) { item = 63; slot = Find(actor, item); }
            if (slot < 0) { item = 66; slot = Find(actor, item); }
            if (slot < 0)
            {
                W(actor, 0xad, I(actor, 0xad) | 0x8000); W(actor, 0x28c, 25); W(actor, 0x1ec, 0);
                W(actor, 0xc1, CombatActorMaintenance.ModifyActionInterval(actor, 25));
                playSound(info.Read(0xad8));
                if (actor[0xa4] != 0) cycleControlledActor();
            }
            else
            {
                W(actor, 0x208 + slot * 4, -1);
                W(actor, 0x170, item switch { 61 => 30, 62 => 70, _ => I(actor, 0x16c) });
                if (item == 66) W(actor, 0x178, I(actor, 0x174));
                state.OwnedItems[item] = unchecked(state.OwnedItems[item] - 1);
                showConsumedItemNotice(item switch { 61 => 0, 62 => 1, 63 => 2, _ => 3 });
            }
            if (info.Type < 8 && (I(actor, 0xad) & 0x8000) == 0)
            {
                if (state.AvailableItems[77] != 0) W(actor, 0x170, unchecked(I(actor, 0x170) << 1));
                if (state.AvailableItems[78] != 0) W(actor, 0x170, unchecked(I(actor, 0x170) + 50));
            }
            if (I(actor, 0x16c) < I(actor, 0x170)) W(actor, 0x170, I(actor, 0x16c));
        }
        cursor.BindReactionSequence(); return false;
    }
    private static int Find(byte[] actor, int item)
    {
        for (int i = 0; i < 6; i++) if (I(actor, 0x208 + i * 4) == item) return i;
        return -1;
    }
    private static int Next(Func<int> next)
    {
        int value = next(); if ((uint)value > 32767) throw new InvalidOperationException("Combat RNG must supply native15-bit samples."); return value;
    }
    private static int I(byte[] actor, int offset) => BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(offset, 4));
    private static void W(byte[] actor, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(actor.AsSpan(offset, 4), value);
}
