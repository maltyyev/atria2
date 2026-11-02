using Atria.Core.Resources;
namespace Atria.Core.Presentation.Exploration;

public sealed record WorldRendererSnapshot(int[] Conditional, int RevealFrame, int RevealCounter);
public sealed partial class WorldSceneRenderer
{
    public WorldRendererSnapshot CaptureRuntime() => new((int[])conditional.Clone(), revealFrame, revealCounter);
    public void RestoreRuntime(WorldRendererSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Conditional is null || value.Conditional.Length != NativeObjectCapacity || (uint)value.RevealFrame >= 10
|| (uint)value.RevealCounter >= 3)
        {
            throw new InvalidDataException("Invalid retained world renderer state.");
        }

        value.Conditional.CopyTo(conditional, 0);
        revealFrame = value.RevealFrame;
        revealCounter = value.RevealCounter;
        ready = false;
    }
}
