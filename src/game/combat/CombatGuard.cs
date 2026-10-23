using System.Buffers.Binary;

namespace Atria.Game.Combat;

public readonly record struct CombatHitParticle(int X, int Y, int Depth, int Id, sbyte Facing);

/// <summary>4063e0 guard probability/sequence transition and406330 knockback effect.</summary>
public static class CombatGuard
{
    public static bool TryBlock(CombatActorImage attacker, CombatActorImage defender, uint milliseconds,
        Func<int> nextNativeRandom, Action<CombatHitParticle> spawnParticle, Action<int> playSound)
    {
        int roll=nextNativeRandom();
        if((uint)roll>32767) throw new InvalidOperationException("Combat RNG must supply native15-bit samples.");
        byte[] a=attacker.Fields,d=defender.Fields;
        var ac=attacker.Animation ?? throw new InvalidOperationException("Missing attacker animation.");
        var dc=defender.Animation ?? throw new InvalidOperationException("Missing defender animation.");
        var info=defender.Binding?.Info ?? throw new InvalidOperationException("Missing defender INF.");
        if((ac.CurrentFrame.FinalDword&0x10)!=0 || unchecked(info.Read(0xb04)-I(d,0x2a8))>=roll%10 ||
            unchecked(I(d,0x78)-dc.CurrentSprite.Height-4)>I(d,0x80) || I(d,0xb1)>=5) return false;
        if((I(d,0xad)&0x100)!=0) { W(d,0x74,I(d,0x80)); W(d,0x70,I(d,0x7c)); }
        sbyte facing=unchecked((sbyte)d[0xfa]);
        if(facing==unchecked((sbyte)a[0xfa]))
        {
            int half=dc.CurrentSprite.Width>>1;
            W(d,0x70,unchecked(I(d,0x70)+(facing==-1?-half:half))); d[0xfa]=unchecked((byte)-facing);
        }
        dc.SelectGuardSequence(); int at=0x150+info.Read(0xb1c)*0x2c;
        W(d,0xad,info.Read(at+8)); W(d,0xbd,unchecked((int)milliseconds+I(d,0xc1)*8));
        int interval=CombatActorMaintenance.ModifyActionInterval(d,info.Read(at+24)); W(d,0xc1,interval);
        W(a,0xbd,unchecked((int)milliseconds+interval*2));
        KnockBack(attacker,defender,milliseconds,spawnParticle,playSound); return true;
    }

    public static void KnockBack(CombatActorImage attacker,CombatActorImage defender,uint milliseconds,
        Action<CombatHitParticle> spawnParticle,Action<int> playSound)
    {
        byte[] a=attacker.Fields,d=defender.Fields;
        var sprite=defender.Animation?.CurrentSprite ?? throw new InvalidOperationException("Missing defender sprite.");
        sbyte facing=unchecked((sbyte)a[0xfa]);
        W(d,0xb1,14); W(d,0x240,80); W(d,0xe6,facing*5);
        int interval=CombatActorMaintenance.ModifyActionInterval(d,30); W(d,0xc1,interval); W(d,0xbd,unchecked(interval+(int)milliseconds));
        spawnParticle(new(unchecked(sprite.Metadata2+I(d,0x7c)),BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(0x109,2)),I(d,0x78),10,facing));
        playSound(3);
    }
    private static int I(byte[] actor,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(offset,4));
    private static void W(byte[] actor,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(actor.AsSpan(offset,4),value);
}
