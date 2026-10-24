namespace Atria.Game.Combat;

/// <summary>40a2a0 visual blink clock and40a370 actual status expiry/revert effects.</summary>
public static class CombatStatusRuntime
{
    private const int WarningDuration = 20_000;
    private const int FastWarningDuration = 12_000;
    private const int UrgentWarningDuration = 6_000;
    private const int BlinkInterval = 1_000;
    private const int FastBlinkInterval = 700;
    private const int UrgentBlinkInterval = 300;
    public static void Advance(CombatSceneRuntime runtime, Span<CombatStatusSlot> globalStatus, uint milliseconds)
    {
        ExpireBank(runtime, -1, globalStatus, milliseconds);
        for (int actorIndex = 0; actorIndex < runtime.Actors.Count; actorIndex++)
        {
            Span<CombatStatusSlot> statuses = runtime.Actors[actorIndex].State.StatusBank.AsSpan();
            ExpireBank(runtime, actorIndex, statuses, milliseconds);
        }
    }

    private static void ExpireBank(CombatSceneRuntime runtime, int owner, Span<CombatStatusSlot> bank, uint milliseconds)
    {
        for (int index = 0; index < bank.Length; index++)
        {
            ref var slot = ref bank[index];
            int deadline = slot.Deadline;
            if (deadline == 0 || unchecked((int)milliseconds) < deadline)
            {
                continue;
            }

            Expire(runtime, owner, ref slot);
            slot = default;
        }
    }

    public static bool AdvanceVisible(ref CombatStatusSlot slot, uint milliseconds)
    {
        int now = unchecked((int)milliseconds), remaining = unchecked(slot.Deadline - now);
        if (slot.BlinkDeadline == 0)
        {
            if (remaining < WarningDuration)
            {
                slot.BlinkDeadline = unchecked(now + BlinkInterval);
                slot.BlinkVisible = 1;
            }

            return true;
        }

        if (slot.BlinkDeadline < now)
        {
            slot.BlinkVisible = slot.BlinkVisible == 0 ? 1 : 0;
            slot.BlinkDeadline = unchecked(now + (remaining < UrgentWarningDuration ? UrgentBlinkInterval : remaining < FastWarningDuration ? FastBlinkInterval : BlinkInterval));
        }

        return slot.BlinkVisible != 0;
    }

    public static void Expire(CombatSceneRuntime runtime, int actorIndex, ref CombatStatusSlot slot)
    {
        int code = slot.Code;
        slot.Code = unchecked(code + 1);
        switch (code)
        {
            case 6:
                Self(runtime, actorIndex, actor => { actor.CriticalBonus = 0; });
                return;
            case 7:
                Self(runtime, actorIndex, actor => { actor.DefenseBonus = 0; });
                return;
            case 10:
                Self(runtime, actorIndex, actor => { actor.Haste = 0; });
                return;
            case 11:
                Self(runtime, actorIndex, actor => { actor.Invulnerable = 0; actor.AttackBonus = 0; actor.CriticalBonus = 0; });
                return;
            case 27:
                Self(runtime, actorIndex, actor => { actor.Invulnerable = 0; actor.Translucent = 0; });
                return;
            case 48:
                Self(runtime, actorIndex, actor => { actor.GuardBonus = 0; });
                return;
            case 51:
                Self(runtime, actorIndex, actor => { actor.AttackBonus = 0; });
                return;
            case 53:
                Self(runtime, actorIndex, actor => { actor.AttackBonus = 0; actor.DefenseBonus = 0; });
                return;
        }

        bool selectedTeam = slot.TeamSide == 1;
        foreach (CombatActorImage image in runtime.Actors)
        {
            CombatActorState a = image.State;
            bool lowTeam = unchecked((sbyte)a.Team) < 100;
            bool selected = selectedTeam ? lowTeam : !lowTeam;
            switch (code)
            {
                case 15:
                    if (selected)
                    {
                        a.Slow = 0;
                    }

                    break;
                case 16:
                case 25:
                    if (selected)
                    {
                        a.AttackBonus = 0;
                    }

                    break;
                case 26:
                    if (selected)
                    {
                        a.DefenseBonus = 0;
                    }

                    break;
                case 29:
                    if (selected)
                    {
                        a.DefenseBonus = 0;
                        a.MagicBarrier = 0;
                    }

                    break;
                case 30:
                    if (selected)
                    {
                        a.DefenseBonus = 0;
                        a.AttackBonus = 0;
                        a.Poison = 0;
                    }

                    break;
                case 31:
                    if (selected)
                    {
                        a.Poison = 0;
                    }

                    break;
                case 34:
                    if (!lowTeam)
                    {
                        a.DefenseBonus = 0;
                        a.AttackBonus = 0;
                        a.CriticalBonus = 0;
                        a.GuardBonus = 0;
                    }

                    break;
                case 39:
                    if (!lowTeam)
                    {
                        a.AttackBonus = 0;
                        a.UndeadAttackBonus = 0;
                    }

                    break;
            }
        }
    }

    private static void Self(CombatSceneRuntime runtime, int actorIndex, Action<CombatActorState> clear)
    {
        if ((uint)actorIndex >= (uint)runtime.Actors.Count)
        {
            throw new InvalidDataException("Self status expiry has no actual actor owner.");
        }

        clear(runtime.Actors[actorIndex].State);
    }

}
