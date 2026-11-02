namespace Atria.Core.Presentation.Dungeon;

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
