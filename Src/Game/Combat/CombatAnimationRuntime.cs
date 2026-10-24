using System.Buffers.Binary;
using Atria.Game.State;

namespace Atria.Game.Combat;

public sealed class CombatCameraShake
{
    public int Amplitude
    {
        get; private set;
    }
    public int Remaining
    {
        get; private set;
    }
    public int Phase
    {
        get; private set;
    }

    public void ApplyFrame(uint flags)
    {
        if ((flags & 0x100) != 0)
        {
            Amplitude = 5;
            Remaining = 10;
        }
        else if ((flags & 0x200) != 0)
        {
            Amplitude = 10;
            Remaining = 15;
        }
        else if ((flags & 0x400) != 0)
        {
            Amplitude = 15;
            Remaining = 20;
        }
        else if ((flags & 0x800) != 0)
        {
            Amplitude = 20;
            Remaining = 25;
        }
        else
        {
            return;
        }

        Phase = 0;
    }

    public int AdvanceVisual()
    {
        if (Remaining == 0)
        {
            return 0;
        }

        Phase = unchecked(Phase + 1);
        int offset = (Phase & 3) < 2 ? Amplitude : 0;
        if (Remaining <= Phase)
        {
            Amplitude = 0;
            Remaining = 0;
            Phase = 0;
        }

        return offset;
    }
}

/// <summary>4061b0 ordered frame advance against actual animation, reaction and jump bindings.</summary>
public static class CombatAnimationRuntime
{
    public static void Advance(
        CombatActorImage image,
        GameState state,
        uint milliseconds,
        int visualDetail,
        Func<int> nextNativeRandom,
        Action<int> playSound,
        Action<int> spawnParticle,
        Action<int, int> applyFrameEffect,
        Action<int> showConsumedItemNotice,
        Action cycleControlledActor,
        CombatCameraShake camera
    )
    {
        CombatActorState actor = image.State;
        CombatAnimationCursor cursor = image.Animation ?? throw new InvalidOperationException("Combat actor has no actual animation binding.");
        CombatInfoCharacter info = image.Binding?.Info ?? throw new InvalidOperationException("Combat actor has no actual INF binding.");
        CombatHitEligibility.RebuildRectangles(actor, cursor.CurrentSprite);
        if (actor.JumpStage != 0)
        {
            CombatJumpRuntime.Step(actor, info, cursor, milliseconds);
            return;
        }

        int now = unchecked((int)milliseconds);
        if (actor.AnimationDeadline > now)
        {
            return;
        }

        actor.AnimationDeadline = unchecked(actor.FrameInterval + now);
        CombatAnimationFrameEffects.ApplyDisplacement(actor);
        CombatAnimationFrameEffects.ApplyEvents(actor, info, cursor.CurrentFrame, visualDetail, nextNativeRandom, playSound, spawnParticle, applyFrameEffect);
        if ((actor.ActionFlags & 0x600) != 0)
        {
            CombatAirborneReaction.Advance(actor, cursor, spawnParticle);
            return;
        }

        actor.FrameIndex = unchecked((short)(unchecked((short)(actor.FrameIndex + 1))));
        if (actor.SecondaryEffect != 0)
        {
            cursor.BeginSequenceCommand(actor.SecondaryEffect, 30, milliseconds);
        }

        camera.ApplyFrame(cursor.CurrentFrame.FinalDword);
        actor.HitConnected = 0;
        if (actor.FrameIndex < cursor.SequenceFrameCount)
        {
            return;
        }

        actor.HitPriority = 0;
        actor.FrameIndex = 0;
        if (actor.Motion == 11 && actor.Rank == 0)
        {
            actor.Active = 0;
            return;
        }

        if (actor.Motion == 15)
        {
            CombatJumpRuntime.Transition(actor, info, cursor, 16, milliseconds);
            return;
        }

        int reaction = actor.Reaction;
        if (reaction is 7 or 10 or 13 or 15 or 16 or 17)
        {
            if (!CombatDownedRecovery.Complete(actor, info, cursor, state, nextNativeRandom, playSound, showConsumedItemNotice, cycleControlledActor))
            {
                return;
            }
        }
        else if ((actor.ActionFlags & (int)CombatActionFlags.Command) != 0)
        {
            int anchor = actor.SpriteX;
            if (unchecked((sbyte)actor.Facing) == -1)
            {
                anchor = unchecked(anchor + cursor.CurrentSprite.Width);
            }

            actor.AnchorX = anchor;
        }

        if (actor.ActionFlags != 1)
        {
            actor.ActionFlags = 0;
        }

        actor.SecondaryEffect = 0;
        actor.Reaction = 0;
    }

}
