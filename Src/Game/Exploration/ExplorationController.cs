using System.Buffers.Binary;
using Atria.Game.State;
using Atria.Game.World;
using Atria.Render.Codecs;

namespace Atria.Game.Exploration;

public enum WorldDirection
{
    Left,
    Right,
    Up,
    Down
}

public enum TravelStartResult
{
    Blocked,
    NeedsReveal,
    Started
}

public readonly record struct ExplorationInput(bool Left = false, bool Right = false, bool Up = false, bool Down = false, bool Confirm = false, bool Cancel = false);
public enum ExplorationInputEffect
{
    None,
    TravelStartedPlayWalk12,
    RevealMode310ClearDirection,
    WorldTransitionPending,
    EnterDungeon330,
    RefreshWorld,
    ActionDialogue,
    OpenStatus
}

public readonly record struct ExplorationPose(int Sequence, int Frame, int X, int Y);
public sealed record ExplorationAnimationStep(ExplorationPose Pose, bool Arrived, bool PlayWalkSound, ExplorationEffect? Effect);
/// <summary>41bfe0/41c220 recovered animation-driven travel. Caller supplies original logical passes and tick observations.</summary>
public sealed partial class ExplorationController
{
    private static ReadOnlySpan<int> IdleX => [0, 4, 0, 0]; //44d378
    private static ReadOnlySpan<int> MovingY => [0, 2, 6, 0]; //44d388

    private readonly GameState state;
    private readonly WorldMapContext map;
    private readonly AnimationFile animation;
    private readonly ExplorationArrival arrival;
    private readonly Func<int, bool> isSceneReferenceBlocked;
    private readonly int tileWidth, tileHeight;
    private int idleFrame, movingFrame, pixelX, pixelY;
    private uint movementDeadline;
    public bool Moving
    {
        get; private set;
    }
    public WorldDirection Direction
    {
        get; private set;
    }
    public int RevealX
    {
        get; private set;
    }
    public int RevealY
    {
        get; private set;
    }
    public int TravelSteps => arrival.TravelSteps;
    public int InputDelay
    {
        get; set;
    }
    public bool RefreshPending
    {
        get; set;
    }

    /// <summary>4043a0/404170 return1 writes only shared46027c=0.</summary>
    public void ClearMovementAfterBattle() => Moving = false;

    public void DelayMovement(uint pausedMilliseconds)
    {
        if (Moving)
            movementDeadline = unchecked(movementDeadline + pausedMilliseconds);
    }
    public ExplorationController(
        GameState state,
        WorldMapContext map,
        AnimationFile animation,
        LegacyRandom random,
        Func<int, bool> isSceneReferenceBlocked,
        WorldDirection direction = WorldDirection.Down,
        int travelSteps = 0
    )
    {
        this.state = state;
        this.map = map;
        this.animation = animation;
        this.isSceneReferenceBlocked = isSceneReferenceBlocked ?? throw new ArgumentNullException(nameof(isSceneReferenceBlocked));
        if ((uint)direction > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        Direction = direction;
        byte[] header = map.GetHeader();
        tileWidth = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(21, 4));
        tileHeight = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(25, 4));
        if (animation.Sequences.Length < 8)
        {
            throw new InvalidDataException("CHIPCHAR requires four idle and four movement sequences.");
        }

        for (int i = 0; i < 8; i++)
        {
            if (animation.Sequences[i].Frames.Length < (i < 4 ? 1 : 10))
            {
                throw new InvalidDataException("World animation lacks required frame range.");
            }
        }

