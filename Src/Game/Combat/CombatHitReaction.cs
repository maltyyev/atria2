using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>406820/406680/406730/4067e0/404690/42e360 actual hit and lethal launch reactions.</summary>
public static class CombatHitReaction
{
    public static void ParticleReaction(CombatActorImage attacker, CombatActorImage target, int reaction, uint milliseconds, Action<int> playSound)
    {
        CombatActorState a = attacker.State, d = target.State;
        playSound(target.Binding!.Info.HitSound);
        CancelOffsetMotion(target);
        d.FrameIndex = 0;
        d.HitPriority = 0;
        d.Motion = 7;
        // Native tests the OLD reaction before replacing it with the particle's reaction.
        if (d.Reaction < 4)
        {
            d.FrameIndex = unchecked((short)(unchecked((short)attacker.Binding!.Info.ReactionStartFrame)));
        }

        d.Reaction = reaction;
        d.ActionFlags = (int)CombatActionFlags.Busy;
        // Sprite ordinals belong to the victim's reaction animation, never the caster's sprite bank.
        target.Animation!.BindReactionSequence(false, true);
        int now = unchecked((int)milliseconds);
        if (a.Motion != (int)CombatMotionKind.Run)
        {
            a.AnimationDeadline = unchecked(now + a.FrameInterval * 4);
        }

        int delay = attacker.Animation!.CurrentFrame.HitDelay;
        d.AnimationDeadline = delay == 0 ? unchecked(a.FrameInterval << 3) : unchecked(now + delay * 4);
        int y = unchecked(d.Depth - target.Animation.CurrentSprite.Height);
        d.SpriteY = y;
        d.AnchorY = y;
    }

    public static void Normal(CombatActorImage attacker, CombatActorImage target, uint milliseconds, Action<int> playSound, Action declaredActorDeathEffect)
    {
        CombatActorState a = attacker.State, d = target.State;
        var frame = attacker.Animation!.CurrentFrame;
        var info = target.Binding!.Info;
        d.Reaction = frame.Reaction;
        d.FrameIndex = 0;
        if (d.Reaction < 4)
        {
            d.FrameIndex = unchecked((short)(unchecked((short)attacker.Binding!.Info.ReactionStartFrame)));
        }

        if (unchecked(d.Depth - target.Animation!.CurrentSprite.Metadata1 - d.SpriteY) < 9 || d.Reaction > 1)
        {
            CancelOffsetMotion(target);
            int mode = d.Motion;
            if (mode < 10 || mode > 12)
            {
                d.Motion = 7;
            }
            else
            {
                d.Motion = 12;
                d.Reaction = 15;
                playSound(info.KnockdownSound);
            }

            int reaction = d.Reaction;
            if ((uint)reaction >= (uint)info.Reactions.Count)
            {
                throw new InvalidDataException("Combat hit reaction exceeds actual D records.");
            }

            d.ActionFlags = info.Reactions[reaction].Flags;
            d.HitPriority = frame.HitPriorityAfterReaction + 1;
            if ((d.ActionFlags & 0x600) == 0)
            {
                playSound(info.HitSound);
                if (reaction is 4 or 5 or 7)
                {
                    d.HorizontalVelocity = a.Facing == 1 ? frame.KnockbackSpeed : -frame.KnockbackSpeed;
                }
                else if (reaction == 14)
                {
                    d.HorizontalVelocity = frame.KnockbackSpeed;
                    d.VelocityDecay = frame.LaunchVelocityDecay;
                }

                int delta = frame.HorizontalStep * unchecked((sbyte)a.Facing);
                d.AnchorX = unchecked(d.AnchorX + delta);
                d.SpriteX = unchecked(d.SpriteX + delta);
                BindAndDelay(attacker, target, milliseconds);
            }
            else
            {
                playSound(info.KnockdownSound);
                BindAndDelay(attacker, target, milliseconds);
                Forces(d, frame.KnockbackSpeed, frame.LaunchVelocityDecay, frame.LaunchDuration, frame.LaunchGravity, unchecked((sbyte)a.Facing));
            }
        }
        else
        {
            Launch(attacker, target, new(10, 10, 5, 4, unchecked((sbyte)a.Facing)), milliseconds, playSound, declaredActorDeathEffect);
        }

        ClampGround(target);
        CancelOffsetMotion(target);
    }

    public static void Launch(
        CombatActorImage attacker,
        CombatActorImage target,
        CombatDeathReaction force,
        uint milliseconds,
        Action<int> playSound,
        Action declaredActorDeathEffect
    )
    {
        CombatActorState d = target.State;
        playSound(target.Binding!.Info.KnockdownSound);
        CancelOffsetMotion(target);
        d.Motion = 8;
        d.Reaction = 8;
        d.FrameIndex = 0;
        d.HitPriority = 3;
        d.ActionFlags = 0x300;
        BindAndDelay(attacker, target, milliseconds);
        Forces(d, force.HorizontalSpeed, force.VerticalSpeed, force.Duration, force.Gravity, force.AttackerFacing);
        ClampGround(target);
        if (d.Health < 1)
        {
            if (d.HasDeathEffect != 0)
            {
                d.FrameInterval = unchecked(d.FrameInterval << 1);
                declaredActorDeathEffect();
            }
            else if (target.Binding.Info.Type < 6)
            {
                d.FrameInterval = unchecked(d.FrameInterval << 1);
            }

            d.DisplayedHealth = 0;
        }
    }

    public static void CancelOffsetMotion(CombatActorImage image)
    {
        CombatActorState d = image.State;
        if ((d.ActionFlags & (int)CombatActionFlags.OffsetMotion) == 0)
        {
            return;
        }

        d.AnchorY = d.SpriteY;
        int x = d.SpriteX;
        if (unchecked((sbyte)d.Facing) == -1)
        {
            x = unchecked(x + image.Animation!.CurrentSprite.Width);
        }

        d.AnchorX = x;
    }

    private static void BindAndDelay(CombatActorImage attacker, CombatActorImage target, uint milliseconds)
    {
        target.Animation!.BindReactionSequence(false);
        CombatActorState a = attacker.State, d = target.State;
        int now = unchecked((int)milliseconds);
        if (a.Motion != (int)CombatMotionKind.Run)
        {
            a.AnimationDeadline = unchecked(now + a.FrameInterval * 4);
        }

        int delay = attacker.Animation!.CurrentFrame.HitDelay;
        d.AnimationDeadline = delay == 0 ? unchecked(a.FrameInterval << 3) : unchecked(now + delay * 4);
    }

    private static void Forces(CombatActorState d, int horizontal, int vertical, int duration, int gravity, sbyte facing)
    {
        d.HorizontalVelocity = unchecked(horizontal * facing);
        d.VerticalVelocity = vertical;
        d.LaunchTicks = duration;
        d.VelocityDecay = gravity;
    }

    private static void ClampGround(CombatActorImage image)
    {
        CombatActorState d = image.State;
        int top = unchecked(d.Depth - image.Animation!.CurrentSprite.Height);
        if (top < d.SpriteY)
        {
            d.SpriteY = top;
            d.AnchorY = top;
        }
    }

}
