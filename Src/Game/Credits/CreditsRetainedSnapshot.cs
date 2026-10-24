namespace Atria.Game.Credits;

public readonly record struct CreditsRetainedSnapshot(int SpeedCounter);
public sealed partial class CreditsController
{
    public CreditsRetainedSnapshot CaptureRetained() => new(SpeedCounter);
    public static void ValidateRetained(CreditsRetainedSnapshot value)
    {
        if ((uint)value.SpeedCounter > 10)
        {
            throw new InvalidDataException("Retained credits speed counter is invalid.");
        }
    }

    public void RestoreRetained(CreditsRetainedSnapshot value)
    {
        ValidateRetained(value);
        SpeedCounter = value.SpeedCounter;
    }
}
