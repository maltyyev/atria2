using System.Buffers.Binary;
using Atria.Render.Codecs;

namespace Atria.Game.Combat;

/// <summary>407520 attack-event, depth, rectangles, teams, priority and invulnerability predicate only.</summary>
public static class CombatHitEligibility
{
    //4364d0 expands each disk SHORT to a DWORD. 40b2c0's short-pointer indices
    //0/2/4/6 address A/B/C/D of rectangle0; 8/10/12/14 address rectangle1.
    public static void RebuildRectangles(Span<byte> actor, SpriteFrame sprite)
    {
        if (actor.Length != CombatSceneDefinition.RuntimeRecordSize || sprite.Rectangles.Length < 2)
            throw new ArgumentException("Combat actor requires attack and hurt sprite rectangles.");
        int x = S(actor, 0x7c), y = S(actor, 0x80);
        SpriteRectangle attack = sprite.Rectangles[0], hurt = sprite.Rectangles[1];
        WS(actor, 0x101, y + attack.B); WS(actor, 0x105, y + attack.D); WS(actor, 0x109, y + hurt.B); WS(actor, 0x10d, y + hurt.D);
        if (actor[0xfa] == 1)
        { WS(actor, 0xff, x + attack.A); WS(actor, 0x103, x + attack.C); WS(actor, 0x107, x + hurt.A); WS(actor, 0x10b, x + hurt.C); }
        else
        { WS(actor, 0xff, x + sprite.Width - attack.C); WS(actor, 0x103, x + sprite.Width - attack.A); WS(actor, 0x107, x + sprite.Width - hurt.C); WS(actor, 0x10b, x + sprite.Width - hurt.A); }
    }
    public static bool CanHit(int attackerIndex, int targetIndex, ReadOnlySpan<byte> attacker, int attackerType,
        AnimationFrame attackFrame, ReadOnlySpan<byte> target, AnimationFrame targetFrame, bool testHitLatch = true)
    {
        if (attacker.Length != CombatSceneDefinition.RuntimeRecordSize || target.Length != CombatSceneDefinition.RuntimeRecordSize ||
            attackFrame.Words.Length != 28 || targetFrame.Words.Length != 28)
            throw new ArgumentException("Incomplete native combat actor/frame record.");
        if (I(attacker, 0x224) == 0 || (testHitLatch && attacker[0xdd] == 0) || (I(attacker, 0xad) & 0x8000) != 0 ||
            S(attacker, 0xff) == S(attacker, 0x103) || attackerIndex == targetIndex || I(target, 0x170) <= 0 ||
            S(target, 0x10b) == S(target, 0x107) || attacker[0xa6] == target[0xa6]) return false;
        int depth = attackerType < 6 ? 21 : attackFrame.Words[24] == 0 ? 13 : attackFrame.Words[24];
        int difference = unchecked(I(target, 0x78) - I(attacker, 0x78));
        int sign = difference >> 31;
        int nativeAbsolute = unchecked((difference ^ sign) - sign);
        if (nativeAbsolute >= depth || S(attacker, 0xff) > S(target, 0x10b) ||
            S(attacker, 0x101) > S(target, 0x10d) || S(target, 0x107) > S(attacker, 0x103) ||
            S(target, 0x109) > S(attacker, 0x105)) return false;
        uint flags = attackFrame.FinalDword;
        return I(target, 0x11c) <= attackFrame.Words[9] &&
            ((targetFrame.FinalDword & 1) == 0 || targetFrame.Words[9] < attackFrame.Words[9]) &&
            (I(target, 0x240) == 0 || (flags & 0x10) != 0) && (flags & 0x40) == 0;
    }
    private static int I(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
    private static short S(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt16LittleEndian(bytes[offset..]);
    private static void WS(Span<byte> bytes,int offset,int value)=>BinaryPrimitives.WriteInt16LittleEndian(bytes[offset..],unchecked((short)value));
}
