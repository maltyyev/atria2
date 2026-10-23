using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>40df80 shared AI range/cooldown/running attack gate.</summary>
public static class CombatAiAttack
{
    public static bool Try(byte[] actor,byte[]? target,CombatAnimationCursor cursor,CombatInfoCharacter info,
        uint milliseconds,int minimumX,int maximumX,int attackIndex,int minimumRank=0,bool normalAttack=false,bool ignoreDepth=false)
    {
        if(target is null) return false;int now=unchecked((int)milliseconds);
        int dx=Abs(Abs(unchecked(I(actor,0x98)-I(target,0x98)))),dy=Abs(unchecked(I(actor,0x78)-I(target,0x78)));
        if((dy>=14 && !ignoreDepth) || I(actor,0x329)>=now || dx>=maximumX || dx<=minimumX || I(actor,0x1ec)<=minimumRank) return false;
        if(I(actor,0xb1)!=2)
        {
            BanditAiDecision.FollowFacing(actor,target,cursor,milliseconds);W(actor,0x329,unchecked(now+info.Read(0xb08)));
            if(normalAttack) CombatManualControl.Attack(actor,info,cursor,milliseconds);else cursor.BeginAttack(attackIndex,milliseconds);
            return true;
        }
        if(info.Type is >=0 and <=5 or 16 or 28 or 29 or 30 or 31)
        { cursor.BeginAttack(info.Read(0xb20)-1,milliseconds);return true; }
        if(info.Type is not (15 or 34 or 36)) cursor.BeginAutomaticIdle(3);
        return false;
    }
    private static int Abs(int value) { int sign=value>>31;return unchecked((value^sign)-sign); }
    private static int I(byte[] actor,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(offset,4));
    private static void W(byte[] actor,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(actor.AsSpan(offset,4),value);
}
