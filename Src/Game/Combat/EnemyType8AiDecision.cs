using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>40f360 exact enemy type8 decision and40e1a0 continuation.</summary>
public static class EnemyType8AiDecision
{
    public static void Begin(CombatActorState actor, CombatActorState? target, CombatAnimationCursor animation, int minimumY, int maximumY, uint milliseconds, Func<int> nextNativeRandom)
    {
        int decision = Next(nextNativeRandom) % 8;
        actor.AttackVariant = 0;
        BanditAiDecision.PrepareMovement(actor, target, animation, minimumY, maximumY, milliseconds, nextNativeRandom, 250, 2, 30, 20);
        switch (decision)
        {
            case 0:
            case 1:
                actor.MovementTicksX = unchecked((short)(unchecked((short)(actor.MovementTicksX + Next(nextNativeRandom) % 40))));
                break;
            case 2:
            case 3:
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

        switch (Next(nextNativeRandom) % 5)
        {
            case 0:
            case 1:
                actor.MovementTicksY = unchecked((short)(unchecked((short)(actor.MovementTicksY + 15))));
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
    }

    public static void Continue(CombatActorState actor, CombatActorState? target, CombatAnimationCursor animation, CombatInfoCharacter info, uint milliseconds, Func<int> nextNativeRandom)
    {
        if (BanditAiDecision.AdvanceMovement(actor, target, animation, milliseconds))
        {
            return;
        }

        int attack = Next(nextNativeRandom) % 2;
        if (!CombatAiAttack.Try(actor, target, animation, info, milliseconds, 30, 85, attack) && actor.Rank > 1)
        {
            CombatAiAttack.Try(actor, target, animation, info, milliseconds, 100, 150, 2);
        }
    }

    private static int Next(Func<int> next)
    {
        int value = next();
        if ((uint)value > 32767)
        {
            throw new InvalidDataException("Enemy AI requires native15-bit RNG.");
        }

        return value;
    }

}
