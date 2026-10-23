namespace Atria.Game.World
{
    public sealed record ModeStackSnapshot(int Depth,int Current,int[] Slots);
    public sealed partial class ModeStack
    {
        public ModeStackSnapshot CaptureRuntime()=>new(Depth,Current,(int[])modes.Clone());
        public void RestoreRuntime(ModeStackSnapshot value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if(value.Slots is null || value.Slots.Length!=SafeCapacity || value.Depth< -1 || value.Depth>=SafeCapacity)
                throw new InvalidDataException("Invalid retained mode stack.");
            value.Slots.CopyTo(modes,0);Depth=value.Depth;Current=value.Current;
        }
        // Native writer reads46fd14 + depth*4, three DWORDs before the mode bank.
        public int LegacySaveMode()
        {
            int index=Depth-3;
            if((uint)index>=SafeCapacity)throw new InvalidDataException("Save stack does not contain the recovered underlying mode slot.");
            return modes[index];
        }
    }
}
namespace Atria.Game.Exploration
{
    public readonly record struct LegacyRandomSnapshot(uint State,ulong Calls);
    public sealed partial class LegacyRandom
    {
        public LegacyRandomSnapshot CaptureRuntime()=>new(State,Calls);
        public void RestoreRuntime(LegacyRandomSnapshot value){State=value.State;Calls=value.Calls;}
    }
    public sealed record ExplorationSnapshot(bool Moving,WorldDirection Direction,int IdleFrame,int MovingFrame,int PixelX,int PixelY,
        uint MovementDeadline,int RevealX,int RevealY,int TravelSteps,int InputDelay,bool RefreshPending);
    public sealed partial class ExplorationController
    {
        public Atria.Game.World.WorldMapContext RuntimeMap=>map;
        public ExplorationSnapshot CaptureRuntime()=>new(Moving,Direction,idleFrame,movingFrame,pixelX,pixelY,movementDeadline,
            RevealX,RevealY,TravelSteps,InputDelay,RefreshPending);
        public void RestoreRuntime(ExplorationSnapshot value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if((uint)value.Direction>3 || value.IdleFrame<0 || value.IdleFrame>=animation.Sequences[(int)value.Direction].Frames.Length ||
                (uint)value.MovingFrame>=10 || (uint)value.RevealX>=10 || (uint)value.RevealY>=10 || value.TravelSteps!=TravelSteps)
                throw new InvalidDataException("Invalid retained exploration animation state or constructor travel count.");
            Moving=value.Moving;Direction=value.Direction;idleFrame=value.IdleFrame;movingFrame=value.MovingFrame;
            pixelX=value.PixelX;pixelY=value.PixelY;movementDeadline=value.MovementDeadline;RevealX=value.RevealX;RevealY=value.RevealY;
            InputDelay=value.InputDelay;RefreshPending=value.RefreshPending;
        }
    }
    public sealed record WorldRendererSnapshot(int[] Conditional,int RevealFrame,int RevealCounter);
    public sealed partial class WorldSceneRenderer
    {
        public WorldRendererSnapshot CaptureRuntime()=>new((int[])conditional.Clone(),revealFrame,revealCounter);
        public void RestoreRuntime(WorldRendererSnapshot value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if(value.Conditional is null || value.Conditional.Length!=NativeObjectCapacity || (uint)value.RevealFrame>=10 || (uint)value.RevealCounter>=3)
                throw new InvalidDataException("Invalid retained world renderer state.");
            value.Conditional.CopyTo(conditional,0);revealFrame=value.RevealFrame;revealCounter=value.RevealCounter;ready=false;
        }
    }
}
