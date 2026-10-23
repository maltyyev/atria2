using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>Actual type9 decision40f4a0 and movement preparation40dc30, after native target selection.</summary>
public static class BanditAiDecision
{
    //40e1a0 type9 AD1 branch: timed movement, run-stop gate, then one actual attack-decision roll.
    public static void Continue(byte[] actor,byte[]? selectedTarget,CombatAnimationCursor animation,CombatInfoCharacter info,uint milliseconds,Func<int> nextNativeRandom)
    {
        int now=unchecked((int)milliseconds);
        if(AdvanceMovement(actor,selectedTarget,animation,milliseconds)) return;
        int roll=nextNativeRandom();
        if((uint)roll>32767) throw new InvalidDataException("Bandit AI requires native15-bit RNG.");
        if(selectedTarget is null) return;
        int dx=Absolute(unchecked(I(actor,0x98)-I(selectedTarget,0x98))), dy=Absolute(unchecked(I(actor,0x78)-I(selectedTarget,0x78)));
        if(dy>=14 || I(actor,0x329)>=now || dx>=85 || dx<=30 || I(actor,0x1ec)<=0) return;
        if(I(actor,0xb1)==2) { animation.BeginAutomaticIdle(3); return; }
        FollowFacing(actor,selectedTarget,animation,milliseconds);
        W(actor,0x329,unchecked(now+info.Read(0xb08)));
        animation.BeginAttack(roll%2,milliseconds);
    }
    //Shared40def0/40e0d0 stage also precedes party and other enemy AD1 decisions.
    internal static bool AdvanceMovement(byte[] actor,byte[]? selectedTarget,CombatAnimationCursor animation,uint milliseconds)
    {
        int now=unchecked((int)milliseconds);
        if(I(actor,0xb1) is 0 or 1) FollowFacing(actor,selectedTarget,animation,milliseconds);
        if(now>I(actor,0xc9))
        {
            bool moving=true;
            if((animation.CurrentFrame.FinalDword&2)==0)
            {
                int variant=I(actor,0x32d);
                moving=variant switch {0=>Step(actor,true)||Step(actor,false),1=>Step(actor,false)||Step(actor,true),2=>StepBoth(actor),_=>false};
            }
            if(!moving) W(actor,0xad,0);
            W(actor,0xc9,unchecked(now+I(actor,0xc5)));
        }
        if(I(actor,0xad)==0 && I(actor,0xb1)==2) { animation.BeginAutomaticIdle(3); return true; }
        return false;
    }

    public static void Begin(byte[] actor, byte[]? selectedTarget, CombatAnimationCursor animation,
        int minimumY, int maximumY, uint milliseconds, Func<int> nextNativeRandom)
    {
        if(actor.Length!=CombatSceneDefinition.RuntimeRecordSize || selectedTarget is not null && selectedTarget.Length!=actor.Length)
            throw new ArgumentException("Incomplete native bandit decision records.");
        W(actor,0x32d,Next(nextNativeRandom)%2);
        PrepareMovement(actor,selectedTarget,animation,minimumY,maximumY,milliseconds,nextNativeRandom);
        switch(Next(nextNativeRandom)%9)
        {
            case 0: case 1: WS(actor,0xde,unchecked((short)(S(actor,0xde)+50))); break;
            case 2: case 3: case 5: WS(actor,0xde,unchecked((short)(S(actor,0xde)<<1))); break;
            case 4: if(I(actor,0xb1)!=2) W(actor,0x9c,unchecked(-I(actor,0x9c))); break;
            default: animation.BeginAutomaticIdle(0); return;
        }
        if(S(actor,0xde)<0) WS(actor,0xde,0);
        switch(Next(nextNativeRandom)%6)
        {
            case 0: case 1: WS(actor,0xe0,unchecked((short)(S(actor,0xe0)+30))); break;
            case 2: case 3: WS(actor,0xe0,unchecked((short)(S(actor,0xe0)>>1))); break;
            case 4: W(actor,0xa0,unchecked(-I(actor,0xa0))); break;
            case 5: WS(actor,0xe0,unchecked((short)(S(actor,0xe0)<<1))); break;
        }
        if(S(actor,0xe0)<0) WS(actor,0xe0,0);
    }

