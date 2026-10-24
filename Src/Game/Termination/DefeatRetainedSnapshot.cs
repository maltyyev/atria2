namespace Atria.Game.Termination;

public readonly record struct DefeatRetainedSnapshot(int DrawCounter);
public sealed partial class DefeatController
{
    public DefeatRetainedSnapshot CaptureRetained() => new(DrawCounter);
    public static void ValidateRetained(DefeatRetainedSnapshot value)
    {
        if ((uint)value.DrawCounter > 20)
        {
            throw new InvalidDataException("Retained defeat draw counter is invalid.");
        }
    }

    public void RestoreRetained(DefeatRetainedSnapshot value)
    {
        ValidateRetained(value);
        DrawCounter = value.DrawCounter;
    }
}
