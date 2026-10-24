using System.Buffers.Binary;

namespace Atria.Game.Combat;

public readonly record struct CombatSpawnResult(int NativeReturn, bool Activated);
/// <summary>407c30/407b90 pending-wave activation. Native return1 does not itself mean an actor activated.</summary>
public static class CombatSpawnController
{
    public static CombatSpawnResult Update(
        CombatActorState actor,
        int encounterCounter,
        int previousEnemyCount,
        int minimumX,
        int maximumX,
        int minimumY,
        int maximumY,
        uint milliseconds,
        Func<int> nextNativeRandom
    )
    {
        if (encounterCounter < actor.SpawnEncounterThreshold)
        {
            return new(0, false);
        }

        int state = actor.SpawnCondition, now = unchecked((int)milliseconds);
        bool activate = state switch
        {
            >= 1 and <= 6 => previousEnemyCount < state * 2 + 2,
            >= 7 and <= 11 => unchecked(actor.SpawnStartTime + (state - 5) * 3000) < now,
            12 => previousEnemyCount == 0,
            _ => false
        };
        if (!activate)
        {
            return new(state is >= 1 and <= 14 ? 1 : 0, false);
        }

        int placement = actor.SpawnPlacement, y = 0;
        if (placement is 1 or 2)
        {
            int range = unchecked(maximumY - minimumY);
            if (range == 0)
            {
                throw new InvalidDataException("Spawn arena RNG divisor is zero.");
            }

            int roll = nextNativeRandom();
            if ((uint)roll > 32767)
            {
                throw new InvalidDataException("Spawn placement requires native15-bit RNG.");
            }

            y = unchecked(roll % range + minimumY);
        }

        actor.SpawnCondition = 0;
        if (placement == 1)
        {
            actor.Depth = y;
            actor.SpriteY = y;
            actor.AnchorX = unchecked(minimumX - 100);
            actor.SpriteX = unchecked(minimumX - 100);
        }
        else if (placement == 2)
        {
            actor.Depth = y;
            actor.AnchorX = unchecked(maximumX + 100);
        }

        return new(1, true);
    }

}
