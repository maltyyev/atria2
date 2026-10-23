using System.Buffers.Binary;
using Atria.Game.State;

namespace Atria.Game.Combat;

public enum CombatHitResult { Blocked, StatusOnly, DamageApplied }
public sealed class CombatHitState
{
    public int ComboTotal { get; internal set; }
    public int[] LastHitTargets { get; }
    public CombatHitState(int actorCount) => LastHitTargets = Enumerable.Repeat(-1, actorCount).ToArray();
}

/// <summary>406d40 ordered hit/status/guard/reaction/damage flow. Actual normal/death reactions are required.</summary>
public static class CombatHitRuntime
{
    public static CombatHitResult Apply(CombatSceneRuntime runtime, CombatHitState hits, int attackerIndex, int targetIndex,
        GameState state, uint milliseconds, bool allowBackGuard, Func<int> nextNativeRandom,
        Action<CombatHitParticle> spawnParticle, Action<int> playSound, Action prepareNormalReaction,
        Action<CombatDeathReaction> applyDeathReaction)
    {
        CombatActorImage attacker = runtime.Actors[attackerIndex], target = runtime.Actors[targetIndex];
        byte[] a = attacker.Fields, d = target.Fields;
        var frame = attacker.Animation!.CurrentFrame;
        int attackerType = attacker.Binding!.Info.Type, targetType = target.Binding!.Info.Type, now = unchecked((int)milliseconds);
        if ((I(d, 0xad) & 0x80) != 0 && a[0xfa] != d[0xfa] && allowBackGuard)
        {
            CombatGuard.KnockBack(attacker, target, milliseconds, spawnParticle, playSound); return CombatHitResult.Blocked;
        }
        hits.LastHitTargets[attackerIndex] = targetIndex;
        if (attackerType is 14 or 23)
        {
            // Approved0.3.6 balance change: halve enemy on-hit slow duration, including refreshes.
            CombatStatusSlots.Insert(d.AsSpan(0x244, 72), 16, 0, 1, unchecked((I(a, 0x1ec) + 5) * 2000 + now), attackerType, now);
            W(d, 0x2ac, 1);
        }
        if (attackerType == 25)
        {
            W(d, 0x2b4, 1);
            CombatStatusSlots.Insert(d.AsSpan(0x244, 72), 32, 0, 1, unchecked((I(a, 0x1ec) + 5) * 4000 + now), attackerType, now);
        }
        if ((attackerType is 13 or 25 or 26) && unchecked((short)frame.Words[8]) == 0)
        {
            W(d, 0xbd, unchecked(now + I(a, 0xc1) * 4)); return CombatHitResult.StatusOnly;
        }
        if (CombatGuard.TryBlock(attacker, target, milliseconds, nextNativeRandom, spawnParticle, playSound)) return CombatHitResult.Blocked;
        int firstRoll = Next(nextNativeRandom);
        if (frame.Words[16] != 0)
        {
            var sprite = target.Animation!.CurrentSprite;
            spawnParticle(new(unchecked(sprite.Metadata2 + I(d, 0x7c)), BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(0x109, 2)), I(d, 0x78), frame.Words[16], unchecked((sbyte)a[0xfa])));
        }
        int amount = CombatAttackDamageMath.BaseAmount(a, attackerType, frame, d, firstRoll, nextNativeRandom);
        bool ordinary = targetType is not (13 or 44 or 45);
        if (targetType is 21 or 22 or 23)
        {
            if (I(a, 0x2b8) != 0) amount = unchecked(amount << 1);
            short attackKind = unchecked((short)frame.Words[23]);
            if (attackKind is 8 or 11) { prepareNormalReaction(); Delay(a, d, frame.Words[15], now, 4, 8); ordinary = false; }
            else { Delay(a, d, frame.Words[15], now, 8, 16); ordinary = false; }
        }
        else if (targetType is 35 or 36 or 37 or 39)
        {
            if ((Next(nextNativeRandom) & 3) != 0) { Delay(a, d, frame.Words[15], now, 8, 16); ordinary = false; }
        }
        if (ordinary)
        {
            prepareNormalReaction();
            if (frame.Words[25] < 31) Delay(a, d, frame.Words[15], now, 4, 8);
            else { int deadline = unchecked(now + frame.Words[25]); W(a, 0xbd, deadline); W(d, 0xbd, deadline); }
        }
        W(d, 0x240, (frame.FinalDword & 0x20) == 0 ? 3 : 10);
        if (I(a, 0xcd) != 0) W(a, 0xcd, unchecked(I(a, 0xbd) + 600));
        if (state.AvailableItems[83] != 0 && (targetType is 12 or 21 or 22 or 23 or 37)) amount = unchecked(amount << 1);
        int combo = I(d, 0x369) < now ? 0 : unchecked(I(d, 0x365) + 1);
        W(d, 0x365, combo); W(d, 0x369, unchecked(now + 1000));
        if (targetType < 8 && combo > 2) W(d, 0x240, 110);
        else hits.ComboTotal = unchecked(hits.ComboTotal + combo);
        amount = CombatAttackDamageMath.CriticalAmount(a, frame, amount, nextNativeRandom);
        CombatDamageApplication.Apply(a, attackerType, d, targetType, amount, applyDeathReaction);
        if (Has(a, 57) && I(a, 0x170) < I(a, 0x16c)) W(a, 0x170, unchecked(I(a, 0x170) + 1));
        return CombatHitResult.DamageApplied;
    }
    private static void Delay(byte[] a, byte[] d, int explicitDelay, int now, int multiplier, int fallbackShiftMultiplier)
    {
        if (I(a, 0xb1) != 2) W(a, 0xbd, unchecked(now + I(a, 0xc1) * multiplier));
        // Native zero-word fallback deliberately omits now.
        W(d, 0xbd, explicitDelay == 0 ? unchecked(I(a, 0xc1) * fallbackShiftMultiplier) : unchecked(now + explicitDelay * multiplier));
    }
    private static bool Has(byte[] actor, int item)
    {
        for (int i = 0; i < 6; i++) if (I(actor, 0x208 + i * 4) == item) return true;
        return false;
    }
    private static int Next(Func<int> next)
    {
        int value = next(); if ((uint)value > 32767) throw new InvalidOperationException("Combat RNG must supply native15-bit samples."); return value;
    }
    private static int I(byte[] actor, int offset) => BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(offset, 4));
    private static void W(byte[] actor, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(actor.AsSpan(offset, 4), value);
}
