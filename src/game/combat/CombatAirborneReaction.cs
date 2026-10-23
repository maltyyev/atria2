using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>405300 AD600 reaction frame, flight, fall and impact transition.</summary>
public static class CombatAirborneReaction
{
    public static void Advance(byte[] actor, CombatAnimationCursor cursor, Action<int> spawnParticle)
    {
        short index = BinaryPrimitives.ReadInt16LittleEndian(actor.AsSpan(0xd5, 2));
        if (index < cursor.SequenceFrameCount - 1)
        {
            BinaryPrimitives.WriteInt16LittleEndian(actor.AsSpan(0xd5, 2), unchecked((short)(index + 1)));
            cursor.RebindFromFrameCounter();
        }
        W(actor, 0x7c, unchecked(I(actor, 0x7c) + I(actor, 0xe6)));
        if ((I(actor, 0xad) & 0x400) == 0)
        {
            W(actor, 0x80, unchecked(I(actor, 0x80) - I(actor, 0xe2)));
            W(actor, 0xf2, unchecked(I(actor, 0xf2) - 1)); W(actor, 0xe2, unchecked(I(actor, 0xe2) - I(actor, 0xea)));
            if (I(actor, 0xe2) < 0) { W(actor, 0xf2, 0); W(actor, 0xe2, 0); }
            if (I(actor, 0xf2) < 1)
            {
                W(actor, 0xf6, unchecked(I(actor, 0xf6) + 1)); cursor.BindReactionSequence(false);
                W(actor, 0x84, cursor.CurrentSprite.Height); W(actor, 0xad, I(actor, 0xad) | 0x400);
                BinaryPrimitives.WriteInt16LittleEndian(actor.AsSpan(0xd5, 2), 0);
            }
            return;
        }
        W(actor, 0x80, unchecked(I(actor, 0x80) + I(actor, 0xe2)));
        W(actor, 0xe2, unchecked(I(actor, 0xe2) + I(actor, 0xea)));
        if (unchecked(I(actor, 0x78) - cursor.CurrentSprite.Height) > I(actor, 0x80)) return;
        spawnParticle(24);
        W(actor, 0xf6, unchecked(I(actor, 0xf6) + 1)); W(actor, 0xad, 0x940); W(actor, 0xb1, 10);
        BinaryPrimitives.WriteInt16LittleEndian(actor.AsSpan(0xd5, 2), 0); cursor.BindReactionSequence(false);
        W(actor, 0x84, cursor.CurrentSprite.Height); W(actor, 0x70, I(actor, 0x7c));
        W(actor, 0x80, unchecked(I(actor, 0x78) - cursor.CurrentSprite.Height));
        if (unchecked((sbyte)actor[0xfa]) == -1) W(actor, 0x70, unchecked(I(actor, 0x70) + cursor.CurrentSprite.Width));
    }
    private static int I(byte[] actor, int offset) => BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(offset, 4));
    private static void W(byte[] actor, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(actor.AsSpan(offset, 4), value);
}
