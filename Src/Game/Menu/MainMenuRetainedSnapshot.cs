namespace Atria.Game.Menu;

public readonly record struct MainMenuRetainedSnapshot(int RevealCounter, int Reveal, int Flash);
public sealed partial class MainMenuController
{
    public MainMenuRetainedSnapshot CaptureRetained() => new(revealCounter, Reveal, flash);
    public static void ValidateRetained(MainMenuRetainedSnapshot value)
    {
        if ((uint)value.RevealCounter > 1 || (uint)value.Reveal > 7 || (uint)value.Flash > 2)
        {
            throw new InvalidDataException("Retained main menu reveal/flash banks are invalid.");
        }
    }

    public void RestoreRetained(MainMenuRetainedSnapshot value)
    {
        ValidateRetained(value);
        revealCounter = value.RevealCounter;
        Reveal = value.Reveal;
        flash = value.Flash;
    }
}
