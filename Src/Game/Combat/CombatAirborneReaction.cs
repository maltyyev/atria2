namespace Atria.Game.Combat;

/// <summary>405300 AD600 reaction frame, flight, fall and impact transition.</summary>
public static class CombatAirborneReaction
{
    public static void Advance(CombatActorState actor, CombatAnimationCursor cursor, Action<int> spawnParticle)
    {
        short index = actor.FrameIndex;
        if (index < cursor.SequenceFrameCount - 1)
        {
            actor.FrameIndex = unchecked((short)(unchecked((short)(index + 1))));
            cursor.RebindFromFrameCounter();
        }

        actor.SpriteX = unchecked(actor.SpriteX + actor.HorizontalVelocity);
        if ((actor.ActionFlags & (int)CombatActionFlags.Falling) == 0)
        {
            actor.SpriteY = unchecked(actor.SpriteY - actor.VerticalVelocity);
            actor.LaunchTicks = unchecked(actor.LaunchTicks - 1);
            actor.VerticalVelocity = unchecked(actor.VerticalVelocity - actor.VelocityDecay);
            if (actor.VerticalVelocity < 0)
            {
                actor.LaunchTicks = 0;
                actor.VerticalVelocity = 0;
            }

            if (actor.LaunchTicks < 1)
            {
                actor.Reaction = unchecked(actor.Reaction + 1);
                cursor.BindReactionSequence(false);
                actor.ActionSpriteHeight = cursor.CurrentSprite.Height;
                actor.ActionFlags = actor.ActionFlags | (int)CombatActionFlags.Falling;
                actor.FrameIndex = 0;
            }

            return;
        }

        actor.SpriteY = unchecked(actor.SpriteY + actor.VerticalVelocity);
        actor.VerticalVelocity = unchecked(actor.VerticalVelocity + actor.VelocityDecay);
        if (unchecked(actor.Depth - cursor.CurrentSprite.Height) > actor.SpriteY)
        {
            return;
        }

        spawnParticle(24);
        actor.Reaction = unchecked(actor.Reaction + 1);
        actor.ActionFlags = 0x940;
        actor.Motion = 10;
        actor.FrameIndex = 0;
        cursor.BindReactionSequence(false);
        actor.ActionSpriteHeight = cursor.CurrentSprite.Height;
        actor.AnchorX = actor.SpriteX;
        actor.SpriteY = unchecked(actor.Depth - cursor.CurrentSprite.Height);
        if (unchecked((sbyte)actor.Facing) == -1)
        {
            actor.AnchorX = unchecked(actor.AnchorX + cursor.CurrentSprite.Width);
        }
    }

}
