using System.Buffers.Binary;
using Atria.Render.Codecs;

namespace Atria.Game.Combat;

/// <summary>407520 attack-event, depth, rectangles, teams, priority and invulnerability predicate only.</summary>
public static class CombatHitEligibility
{
    //4364d0 expands each disk SHORT to a DWORD. 40b2c0's short-pointer indices
    //0/2/4/6 address A/B/C/D of rectangle0; 8/10/12/14 address rectangle1.
    public static void RebuildRectangles(CombatActorState actor, SpriteFrame sprite)
    {
        if (sprite.Rectangles.Length < 2)
        {
            throw new ArgumentException("Combat actor requires attack and hurt sprite rectangles.");
        }

        int x = unchecked((short)actor.SpriteX), y = unchecked((short)actor.SpriteY);
        SpriteRectangle attack = sprite.Rectangles[0], hurt = sprite.Rectangles[1];
        actor.AttackTop = unchecked((short)(y + attack.B));
        actor.AttackBottom = unchecked((short)(y + attack.D));
        actor.HurtTop = unchecked((short)(y + hurt.B));
        actor.HurtBottom = unchecked((short)(y + hurt.D));
        if (actor.Facing == 1)
        {
            actor.AttackLeft = unchecked((short)(x + attack.A));
            actor.AttackRight = unchecked((short)(x + attack.C));
            actor.HurtLeft = unchecked((short)(x + hurt.A));
            actor.HurtRight = unchecked((short)(x + hurt.C));
        }
        else
        {
            actor.AttackLeft = unchecked((short)(x + sprite.Width - attack.C));
            actor.AttackRight = unchecked((short)(x + sprite.Width - attack.A));
            actor.HurtLeft = unchecked((short)(x + sprite.Width - hurt.C));
            actor.HurtRight = unchecked((short)(x + sprite.Width - hurt.A));
        }
    }

    public static bool CanHit(
        int attackerIndex,
        int targetIndex,
        CombatActorState attacker,
        int attackerType,
        AnimationFrame attackFrame,
        CombatActorState target,
        AnimationFrame targetFrame,
        bool testHitLatch = true
    )
    {
        if (attackFrame.Words.Length != Atria.Render.Codecs.AnimationFrame.WordCount || targetFrame.Words.Length != Atria.Render.Codecs.AnimationFrame.WordCount)
        {
            throw new ArgumentException("Incomplete native combat actor/frame record.");
        }

        if (attacker.Active == 0 || (testHitLatch && attacker.HitLatch == 0) || (attacker.ActionFlags & (int)CombatActionFlags.Dead) != 0
|| attacker.AttackLeft == attacker.AttackRight || attackerIndex == targetIndex || target.Health <= 0 || target.HurtRight == target.HurtLeft || attacker.Team == target.Team)
        {
            return false;
        }

        int depth = attackerType < 6 ? 21 : attackFrame.DepthTolerance == 0 ? 13 : attackFrame.DepthTolerance;
        int difference = unchecked(target.Depth - attacker.Depth);
        int sign = difference >> 31;
        int nativeAbsolute = unchecked((difference ^ sign) - sign);
        if (nativeAbsolute >= depth || attacker.AttackLeft > target.HurtRight || attacker.AttackTop > target.HurtBottom
|| target.HurtLeft > attacker.AttackRight || target.HurtTop > attacker.AttackBottom)
        {
            return false;
        }

        uint flags = attackFrame.FinalDword;
        return target.HitPriority <= attackFrame.HitPriority && ((targetFrame.FinalDword & 1) == 0 || targetFrame.HitPriority < attackFrame.HitPriority)
&& (target.Invulnerable == 0 || (flags & 0x10) != 0) && (flags & 0x40) == 0;
    }

}
