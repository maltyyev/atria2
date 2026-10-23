using System.Buffers.Binary;

namespace Atria.Game.Combat;

public readonly record struct CombatSpawnResult(int NativeReturn, bool Activated);

/// <summary>407c30/407b90 pending-wave activation. Native return1 does not itself mean an actor activated.</summary>
public static class CombatSpawnController
{
    public static CombatSpawnResult Update(Span<byte> actor,int encounterCounter,int previousEnemyCount,int minimumX,int maximumX,
        int minimumY,int maximumY,uint milliseconds,Func<int> nextNativeRandom)
    {
        if(actor.Length!=CombatSceneDefinition.RuntimeRecordSize) throw new ArgumentException("Incomplete spawn actor record.");
        if(encounterCounter<I(actor,0x68)) return new(0,false);
        int state=I(actor,0x60), now=unchecked((int)milliseconds);
        bool activate=state switch
        {
            >=1 and <=6=>previousEnemyCount<state*2+2,
            >=7 and <=11=>unchecked(I(actor,0x6c)+(state-5)*3000)<now,
            12=>previousEnemyCount==0,_=>false
        };
        if(!activate) return new(state is >=1 and <=14?1:0,false);
        int placement=I(actor,0x64), y=0;
        if(placement is 1 or 2)
        {
            int range=unchecked(maximumY-minimumY);
            if(range==0) throw new InvalidDataException("Spawn arena RNG divisor is zero.");
            int roll=nextNativeRandom();
            if((uint)roll>32767) throw new InvalidDataException("Spawn placement requires native15-bit RNG.");
            y=unchecked(roll%range+minimumY);
        }
        W(actor,0x60,0);
        if(placement==1)
        {
            W(actor,0x78,y); W(actor,0x80,y); W(actor,0x70,unchecked(minimumX-100)); W(actor,0x7c,unchecked(minimumX-100));
        }
        else if(placement==2)
        { W(actor,0x78,y); W(actor,0x70,unchecked(maximumX+100)); }
        return new(1,true);
    }
    private static int I(ReadOnlySpan<byte> bytes,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
    private static void W(Span<byte> bytes,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(bytes[offset..],value);
}
