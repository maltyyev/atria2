namespace Atria.Game.Menu
{
    public readonly record struct MainMenuRetainedSnapshot(int RevealCounter,int Reveal,int Flash);
    public sealed partial class MainMenuController
    {
        public MainMenuRetainedSnapshot CaptureRetained()=>new(revealCounter,Reveal,flash);
        public static void ValidateRetained(MainMenuRetainedSnapshot value)
        {
            if((uint)value.RevealCounter>1 || (uint)value.Reveal>7 || (uint)value.Flash>2)
                throw new InvalidDataException("Retained main menu reveal/flash banks are invalid.");
        }
        public void RestoreRetained(MainMenuRetainedSnapshot value)
        {ValidateRetained(value);revealCounter=value.RevealCounter;Reveal=value.Reveal;flash=value.Flash;}
    }
}
namespace Atria.Game.Credits
{
    public readonly record struct CreditsRetainedSnapshot(int SpeedCounter);
    public sealed partial class CreditsController
    {
        public CreditsRetainedSnapshot CaptureRetained()=>new(SpeedCounter);
        public static void ValidateRetained(CreditsRetainedSnapshot value)
        {if((uint)value.SpeedCounter>10)throw new InvalidDataException("Retained credits speed counter is invalid.");}
        public void RestoreRetained(CreditsRetainedSnapshot value){ValidateRetained(value);SpeedCounter=value.SpeedCounter;}
    }
}
namespace Atria.Game.Termination
{
    public readonly record struct DefeatRetainedSnapshot(int DrawCounter);
    public sealed partial class DefeatController
    {
        public DefeatRetainedSnapshot CaptureRetained()=>new(DrawCounter);
        public static void ValidateRetained(DefeatRetainedSnapshot value)
        {if((uint)value.DrawCounter>20)throw new InvalidDataException("Retained defeat draw counter is invalid.");}
        public void RestoreRetained(DefeatRetainedSnapshot value){ValidateRetained(value);DrawCounter=value.DrawCounter;}
    }
}
