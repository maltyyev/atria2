using System.Buffers.Binary;
using Atria.Render.Codecs;

namespace Atria.Game.Combat;

/// <summary>409a80/409a30 HUD and40a2a0/40a370 scheduled visual status state.</summary>
public sealed class CombatHudRaster(CombatSceneRuntime runtime,CombatBattleAssets assets,byte[] globalStatus)
{
    private readonly bool[] globalVisible=new bool[10],actorVisible=new bool[3];
    private int leader=-1,now;
    private bool commands;
    public int CurrentActorIndex { get; private set; } = -1;
    public void AdvanceVisualState(uint milliseconds,bool commandSelectionActive)
    {
        if(globalStatus.Length!=240) throw new ArgumentException("Combat global status bank must contain ten slots.");
        now=unchecked((int)milliseconds);commands=commandSelectionActive;leader=-1;
        for(int i=0;i<10;i++) globalVisible[i]=AdvanceSlot(globalStatus.AsSpan(i*24,24),-1,milliseconds);
        for(int i=runtime.Scene.PartyStartIndex;i<runtime.Actors.Count;i++) if(I(runtime.Actors[i].Fields,0x230)!=0) { leader=i;break; }
        Array.Clear(actorVisible);
        CurrentActorIndex=leader>=0?leader:runtime.Actors.Count-1;
        if(leader<0) return;
        int visibleTarget=runtime.TargetIndices[leader];
        if(visibleTarget>=0 && now<I(runtime.Actors[leader].Fields,0xa9)) CurrentActorIndex=visibleTarget;
        CombatActorImage actor=runtime.Actors[leader];
        if(actor.Binding!.Info.Read(0xb28)==0 || !commands)
            for(int i=0;i<3;i++) actorVisible[i]=AdvanceSlot(actor.Fields.AsSpan(0x244+i*24,24),leader,milliseconds);
    }
    private bool AdvanceSlot(Span<byte> slot,int owner,uint milliseconds)
    {
        if(now<Read(slot,0)) return CombatStatusRuntime.AdvanceVisible(slot,milliseconds);
        if(Read(slot,16)!=0) { CombatStatusRuntime.Expire(runtime,owner,slot);slot.Clear(); }
        return false;
    }
    public void Draw(ushort[] target)
    {
        if(target.Length!=640*480) throw new ArgumentException("Combat HUD target must be640x480.");
        int left=10,right=500;
        for(int i=0;i<10;i++)
        {
            if(!globalVisible[i]) continue;
            bool far=I(globalStatus,i*24+12)!=0;int x=far?right:left;
            Sprite(assets.MagicIcons,I(globalStatus,i*24+4),x,360,target);
            if(far) right+=35;else left+=35;
        }
        if(leader<0) return;
        CombatActorImage image=runtime.Actors[leader];byte[] a=image.Fields;
        Bar(a,0,153,448,target);
        Sprite(assets.Faces,image.Binding!.Info.Type,30,425,target);
        ReadOnlySpan<int> equipmentX=[160,214,266];
        for(int i=0;i<3;i++) { int item=I(a,0x208+i*4);if(item!=-1) Sprite(assets.Items,checked(item+10),equipmentX[i],411,target); }
        var info=image.Binding.Info;
        if(info.Read(0xb28)==0 || !commands)
        {
            for(int i=0;i<3;i++) if(actorVisible[i]) Sprite(assets.MagicIcons,I(a,0x248+i*24),369+i*35,424,target);
        }
        else DrawCommands(image,target);
        Bar(a,1,153,463,target);
        int targetIndex=runtime.TargetIndices[leader];
        if(targetIndex>=0 && now<I(a,0xa9)) Bar(runtime.Actors[targetIndex].Fields,2,20,10,target);
    }
    private void Bar(byte[] a,int kind,int x,int y,ushort[] target)
    {
        int at=kind==1?0x18:4,current=I(a,kind==1?0x178:0x170),effective=Math.Max(current,I(a,at));
        if(effective<0) throw new InvalidDataException("Combat bar effective value is negative.");
        int digit=Math.Min(9,effective/163),maximum=Math.Min(163,I(a,at+16));
        Gradient(target,x,y,x+maximum,y+8,CombatHudPalette.Background);
        int filled=effective%163;
        if(filled!=0) Gradient(target,x,y,x+filled,y+8,kind==1?CombatHudPalette.MagicPoints:CombatHudPalette.HitPoints);
        Sprite(assets.Numbers,digit,x-15,y,target);
        int tailEnd=Math.Min(unchecked(x+filled+I(a,at+4)),unchecked(x+maximum));
        if(x+filled<tailEnd) Fill(target,x+filled,y,unchecked(tailEnd+1),y+8,kind==1?(ushort)0x000f:(ushort)0x7be0);
    }
    private void DrawCommands(CombatActorImage image,ushort[] target)
    {
        byte[] a=image.Fields;var info=image.Binding!.Info;int category=I(a,0x2c);
        if((uint)category>=10) throw new InvalidDataException("Combat HUD command category exceeds native table.");
        int matched=0,drawn=0,x=368;
        for(int i=0;i<info.Read(0xb28);i++)
        {
            if(info.Read(0x7e4+i*24)!=category) continue;
            matched++;
            if(I(a,0x30)<=matched)
            {
                Sprite(assets.MagicIcons,info.Read(0x7e0+i*24)-1,x+1,424,target);
                if(drawn==I(a,0x34)) Outline(target,x,423,x+33,458,0xffe0);
                drawn++;x+=40;
            }
            if(x==568) break;
        }
        if(matched<info.Read(0xaa8+category*4)) Sprite(assets.Arrows,2,566,437,target);
        if(matched!=drawn) Sprite(assets.Arrows,0,354,437,target);
        if(category>0) Sprite(assets.Arrows,1,458,412,target);
        if(category<9 && info.Read(0xaac+category*4)>0 && category<I(a,0x17c)) Sprite(assets.Arrows,3,458,460,target);
    }
    private static void Sprite(SpriteFile bank,int frame,int x,int y,ushort[] target)
    {
        if((uint)frame>=(uint)bank.Frames.Length) throw new InvalidDataException("Combat HUD sprite index exceeds actual bank.");
        SpriteRaster.Draw565(bank.Frames[frame],target,640,480,640,x,y);
    }
    private static void Gradient(ushort[] target,int left,int top,int right,int bottom,ReadOnlySpan<ushort> palette)
    {
        for(int y=top;y<=bottom;y++) Fill(target,left,y,right,y,palette[(y-top)%32]);
    }
    private static void Fill(ushort[] target,int left,int top,int right,int bottom,ushort color)
    {
        left=Math.Clamp(left,0,640);right=Math.Clamp(right,0,640);top=Math.Clamp(top,0,480);bottom=Math.Clamp(bottom,0,479);
        if(right<=left) return;
        for(int y=top;y<=bottom;y++) target.AsSpan(y*640+left,right-left).Fill(color);
    }
    private static void Outline(ushort[] target,int left,int top,int right,int bottom,ushort color)
    {
        if(left==right) right+=2;if(top==bottom) bottom+=2;
        Fill(target,left,top,right,top,color);Fill(target,left,bottom,right,bottom,color);
        Fill(target,left,top,left+1,bottom-1,color);Fill(target,right-1,top,right,bottom-1,color);
    }
    private static int I(byte[] a,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(a.AsSpan(offset,4));
    private static int Read(ReadOnlySpan<byte> a,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(a.Slice(offset,4));
}


