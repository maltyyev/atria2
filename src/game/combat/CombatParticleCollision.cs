using System.Buffers.Binary;
using Atria.Game.State;

namespace Atria.Game.Combat;

public interface ICombatParticleImpactEffects
{
    void Reaction(CombatActorImage owner,CombatActorImage target,int reaction);
    void Launch(CombatActorImage owner,CombatActorImage target,CombatDeathReaction force);
    void Damage(CombatActorImage owner,CombatActorImage target,int amount);
    void Block(CombatActorImage owner,CombatActorImage target);
    void Spawn(CombatHitParticle particle);
    void Expire(CombatParticleState particle);
}

/// <summary>40d100 exact projectile eligibility and damage path, separate from ordinary406d40 attacks.</summary>
public static class CombatParticleCollision
{
    public static int Apply(CombatParticleState particle,IReadOnlyList<CombatActorImage> actors,GameState state,
        Func<bool> magicActive,Func<int> nextNativeRandom,ICombatParticleImpactEffects effects)
    {
        var owner=particle.Owner??throw new InvalidOperationException("Damaging particle has no actual owner.");
        int[] p=particle.Fields;var first=particle.FirstFrame;var sprite=particle.CurrentSprite;
        int x=p[3],y=p[4],width=sprite.Width,height=sprite.Height,velocity=p[10],hits=0;
        foreach(var target in actors)
        {
            byte[] t=target.Fields;
            int difference=unchecked(I(t,0x78)-p[2]);int sign=difference>>31;int distance=unchecked((difference^sign)-sign);
            if(I(t,0x60)!=0||I(t,0x1ec)==0||t[0xa6]==owner.Fields[0xa6]||ReferenceEquals(target,owner)||
                S(t,0x107)>unchecked(x+width)||S(t,0x109)>unchecked(y+height)||x>S(t,0x10b)||y>S(t,0x10d)||
                I(t,0x11c)>=first.Words[9]||distance>p[15]||I(t,0x240)!=0)continue;
            hits++;W(t,0x11c,first.Words[9]);W(t,0x240,15);
            int velocityFacing=velocity>=0?1:-1;
            if((I(t,0xad)&0x80)==0||unchecked((sbyte)t[0xfa])==velocityFacing)
            {
                W(t,0xc1,CombatActorMaintenance.ModifyActionInterval(t,30));
                int reaction=first.Words[23];
                if(reaction<=7)effects.Reaction(owner,target,reaction);
                else effects.Launch(owner,target,new(10,10,6,3,unchecked((sbyte)owner.Fields[0xfa])));
                int amount;
                if(!magicActive())
                {
                    amount=first.Words[8]>>(state.AvailableItems[82]!=0?1:0);
                    if(I(t,0x2bc)!=0)amount>>=1;
                }
                else
                {
                    amount=unchecked(first.Words[8]/3+I(owner.Fields,0x17c)*20);
                    int defense=I(t,0x1c0);if(defense!=0)amount=unchecked(amount+(nextNativeRandom()%defense)*-2);
                    if(amount<0)amount=1;
                    if(!magicActive()&&I(t,0x2bc)!=0)amount>>=1;
                }
                effects.Damage(owner,target,amount);
            }
            else effects.Block(owner,target);
            if(first.Words[16]!=0)
                effects.Spawn(new(p[6]==1?unchecked(p[3]+particle.CurrentSprite.Width):p[3],p[4],p[2],first.Words[16],unchecked((sbyte)p[6])));
            if(first.Words[9]<2){effects.Expire(particle);return hits;}
        }
        return hits;
    }
    private static int I(byte[] bytes,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset,4));
    private static short S(byte[] bytes,int offset)=>BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset,2));
    private static void W(byte[] bytes,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset,4),value);
}
