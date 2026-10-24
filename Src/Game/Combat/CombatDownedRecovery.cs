using System.Buffers.Binary;
using Atria.Game.State;

namespace Atria.Game.Combat;

/// <summary>405b90: actual downed/recovery sequence transitions and ordered revival item consumption.</summary>
public static class CombatDownedRecovery
{
    // True means the caller may perform normal animation completion cleanup.
    public static bool Complete(
        CombatActorState actor,
        CombatInfoCharacter info,
        CombatAnimationCursor cursor,
        GameState state,
        Func<int> nextNativeRandom,
        Action<int> playSound,
        Action<int> showConsumedItemNotice,
        Action cycleControlledActor
    )
    {
        actor.FrameInterval = 30;
        if (actor.Motion != 10)
        {
            if (actor.Motion == 11)
            {
                if (actor.Rank == 0)
                {
                    return true;
                }

                actor.Reaction = unchecked(actor.Reaction + 1);
                actor.Motion = 13;
                actor.ActionFlags = 0x840;
                cursor.BindReactionSequence();
                if (info.Type is >= 0 and <= 5)
                {
                    actor.Invulnerable = 80;
                }
                else if (info.Type is 31 or 35 or 36 or 37 or 40 or 41 or 42 or 43 or 46)
                {
                    actor.Invulnerable = 100;
                }

                return false;
            }

            if ((Next(nextNativeRandom) & 3) == 0)
            {
                actor.Invulnerable = 40;
            }
        }

        actor.HorizontalVelocity = actor.HorizontalVelocity >> 2;
        actor.Motion = 11;
        actor.Reaction = 17;
        actor.ActionFlags = 0x840;
        actor.FrameInterval = CombatActorMaintenance.ModifyActionInterval(actor, (Next(nextNativeRandom) & 15) + 50);
        if (actor.Health < 1)
        {
            int item = 61, slot = Find(actor, item);
            if (slot < 0)
            {
                item = 62;
                slot = Find(actor, item);
            }

            if (slot < 0)
            {
                item = 63;
                slot = Find(actor, item);
            }

            if (slot < 0)
            {
                item = 66;
                slot = Find(actor, item);
            }

            if (slot < 0)
            {
                actor.ActionFlags = actor.ActionFlags | (int)CombatActionFlags.Dead;
                actor.DeathFadeTicks = 25;
                actor.Rank = 0;
                actor.FrameInterval = CombatActorMaintenance.ModifyActionInterval(actor, 25);
                playSound(info.DeathSound);
                if (actor.ControlledBy != 0)
                {
                    cycleControlledActor();
                }
            }
            else
            {
                actor.Equipment[slot] = -1;
                actor.Health = item switch
                {
                    61 => 30,
                    62 => 70,
                    _ => actor.MaximumHealth
                };
                if (item == 66)
                {
                    actor.Mana = actor.MaximumMana;
                }

                state.OwnedItems[item] = unchecked(state.OwnedItems[item] - 1);
                showConsumedItemNotice(item switch
                {
                    61 => 0,
                    62 => 1,
                    63 => 2,
                    _ => 3
                });
            }

            if (info.Type < 8 && (actor.ActionFlags & (int)CombatActionFlags.Dead) == 0)
            {
                if (state.AvailableItems[77] != 0)
                {
                    actor.Health = unchecked(actor.Health << 1);
                }

                if (state.AvailableItems[78] != 0)
                {
                    actor.Health = unchecked(actor.Health + 50);
                }
            }

            if (actor.MaximumHealth < actor.Health)
            {
                actor.Health = actor.MaximumHealth;
            }
        }

        cursor.BindReactionSequence();
        return false;
    }

    private static int Find(CombatActorState actor, int item)
    {
        for (int i = 0; i < 6; i++)
        {
            if (actor.Equipment[i] == item)
            {
                return i;
            }
        }

        return -1;
    }

    private static int Next(Func<int> next)
    {
        int value = next();
        if ((uint)value > 32767)
        {
            throw new InvalidOperationException("Combat RNG must supply native15-bit samples.");
        }

        return value;
    }

}
