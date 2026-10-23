using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>411e20 and target-search leaves. Managed target indices use−1 for native null pointers.</summary>
public static class CombatTargetSelection
{
    public static int Select(IReadOnlyList<byte[]> actors,IReadOnlyList<int> currentTargets,int attackerIndex,
        int first,int end,uint milliseconds,Func<int> nextNativeRandom)
    {
        if(actors.Count<6 || currentTargets.Count!=actors.Count || (uint)attackerIndex>=(uint)actors.Count || first<0 || end<first || end>actors.Count)
            throw new ArgumentException("Incomplete native target-selection domain.");
        foreach(byte[] record in actors) if(record.Length!=CombatSceneDefinition.RuntimeRecordSize) throw new ArgumentException("Incomplete combat target actor.");
        byte[] attacker=actors[attackerIndex]; int now=unchecked((int)milliseconds);
        int Next() { int value=nextNativeRandom(); if((uint)value>32767) throw new InvalidDataException("Target selection requires native15-bit RNG."); return value; }
        if(unchecked((sbyte)attacker[0xa6])<100)
            for(int i=actors.Count-6;i<actors.Count;i++)
                if(I(actors[i],0x170)>0 && HasItem(actors[i],56) && (Next()&1)!=0) return i;
        if(I(attacker,0x371)==0)
        { W(attacker,0x36d,Next()%11); W(attacker,0x371,unchecked(now+13000)); }
        else if(I(attacker,0x371)<now)
        {
            int mode=unchecked(I(attacker,0x36d)+1); if(mode>10) mode=0;
            W(attacker,0x36d,mode); W(attacker,0x371,unchecked(now+13000));
        }
        int result=I(attacker,0x36d) switch
        {
            1 or 5 or 10=>FindControlled(), 2=>FindExtreme(0x1f4,true),3=>FindExtreme(0x1f4,false),
            4 or 9=>FindRetaliating(),8=>FindExtreme(0x170,true),_=>FindNearest()
        };
        if(result<0) result=FindNearest();
        if(result>=0 && HasItem(actors[result],55)) result=-1;
        return result;

        int FindControlled()
        { for(int i=first;i<end;i++) if(Eligible(attacker,actors[i]) && actors[i][0xa4]!=0) return i; return -1; }
        int FindRetaliating()
        { for(int i=first;i<end;i++) if(Eligible(attacker,actors[i]) && currentTargets[i]==attackerIndex) return i; return -1; }
        int FindExtreme(int field,bool maximum)
        {
            int selected=-1,best=maximum?0:10000;
            for(int i=first;i<end;i++) if(Eligible(attacker,actors[i]))
            {
                int value=I(actors[i],field);
                if(maximum?value>best:value<best) { selected=i; best=value; }
            }
            return selected;
        }
        int FindNearest()
        {
            int selected=-1,best=5000;
            for(int i=first;i<end;i++) if(Eligible(attacker,actors[i]))
            {
                int distance=unchecked(Abs(unchecked(I(actors[i],0x98)-I(attacker,0x98)))+Abs(unchecked(I(actors[i],0x78)-I(attacker,0x78))));
                if(distance<best) { best=distance; selected=i; }
            }
            return selected;
        }
    }

    public static bool Eligible(ReadOnlySpan<byte> attacker,ReadOnlySpan<byte> target)
    {
        if(attacker.Length!=CombatSceneDefinition.RuntimeRecordSize || target.Length!=attacker.Length) throw new ArgumentException("Incomplete target predicate actor.");
        if(I(target,0x60)!=0 || I(target,0x170)<=0 || I(target,0x224)==0) return false;
        int team=unchecked((sbyte)attacker[0xa6]),other=unchecked((sbyte)target[0xa6]);
        if(team>99) return other<100;
        return (team==other || target[0xa7]!=0) && team!=other;
    }
    private static bool HasItem(ReadOnlySpan<byte> actor,int id)
    { for(int i=0;i<6;i++) if(I(actor,0x208+i*4)==id) return true; return false; }
    private static int Abs(int value) { int sign=value>>31; return unchecked((value^sign)-sign); }
    private static int I(ReadOnlySpan<byte> bytes,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
    private static void W(Span<byte> bytes,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(bytes[offset..],value);
}
