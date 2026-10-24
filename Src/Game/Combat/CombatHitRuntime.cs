using Atria.Game.Exploration;
using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.Combat;

public enum CombatHitResult
{
    Blocked,
    StatusOnly,
    DamageApplied
}

public sealed class CombatHitState
{
    public int ComboTotal
    {
        get; internal set;
    }
    public int[] LastHitTargets
    {
        get;
    }

    public CombatHitState(int actorCount) => LastHitTargets = Enumerable.Repeat(-1, actorCount).ToArray();
}

/// <summary>406d40 ordered hit/status/guard/reaction/damage flow. Actual normal/death reactions are required.</summary>
public static class CombatHitRuntime
{
    private const int StatusRankOffset = 5;
    private const int SlowMillisecondsPerRank = 2_000;
    private const int PoisonMillisecondsPerRank = 4_000;
    private const int NormalHitDelayMultiplier = 4;
    private const int NormalFallbackDelayMultiplier = 8;
    private const int ResistedHitDelayMultiplier = 8;
    private const int ResistedFallbackDelayMultiplier = 16;
    private const int ChainExtensionMilliseconds = 600;
    private const int ComboWindowMilliseconds = 1_000;
    private const int LastUnprotectedComboHit = 2;
    private const int ComboProtectionTicks = 110;
    public static CombatHitResult Apply(
        CombatSceneRuntime runtime,
        CombatHitState hits,
        int attackerIndex,
        int targetIndex,
        GameState state,
        uint milliseconds,
        bool allowBackGuard,
        Func<int> nextNativeRandom,
        Action<CombatHitParticle> spawnParticle,
        Action<int> playSound,
        Action prepareNormalReaction,
        Action<CombatDeathReaction> applyDeathReaction
    )
    {
        CombatActorImage attacker = runtime.Actors[attackerIndex], target = runtime.Actors[targetIndex];
        CombatActorState a = attacker.State, d = target.State;
        var frame = attacker.Animation!.CurrentFrame;
        int attackerType = attacker.Binding!.Info.Type, targetType = target.Binding!.Info.Type, now = unchecked((int)milliseconds);
        var attackerTraits = CombatHitTraits.For(attackerType);
        var targetTraits = CombatHitTraits.For(targetType);
        if ((d.ActionFlags & (int)CombatActionFlags.Guarding) != 0 && a.Facing != d.Facing && allowBackGuard)
        {
            CombatGuard.KnockBack(attacker, target, milliseconds, spawnParticle, playSound);
            return CombatHitResult.Blocked;
        }

        hits.LastHitTargets[attackerIndex] = targetIndex;
        if (attackerTraits.SlowsTarget)
        {
            // Approved0.3.6 balance change: halve enemy on-hit slow duration, including refreshes.
            CombatStatusSlots.Insert(d.StatusBank, 16, 0, 1, unchecked((a.Rank + StatusRankOffset) * SlowMillisecondsPerRank + now), attackerType, now);
            d.Slow = 1;
        }

        if (attackerTraits.PoisonsTarget)
        {
            d.Poison = 1;
            CombatStatusSlots.Insert(d.StatusBank, 32, 0, 1, unchecked((a.Rank + StatusRankOffset) * PoisonMillisecondsPerRank + now), attackerType, now);
        }

        if (attackerTraits.AllowsStatusOnlyHit && unchecked((short)frame.Damage) == 0)
        {
            d.AnimationDeadline = unchecked(now + a.FrameInterval * NormalHitDelayMultiplier);
            return CombatHitResult.StatusOnly;
        }

        if (CombatGuard.TryBlock(attacker, target, milliseconds, nextNativeRandom, spawnParticle, playSound))
        {
            return CombatHitResult.Blocked;
        }

        int firstRoll = Next(nextNativeRandom);
        if (frame.HitParticle != 0)
        {
            var sprite = target.Animation!.CurrentSprite;
            spawnParticle(new(
                unchecked(sprite.Metadata2 + d.SpriteX),
                d.HurtTop,
                d.Depth,
                frame.HitParticle,
                unchecked((sbyte)a.Facing)
            ));
        }

        int amount = CombatAttackDamageMath.BaseAmount(a.AttackStats, attackerType, frame, d.AttackStats, firstRoll, nextNativeRandom);
        bool ordinary = !targetTraits.IgnoresNormalReaction;
        if (targetTraits.Undead)
        {
            if (a.UndeadAttackBonus != 0)
            {
                amount = unchecked(amount << 1);
            }

            short attackKind = unchecked((short)frame.Reaction);
            if (attackKind is 8 or 11)
            {
                prepareNormalReaction();
                Delay(a, d, frame.HitDelay, now, NormalHitDelayMultiplier, NormalFallbackDelayMultiplier);
                ordinary = false;
            }
            else
            {
                Delay(a, d, frame.HitDelay, now, ResistedHitDelayMultiplier, ResistedFallbackDelayMultiplier);
                ordinary = false;
            }
        }
        else if (targetTraits.MayResistReaction)
        {
            if ((Next(nextNativeRandom) & 3) != 0)
            {
                Delay(a, d, frame.HitDelay, now, ResistedHitDelayMultiplier, ResistedFallbackDelayMultiplier);
                ordinary = false;
            }
        }

        if (ordinary)
        {
            prepareNormalReaction();
            if (frame.EffectDuration < 31)
            {
                Delay(a, d, frame.HitDelay, now, NormalHitDelayMultiplier, NormalFallbackDelayMultiplier);
            }
            else
            {
                int deadline = unchecked(now + frame.EffectDuration);
                a.AnimationDeadline = deadline;
                d.AnimationDeadline = deadline;
            }
        }

        d.Invulnerable = (frame.FinalDword & 0x20) == 0 ? 3 : 10;
        if (a.ChainDeadline != 0)
        {
            a.ChainDeadline = unchecked(a.AnimationDeadline + ChainExtensionMilliseconds);
        }

        if (state.AvailableItems[ItemIds.SilverClothes] != 0 && targetTraits.VulnerableToSilver)
        {
            amount = unchecked(amount << 1);
        }

        int combo = d.ComboDeadline < now ? 0 : unchecked(d.ComboCount + 1);
        d.ComboCount = combo;
        d.ComboDeadline = unchecked(now + ComboWindowMilliseconds);
        if (targetType < CombatCharacterTypes.FirstEnemy && combo > LastUnprotectedComboHit)
        {
            d.Invulnerable = ComboProtectionTicks;
        }
        else
        {
            hits.ComboTotal = unchecked(hits.ComboTotal + combo);
        }

        amount = CombatAttackDamageMath.CriticalAmount(a.AttackStats, frame, amount, nextNativeRandom);
        CombatDamageApplication.Apply(a, attackerType, d, targetType, amount, applyDeathReaction);
        if (a.Equipment.Contains(ItemIds.BloodAmulet) && a.Health < a.MaximumHealth)
        {
            a.Health = unchecked(a.Health + 1);
        }

        return CombatHitResult.DamageApplied;
    }

    private static void Delay(CombatActorState a, CombatActorState d, int explicitDelay, int now, int multiplier, int fallbackShiftMultiplier)
    {
        if (a.Motion != (int)CombatMotionKind.Run)
        {
            a.AnimationDeadline = unchecked(now + a.FrameInterval * multiplier);
        }
        // Native zero-word fallback deliberately omits now.
        d.AnimationDeadline = explicitDelay == 0 ? unchecked(a.FrameInterval * fallbackShiftMultiplier) : unchecked(now + explicitDelay * multiplier);
    }

    private static int Next(Func<int> next)
    {
        int value = next();
        LegacyRandom.ValidateSample(value);

        return value;
    }

}
