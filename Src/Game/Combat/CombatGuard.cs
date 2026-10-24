using System.Buffers.Binary;

namespace Atria.Game.Combat;

public readonly record struct CombatHitParticle(int X, int Y, int Depth, int Id, sbyte Facing);
/// <summary>4063e0 guard probability/sequence transition and406330 knockback effect.</summary>
public static class CombatGuard
{
    public static bool TryBlock(
        CombatActorImage attacker,
        CombatActorImage defender,
        uint milliseconds,
        Func<int> nextNativeRandom,
        Action<CombatHitParticle> spawnParticle,
        Action<int> playSound
    )
    {
        int roll = nextNativeRandom();
        if ((uint)roll > 32767)
        {
            throw new InvalidOperationException("Combat RNG must supply native15-bit samples.");
        }

        CombatActorState a = attacker.State, d = defender.State;
        var ac = attacker.Animation ?? throw new InvalidOperationException("Missing attacker animation.");
        var dc = defender.Animation ?? throw new InvalidOperationException("Missing defender animation.");
        var info = defender.Binding?.Info ?? throw new InvalidOperationException("Missing defender INF.");
        if ((ac.CurrentFrame.FinalDword & 0x10) != 0 || unchecked(info.GuardFailureThreshold - d.GuardBonus) >= roll % 10
|| unchecked(d.Depth - dc.CurrentSprite.Height - 4) > d.SpriteY || d.Motion >= 5)
        {
            return false;
        }

        if ((d.ActionFlags & (int)CombatActionFlags.OffsetMotion) != 0)
        {
            d.AnchorY = d.SpriteY;
            d.AnchorX = d.SpriteX;
        }

        sbyte facing = unchecked((sbyte)d.Facing);
        if (facing == unchecked((sbyte)a.Facing))
        {
            int half = dc.CurrentSprite.Width >> 1;
            d.AnchorX = unchecked(d.AnchorX + (facing == -1 ? -half : half));
            d.Facing = unchecked((byte)-facing);
        }

        dc.SelectGuardSequence();
        var attack = info.Guard;
        d.ActionFlags = attack.ResultFlags;
        d.AnimationDeadline = unchecked((int)milliseconds + d.FrameInterval * 8);
        int interval = CombatActorMaintenance.ModifyActionInterval(d, attack.FrameInterval);
        d.FrameInterval = interval;
        a.AnimationDeadline = unchecked((int)milliseconds + interval * 2);
        KnockBack(attacker, defender, milliseconds, spawnParticle, playSound);
        return true;
    }

    public static void KnockBack(CombatActorImage attacker, CombatActorImage defender, uint milliseconds, Action<CombatHitParticle> spawnParticle, Action<int> playSound)
    {
        CombatActorState a = attacker.State, d = defender.State;
        var sprite = defender.Animation?.CurrentSprite ?? throw new InvalidOperationException("Missing defender sprite.");
        sbyte facing = unchecked((sbyte)a.Facing);
        d.Motion = 14;
        d.Invulnerable = 80;
        d.HorizontalVelocity = facing * 5;
        int interval = CombatActorMaintenance.ModifyActionInterval(d, 30);
        d.FrameInterval = interval;
        d.AnimationDeadline = unchecked(interval + (int)milliseconds);
        spawnParticle(new(unchecked(sprite.Metadata2 + d.SpriteX), d.HurtTop, d.Depth, 10, facing));
        playSound(3);
    }

}
