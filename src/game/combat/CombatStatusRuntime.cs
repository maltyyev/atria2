using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>40a2a0 visual blink clock and40a370 actual status expiry/revert effects.</summary>
public static class CombatStatusRuntime
{
    public static bool AdvanceVisible(Span<byte> slot,uint milliseconds)
    {
        if(slot.Length!=24) throw new ArgumentException("Invalid combat status slot.");
        int now=unchecked((int)milliseconds),remaining=unchecked(I(slot,0)-now);
        if(I(slot,16)==0)
        {
            if(remaining<20000) { W(slot,16,unchecked(now+1000));W(slot,20,1); }
            return true;
        }
        if(I(slot,16)<now)
        {
            W(slot,20,I(slot,20)==0?1:0);
            W(slot,16,unchecked(now+(remaining<6000?300:remaining<12000?700:1000)));
        }
        return I(slot,20)!=0;
    }
    public static void Expire(CombatSceneRuntime runtime,int actorIndex,Span<byte> slot)
    {
        if(slot.Length!=24) throw new ArgumentException("Invalid combat status slot.");
        int code=I(slot,4);W(slot,4,unchecked(code+1));
        switch(code)
        {
            case 6:Self(runtime,actorIndex,0x29c);return;
            case 7:Self(runtime,actorIndex,0x2a0);return;
            case 10:Self(runtime,actorIndex,0x2b0);return;
            case 11:Self(runtime,actorIndex,0x240,0x2a4,0x29c);return;
            case 27:Self(runtime,actorIndex,0x240,0x2c0);return;
            case 48:Self(runtime,actorIndex,0x2a8);return;
            case 51:Self(runtime,actorIndex,0x2a4);return;
            case 53:Self(runtime,actorIndex,0x2a4,0x2a0);return;
        }
        bool selectedTeam=I(slot,12)==1;
        foreach(CombatActorImage image in runtime.Actors)
        {
            Span<byte> a=image.Fields;bool lowTeam=unchecked((sbyte)a[0xa6])<100;
            bool selected=selectedTeam?lowTeam:!lowTeam;
            switch(code)
            {
                case 15:if(selected) W(a,0x2ac,0);break;
                case 16:case 25:if(selected) W(a,0x2a4,0);break;
                case 26:if(selected) W(a,0x2a0,0);break;
                case 29:if(selected) { W(a,0x2a0,0);W(a,0x2bc,0); }break;
                case 30:if(selected) { W(a,0x2a0,0);W(a,0x2a4,0);W(a,0x2b4,0); }break;
                case 31:if(selected) W(a,0x2b4,0);break;
                case 34:if(!lowTeam) { W(a,0x2a0,0);W(a,0x2a4,0);W(a,0x29c,0);W(a,0x2a8,0); }break;
                case 39:if(!lowTeam) { W(a,0x2a4,0);W(a,0x2b8,0); }break;
            }
        }
    }
    private static void Self(CombatSceneRuntime runtime,int actorIndex,int one,int two=-1,int three=-1)
    {
        if((uint)actorIndex>=(uint)runtime.Actors.Count) throw new InvalidDataException("Self status expiry has no actual actor owner.");
        Span<byte> a=runtime.Actors[actorIndex].Fields;W(a,one,0);if(two>=0) W(a,two,0);if(three>=0) W(a,three,0);
    }
    private static int I(ReadOnlySpan<byte> bytes,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset,4));
    private static void W(Span<byte> bytes,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(offset,4),value);
}
