using System.Buffers.Binary;
using Atria.Game.Controls;
using Atria.Game.State;

namespace Atria.Game.Combat;

/// <summary>Semantic requests preserve the original manual eligibility and execute exactly one transition.</summary>
public static class CombatSemanticControl
{
    public static bool Variation(CombatActorImage image,int direction,int requiredFlags,uint now)
    {
        byte[] a=image.Fields;var info=image.Binding!.Info;int flags=I(a,0xad);
        if((flags&32)==0||(I(a,0xb9)!=0&&I(a,0xb1)!=0))return false;
        if(requiredFlags!=0&&requiredFlags!=flags)return false;
        for(int i=info.Read(0xb1c);i<info.Read(0xb20);i++)
        {
            int at=0x150+i*44;if(info.Read(at+4)!=flags||info.Read(at+16)!=direction)continue;
            CombatHitReaction.CancelOffsetMotion(image);image.Animation!.BeginVariation(i,now);return true;
        }
        return false;
    }
    public static bool AirAttack(CombatActorImage image,uint now)
    {
        byte[] a=image.Fields;var info=image.Binding!.Info;int flags=I(a,0xad);
        if((flags&32)==0||I(a,0xb1)!=0||I(a,0xb9) is not (16 or 17))return false;
        bool result=false;
        // Original loop can visit duplicate rows; preserve its final binding.
        for(int i=info.Read(0xb1c);i<info.Read(0xb20);i++)if(I(a,0xad)==info.Read(0x154+i*44))
        {image.Animation!.BeginVariation(i,now);result=true;}
        return result;
    }
    public static bool Execute(string id,CombatSceneRuntime runtime,CombatActorImage image,GameState state,uint now,
        bool busy,Action<int> sound,Action<int> rank,Action<int> item)
    {
        byte[] a=image.Fields;var info=image.Binding!.Info;
        if(id.StartsWith("v:",StringComparison.Ordinal))
        {string[] parts=id.Split(':');return parts.Length is 2 or 3&&int.TryParse(parts[1],out int d)&&Variation(image,d,parts.Length==3&&int.TryParse(parts[2],out int f)?f:0,now);}
        if(I(a,0xad)>=2)return false;
        if(id.StartsWith("s:",StringComparison.Ordinal)&&int.TryParse(id.AsSpan(2),out int strategy)&&strategy is >=0 and <5)
        {
            W(a,0x2c,0);W(a,0x34,strategy);W(a,0x30,0);
            CombatManualControl.ExecuteCommand(runtime,image,state,now,busy,sound,rank,item);return true;
        }
        int index=MoveCatalog.FindCommand(info,id);if(index<0||busy)return false;
        int category=info.Read(0x7e4+index*24);if(category>I(a,0x17c))return false;
        int first=index;while(first>0&&info.Read(0x7e4+(first-1)*24)==category)first--;
        int oldCat=I(a,0x2c),oldRank=I(a,0x34),oldOffset=I(a,0x30);int oldMp=I(a,0x178);
        W(a,0x2c,category);W(a,0x34,index-first);W(a,0x30,0);
        CombatManualControl.ExecuteCommand(runtime,image,state,now,busy,sound,rank,item);
        bool fired=image.CurrentCommandIndex==index&&I(a,0xb1)==6&&I(a,0xad)==0x140;
        if(!fired&&oldMp==I(a,0x178)){W(a,0x2c,oldCat);W(a,0x34,oldRank);W(a,0x30,oldOffset);}
        return fired;
    }
    public static int Direction(InputRouter input,int player,byte facing)
    {
        int x=(input.Held(player,GameAction.Right)?1:0)-(input.Held(player,GameAction.Left)?1:0);
        int y=(input.Held(player,GameAction.Up)?1:0)-(input.Held(player,GameAction.Down)?1:0);
        if(facing==255)x=-x;
        return (x,y) switch {(-1,-1)=>1,(0,-1)=>2,(1,-1)=>3,(-1,0)=>4,(1,0)=>6,(-1,1)=>7,(0,1)=>8,(1,1)=>9,_=>0};
    }
    private static int I(byte[] a,int at)=>BinaryPrimitives.ReadInt32LittleEndian(a.AsSpan(at));
    private static void W(byte[] a,int at,int v)=>BinaryPrimitives.WriteInt32LittleEndian(a.AsSpan(at),v);
}
