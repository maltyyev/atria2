using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>One requested directional move, driven by original ANI progress and INF prerequisites.</summary>
public sealed class CombatMoveSequence
{
    private CombatActorImage? actor;
    private int[] steps=[];
    private int next,sequence,flags;
    private uint began;
    public bool Active=>actor is not null;
    public void Cancel(){actor=null;steps=[];next=0;}
    public static int[]? Plan(CombatInfoCharacter info,int initialFlags,string id)
    {
        string[] parts=id.Split(':');
        if(parts.Length is not (2 or 3)||parts[0]!="v"||!int.TryParse(parts[1],out int direction))return null;
        int required=0;if(parts.Length==3&&!int.TryParse(parts[2],out required))return null;
        var pending=new Queue<(int Flags,int[] Path)>();var seen=new HashSet<int>{initialFlags};pending.Enqueue((initialFlags,[]));
        while(pending.TryDequeue(out var node))
        {
            if((node.Flags&32)==0)continue;
            for(int i=info.Read(0xb1c);i<info.Read(0xb20);i++)
            {
                int at=0x150+i*44,d=info.Read(at+16);
                if(info.Read(at+4)!=node.Flags||d is not (1 or 2 or 3 or 4 or 6 or 7 or 8 or 9))continue;
                // Variation dispatch picks the first matching row for a given state and direction.
                bool shadowed=false;for(int j=info.Read(0xb1c);j<i;j++)if(info.Read(0x154+j*44)==node.Flags&&info.Read(0x160+j*44)==d){shadowed=true;break;}
                if(shadowed)continue;
                int[] path=[..node.Path,i];
                if(d==direction&&(required==0||required==node.Flags))return path;
                int after=info.Read(at+8);if(seen.Add(after))pending.Enqueue((after,path));
            }
        }
        return null;
    }
    public bool Start(CombatActorImage image,string id,uint now)
    {
        Cancel();var a=image.Fields;var info=image.Binding!.Info;
        if(I(a,0xad) is <0 or >1||I(a,0xb9)!=0||I(a,0x170)<=0||I(a,0x224)==0)return false;
        if(Plan(info,info.Read(0x158),id) is null)return false;
        CombatManualControl.Attack(a,info,image.Animation!,now);
        var plan=Plan(info,I(a,0xad),id);if(plan is null)return false;
        actor=image;steps=plan;next=0;Remember(now);return true;
    }
    public bool Advance(CombatActorImage image,uint now)
    {
        if(actor is null)return false;
        if(!ReferenceEquals(actor,image)||image.Animation!.SequenceIndex!=sequence||I(image.Fields,0xad)!=flags||
            I(image.Fields,0xb1)!=6||I(image.Fields,0xb9)!=0||I(image.Fields,0x170)<=0||I(image.Fields,0x224)==0)
        {Cancel();return false;}
        // Do not chain at render frequency or skip the prerequisite's wind-up frames.
        var animation=image.Animation;
        if(animation.FrameIndex<animation.SequenceFrameCount-1||unchecked((int)(now-began))<Math.Max(1,I(image.Fields,0xc1)))return true;
        var info=image.Binding!.Info;int at=0x150+steps[next]*44;
        if(!CombatSemanticControl.Variation(image,info.Read(at+16),info.Read(at+4),now)){Cancel();return false;}
        if(++next==steps.Length)Cancel();else Remember(now);
        return true;
    }
    private void Remember(uint now){sequence=actor!.Animation!.SequenceIndex;flags=I(actor.Fields,0xad);began=now;}
    private static int I(byte[] a,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(a.AsSpan(offset));
}
