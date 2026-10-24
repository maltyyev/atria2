using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>40ef40/40f1d0 party AD0 decisions and40f0d0/40f150 AD1 attacks.</summary>
public static class PartyAiDecision
{
    //False means the native edge-escape branch clears its selected target pointer.
    public static bool Begin(
        CombatActorState actor,
        int type,
        CombatActorState? target,
        CombatAnimationCursor animation,
        int minimumY,
        int maximumY,
        uint milliseconds,
        Func<int> nextNativeRandom
    )
    {
        if ((uint)type > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        int decision = Next(nextNativeRandom) % 5;
        int vertical = Next(nextNativeRandom) % 80 + (type == 0 ? 10 : 0);
        if ((decision & 1) != 0)
        {
            vertical = -vertical;
        }

        int x = actor.SpriteX, minimumX = type == 0 ? 50 : 20, maximumX = type == 0 ? 600 : 640;
        if (x < minimumX || x > maximumX)
        {
            int dx = x < minimumX ? 250 : -250;
            //40da00: explicit displacement, threshold200/far motion2;32d is retained.
            actor.Motion = (int)CombatMotionKind.Walk;
            actor.ActionFlags = 0;
            actor.FrameIndex = 0;
            actor.ActionFlags = 1;
            int mode = 1;
            if (unchecked(Abs(dx) + Abs(vertical)) > 200)
            {
                mode = 2;
                BanditAiDecision.Turn(actor, dx < 0 ? -1 : 1, animation.CurrentSprite.Width);
            }

            animation.BeginAutomaticMovement(mode, dx, vertical, milliseconds);
            return false;
        }

        actor.AttackVariant = 2;
        BanditAiDecision.PrepareMovement(actor, target, animation, minimumY, maximumY, milliseconds, nextNativeRandom, 300, 2, type == 0 ? 30 : 10, 10);
        switch (decision)
        {
            case 0:
            case 1:
                actor.MovementTicksX = unchecked((short)(unchecked((short)(actor.MovementTicksX + (type == 0 ? 40 : 20)))));
                break;
            case 2:
            case 3:
                actor.MovementTicksX = unchecked((short)(unchecked((short)(actor.MovementTicksX >> 1))));
                break;
            case 4:
                if (actor.Motion != (int)CombatMotionKind.Run)
                {
                    actor.MovementStepX = unchecked(-actor.MovementStepX);
                }

                break;
        }

        if (actor.MovementTicksX < 0)
        {
            actor.MovementTicksX = 0;
        }

        switch (Next(nextNativeRandom) % 5)
        {
            case 0:
            case 1:
                actor.MovementTicksY = unchecked((short)(unchecked((short)(actor.MovementTicksY + 10))));
                break;
            case 2:
            case 3:
                actor.MovementTicksY = unchecked((short)(unchecked((short)(actor.MovementTicksY >> 1))));
                break;
            case 4:
                actor.MovementStepY = unchecked(-actor.MovementStepY);
                break;
        }

        if (actor.MovementTicksY < 0)
        {
            actor.MovementTicksY = 0;
        }

        return true;
    }

    public static void Continue(
        CombatActorState actor,
        int type,
        CombatActorState? target,
        CombatAnimationCursor animation,
        CombatInfoCharacter info,
        uint milliseconds,
        Func<int> nextNativeRandom
    )
    {
        if ((uint)type > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        if (BanditAiDecision.AdvanceMovement(actor, target, animation, milliseconds) || target is null)
        {
            return;
        }

        int dy = Abs(unchecked(actor.Depth - target.Depth)), dx = Abs(Abs(unchecked(actor.TrackingX - target.TrackingX)));
        if (dy >= 14 || dx <= 30)
        {
            return;
        }

        if (dx < (type == 0 ? 80 : 75))
        {
            if (actor.Motion == (int)CombatMotionKind.Run)
            {
                animation.BeginAutomaticIdle(3);
                return;
            }

            BanditAiDecision.FollowFacing(actor, target, animation, milliseconds);
            CombatManualControl.Attack(actor, info, animation, milliseconds);
            return;
        }

        if (dx < (type == 0 ? 120 : 110))
        {
            BeginRandomVariation(animation, info, milliseconds, nextNativeRandom);
        }
    }

    private static void BeginRandomVariation(CombatAnimationCursor animation, CombatInfoCharacter info, uint milliseconds, Func<int> next)
    {
        int first = info.FirstVariation + 1, count = unchecked(info.VariationEnd - info.FirstVariation - 4);
        if (count <= 0 || first < 0 || first + count > 29)
        {
            throw new InvalidDataException("Party AI variation selection has no bounded native candidate range.");
        }

        bool valid = false;
        for (int i = first; i < first + count; i++)
        {
            if (!Diagonal(info.Attacks[i].Direction))
            {
                valid = true;
                break;
            }
        }

        if (!valid)
        {
            throw new InvalidDataException("Party AI variation rejection loop has no non-diagonal candidate.");
        }

        while (true)
        {
            int selected = first + Next(next) % count;
            if (!Diagonal(info.Attacks[selected].Direction))
            {
                animation.BeginAttack(selected, milliseconds);
                return;
            }
        }
    }

    private static bool Diagonal(int code) => code is 1 or 3 or 7 or 9;
    private static int Next(Func<int> next)
    {
        int value = next();
        if ((uint)value > 32767)
        {
            throw new InvalidDataException("Party AI requires native15-bit RNG.");
        }

        return value;
    }

    private static int Abs(int value)
    {
        int sign = value >> 31;
        return unchecked((value ^ sign) - sign);
    }

}
