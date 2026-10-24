using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>40df80 shared AI range/cooldown/running attack gate.</summary>
public static class CombatAiAttack
{
    public static bool Try(
        CombatActorState actor,
        CombatActorState? target,
        CombatAnimationCursor cursor,
        CombatInfoCharacter info,
        uint milliseconds,
        int minimumX,
        int maximumX,
        int attackIndex,
        int minimumRank = 0,
        bool normalAttack = false,
        bool ignoreDepth = false
    )
    {
        if (target is null)
        {
            return false;
        }

        int now = unchecked((int)milliseconds);
        int dx = Abs(Abs(unchecked(actor.TrackingX - target.TrackingX))), dy = Abs(unchecked(actor.Depth - target.Depth));
        if ((dy >= 14 && !ignoreDepth) || actor.AttackDecisionDeadline >= now || dx >= maximumX || dx <= minimumX || actor.Rank <= minimumRank)
        {
            return false;
        }

        if (actor.Motion != (int)CombatMotionKind.Run)
        {
            BanditAiDecision.FollowFacing(actor, target, cursor, milliseconds);
            actor.AttackDecisionDeadline = unchecked(now + info.AttackDelay);
            if (normalAttack)
            {
                CombatManualControl.Attack(actor, info, cursor, milliseconds);
            }
            else
            {
                cursor.BeginAttack(attackIndex, milliseconds);
            }

            return true;
        }

        if (info.Type is >= 0 and <= 5 or 16 or 28 or 29 or 30 or 31)
        {
            cursor.BeginAttack(info.VariationEnd - 1, milliseconds);
            return true;
        }

        if (info.Type is not (15 or 34 or 36))
        {
            cursor.BeginAutomaticIdle(3);
        }

        return false;
    }

    private static int Abs(int value)
    {
        int sign = value >> 31;
        return unchecked((value ^ sign) - sign);
    }

}
