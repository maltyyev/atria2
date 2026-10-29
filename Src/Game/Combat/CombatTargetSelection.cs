using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>411e20 and target-search leaves. Managed target indices use−1 for native null pointers.</summary>
public static class CombatTargetSelection
{
    public static int Select(
        IReadOnlyList<CombatActorState> actors,
        IReadOnlyList<int> currentTargets,
        int attackerIndex,
        int first,
        int end,
        uint milliseconds,
        Func<int> nextNativeRandom,
        int partyStart = 0
    )
    {
        if (partyStart < 0 || partyStart > actors.Count || currentTargets.Count != actors.Count || (uint)attackerIndex >= (uint)actors.Count || first < 0 || end < first
|| end > actors.Count)
        {
            throw new ArgumentException("Incomplete native target-selection domain.");
        }

        CombatActorState attacker = actors[attackerIndex];
        int now = unchecked((int)milliseconds);
        int Next()
        {
            int value = nextNativeRandom();
            if ((uint)value > 32767)
            {
                throw new InvalidDataException("Target selection requires native15-bit RNG.");
            }

            return value;
        }

        if (unchecked((sbyte)attacker.Team) < 100)
        {
            for (int i = partyStart; i < actors.Count; i++)
            {
                if (actors[i].Health > 0 && HasItem(actors[i], 56) && (Next() & 1) != 0)
                {
                    return i;
                }
            }
        }

        if (attacker.TargetStrategyDeadline == 0)
        {
            attacker.TargetStrategy = Next() % 11;
            attacker.TargetStrategyDeadline = unchecked(now + 13000);
        }
        else if (attacker.TargetStrategyDeadline < now)
        {
            int mode = unchecked(attacker.TargetStrategy + 1);
            if (mode > 10)
            {
                mode = 0;
            }

            attacker.TargetStrategy = mode;
            attacker.TargetStrategyDeadline = unchecked(now + 13000);
        }

        int result = attacker.TargetStrategy switch
        {
            1 or 5 or 10 => FindControlled(),
            2 => FindExtreme(a => a.Attack, true),
            3 => FindExtreme(a => a.Attack, false),
            4 or 9 => FindRetaliating(),
            8 => FindExtreme(a => a.Health, true),
            _ => FindNearest()
        };
        if (result < 0)
        {
            result = FindNearest();
        }

        if (result >= 0 && HasItem(actors[result], 55))
        {
            result = -1;
        }

        return result;
        int FindControlled()
        {
            for (int i = first; i < end; i++)
            {
                if (Eligible(attacker, actors[i]) && actors[i].ControlledBy != 0)
                {
                    return i;
                }
            }

            return -1;
        }

        int FindRetaliating()
        {
            for (int i = first; i < end; i++)
            {
                if (Eligible(attacker, actors[i]) && currentTargets[i] == attackerIndex)
                {
                    return i;
                }
            }

            return -1;
        }

        int FindExtreme(Func<CombatActorState, int> valueOf, bool maximum)
        {
            int selected = -1, best = maximum ? 0 : 10000;
            for (int i = first; i < end; i++)
            {
                if (Eligible(attacker, actors[i]))
                {
                    int value = valueOf(actors[i]);
                    if (maximum ? value > best : value < best)
                    {
                        selected = i;
                        best = value;
                    }
                }
            }

            return selected;
        }

        int FindNearest()
        {
            int selected = -1, best = 5000;
            for (int i = first; i < end; i++)
            {
                if (Eligible(attacker, actors[i]))
                {
                    int distance = unchecked(Abs(unchecked(actors[i].TrackingX - attacker.TrackingX)) + Abs(unchecked(actors[i].Depth - attacker.Depth)));
                    if (distance < best)
                    {
                        best = distance;
                        selected = i;
                    }
                }
            }

            return selected;
        }
    }

    public static bool Eligible(CombatActorState attacker, CombatActorState target)
    {
        if (target.SpawnCondition != 0 || target.Health <= 0 || target.Active == 0)
        {
            return false;
        }

        int team = unchecked((sbyte)attacker.Team), other = unchecked((sbyte)target.Team);
        if (team > 99)
        {
            return other < 100;
        }

        return (team == other || target.CanTurn != 0) && team != other;
    }

    private static bool HasItem(CombatActorState actor, int id)
    {
        for (int i = 0; i < 6; i++)
        {
            if (actor.Equipment[i] == id)
            {
                return true;
            }
        }

        return false;
    }

    private static int Abs(int value)
    {
        int sign = value >> 31;
        return unchecked((value ^ sign) - sign);
    }

}
