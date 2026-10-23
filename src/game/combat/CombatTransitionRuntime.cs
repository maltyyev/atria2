namespace Atria.Game.Combat;

public readonly record struct CombatTransitionFrame(int Index,int Top,int Bottom,int Left,int Right,
    bool EntireBlack,bool DrawLabel);

/// <summary>403f70's nineteen serial visual steps. Caller advances only after presenting that step.</summary>
public sealed class CombatTransitionRuntime
{
    public const int FrameCount=19;
    public int PresentedFrames { get; private set; }
    public bool Completed=>PresentedFrames==FrameCount;
    public CombatTransitionFrame Frame
    {
        get
        {
            if(Completed) throw new InvalidOperationException("The battle transition has presented every native step.");
            int n=PresentedFrames,bottom=455-24*n;
            return new(n,24+24*n,bottom,32+32*n,607-32*n,bottom<240,bottom>47);
        }
    }
    public void AfterPresented()
    {
        if(Completed) throw new InvalidOperationException("The battle transition cannot advance past its final presentation.");
        PresentedFrames++;
    }
}
