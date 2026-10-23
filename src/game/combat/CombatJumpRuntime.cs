using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>408fe0/408ed0/408c20/408d30/405800, with separate main and auxiliary jump frame bindings.</summary>
public static class CombatJumpRuntime
{
    public static bool Begin(byte[] actor, CombatInfoCharacter info, CombatAnimationCursor cursor, uint milliseconds)
    {
        int mode = I(actor, 0xb1);
        if (mode is < 0 or > 2) return false; //408fe0 caller invokes ordinary walking in this branch.
        W(actor, 0xb5, mode);
        int previousAnchor = I(actor, 0x70), previousMetadata = cursor.CurrentSprite.Metadata2;
        Transition(actor, info, cursor, 15, milliseconds);
        if (mode == 2)
        {
            Transition(actor, info, cursor, 16, milliseconds);
            if (unchecked((sbyte)actor[0xfa]) == 1)
            {
                W(actor, 0x70, unchecked(previousMetadata + previousAnchor - cursor.CurrentSprite.Metadata2));
                W(actor, 0x7c, I(actor, 0x70));
            }
        }
        return true;
    }

    public static void Transition(byte[] actor, CombatInfoCharacter info, CombatAnimationCursor cursor, int stage, uint milliseconds)
    {
        int now = unchecked((int)milliseconds);
        W(actor, 0xb9, stage); W(actor, 0x331, unchecked(I(actor, 0x331) + 1));
        if (stage == 15)
        {
            W(actor, 0x88, 0); W(actor, 0x8c, 0); W(actor, 0x331, 0); W(actor, 0xb9, 0);
            BindMain(actor, info, cursor, 15, now); return;
        }
        if (stage == 18)
        {
            W(actor, 0xb9, 0); BindMain(actor, info, cursor, 18, now); return;
        }
        if (stage is not (16 or 17)) return;
        if (stage == 16) W(actor, 0xb1, 0);
        int index = I(actor, 0x331), at = (index + 7) * 0x18;
        cursor.BindJumpSequence(index, false);
        W(actor, 0x339, info.Read(at + 12)); W(actor, 0x335, unchecked(info.Read(at + 12) + now));
        W(actor, 0xc5, info.Read(at + 16)); W(actor, 0xc9, unchecked(info.Read(at + 16) + now));
        W(actor, 0xad, info.Read(at + 8)); W(actor, 0x33d, 17);
        int facing = unchecked((sbyte)actor[0xfa]);
        W(actor, 0xe6, I(actor, 0xb5) switch { 0 => 0, 1 => facing << 2, _ => facing * 5 });
        if (stage == 16) W(actor, 0xe2, -13);
    }

    public static void Step(byte[] actor, CombatInfoCharacter info, CombatAnimationCursor cursor, uint milliseconds)
    {
        int now = unchecked((int)milliseconds);
        if (I(actor, 0xc9) <= now)
        {
            W(actor, 0x70, unchecked(I(actor, 0x70) + I(actor, 0xe6))); W(actor, 0x7c, I(actor, 0x70)); W(actor, 0x8c, I(actor, 0x7c));
            if (I(actor, 0x88) == 0) W(actor, 0x74, unchecked(I(actor, 0x80) + I(actor, 0xe2)));
            else W(actor, 0x74, unchecked(I(actor, 0x74) + I(actor, 0xe2)));
            W(actor, 0x80, I(actor, 0x74)); W(actor, 0x88, I(actor, 0x74));
            W(actor, 0xc9, unchecked(I(actor, 0xc5) + now)); W(actor, 0xe2, unchecked(I(actor, 0xe2) + 1));
            if (unchecked((sbyte)actor[0xfa]) == -1) W(actor, 0x7c, unchecked(I(actor, 0x7c) - cursor.CurrentSprite.Width));
            int stage = I(actor, 0xb9);
            if (stage == 16)
            {
                if (I(actor, 0xe2) == 0) W(actor, 0xe2, -1);
                W(actor, 0x33d, unchecked(I(actor, 0x33d) - 1));
                if (I(actor, 0x33d) == 0) { Transition(actor, info, cursor, 17, milliseconds); return; }
            }
            if (stage is 16 or 17 && I(actor, 0x78) <= unchecked(I(actor, 0x80) + cursor.CurrentSprite.Height))
            {
                W(actor, 0x80, unchecked(I(actor, 0x78) - cursor.CurrentSprite.Height)); W(actor, 0x74, I(actor, 0x80));
                Transition(actor, info, cursor, 18, milliseconds); return;
            }
        }
        if (I(actor, 0x335) <= now)
        {
            short frame = unchecked((short)(S(actor, 0xd9) + 1));
            if (frame >= cursor.AuxiliaryFrameCount) frame--;
            WS(actor, 0xd9, frame); W(actor, 0x335, unchecked(I(actor, 0x339) + now)); cursor.BindAuxiliaryFrame();
        }
        if (I(actor, 0xb1) == 0) return;
        if (I(actor, 0xbd) <= now)
        {
            short frame = unchecked((short)(S(actor, 0xd5) + 1)); WS(actor, 0xd5, frame);
            W(actor, 0xbd, unchecked(I(actor, 0xc1) + now));
            if (frame >= cursor.SequenceFrameCount) { W(actor, 0xb1, 0); WS(actor, 0xd5, 0); return; }
        }
        cursor.RebindFromFrameCounter(); W(actor, 0x7c, I(actor, 0x70)); W(actor, 0x80, I(actor, 0x74));
        int x = I(actor, 0x7c);
        if (unchecked((sbyte)actor[0xfa]) == -1) x = unchecked(x - cursor.CurrentSprite.Width - cursor.CurrentFrame.OffsetX);
        else x = unchecked(x + cursor.CurrentFrame.OffsetX);
        W(actor, 0x7c, x); W(actor, 0x80, unchecked(I(actor, 0x80) + cursor.CurrentFrame.OffsetY));
    }
    private static void BindMain(byte[] actor, CombatInfoCharacter info, CombatAnimationCursor cursor, int mode, int now)
    {
        int index = I(actor, 0x331), at = (index + 7) * 0x18;
        cursor.BindJumpSequence(index, true); W(actor, 0xb1, mode); W(actor, 0xad, 0x40);
        W(actor, 0x84, cursor.CurrentSprite.Height); W(actor, 0xc1, info.Read(at + 12)); W(actor, 0xbd, unchecked(info.Read(at + 12) + now));
    }
    private static int I(byte[] actor, int offset) => BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(offset, 4));
    private static short S(byte[] actor, int offset) => BinaryPrimitives.ReadInt16LittleEndian(actor.AsSpan(offset, 2));
    private static void W(byte[] actor, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(actor.AsSpan(offset, 4), value);
    private static void WS(byte[] actor, int offset, short value) => BinaryPrimitives.WriteInt16LittleEndian(actor.AsSpan(offset, 2), value);
}
