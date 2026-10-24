namespace Atria.Game.Combat;

/// <summary>Actual type9 decision40f4a0 and movement preparation40dc30, after native target selection.</summary>
public static class BanditAiDecision
{
    //40e1a0 type9 AD1 branch: timed movement, run-stop gate, then one actual attack-decision roll.
    public static void Continue(
        CombatActorState actor,
        CombatActorState? selectedTarget,
        CombatAnimationCursor animation,
        CombatInfoCharacter info,
        uint milliseconds,
        Func<int> nextNativeRandom
    )
    {
        int now = unchecked((int)milliseconds);
        if (AdvanceMovement(actor, selectedTarget, animation, milliseconds))
        {
            return;
        }

        int roll = nextNativeRandom();
        if ((uint)roll > 32767)
        {
            throw new InvalidDataException("Bandit AI requires native15-bit RNG.");
        }

        if (selectedTarget is null)
        {
            return;
        }

        int dx = Absolute(unchecked(actor.TrackingX - selectedTarget.TrackingX)), dy = Absolute(unchecked(actor.Depth - selectedTarget.Depth));
        if (dy >= 14 || actor.AttackDecisionDeadline >= now || dx >= 85 || dx <= 30 || actor.Rank <= 0)
        {
            return;
        }

        if (actor.Motion == (int)CombatMotionKind.Run)
        {
            animation.BeginAutomaticIdle(3);
            return;
        }

        FollowFacing(actor, selectedTarget, animation, milliseconds);
        actor.AttackDecisionDeadline = unchecked(now + info.AttackDelay);
        animation.BeginAttack(roll % 2, milliseconds);
    }

    //Shared40def0/40e0d0 stage also precedes party and other enemy AD1 decisions.
    internal static bool AdvanceMovement(CombatActorState actor, CombatActorState? selectedTarget, CombatAnimationCursor animation, uint milliseconds)
    {
        int now = unchecked((int)milliseconds);
        if (actor.Motion is 0 or 1)
        {
            FollowFacing(actor, selectedTarget, animation, milliseconds);
        }

        if (now > actor.MovementDeadline)
        {
            bool moving = true;
            if ((animation.CurrentFrame.FinalDword & 2) == 0)
            {
                int variant = actor.AttackVariant;
                moving = variant switch
                {
                    0 => Step(actor, true) || Step(actor, false),
                    1 => Step(actor, false) || Step(actor, true),
                    2 => StepBoth(actor),
                    _ => false
                };
            }

            if (!moving)
            {
                actor.ActionFlags = 0;
            }

            actor.MovementDeadline = unchecked(now + actor.MovementInterval);
        }

        if (actor.ActionFlags == 0 && actor.Motion == (int)CombatMotionKind.Run)
        {
            animation.BeginAutomaticIdle(3);
            return true;
        }

        return false;
    }

    public static void Begin(
        CombatActorState actor,
        CombatActorState? selectedTarget,
        CombatAnimationCursor animation,
        int minimumY,
        int maximumY,
        uint milliseconds,
        Func<int> nextNativeRandom
    )
    {
        actor.AttackVariant = Next(nextNativeRandom) % 2;
        PrepareMovement(actor, selectedTarget, animation, minimumY, maximumY, milliseconds, nextNativeRandom);
        switch (Next(nextNativeRandom) % 9)
        {
            case 0:
            case 1:
                actor.MovementTicksX = unchecked((short)(unchecked((short)(actor.MovementTicksX + 50))));
                break;
            case 2:
            case 3:
            case 5:
                actor.MovementTicksX = unchecked((short)(unchecked((short)(actor.MovementTicksX << 1))));
                break;
            case 4:
                if (actor.Motion != (int)CombatMotionKind.Run)
                {
                    actor.MovementStepX = unchecked(-actor.MovementStepX);
                }

                break;
            default:
                animation.BeginAutomaticIdle(0);
                return;
        }

        if (actor.MovementTicksX < 0)
        {
            actor.MovementTicksX = 0;
        }

        switch (Next(nextNativeRandom) % 6)
        {
            case 0:
            case 1:
                actor.MovementTicksY = unchecked((short)(unchecked((short)(actor.MovementTicksY + 30))));
                break;
            case 2:
            case 3:
                actor.MovementTicksY = unchecked((short)(unchecked((short)(actor.MovementTicksY >> 1))));
                break;
            case 4:
                actor.MovementStepY = unchecked(-actor.MovementStepY);
                break;
            case 5:
                actor.MovementTicksY = unchecked((short)(unchecked((short)(actor.MovementTicksY << 1))));
                break;
        }

        if (actor.MovementTicksY < 0)
        {
            actor.MovementTicksY = 0;
        }
    }

