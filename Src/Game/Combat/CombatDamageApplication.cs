using System.Buffers.Binary;

namespace Atria.Game.Combat;

public readonly record struct CombatDeathReaction(int HorizontalSpeed, int VerticalSpeed, int Duration, int Gravity, sbyte AttackerFacing);
public readonly record struct CombatDamageResult(int AppliedAmount, bool Lethal);
/// <summary>406b40/406cc0 HP and recovery-field application only; attack calculation/reactions remain separate.</summary>
public static class CombatDamageApplication
{
    public static CombatDamageResult Apply(CombatActorState attacker, int attackerType, CombatActorState target, int targetType, int incomingAmount, Action<CombatDeathReaction> applyActualDeathReaction)
    {
        ArgumentNullException.ThrowIfNull(applyActualDeathReaction);
        if (target.DisplayedHealth < target.Health)
        {
            target.DisplayedHealth = target.Health;
        }

        int amount = ScaleByControlRank(attacker, attackerType, incomingAmount);
        amount = ScaleByControlRank(target, targetType, amount);
        int hp = unchecked(target.Health - amount);
        target.Health = hp;
        if (hp < 1)
        {
            target.Health = 0;
            target.RecoverableHealth = 0;
            applyActualDeathReaction(new(15, 15, 6, 4, unchecked((sbyte)attacker.Facing)));
            return new(amount, true);
        }

        unchecked
        {
            int recovery = target.RecoverableHealth;
            recovery = recovery == 0 ? amount >> 1 : recovery + (amount >> 1) - (amount >> 2);
            if (HasEquipment(target, 40))
            {
                recovery += amount / 10;
            }

            if (HasEquipment(target, 41))
            {
                recovery += amount / 5;
            }

            if (HasEquipment(target, 42))
            {
                recovery += amount / 3;
            }

            target.RecoverableHealth = recovery;
            int timer = target.BaseRecoveryInterval;
            if (HasEquipment(target, 58))
            {
                timer -= 300;
            }
            else if (HasEquipment(target, 59))
            {
                timer -= 200;
            }
            else if (HasEquipment(target, 60))
            {
                timer -= 100;
            }

            target.HealthRecoveryInterval = timer < 0 ? 50 : timer;
        }

        return new(amount, false);
    }

    public static int ScaleByControlRank(CombatActorState actor, int type, int amount)
    {
        if (type >= 8 || actor.ControlledBy != 0)
        {
            return amount;
        }

        return actor.ControlRank switch
        {
            1 => unchecked(amount * 10) / 15,
            2 => amount >> 1,
            3 => amount >> 2,
            4 => 0,
            _ => amount
        };
    }

    private static bool HasEquipment(CombatActorState actor, int id)
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

}
