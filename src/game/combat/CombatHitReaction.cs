using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>406820/406680/406730/4067e0/404690/42e360 actual hit and lethal launch reactions.</summary>
public static class CombatHitReaction
{
    public static void ParticleReaction(CombatActorImage attacker,CombatActorImage target,int reaction,uint milliseconds,Action<int> playSound)
    {
        byte[] a=attacker.Fields,d=target.Fields;
        playSound(target.Binding!.Info.Read(0xad0)); CancelOffsetMotion(target); WS(d,0xd5,0); W(d,0x11c,0);W(d,0xb1,7);
        // Native tests the OLD reaction before replacing it with the particle's reaction.
        if(I(d,0xf6)<4) WS(d,0xd5,unchecked((short)attacker.Binding!.Info.Read(0xaf8)));
        W(d,0xf6,reaction);W(d,0xad,0x40);target.Animation!.BindReactionSequence(false,true);
        //406540 indexes the target SPR with the attacker's bound-frame sprite ordinal.
        target.Animation.BindDisplayedSpriteIndex(attacker.Animation!.CurrentFrame.SpriteFrameIndex);
        int now=unchecked((int)milliseconds);
        if(I(a,0xb1)!=2) W(a,0xbd,unchecked(now+I(a,0xc1)*4));
        int delay=attacker.Animation.CurrentFrame.Words[15];W(d,0xbd,delay==0?unchecked(I(a,0xc1)<<3):unchecked(now+delay*4));
        int y=unchecked(I(d,0x78)-target.Animation.CurrentSprite.Height);W(d,0x80,y);W(d,0x74,y);
    }
    public static void Normal(CombatActorImage attacker, CombatActorImage target, uint milliseconds,
        Action<int> playSound, Action declaredActorDeathEffect)
    {
        byte[] a=attacker.Fields,d=target.Fields;
        var frame=attacker.Animation!.CurrentFrame;
        var info=target.Binding!.Info;
        W(d,0xf6,frame.Words[23]); WS(d,0xd5,0);
        if(I(d,0xf6)<4) WS(d,0xd5,unchecked((short)attacker.Binding!.Info.Read(0xaf8)));
        if(unchecked(I(d,0x78)-target.Animation!.CurrentSprite.Metadata1-I(d,0x80))<9 || I(d,0xf6)>1)
        {
            CancelOffsetMotion(target);
            int mode=I(d,0xb1);
            if(mode<10 || mode>12) W(d,0xb1,7);
            else { W(d,0xb1,12); W(d,0xf6,15); playSound(info.Read(0xad4)); }
            int reaction=I(d,0xf6);
            if((uint)reaction>=(uint)info.Read(0xb24)) throw new InvalidDataException("Combat hit reaction exceeds actual D records.");
            W(d,0xad,info.Read(0x67c+reaction*16)); W(d,0x11c,frame.Words[26]+1);
            if((I(d,0xad)&0x600)==0)
            {
                playSound(info.Read(0xad0));
                if(reaction is 4 or 5 or 7) W(d,0xe6,a[0xfa]==1?frame.Words[14]:-frame.Words[14]);
                else if(reaction==14) { W(d,0xe6,frame.Words[14]); W(d,0xea,frame.Words[12]); }
                int delta=frame.Words[6]*unchecked((sbyte)a[0xfa]);
                W(d,0x70,unchecked(I(d,0x70)+delta)); W(d,0x7c,unchecked(I(d,0x7c)+delta));
                BindAndDelay(attacker,target,milliseconds);
            }
            else
            {
                playSound(info.Read(0xad4)); BindAndDelay(attacker,target,milliseconds);
                Forces(d,frame.Words[14],frame.Words[12],frame.Words[11],frame.Words[13],unchecked((sbyte)a[0xfa]));
            }
        }
        else Launch(attacker,target,new(10,10,5,4,unchecked((sbyte)a[0xfa])),milliseconds,playSound,declaredActorDeathEffect);
        ClampGround(target); CancelOffsetMotion(target);
    }

    public static void Launch(CombatActorImage attacker,CombatActorImage target,CombatDeathReaction force,uint milliseconds,
        Action<int> playSound,Action declaredActorDeathEffect)
    {
        byte[] d=target.Fields;
        playSound(target.Binding!.Info.Read(0xad4)); CancelOffsetMotion(target);
        W(d,0xb1,8); W(d,0xf6,8); WS(d,0xd5,0); W(d,0x11c,3); W(d,0xad,0x300);
        BindAndDelay(attacker,target,milliseconds);
        Forces(d,force.Field0,force.Field1,force.Field2,force.Field3,force.AttackerFacing);
        ClampGround(target);
        if(I(d,0x170)<1)
        {
            if(d[0x11b]!=0) { W(d,0xc1,unchecked(I(d,0xc1)<<1)); declaredActorDeathEffect(); }
            else if(target.Binding.Info.Type<6) W(d,0xc1,unchecked(I(d,0xc1)<<1));
            W(d,4,0);
        }
    }
    public static void CancelOffsetMotion(CombatActorImage image)
    {
        byte[] d=image.Fields;
        if((I(d,0xad)&0x100)==0) return;
        W(d,0x74,I(d,0x80)); int x=I(d,0x7c);
        if(unchecked((sbyte)d[0xfa])==-1) x=unchecked(x+image.Animation!.CurrentSprite.Width);
        W(d,0x70,x);
    }
    private static void BindAndDelay(CombatActorImage attacker,CombatActorImage target,uint milliseconds)
    {
        target.Animation!.BindReactionSequence(false); byte[] a=attacker.Fields,d=target.Fields;
        int now=unchecked((int)milliseconds);
        if(I(a,0xb1)!=2) W(a,0xbd,unchecked(now+I(a,0xc1)*4));
        int delay=attacker.Animation!.CurrentFrame.Words[15];
        W(d,0xbd,delay==0?unchecked(I(a,0xc1)<<3):unchecked(now+delay*4));
    }
    private static void Forces(byte[] d,int horizontal,int vertical,int duration,int gravity,sbyte facing)
    {
        W(d,0xe6,unchecked(horizontal*facing)); W(d,0xe2,vertical); W(d,0xf2,duration); W(d,0xea,gravity);
    }
    private static void ClampGround(CombatActorImage image)
    {
        byte[] d=image.Fields; int top=unchecked(I(d,0x78)-image.Animation!.CurrentSprite.Height);
        if(top<I(d,0x80)) { W(d,0x80,top); W(d,0x74,top); }
    }
    private static int I(byte[] actor,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(offset,4));
    private static void W(byte[] actor,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(actor.AsSpan(offset,4),value);
    private static void WS(byte[] actor,int offset,short value)=>BinaryPrimitives.WriteInt16LittleEndian(actor.AsSpan(offset,2),value);
}