        arrival = new(state, map, random, travelSteps);
    }

    /// <summary>41ba20 input priority. Caller fulfills returned modal/transition effects before advancing unrelated state.</summary>
    public ExplorationInputEffect HandleInput(ExplorationInput input)
    {
        if (InputDelay != 0)
        {
            InputDelay = unchecked(InputDelay - 1);
            return ExplorationInputEffect.None;
        }

        if (state.ChangedChip != 0)
        {
            return ExplorationInputEffect.WorldTransitionPending;
        }

        if (state.DungeonEntryPending != 0 && state.ScriptActive == 0)
        {
            state.DungeonEntryPending = 0;
            return ExplorationInputEffect.EnterDungeon330;
        }

        if (RefreshPending)
        {
            ExplorationRules.MarkArrival(map, state.WorldX, state.WorldY);
            RefreshPending = false;
            return ExplorationInputEffect.RefreshWorld;
        }

        if (Moving)
        {
            return ExplorationInputEffect.None;
        }

        WorldDirection? direction = input.Left ? WorldDirection.Left : input.Right ? WorldDirection.Right : input.Up ? WorldDirection.Up : input.Down ? WorldDirection.Down : null;
        if (direction is { } d)
        {
            return BeginTravel(d) switch
            {
                TravelStartResult.Started => ExplorationInputEffect.TravelStartedPlayWalk12,
                TravelStartResult.NeedsReveal => ExplorationInputEffect.RevealMode310ClearDirection,
                _ => ExplorationInputEffect.None
            };
        }

        if (input.Confirm)
        {
            return ExplorationInputEffect.ActionDialogue;
        }

        if (input.Cancel)
        {
            return ExplorationInputEffect.OpenStatus;
        }

        return ExplorationInputEffect.None;
    }

    public TravelStartResult BeginTravel(WorldDirection direction)
    {
        if ((uint)direction > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        if (Moving)
        {
            return TravelStartResult.Blocked;
        }

        int x = state.WorldX, y = state.WorldY;
        if ((uint)x >= 10 || (uint)y >= 10)
        {
            throw new InvalidDataException("World position outside map.");
        }

        int nx = x + (direction == WorldDirection.Left ? -1 : direction == WorldDirection.Right ? 1 : 0);
        int ny = y + (direction == WorldDirection.Up ? -1 : direction == WorldDirection.Down ? 1 : 0);
        if ((uint)nx >= 10 || (uint)ny >= 10)
        {
            return TravelStartResult.Blocked;
        }

        var tile = map.TileBytes.Slice((ny * 10 + nx) * 328, 328);
        if (tile[0] != 1)
        {
            RevealX = x;
            RevealY = y;
            return TravelStartResult.NeedsReveal;
        }

        if (ExplorationRules.Read(tile, 4) == 64)
        {
            return TravelStartResult.Blocked;
        }

        Direction = direction;
        Moving = true;
        movingFrame = 0;
        pixelX = unchecked(tileWidth * x);
        pixelY = unchecked(tileHeight * y);
        if (direction == WorldDirection.Up)
        {
            pixelY = unchecked(pixelY - 10);
        }
        else if (direction == WorldDirection.Right)
        {
            pixelY = unchecked(pixelY - 2);
        }
        // Native41bfe0 does not reset470? deadline46dc98; its previous idle-pass value survives.
        return TravelStartResult.Started;
    }

    public ExplorationAnimationStep AdvanceAnimation(Func<uint> getTickCount)
    {
        ArgumentNullException.ThrowIfNull(getTickCount);
        int d = (int)Direction, tileX = unchecked(tileWidth * state.WorldX), tileY = unchecked(tileHeight * state.WorldY);
        if (!Moving)
        {
            var pose = new ExplorationPose(d, idleFrame, unchecked(tileX + 24 + IdleX[d]), unchecked(tileY - 10));
            movementDeadline = unchecked(movementDeadline + 1);
            if (unchecked((int)movementDeadline) > 5)
            {
                movementDeadline = 0;
                idleFrame++;
            }

            if (idleFrame >= animation.Sequences[d].Frames.Length)
            {
                idleFrame = 0;
            }
            // Same original46dc98 aliases idle counter and movement deadline.
            return new(pose, false, false, null);
        }

        var movingPose = new ExplorationPose(d + 4, movingFrame, unchecked(pixelX + 24), unchecked(pixelY + MovingY[d] - 10));
        if (movementDeadline < getTickCount())
        {
            movementDeadline = unchecked(getTickCount() + 30);
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
            0 => unchecked(pixelX + frame.OffsetX) <= unchecked(tileX - 66),
            1 => unchecked(pixelX + frame.OffsetX) >= unchecked(tileX + 66),
            2 => unchecked(pixelY + frame.OffsetY + MovingY[d]) <= unchecked(tileY - 48),
            3 => unchecked(pixelY + frame.OffsetY) >= unchecked(tileY + 48),
            _ => false
        };
        if (!done)
        {
            return new(movingPose, false, sound, null);
        }

        Moving = false;
        if (d == 0)
        {
            state.WorldX--;
        }
        else if (d == 1)
        {
            state.WorldX++;
        }
        else if (d == 2)
        {
            state.WorldY--;
        }
        else
        {
            state.WorldY++;
        }

        idleFrame = 0;
        return new(movingPose, true, sound, arrival.CompleteStep(isSceneReferenceBlocked));
    }

    /// <summary>Mode310 cursor uses edge-triggered keys. Parent returns mode300 after successful reveal or cancel.</summary>
    public void MoveRevealCursor(WorldDirection direction)
    {
        switch (direction)
        {
            case WorldDirection.Left:
                if (RevealX > 0)
                {
                    RevealX--;
                }

                break;
            case WorldDirection.Right:
                if (RevealX < 9)
                {
                    RevealX++;
                }

                break;
            case WorldDirection.Up:
                if (RevealY > 0)
                {
                    RevealY--;
                }

                break;
            case WorldDirection.Down:
                if (RevealY < 9)
                {
                    RevealY++;
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(direction));
        }
    }

    public bool RevealSelected()
    {
        if (!ExplorationRules.CanReveal(map, state.WorldX, state.WorldY, RevealX, RevealY))
        {
            return false;
        }

        if (!ExplorationRules.Reveal(map, state, RevealX, RevealY))
        {
            return false;
        }

        ExplorationRules.MarkArrival(map, state.WorldX, state.WorldY);
        return true;
    }
}
