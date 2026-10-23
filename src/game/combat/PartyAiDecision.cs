using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>40ef40/40f1d0 party AD0 decisions and40f0d0/40f150 AD1 attacks.</summary>
public static class PartyAiDecision
{
    //False means the native edge-escape branch clears its selected target pointer.
    public static bool Begin(byte[] actor,int type,byte[]? target,CombatAnimationCursor animation,
        int minimumY,int maximumY,uint milliseconds,Func<int> nextNativeRandom)
    {
        if((uint)type>5) throw new ArgumentOutOfRangeException(nameof(type));
        int decision=Next(nextNativeRandom)%5;
        int vertical=Next(nextNativeRandom)%80+(type==0?10:0);
        if((decision&1)!=0) vertical=-vertical;
        int x=I(actor,0x7c),minimumX=type==0?50:20,maximumX=type==0?600:640;
        if(x<minimumX || x>maximumX)
        {
            int dx=x<minimumX?250:-250;
            //40da00: explicit displacement, threshold200/far motion2;32d is retained.
            W(actor,0xb1,1);W(actor,0xad,0);WS(actor,0xd5,0);W(actor,0xad,1);
            int mode=1;
            if(unchecked(Abs(dx)+Abs(vertical))>200)
            { mode=2;BanditAiDecision.Turn(actor,dx<0?-1:1,animation.CurrentSprite.Width); }
            animation.BeginAutomaticMovement(mode,dx,vertical,milliseconds);
            return false;
        }
        W(actor,0x32d,2);
        BanditAiDecision.PrepareMovement(actor,target,animation,minimumY,maximumY,milliseconds,nextNativeRandom,300,2,type==0?30:10,10);
        switch(decision)
        {
            case 0:case 1:WS(actor,0xde,unchecked((short)(S(actor,0xde)+(type==0?40:20))));break;
            case 2:case 3:WS(actor,0xde,unchecked((short)(S(actor,0xde)>>1)));break;
            case 4:if(I(actor,0xb1)!=2) W(actor,0x9c,unchecked(-I(actor,0x9c)));break;
        }
        if(S(actor,0xde)<0) WS(actor,0xde,0);
        switch(Next(nextNativeRandom)%5)
        {
            case 0:case 1:WS(actor,0xe0,unchecked((short)(S(actor,0xe0)+10)));break;
            case 2:case 3:WS(actor,0xe0,unchecked((short)(S(actor,0xe0)>>1)));break;
            case 4:W(actor,0xa0,unchecked(-I(actor,0xa0)));break;
        }
        if(S(actor,0xe0)<0) WS(actor,0xe0,0);
        return true;
    }
    public static void Continue(byte[] actor,int type,byte[]? target,CombatAnimationCursor animation,CombatInfoCharacter info,
        uint milliseconds,Func<int> nextNativeRandom)
    {
        if((uint)type>5) throw new ArgumentOutOfRangeException(nameof(type));
        if(BanditAiDecision.AdvanceMovement(actor,target,animation,milliseconds) || target is null) return;
        int dy=Abs(unchecked(I(actor,0x78)-I(target,0x78))),dx=Abs(Abs(unchecked(I(actor,0x98)-I(target,0x98))));
        if(dy>=14 || dx<=30) return;
        if(dx<(type==0?80:75))
        {
            if(I(actor,0xb1)==2) { animation.BeginAutomaticIdle(3);return; }
            BanditAiDecision.FollowFacing(actor,target,animation,milliseconds);
            CombatManualControl.Attack(actor,info,animation,milliseconds);return;
        }
        if(dx<(type==0?120:110)) BeginRandomVariation(animation,info,milliseconds,nextNativeRandom);
    }
    private static void BeginRandomVariation(CombatAnimationCursor animation,CombatInfoCharacter info,uint milliseconds,Func<int> next)
    {
        int first=info.Read(0xb1c)+1,count=unchecked(info.Read(0xb20)-info.Read(0xb1c)-4);
        if(count<=0 || first<0 || first+count>29) throw new InvalidDataException("Party AI variation selection has no bounded native candidate range.");
        bool valid=false;
        for(int i=first;i<first+count;i++) if(!Diagonal(info.Read(0x160+i*44))) { valid=true;break; }
        if(!valid) throw new InvalidDataException("Party AI variation rejection loop has no non-diagonal candidate.");
        while(true)
        {
            int selected=first+Next(next)%count;
            if(!Diagonal(info.Read(0x160+selected*44))) { animation.BeginAttack(selected,milliseconds);return; }
        }
    }
    private static bool Diagonal(int code)=>code is 1 or 3 or 7 or 9;
    private static int Next(Func<int> next)
    {
        int value=next();if((uint)value>32767) throw new InvalidDataException("Party AI requires native15-bit RNG.");return value;
    }
    private static int Abs(int value) { int sign=value>>31;return unchecked((value^sign)-sign); }
    private static int I(byte[] actor,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(offset,4));
    private static short S(byte[] actor,int offset)=>BinaryPrimitives.ReadInt16LittleEndian(actor.AsSpan(offset,2));
    private static void W(byte[] actor,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(actor.AsSpan(offset,4),value);
    private static void WS(byte[] actor,int offset,short value)=>BinaryPrimitives.WriteInt16LittleEndian(actor.AsSpan(offset,2),value);
}
