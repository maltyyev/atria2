using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>40f360 exact enemy type8 decision and40e1a0 continuation.</summary>
public static class EnemyType8AiDecision
{
    public static void Begin(byte[] actor,byte[]? target,CombatAnimationCursor animation,int minimumY,int maximumY,uint milliseconds,Func<int> nextNativeRandom)
    {
        int decision=Next(nextNativeRandom)%8;W(actor,0x32d,0);
        BanditAiDecision.PrepareMovement(actor,target,animation,minimumY,maximumY,milliseconds,nextNativeRandom,250,2,30,20);
        switch(decision)
        {
            case 0:case 1:WS(actor,0xde,unchecked((short)(S(actor,0xde)+Next(nextNativeRandom)%40)));break;
            case 2:case 3:WS(actor,0xde,unchecked((short)(S(actor,0xde)<<1)));break;
            case 4:if(I(actor,0xb1)!=2) W(actor,0x9c,unchecked(-I(actor,0x9c)));break;
            default:animation.BeginAutomaticIdle(0);return;
        }
        if(S(actor,0xde)<0) WS(actor,0xde,0);
        switch(Next(nextNativeRandom)%5)
        {
            case 0:case 1:WS(actor,0xe0,unchecked((short)(S(actor,0xe0)+15)));break;
            case 2:case 3:WS(actor,0xe0,unchecked((short)(S(actor,0xe0)>>1)));break;
            case 4:W(actor,0xa0,unchecked(-I(actor,0xa0)));break;
        }
        if(S(actor,0xe0)<0) WS(actor,0xe0,0);
    }
    public static void Continue(byte[] actor,byte[]? target,CombatAnimationCursor animation,CombatInfoCharacter info,uint milliseconds,Func<int> nextNativeRandom)
    {
        if(BanditAiDecision.AdvanceMovement(actor,target,animation,milliseconds)) return;
        int attack=Next(nextNativeRandom)%2;
        if(!CombatAiAttack.Try(actor,target,animation,info,milliseconds,30,85,attack) && I(actor,0x1ec)>1)
            CombatAiAttack.Try(actor,target,animation,info,milliseconds,100,150,2);
    }
    private static int Next(Func<int> next)
    {
        int value=next();if((uint)value>32767) throw new InvalidDataException("Enemy AI requires native15-bit RNG.");return value;
    }
    private static int I(byte[] actor,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(offset,4));
    private static short S(byte[] actor,int offset)=>BinaryPrimitives.ReadInt16LittleEndian(actor.AsSpan(offset,2));
    private static void W(byte[] actor,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(actor.AsSpan(offset,4),value);
    private static void WS(byte[] actor,int offset,short value)=>BinaryPrimitives.WriteInt16LittleEndian(actor.AsSpan(offset,2),value);
}