    internal static void PrepareMovement(byte[] actor,byte[]? target,CombatAnimationCursor animation,int minimumY,int maximumY,uint now,Func<int> next,
        int distanceThreshold=300,int farMode=2,int closeX=20,int closeY=10)
    {
        int previousSpriteWidth=animation.CurrentSprite.Width;
        W(actor,0xb1,1); W(actor,0xad,0); WS(actor,0xd5,0);
        if(target is null) { animation.BeginAutomaticIdle(0); return; }
        int dx=unchecked(I(target,0x98)-I(actor,0x98)), dy=unchecked(I(target,0x78)-I(actor,0x78));
        int ax=Absolute(dx), ay=Absolute(dy), mode=1;
        W(actor,0xad,1);
        if(unchecked(ax+ay)>distanceThreshold)
        { mode=farMode; Turn(actor,dx<0?-1:1,previousSpriteWidth); }
        else if(ax<closeX && ay<closeY)
        {
            int targetX=I(target,0x98);
            int x=targetX<321?unchecked(targetX+Next(next)%40+20):unchecked(targetX-Next(next)%40+20);
            dx=unchecked(x-I(actor,0x98));
            int range=unchecked(maximumY-minimumY);
            if(range==0) throw new InvalidDataException("Bandit arena vertical RNG divisor is zero.");
            dy=unchecked(Next(next)%range-I(actor,0x78)+minimumY);
            Turn(actor,dx<0?-1:1,previousSpriteWidth);
        }
        animation.BeginAutomaticMovement(mode,dx,dy,now);
    }
    internal static void Turn(Span<byte> actor,int direction,int currentSpriteWidth)
    {
        int old=unchecked((sbyte)actor[0xfa]);
        if(old==direction) return;
        if(old==1) W(actor,0x70,unchecked(I(actor,0x70)+currentSpriteWidth));
        else if(old==-1) W(actor,0x70,unchecked(I(actor,0x70)-currentSpriteWidth));
        actor[0xfa]=unchecked((byte)direction);
    }
    internal static void FollowFacing(byte[] actor,byte[]? target,CombatAnimationCursor animation,uint milliseconds)
    {
        int now=unchecked((int)milliseconds);
        if(I(actor,0xad)==0x40 || target is null || unchecked(now-I(actor,0x120))<=999) return;
        W(actor,0x120,now); int targetX=I(target,0x98), x=I(actor,0x98);
        int direction=targetX<x?-1:targetX>x?1:0;
        if(direction==0) return;
        int old=unchecked((sbyte)actor[0xfa]);
        if(old==-direction)
        { Turn(actor,direction,animation.CurrentSprite.Width); animation.BeginMotion(4,milliseconds); }
        actor[0xfa]=unchecked((byte)direction);
    }
    private static bool Step(byte[] actor,bool horizontal)
    {
        int countAt=horizontal?0xde:0xe0; short count=S(actor,countAt);
        if(count==0) return false;
        int positionAt=horizontal?0x70:0x78, speedAt=horizontal?0x9c:0xa0;
        W(actor,positionAt,unchecked(I(actor,positionAt)+I(actor,speedAt))); WS(actor,countAt,unchecked((short)(count-1))); return true;
    }
    private static bool StepBoth(byte[] actor) { bool horizontal=Step(actor,true),vertical=Step(actor,false); return horizontal||vertical; }
    private static int Absolute(int value) { int sign=value>>31; return unchecked((value^sign)-sign); }
    private static int Next(Func<int> next)
    { int value=next(); if((uint)value>32767) throw new InvalidDataException("Bandit AI requires native15-bit RNG."); return value; }
    private static int I(ReadOnlySpan<byte> bytes,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
    private static short S(ReadOnlySpan<byte> bytes,int offset)=>BinaryPrimitives.ReadInt16LittleEndian(bytes[offset..]);
    private static void W(Span<byte> bytes,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(bytes[offset..],value);
    private static void WS(Span<byte> bytes,int offset,short value)=>BinaryPrimitives.WriteInt16LittleEndian(bytes[offset..],value);
}

