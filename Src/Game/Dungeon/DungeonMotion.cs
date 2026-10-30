using System.Buffers.Binary;
using Atria.Game.Exploration;
using Atria.Game.State;
using Atria.Render.Codecs;

namespace Atria.Game.Dungeon;

public readonly record struct DungeonMotionStep(ExplorationPose Pose, bool Arrived, bool PlayWalkSound);
/// <summary>Dungeon41fdd0 ANI travel; explicitly distinct from world41c220 timing/endpoints.</summary>
public sealed partial class DungeonMotion
{
    private readonly GameState state;
    private readonly DungeonMap map;
    private readonly AnimationFile animation;
    private readonly int tileWidth, tileHeight;
    private int idleFrame, movingFrame, pixelX, pixelY;
    private uint deadline;
    public bool Moving
    {
        get; private set;
    }
    public WorldDirection Direction { get; private set; } = WorldDirection.Down;

    /// <summary>4043a0/404170 return1 writes only shared46027c=0.</summary>
    public void ClearMovementAfterBattle() => Moving = false;
    public DungeonMotion(GameState state, DungeonMap map, AnimationFile animation)
    {
        this.state = state;
        this.map = map;
        this.animation = animation;
        var header = map.Context.GetHeader();
        tileWidth = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(21, 4));
        tileHeight = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(25, 4));
        if (animation.Sequences.Length < 8)
        {
            throw new InvalidDataException("Dungeon CHIPCHAR requires8 sequences.");
        }

        for (int i = 0; i < 8; i++)
        {
            if (animation.Sequences[i].Frames.Length < (i < 4 ? 1 : 10))
            {
                throw new InvalidDataException("Dungeon ANI frame range missing.");
            }
        }
    }

    public bool Begin(WorldDirection direction)
    {
        if ((uint)direction > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        if (Moving)
        {
            return false;
        }

        int x = state.WorldX + (direction == WorldDirection.Left ? -1 : direction == WorldDirection.Right ? 1 : 0);
        int y = state.WorldY + (direction == WorldDirection.Up ? -1 : direction == WorldDirection.Down ? 1 : 0);
        if (!map.CanEnter(x, y))
        {
            return false;
        }

        Direction = direction;
        Moving = true;
        movingFrame = 0;
        pixelX = unchecked(tileWidth * state.WorldX);
        pixelY = unchecked(tileHeight * state.WorldY);
        if (direction == WorldDirection.Up)
        {
            pixelY = unchecked(pixelY - 10);
        }
        else if (direction == WorldDirection.Right)
        {
            pixelY = unchecked(pixelY - 2);
        }

        return true;
    }

    //420bc0 increments the shared moving-frame cursor before41fdd0 on a visibility refresh pass.
    public void ApplyVisibilityRefreshFrameIncrement() => movingFrame++;
    public void DelayMovement(uint pausedMilliseconds)
    {
        if (Moving)
            deadline = unchecked(deadline + pausedMilliseconds);
    }

    public DungeonMotionStep Advance(Func<uint> getTickCount)
    {
        int d = (int)Direction, tx = unchecked(tileWidth * state.WorldX), ty = unchecked(tileHeight * state.WorldY);
        if (!Moving)
        {
            int x = tx + 24 + (d == 1 ? 4 : 0);
            var pose = new ExplorationPose(d, idleFrame, x, ty - 10);
            deadline = unchecked(deadline + 1);
            if (unchecked((int)deadline) > 5)
            {
                deadline = 0;
                idleFrame++;
            }

            if (idleFrame >= animation.Sequences[d].Frames.Length)
            {
                idleFrame = 0;
            }

            return new(pose, false, false);
        }

        int dy = d == 1 ? 2 : d == 2 ? 6 : 0;
        var movingPose = new ExplorationPose(d + 4, movingFrame, pixelX + 24, pixelY + dy - 10);
        if (deadline < getTickCount())
        {
            deadline = unchecked(getTickCount() + 25);
            movingFrame++;
        }

        bool sound = false;
        if (movingFrame > 9)
        {
            sound = true;
            var last = animation.Sequences[d + 4].Frames[movingFrame - 1];
            if (d == 0)
            {
                pixelX = unchecked(pixelX + last.OffsetX - 2);
            }
            else if (d == 1)
            {
                pixelX = unchecked(pixelX + last.OffsetX);
            }
            else
            {
                pixelY = unchecked(pixelY + last.OffsetY);
            }

            movingFrame = 0;
        }

        var frame = animation.Sequences[d + 4].Frames[movingFrame];
        bool done = d switch
        {
            0 => unchecked(pixelX + frame.OffsetX) <= unchecked(tx - 64),
            1 => unchecked(pixelX + frame.OffsetX) >= unchecked(tx + 64),
            2 => unchecked(pixelY + frame.OffsetY + dy) <= unchecked(ty - 48),
            3 => unchecked(pixelY + frame.OffsetY) >= unchecked(ty + 48),
            _ => false
        };
        if (!done)
        {
            return new(movingPose, false, sound);
        }

        Moving = false;
        if (d == 0)
        {
            state.WorldX--;
            movingFrame = 1;
        }
        else if (d == 1)
        {
            state.WorldX++;
            movingFrame = 1;
        }
        else if (d == 2)
        {
            state.WorldY--;
        }
        else
        {
            state.WorldY++;
        }
        //41fdd0 retains idleFrame46dc90; only46dc94 gets1 for horizontal completion.
        return new(movingPose, true, sound);
    }
}
