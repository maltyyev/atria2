using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>413110 exact initial attribute projection; requires the caller's ordered native RNG.</summary>
public static class CombatEnemyAttributes
{
    // Each actor reduces the delay on its shared binding, not on a private copy.
    public static void Initialize(CombatActorState actor, CombatInfoCharacter sharedInfo, int regionIndex, int variant, Func<int> nextNativeRandom, ref int accumulatedAward)
    {
        int kind = sharedInfo.Type;
        //413110 returns before any table access for scripted high-HP enemies, even for an otherwise invalid type.
        if (kind > 7 && actor.Health > 9999)
        {
            return;
        }

        if ((uint)kind >= (uint)CombatAttributeCatalog.Count)
        {
            throw new NotSupportedException("Combat attribute type lies outside byte-established rows0..51.");
        }

        var definition = CombatAttributeCatalog.Get(kind);
        if (kind > 7)
        {
            if ((uint)regionIndex > (uint)(definition.RegionGrowth.Count + 1))
            {
                throw new NotSupportedException("Combat region exceeds the recovered eight incremental attribute columns.");
            }

            unchecked
            {
                int hp = definition.Health, attack = definition.Attack, defense = definition.Defense;
                for (int i = 1; i < regionIndex; i++)
                {
                    var growth = definition.RegionGrowth[i - 1];
                    hp += growth.Health;
                    attack += growth.Attack;
                    defense += growth.Defense;
                }

                if (unchecked((sbyte)actor.Team) > 99)
                {
                    hp <<= 1;
                    attack >>= 1;
                    defense <<= 2;
                }

                actor.Health = hp;
                actor.MaximumHealth = hp;
                actor.Attack = attack;
                actor.Defense = defense;
                actor.CriticalChance = definition.CriticalChance;
                int random = nextNativeRandom();
                if ((uint)random > 32767)
                {
                    throw new InvalidDataException("Combat RNG did not return a native15-bit sample.");
                }

                actor.Rank = random % (regionIndex + 1) + 1;
                actor.MagicRank = definition.MagicRank;
                actor.HealthBarMaximum = hp;
                actor.ManaBarMaximum = actor.MaximumMana;
                accumulatedAward += hp / 4 + definition.AwardBonus;
                int delay = sharedInfo.AttackDelay;
                if (delay > 0)
                {
                    sharedInfo.AttackDelay = Math.Max(0, delay - regionIndex * 200);
                }

                if (variant == 0)
                {
                    hp += hp >> 1;
                    attack += regionIndex / 2;
                }
                else if (variant == 1)
                {
                    hp += hp >> 2;
                    attack += regionIndex / 3;
                }

                actor.Health = hp;
                actor.MaximumHealth = hp;
                actor.HealthBarMaximum = hp;
                actor.Attack = attack;
            }
        }

        actor.BaseRecoveryInterval = definition.RecoveryInterval;
        (int code, int delay, int rank) special = kind switch
        {
            38 => (40, 20000, 11),
            39 => (7, 40000, 12),
            40 => (25, 20000, regionIndex),
            41 => (25, 30000, unchecked(regionIndex + 1)),
            42 => (25, 30000, unchecked(regionIndex + 1)),
            43 => (25, 20000, unchecked(regionIndex + 2)),
            44 => (3, 70000, 12),
            _ => (0, 0, 0)
        };
        if (kind is >= 38 and <= 44)
        {
            actor.SpecialAction = special.code;
            actor.SpecialActionInterval = special.delay;
            actor.Rank = special.rank;
        }
    }
}
