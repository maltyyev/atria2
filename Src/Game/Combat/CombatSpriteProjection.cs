using System.Buffers.Binary;
using Atria.Render.Codecs;

namespace Atria.Game.Combat;

/// <summary>404ee0 coordinate stage after actual input/AI and frame selection; no guessed animation progression.</summary>
public static class CombatSpriteProjection
{
    internal static int FlightHeight(int type, int motion) => motion is >= 8 and <= 13 ? 0 : type switch
    {
        19 => 80,
        45 => 110,
        _ => 0
    };
    public static void Update(CombatActorState actor, int type, AnimationFrame frame, SpriteFrame sprite)
    {
        if (frame.Words.Length != Atria.Render.Codecs.AnimationFrame.WordCount)
        {
            throw new ArgumentException("Incomplete actual ANI frame.");
        }

        unchecked
        {
            int mode = actor.Motion, jump = actor.JumpStage, y = actor.Depth;
            if (jump is 16 or 17)
            {
                if (y < actor.SpriteY + sprite.Height)
                {
                    actor.AnchorY = y - sprite.Height;
                    actor.SpriteY = y - sprite.Height;
                }

                return;
            }

            int projectedY;
            switch (mode)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                    if (actor.FrameIndex == 0)
                    {
                        projectedY = frame.OffsetY - sprite.Height + y;
                        actor.SequenceOriginY = projectedY;
                        actor.SequenceOriginDepth = y;
                    }
                    else
                    {
                        projectedY = frame.OffsetY - actor.SequenceOriginDepth + actor.SequenceOriginY + y;
                    }

                    break;
                case 11:
                case 12:
                case 14:
                    projectedY = y - sprite.Metadata1;
                    break;
                default:
                    projectedY = type == 19 || (actor.ActionFlags & (int)CombatActionFlags.OffsetMotion) != 0 ? frame.OffsetY - actor.ActionSpriteHeight + y : y - sprite.Height;
                    break;
            }

            actor.SpriteY = projectedY;
            int x = actor.AnchorX, projectedX;
            if (actor.ActionFlags == 0x940)
            {
                projectedX = actor.HorizontalVelocity < 0 ? frame.OffsetX + x : x - frame.OffsetX;
                if (unchecked((sbyte)actor.Facing) == -1)
                {
                    projectedX -= sprite.Width;
                }
            }
            else
            {
                projectedX = actor.Facing == 1 ? frame.OffsetX + x : x - sprite.Width - frame.OffsetX;
            }

            actor.SpriteX = projectedX;
            actor.SpriteY = projectedY - FlightHeight(type, mode);
        }
    }

    //407e10 performs this after bounds/animation handling, rather than inside404ee0.
    public static void UpdateTrackingX(CombatActorState actor, SpriteFrame sprite)
    {
        actor.TrackingX = unchecked(sprite.Metadata2 + actor.SpriteX);
    }

}
