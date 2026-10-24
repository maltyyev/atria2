using Atria.Game.Exploration;
using Atria.Render.Codecs;

namespace Atria.Game.Combat;

public static class CombatAttackDamageMath
{
    private const int MinimumDamage = 1;
    private const int BackstabRollThreshold = 100;
    private const int LastReactionWithoutCritical = 6;
    private const int CriticalMultiplierShift = 4;

    public static int BaseAmount(CombatAttackStats attacker, int attackerType, AnimationFrame attackFrame,
        CombatAttackStats target, int firstRoll, Func<int> nextNativeRandom)
    {
        attackFrame.Validate();
        LegacyRandom.ValidateSample(firstRoll);
        int amount = Math.Max(MinimumDamage,
            unchecked(attackFrame.Damage - target.DefenseBonus - target.Defense + attacker.AttackBonus + attacker.Attack));

        // The original backstab test uses a signed low byte, including negative values from 128..255.
        bool backstab = attackerType == CombatCharacterTypes.Spy
            && unchecked((sbyte)firstRoll) < BackstabRollThreshold
            && target.Facing == attacker.Facing;
        if (!backstab)
        {
            return amount;
        }

        if (attacker.Rank == 0)
        {
            throw new InvalidDataException("Backstab rank divisor is zero.");
        }

        int roll = nextNativeRandom();
        LegacyRandom.ValidateSample(roll);
        return unchecked(amount + roll % attacker.Rank * attacker.Attack);
    }

    public static int CriticalAmount(CombatAttackStats attacker, AnimationFrame attackFrame, int amount, Func<int> nextNativeRandom)
    {
        attackFrame.Validate();
        if (attackFrame.Reaction <= LastReactionWithoutCritical)
        {
            return amount;
        }

        int roll = nextNativeRandom();
        LegacyRandom.ValidateSample(roll);
        int chance = unchecked(attacker.CriticalBonus + attacker.CriticalChance);
        int multiplier = (chance >> CriticalMultiplierShift) + 1;
        return (roll & byte.MaxValue) < chance ? unchecked(multiplier * amount) : amount;
    }
}
