using System.Text.Json.Serialization;
using Atria.Game.Exploration;

namespace Atria.Game.Dungeon;

public sealed record DungeonVisibilitySnapshot(int[] Values, DungeonTorch[] Torches, int NextTorch, int[] Scratch);
public sealed partial class DungeonVisibility
{
    public DungeonVisibilitySnapshot CaptureRuntime() => new((int[])values.Clone(), GetTorchBank(), nextTorch, GetScratchNeighbors());
    public void RestoreRuntime(DungeonVisibilitySnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Values is not { Length: 100 } || value.Torches is not { Length: 20 } || value.Scratch is not { Length: 9 }
|| (uint)value.NextTorch >= 20)
        {
            throw new InvalidDataException("Invalid retained dungeon visibility bank.");
        }

        value.Values.CopyTo(values, 0);
        value.Torches.CopyTo(torches, 0);
        value.Scratch.CopyTo(scratchNeighbors, 0);
        nextTorch = value.NextTorch;
    }
}

public sealed record DungeonMotionSnapshot(bool Moving, WorldDirection Direction, int IdleFrame, int MovingFrame, int PixelX, int PixelY, uint Deadline);
public sealed partial class DungeonMotion
{
    public DungeonMotionSnapshot CaptureRuntime() => new(Moving, Direction, idleFrame, movingFrame, pixelX, pixelY, deadline);
    public void RestoreRuntime(DungeonMotionSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        // Visibility refresh shares and increments the moving-frame bank even while idle.
        if ((uint)value.Direction > 3 || value.IdleFrame < 0 || value.IdleFrame >= animation.Sequences[(int)value.Direction].Frames.Length
|| value.MovingFrame < 0)
        {
            throw new InvalidDataException("Invalid retained dungeon movement state.");
        }

        Moving = value.Moving;
        Direction = value.Direction;
        idleFrame = value.IdleFrame;
        movingFrame = value.MovingFrame;
        pixelX = value.PixelX;
        pixelY = value.PixelY;
        deadline = value.Deadline;
    }
}

// Accept obsolete properties from old saves without retaining or writing their removed state.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
public readonly record struct DungeonArrivalSnapshot(int FireCountdown);
public sealed partial class DungeonArrival
{
    public DungeonArrivalSnapshot CaptureRuntime() => new(FireCountdown);
    public void RestoreRuntime(DungeonArrivalSnapshot value)
    {
        FireCountdown = value.FireCountdown;
    }
}

public readonly record struct DungeonRasterSnapshot(int MarkerFrame, int MarkerCalls, int TorchFrame, int TorchCalls);
public sealed partial class DungeonSceneRenderer
{
    public DungeonRasterSnapshot CaptureRuntime() => new(markerFrame, markerCalls, torchFrame, torchCalls);
    public void RestoreRuntime(DungeonRasterSnapshot value)
    {
        if ((uint)value.MarkerFrame >= 4 || (uint)value.MarkerCalls >= 5 || (uint)value.TorchFrame >= 6 || (uint)value.TorchCalls >= 3)
        {
            throw new InvalidDataException("Invalid retained dungeon raster clocks.");
        }

        markerFrame = value.MarkerFrame;
        markerCalls = value.MarkerCalls;
        torchFrame = value.TorchFrame;
        torchCalls = value.TorchCalls;
        ready = false;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
public sealed record DungeonControllerSnapshot(
    int Modal,
    int Selection,
    int TorchCost,
    bool ExitPending,
    bool ScriptPending,
    bool SaveGate,
    bool HudVisible,
    bool ActionTileIsSwitch,
    int TreasureKind,
    int TreasureValue,
    int TreasureAmount,
    int TreasureItem,
    byte[] PendingFunction,
    int AmbientCalls,
    int RandomAmbientCalls,
    int TorchAdjustCalls,
    DungeonArrivalSnapshot Arrival,
    DungeonMotionSnapshot Motion,
    DungeonVisibilitySnapshot Visibility
);
public sealed partial class DungeonController
{
    public DungeonMap RuntimeMap => map;

    public DungeonControllerSnapshot CaptureRuntime() => new(
        Modal,
        Selection,
        TorchCost,
        ExitPending,
        ScriptPending,
        SaveGate,
        HudVisible,
        ActionTileIsSwitch,
        TreasureKind,
        TreasureValue,
        TreasureAmount,
        TreasureItem,
        PendingFunction.ToArray(),
        ambientCalls,
        randomAmbientCalls,
        torchAdjustCalls,
        arrival.CaptureRuntime(),
        Motion.CaptureRuntime(),
        Visibility.CaptureRuntime()
    );
    public void RestoreRuntime(DungeonControllerSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Modal is not (0 or 3 or 5 or 6 or 99) || (uint)value.Selection > 1 || value.PendingFunction is null
|| value.PendingFunction.Length > 4096)
        {
            throw new InvalidDataException("Invalid retained dungeon controller state.");
        }
        // Validate child states on detached controller before publishing this instance.
        Motion.RestoreRuntime(value.Motion);
        Visibility.RestoreRuntime(value.Visibility);
        arrival.RestoreRuntime(value.Arrival);
        Modal = value.Modal == 5 ? 0 : value.Modal; // Retired modal ID in pre0.3.5 snapshots.
        Selection = value.Selection;
        TorchCost = value.TorchCost;
        ExitPending = value.ExitPending;
        ScriptPending = value.ScriptPending;
        SaveGate = value.SaveGate;
        HudVisible = value.HudVisible;
        ActionTileIsSwitch = value.ActionTileIsSwitch;
        TreasureKind = value.TreasureKind;
        TreasureValue = value.TreasureValue;
        TreasureAmount = value.TreasureAmount;
        TreasureItem = value.TreasureItem;
        PendingFunction = (byte[])value.PendingFunction.Clone();
        ambientCalls = value.AmbientCalls;
        randomAmbientCalls = value.RandomAmbientCalls;
        torchAdjustCalls = value.TorchAdjustCalls;
    }
}
