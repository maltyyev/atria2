using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>408fe0/408ed0/408c20/408d30/405800, with separate main and auxiliary jump frame bindings.</summary>
public static class CombatJumpRuntime
{
    public static bool Begin(CombatActorState actor, CombatInfoCharacter info, CombatAnimationCursor cursor, uint milliseconds)
    {
        int mode = actor.Motion;
        if (mode is < 0 or > 2)
        {
            return false; //408fe0 caller invokes ordinary walking in this branch.
        }

        actor.JumpOriginMotion = mode;
        int previousAnchor = actor.AnchorX, previousMetadata = cursor.CurrentSprite.Metadata2;
        Transition(actor, info, cursor, 15, milliseconds);
        if (mode == 2)
        {
            Transition(actor, info, cursor, 16, milliseconds);
            if (unchecked((sbyte)actor.Facing) == 1)
            {
                actor.AnchorX = unchecked(previousMetadata + previousAnchor - cursor.CurrentSprite.Metadata2);
                actor.SpriteX = actor.AnchorX;
            }
        }

        return true;
    }

    public static void Transition(CombatActorState actor, CombatInfoCharacter info, CombatAnimationCursor cursor, int stage, uint milliseconds)
    {
        int now = unchecked((int)milliseconds);
        actor.JumpStage = stage;
        actor.JumpSequenceIndex = unchecked(actor.JumpSequenceIndex + 1);
        if (stage == 15)
        {
            actor.JumpY = 0;
            actor.JumpX = 0;
            actor.JumpSequenceIndex = 0;
            actor.JumpStage = 0;
            BindMain(actor, info, cursor, 15, now);
            return;
        }

        if (stage == 18)
        {
            actor.JumpStage = 0;
            BindMain(actor, info, cursor, 18, now);
            return;
        }

        if (stage is not (16 or 17))
        {
            return;
        }

        if (stage == 16)
        {
            actor.Motion = (int)CombatMotionKind.Idle;
        }

        int index = actor.JumpSequenceIndex;
        var jump = info.Jumps[index];
        cursor.BindJumpSequence(index, false);
        actor.JumpFrameInterval = jump.FrameInterval;
        actor.JumpAnimationDeadline = unchecked(jump.FrameInterval + now);
        actor.MovementInterval = jump.MovementInterval;
        actor.MovementDeadline = unchecked(jump.MovementInterval + now);
        actor.ActionFlags = jump.Flags;
        actor.JumpRiseTicks = 17;
        int facing = unchecked((sbyte)actor.Facing);
        actor.HorizontalVelocity = actor.JumpOriginMotion switch
        {
            0 => 0,
            1 => facing << 2,
            _ => facing * 5
        };
        if (stage == 16)
        {
            actor.VerticalVelocity = -13;
        }
    }

    public static void Step(CombatActorState actor, CombatInfoCharacter info, CombatAnimationCursor cursor, uint milliseconds)
    {
        int now = unchecked((int)milliseconds);
        if (actor.MovementDeadline <= now)
        {
            actor.AnchorX = unchecked(actor.AnchorX + actor.HorizontalVelocity);
            actor.SpriteX = actor.AnchorX;
            actor.JumpX = actor.SpriteX;
            if (actor.JumpY == 0)
            {
                actor.AnchorY = unchecked(actor.SpriteY + actor.VerticalVelocity);
            }
            else
            {
                actor.AnchorY = unchecked(actor.AnchorY + actor.VerticalVelocity);
            }

            actor.SpriteY = actor.AnchorY;
            actor.JumpY = actor.AnchorY;
            actor.MovementDeadline = unchecked(actor.MovementInterval + now);
            actor.VerticalVelocity = unchecked(actor.VerticalVelocity + 1);
            if (unchecked((sbyte)actor.Facing) == -1)
            {
                actor.SpriteX = unchecked(actor.SpriteX - cursor.CurrentSprite.Width);
            }

            int stage = actor.JumpStage;
            if (stage == 16)
            {
                if (actor.VerticalVelocity == 0)
                {
                    actor.VerticalVelocity = -1;
                }

                actor.JumpRiseTicks = unchecked(actor.JumpRiseTicks - 1);
                if (actor.JumpRiseTicks == 0)
                {
                    Transition(actor, info, cursor, 17, milliseconds);
                    return;
                }
            }

            if (stage is 16 or 17 && actor.Depth <= unchecked(actor.SpriteY + cursor.CurrentSprite.Height))
            {
                actor.SpriteY = unchecked(actor.Depth - cursor.CurrentSprite.Height);
                actor.AnchorY = actor.SpriteY;
                Transition(actor, info, cursor, 18, milliseconds);
                return;
            }
        }

        if (actor.JumpAnimationDeadline <= now)
        {
            short frame = unchecked((short)(actor.JumpFrameIndex + 1));
            if (frame >= cursor.AuxiliaryFrameCount)
            {
                frame--;
            }

            actor.JumpFrameIndex = unchecked((short)(frame));
            actor.JumpAnimationDeadline = unchecked(actor.JumpFrameInterval + now);
            cursor.BindAuxiliaryFrame();
        }

        if (actor.Motion == (int)CombatMotionKind.Idle)
        {
            return;
        }

        if (actor.AnimationDeadline <= now)
        {
            short frame = unchecked((short)(actor.FrameIndex + 1));
            actor.FrameIndex = unchecked((short)(frame));
            actor.AnimationDeadline = unchecked(actor.FrameInterval + now);
            if (frame >= cursor.SequenceFrameCount)
            {
                actor.Motion = (int)CombatMotionKind.Idle;
                actor.FrameIndex = 0;
                return;
            }
        }

        cursor.RebindFromFrameCounter();
        actor.SpriteX = actor.AnchorX;
        actor.SpriteY = actor.AnchorY;
        int x = actor.SpriteX;
        if (unchecked((sbyte)actor.Facing) == -1)
        {
            x = unchecked(x - cursor.CurrentSprite.Width - cursor.CurrentFrame.OffsetX);
        }
        else
        {
            x = unchecked(x + cursor.CurrentFrame.OffsetX);
        }

        actor.SpriteX = x;
        actor.SpriteY = unchecked(actor.SpriteY + cursor.CurrentFrame.OffsetY);
    }

    private static void BindMain(CombatActorState actor, CombatInfoCharacter info, CombatAnimationCursor cursor, int mode, int now)
    {
        int index = actor.JumpSequenceIndex;
        var jump = info.Jumps[index];
        cursor.BindJumpSequence(index, true);
        actor.Motion = mode;
        actor.ActionFlags = (int)CombatActionFlags.Busy;
        actor.ActionSpriteHeight = cursor.CurrentSprite.Height;
        actor.FrameInterval = jump.FrameInterval;
        actor.AnimationDeadline = unchecked(jump.FrameInterval + now);
    }

}
