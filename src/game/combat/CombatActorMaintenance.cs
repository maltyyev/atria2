using System.Buffers.Binary;
using Atria.Render.Codecs;

namespace Atria.Game.Combat;

/// <summary>Small recovered per-actor stages; caller owns native actor visitation/order.</summary>
public static class CombatActorMaintenance
{
    public static void RegenerateAndUpdateDisplay(Span<byte> actor, uint milliseconds)
    {
        Validate(actor);
        unchecked
        {
            int now = (int)milliseconds;
            if (I(actor, 8) > 0 && I(actor, 0xc) < now)
            {
                W(actor, 0x170, I(actor, 0x170) + 1);
                W(actor, 8, I(actor, 8) - 1);
                W(actor, 0xc, I(actor, 0x10) + now);
            }
            int shown = I(actor, 4), hp = I(actor, 0x170);
            if (hp + 10 < shown) W(actor, 4, shown - 10);
            else if (hp < shown) W(actor, 4, shown - 1);
        }
    }

    public static int ModifyActionInterval(ReadOnlySpan<byte> actor, int interval)
    { Validate(actor); return I(actor, 0x2b0) != 0 ? interval >> 1 : interval; }

    public static void CaptureSecondaryAction(Span<byte> actor, AnimationFrame frame)
    {
        Validate(actor);
        if (frame.Words.Length != 28) throw new ArgumentException("Incomplete combat animation frame.");
        if (unchecked((short)frame.Words[22]) == 0) return;
        W(actor, 0x58, frame.Words[25]); W(actor, 0x54, frame.Words[22]);
    }
    private static void Validate(ReadOnlySpan<byte> actor)
    { if (actor.Length != CombatSceneDefinition.RuntimeRecordSize) throw new ArgumentException("Incomplete combat actor record."); }
    private static int I(ReadOnlySpan<byte> bytes,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
    private static void W(Span<byte> bytes,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(bytes[offset..],value);
}