    internal static void PrepareMovement(
        CombatActorState actor,
        CombatActorState? target,
        CombatAnimationCursor animation,
        int minimumY,
        int maximumY,
        uint now,
        Func<int> next,
        int distanceThreshold = 300,
        int farMode = 2,
        int closeX = 20,
        int closeY = 10
    )
    {
        int previousSpriteWidth = animation.CurrentSprite.Width;
        actor.Motion = (int)CombatMotionKind.Walk;
        actor.ActionFlags = 0;
        actor.FrameIndex = 0;
        if (target is null)
        {
            animation.BeginAutomaticIdle(0);
            return;
        }

        int dx = unchecked(target.TrackingX - actor.TrackingX), dy = unchecked(target.Depth - actor.Depth);
        int ax = Absolute(dx), ay = Absolute(dy), mode = 1;
        actor.ActionFlags = 1;
        if (unchecked(ax + ay) > distanceThreshold)
        {
            mode = farMode;
            Turn(actor, dx < 0 ? -1 : 1, previousSpriteWidth);
        }
        else if (ax < closeX && ay < closeY)
        {
            int targetX = target.TrackingX;
            int x = targetX < 321 ? unchecked(targetX + Next(next) % 40 + 20) : unchecked(targetX - Next(next) % 40 + 20);
            dx = unchecked(x - actor.TrackingX);
            int range = unchecked(maximumY - minimumY);
            if (range == 0)
            {
                throw new InvalidDataException("Bandit arena vertical RNG divisor is zero.");
            }

            dy = unchecked(Next(next) % range - actor.Depth + minimumY);
            Turn(actor, dx < 0 ? -1 : 1, previousSpriteWidth);
        }

        animation.BeginAutomaticMovement(mode, dx, dy, now);
    }

    internal static void Turn(CombatActorState actor, int direction, int currentSpriteWidth)
    {
        int old = unchecked((sbyte)actor.Facing);
        if (old == direction)
        {
            return;
        }

        if (old == 1)
        {
            actor.AnchorX = unchecked(actor.AnchorX + currentSpriteWidth);
        }
        else if (old == -1)
        {
            actor.AnchorX = unchecked(actor.AnchorX - currentSpriteWidth);
        }

        actor.Facing = unchecked((byte)direction);
    }

    internal static void FollowFacing(CombatActorState actor, CombatActorState? target, CombatAnimationCursor animation, uint milliseconds)
    {
        int now = unchecked((int)milliseconds);
        if (actor.ActionFlags == (int)CombatActionFlags.Busy || target is null || unchecked(now - actor.FacingDecisionTime) <= 999)
        {
            return;
        }

        actor.FacingDecisionTime = now;
        int targetX = target.TrackingX, x = actor.TrackingX;
        int direction = targetX < x ? -1 : targetX > x ? 1 : 0;
        if (direction == 0)
        {
            return;
        }

        int old = unchecked((sbyte)actor.Facing);
        if (old == -direction)
        {
            Turn(actor, direction, animation.CurrentSprite.Width);
            animation.BeginMotion(4, milliseconds);
        }

        actor.Facing = unchecked((byte)direction);
    }

    private static bool Step(CombatActorState actor, bool horizontal)
    {
        short count = horizontal ? actor.MovementTicksX : actor.MovementTicksY;
        if (count == 0)
        {
            return false;
        }

        if (horizontal)
        {
            actor.AnchorX = unchecked(actor.AnchorX + actor.MovementStepX);
            actor.MovementTicksX = unchecked((short)(count - 1));
        }
        else
        {
            actor.Depth = unchecked(actor.Depth + actor.MovementStepY);
            actor.MovementTicksY = unchecked((short)(count - 1));
        }
        return true;
    }

    private static bool StepBoth(CombatActorState actor)
    {
        bool horizontal = Step(actor, true), vertical = Step(actor, false);
        return horizontal || vertical;
    }

    private static int Absolute(int value)
    {
        int sign = value >> 31;
        return unchecked((value ^ sign) - sign);
    }

    private static int Next(Func<int> next)
    {
        int value = next();
        if ((uint)value > 32767)
        {
            throw new InvalidDataException("Bandit AI requires native15-bit RNG.");
        }

        return value;
    }

}
