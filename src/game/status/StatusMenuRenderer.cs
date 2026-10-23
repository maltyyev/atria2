using System.Globalization;
using System.Text;
using Atria.Game.State;
using Atria.Render.Codecs;
using Atria.Render.UI;
using Atria.Resources;
namespace Atria.Game.Status;

public enum StatusPrimitive { Fill,Tint,Panel,ItemSprite,Portrait,EtcSprite,Text }
public sealed record StatusDrawCommand(StatusPrimitive Primitive,int X,int Y,int A=0,int B=0,ushort Color=0,bool Small=true,int Palette=0,ReadOnlyMemory<byte> Text=default);
public readonly record struct StatusRenderFrame(int ArrowPhase=0,int TextPhase=0,bool PageFlash=false,bool SelectionFlash=false);
public sealed class StatusRenderClock
{
    private int count,phase;
    public StatusRenderFrame AdvanceLogicalFrame(StatusMenuRuntime runtime)
    {
        void Advance(){if(++count>3){count=0;phase=(phase+1)&3;}}
        bool equipment=runtime.Menu.DetailActive&&runtime.Menu.Page==0;
        bool before=equipment&&!runtime.Equipment.ListActive;
        if(before)Advance();
        var result=new StatusRenderFrame(phase,0,runtime.Menu.PageFlash!=0,runtime.Equipment.SelectionFlash!=0);
        runtime.Menu.AdvancePageFlash();if(equipment&&runtime.Equipment.Dialog==1)runtime.Equipment.AdvanceSelectionFlash();
        if(!before&&runtime.Menu.DetailActive&&(equipment?runtime.Equipment.Candidates.Count>0:runtime.TextList?.Ids.Count>0))Advance();
        return result;
    }
    public void Reset(){count=phase=0;}
}
/// <summary>414c70 static panels plus417f90 root composition, exact native positions.</summary>
public sealed class StatusMenuRenderer
{
    private readonly SpriteFile item,etc,portraits;
    private readonly BitmapFont small,large;
    private readonly ItemCatalog items;
    private readonly ReadOnlyMemory<byte> introductions;
    private readonly IReadOnlyList<ReadOnlyMemory<byte>> messages;
    public StatusMenuRenderer(SpriteFile item,SpriteFile etc,SpriteFile portraits,BitmapFont small,BitmapFont large,ItemCatalog items,IReadOnlyList<ReadOnlyMemory<byte>> messages,ReadOnlyMemory<byte> introductions=default)
    {
        if(small.Width!=8||small.Height!=16||large.Width!=16||large.Height!=24)throw new ArgumentException("Status requires original8x16 and16x24 fonts.");
        if(messages.Count<353)throw new InvalidDataException("Status TEXTmsg labels absent.");
        this.item=item;this.etc=etc;this.portraits=portraits;this.small=small;this.large=large;this.items=items;this.messages=messages;this.introductions=introductions;
    }
    public IReadOnlyList<StatusDrawCommand> BuildRootPlan(StatusMenuRuntime runtime,GameState state,StatusRenderFrame frame)
    {
        var ops=new List<StatusDrawCommand>();var menu=runtime.Menu;int actor=menu.SelectedActor,kind=state.GetPartyInt(actor,64);
        if((uint)kind>=6)throw new InvalidDataException("Status actor class exceeds six native records.");
        void Text(ReadOnlyMemory<byte> text,int x,int y,bool sm=true,int palette=0,ushort color=0)=>ops.Add(new(StatusPrimitive.Text,x,y,Color:color,Small:sm,Palette:palette,Text:text));
        void Pair(ReadOnlyMemory<byte> text,int x,int y,int palette=0,ushort color=0xffe0){Text(text,x+1,y+1);Text(text,x,y,palette:palette,color:color);}
        void Box(StatusPrimitive op,int x,int y,int w,int h,ushort color=0)=>ops.Add(new(op,x,y,w,h,color));
        Box(StatusPrimitive.Fill,14,14,133,165);
        Box(StatusPrimitive.Tint,14,206,132,260,menu.DetailActive?(ushort)0:(ushort)15);
        Box(StatusPrimitive.Tint,174,14,292,452,menu.DetailActive&&menu.Page==2?(ushort)15:(ushort)0);
        Box(StatusPrimitive.Tint,494,14,132,452,menu.DetailActive&&menu.Page==0?(ushort)15:(ushort)0);
        Box(StatusPrimitive.Panel,0,0,3,4);Box(StatusPrimitive.Panel,0,192,3,7);Box(StatusPrimitive.Panel,160,0,8,13);Box(StatusPrimitive.Panel,480,0,3,13);
        for(int i=0;i<5;i++)
        {
            int y=245+i*38;var text=messages[12+i];
            Text(text,25,y+1,false);Text(text,25,y-1,false);Text(text,23,y+1,false);Text(text,23,y-1,false);
            Text(text,24,y,false,palette:menu.Page==i&&!frame.PageFlash?0x4cc484:menu.Page==i?0:0x4cc228,color:0xffff);
        }
        //414780 labels and414ba0 static main statistics.
        for(int i=0;i<3;i++)Text(messages[73+i],20,120+i*18,palette:0x4cc228);
        Pair(messages[352],178,20,0x4cc228);
        for(int i=0;i<9;i++)Pair(messages[25+i+(i==8?1:0)],194,38+i*17,0x4cc228);
        ops.Add(new(StatusPrimitive.Portrait,21,30,actor));
        var name=state.PartyRecords.AsMemory(actor*280,30);int end=name.Span.IndexOf((byte)0);if(end>=0)name=name[..end];
        Text(name,68,120,palette:0x4cc228);Text(messages[6+kind],68,156,palette:0x4cc228);
        int level=state.GetPartyInt(actor,200);
        if(level==13)
        {
            int stage=state.GetPartyInt(actor,276);
            if((uint)stage<3)Text(messages[76+stage],68,138,palette:stage switch{0=>0x4cc484,1=>0x4cc26c,_=>0x4cc400});
        }
        else Text(Number(level),68,138,palette:0x4cc228);
        Pair(Join(Number(state.GetPartyInt(actor,192))," / "u8.ToArray(),Number(state.GetPartyInt(actor,196))),318,38,0x4cc228);
        int[] offsets=[72,80,208,204,212,168,156,224];
        for(int i=0;i<offsets.Length;i++)Pair(Number(state.GetPartyInt(actor,offsets[i])),318,55+i*17,0x4cc228);
        int capacity=state.GetPartyInt(actor,252);if((uint)capacity>6)throw new InvalidDataException("Status equipment capacity exceeds six slots.");
        for(int slot=0;slot<Math.Max(3,capacity);slot++)
        {
            int y=24+slot*70,category=StatusEquipment.Category(kind,slot),id=state.GetPartyInt(actor,228+slot*4);
            Pair(messages[35+category],556,y,0x4cc228);
            if(id==-1)ops.Add(new(StatusPrimitive.ItemSprite,516,y,category));
            else
            {
                if((uint)id>=88)throw new InvalidDataException("Status equipped item exceeds88 definitions.");
                ops.Add(new(StatusPrimitive.ItemSprite,516,y,id+10));Pair(items.GetItem(id).Name,496,y+34,color:0xffe0);
            }
        }
        return ops.AsReadOnly();
    }
    public IReadOnlyList<StatusDrawCommand> BuildPlan(StatusMenuRuntime runtime,GameState state,StatusRenderFrame frame,QuestJournalCatalog? journal=null,bool alternateJournalHeading=false)
    {
        var ops=BuildRootPlan(runtime,state,frame).ToList();
        if(!runtime.Menu.DetailActive)return ops.AsReadOnly();
        int actor=runtime.Menu.SelectedActor;
        void Pair(ReadOnlyMemory<byte> text,int x,int y,int palette=0,ushort color=0xffff,bool sm=true)
        {ops.Add(new(StatusPrimitive.Text,x+1,y+1,Small:sm,Text:text));ops.Add(new(StatusPrimitive.Text,x,y,Color:color,Small:sm,Palette:palette,Text:text));}
        if(runtime.Menu.Page==2)
        {
            for(int i=0;i<4;i++)
            {
                Pair(messages[17+i],194,225+i*17,0x4cc26c);
                Pair(messages[21+i],322,225+i*17,0x4cc26c);
                Pair(Number(state.GetPartyInt(actor,100+i*4)),286,225+i*17,0x4cc26c);
            }
            int[] offsets=[116,120,184,88];
            for(int i=0;i<4;i++)Pair(Number(state.GetPartyInt(actor,offsets[i])),414,225+i*17,0x4cc26c);
            for(int line=0;line<10;line++)Pair(DatTables.GetCharacterIntroductionLine(introductions,actor,line),196,298+line*16);
        }
        else if(runtime.Menu.Page==1)
        {
            var list=runtime.TextList??throw new InvalidOperationException("Item page has no native list.");
            AppendItemList(ops,state,list.Ids,list.Scroll,list.Row,frame,list.DescriptionOpen?runtime.Description:null);
        }
        else if(runtime.Menu.Page is 3 or 4)
        {
            int page=runtime.Menu.Page;var list=runtime.TextList??throw new InvalidOperationException("Status text detail has no native list.");
            ReadOnlyMemory<byte> Name(int id)
            {
                if(page==4)return (journal??throw new ArgumentNullException(nameof(journal))).GetHeading(id,alternateJournalHeading?1:0);
                var bytes=state.QuestItemRecords.AsMemory(id*74+4,30);int end=bytes.Span.IndexOf((byte)0);return end<0?bytes:bytes[..end];
            }
            ops.Add(new(StatusPrimitive.Tint,110,302,420,168,15));ops.Add(new(StatusPrimitive.Panel,96,288,12,4));
            if(list.Ids.Count==0)
            {
                var empty=messages[page==3?353:359];int x=page==3?218:208,y=380;
                foreach(var delta in new[]{(1,1),(1,-1),(-1,1),(-1,-1)})ops.Add(new(StatusPrimitive.Text,x+delta.Item1,y+delta.Item2,Small:false,Text:empty));
                ops.Add(new(StatusPrimitive.Text,x,y,Small:false,Palette:0x4cc370,Text:empty));return ops.AsReadOnly();
            }
            for(int row=0;row<3&&list.Scroll+row<list.Ids.Count;row++)
            {
                int id=list.Ids[list.Scroll+row],stage=page==3?StatusQuestRules.Stage(state,id):0;
                var text=Name(id);if(stage!=0)text=Join(text," "u8.ToArray(),messages[stage==1?118:119]);
                Pair(text,136,324+row*48,stage switch{1=>0x4cc400,2=>0x4cc370,_=>0x4cc228},sm:false);
                if(page==4&&id!=0)Pair(JournalStatus(state.EventStatuses[id]),136,348+row*48,color:state.EventStatuses[id]==4?(ushort)0x8410:(ushort)0x07ff);
            }
            ops.Add(new(StatusPrimitive.EtcSprite,118,322+list.Row*48,22+frame.ArrowPhase));
            if(list.Scroll>0)ops.Add(new(StatusPrimitive.EtcSprite,316,306,18));
            if(list.Scroll+3<list.Ids.Count)ops.Add(new(StatusPrimitive.EtcSprite,316,450,19));
            if(list.DescriptionOpen)
            {
                ops.Add(new(StatusPrimitive.Fill,62,174,517,133));ops.Add(new(StatusPrimitive.Panel,48,160,15,3));
                int id=list.SelectedId;ops.Add(new(StatusPrimitive.Text,80,page==4?182:180,Color:0xffff,Text:Name(id)));
                if(page==3)
                {
                    int stage=StatusQuestRules.Stage(state,id);
                    if(stage==1)ops.Add(new(StatusPrimitive.Text,80,196,Color:0x07ff,Text:messages[358]));
                    else if(stage==2)ops.Add(new(StatusPrimitive.Text,80,196,Palette:0x4cc370,Text:messages[68+StatusQuestRules.Region(id)]));
                    for(int line=0;line<runtime.Description.Length;line++)ops.Add(new(StatusPrimitive.Text,80,212+line*19,Palette:0x4cc400,Text:runtime.Description[line]));
                }
                else for(int line=0;line<runtime.Description.Length;line++)ops.Add(new(StatusPrimitive.Text,80,206+line*19,Palette:0x4cc228,Text:runtime.Description[line]));
            }
        }
        else if(runtime.Menu.Page==0)
        {
            var equipment=runtime.Equipment;int dy=equipment.Slot*70;
            ops.Add(new(StatusPrimitive.EtcSprite,499,25+dy,equipment.ListActive?22:22+frame.ArrowPhase));
            if(equipment.Dialog==1)
            {
                ops.Add(new(StatusPrimitive.Fill,302,30+dy,293,37));ops.Add(new(StatusPrimitive.Panel,288,16+dy,8,0));
                void Option(int message,int x,bool selected)
                {
                    var text=messages[message];ops.Add(new(StatusPrimitive.Text,x+1,39+dy,Small:false,Text:text));
                    ops.Add(new(StatusPrimitive.Text,x,38+dy,Color:0xffff,Small:false,Palette:selected?(frame.SelectionFlash?0:0x4cc484):0x4cc228,Text:text));
                }
                Option(116,332,equipment.ChangeSelected);Option(117,462,equipment.RemoveSelected);
            }
            else
            {
                if(equipment.ListActive)AppendItemList(ops,state,equipment.Candidates,equipment.Scroll,equipment.Row,frame);
                if(equipment.Unavailable)
                {
                    ops.Add(new(StatusPrimitive.Tint,78,334,484,68,15));ops.Add(new(StatusPrimitive.Panel,64,320,14,1));
                    Pair(messages[356],128,340,0x4cc370,sm:false);Pair(messages[357],128,372,0x4cc370,sm:false);
                }
                if(equipment.Conflict)
                {
                    ops.Add(new(StatusPrimitive.Tint,78,334,484,36,15));ops.Add(new(StatusPrimitive.Panel,64,320,14,0));Pair(messages[355],96,340,0x4cc370,sm:false);
                }
            }
        }
        return ops.AsReadOnly();
    }
    private static readonly byte[] journalClosed=CodePagesEncodingProvider.Instance.GetEncoding(1251)!.GetBytes("Закрыто");
    private static readonly byte[] journalActive=CodePagesEncodingProvider.Instance.GetEncoding(1251)!.GetBytes("В работе");
    private static byte[] JournalStatus(int status)=>status==4?journalClosed:journalActive;
    private void AppendItemList(List<StatusDrawCommand> ops,GameState state,IReadOnlyList<int> ids,int scroll,int row,StatusRenderFrame frame,ReadOnlyMemory<byte>[]? description=null)
    {
        void Text(ReadOnlyMemory<byte> text,int x,int y,int palette=0,ushort color=0,bool sm=true)=>ops.Add(new(StatusPrimitive.Text,x,y,Color:color,Small:sm,Palette:palette,Text:text));
        void Pair(ReadOnlyMemory<byte> text,int x,int y,int palette=0,ushort color=0xffff){Text(text,x+1,y+1);Text(text,x,y,palette,color);}
        ops.Add(new(StatusPrimitive.Tint,46,302,548,168,15));ops.Add(new(StatusPrimitive.Panel,32,288,16,4));
        if(ids.Count<1)
        {
            foreach(var delta in new[]{(-1,-1),(1,-1),(-1,1),(1,1)})Text(messages[353],218+delta.Item1,380+delta.Item2,sm:false);
            Text(messages[353],218,380,0x4cc370,sm:false);return;
        }
        for(int i=0;i<3&&scroll+i<ids.Count;i++)
        {
            int id=ids[scroll+i],y=336+i*48;if((uint)id>=88)throw new InvalidDataException("Status inventory ID exceeds88 definitions.");
            var record=items.GetItem(id);int Read(int at)=>record.GetInteger((at-30)/4);
            ushort availabilityColor=state.AvailableItems[id]>0?(ushort)0xffe0:(ushort)0x8410;
            ops.Add(new(StatusPrimitive.ItemSprite,72,y-16,id+10));Pair(record.Name,108,y-16,color:availabilityColor);
            Pair(messages[354],238,y-16,color:availabilityColor);
            Pair(Join(Number(state.AvailableItems[id]),"/"u8.ToArray(),Number(state.OwnedItems[id])," ]"u8.ToArray()),322,y-16,color:availabilityColor);
            if(Read(50)!=0&&id<48)
            {
                Text(Join(messages[id<25?79:81],Number(Read(50))),382,y-16,color:0xffff);
                if(Read(54)!=0)Text(Join(messages[id<25?80:82],Number(Read(54)),"%"u8.ToArray()),482,y-16,color:0xffff);
            }
            int count=Read(58);if(count>3)throw new InvalidDataException("Status item modifier count exceeds three fields.");
            var modifiers=Enumerable.Range(0,count).Where(j=>Read(62+j*4)!=12).ToArray();
            for(int index=0;index<modifiers.Length;index++)
            {
                int j=modifiers[index];int code=Read(62+j*4),amount=Read(74+j*4);if((uint)(45+code)>=(uint)messages.Count)throw new InvalidDataException("Status modifier label exceeds TEXTmsg.");
                var text=Join(index==0?"( "u8.ToArray():" "u8.ToArray(),messages[45+code],amount>0?"+"u8.ToArray():Array.Empty<byte>(),Number(amount),index==modifiers.Length-1?" )"u8.ToArray():Array.Empty<byte>());
                Pair(text,136+index*144,y,0x4cc400);
            }
            int[] special=id switch
            {
                55=>[83,84],56=>[85],57=>[86],58=>[87,88],59=>[89,90],60=>[91,92],61 or 62=>[93,94],
                63=>[97,98],64=>[99,100],65=>[101,102],66=>[103,104],>=67 and <=69=>[105],70=>[106],71=>[107],
                >=72 and <=76=>[108],77=>[109],78=>[110],79=>[111],80=>[112],81=>[113],82=>[114],83=>[115],_=>[]
            };
            if(special.Length>0)
            {
                var parts=new List<ReadOnlyMemory<byte>> {"( "u8.ToArray()};
                for(int j=0;j<special.Length;j++){if(j!=0)parts.Add("   "u8.ToArray());parts.Add(messages[special[j]]);}parts.Add(" )"u8.ToArray());
                Text(Join(parts.ToArray()),136,y,0x4cc370);
            }
        }
        ops.Add(new(StatusPrimitive.EtcSprite,54,322+row*48,22+frame.ArrowPhase));
        if(scroll>0)ops.Add(new(StatusPrimitive.EtcSprite,316,306,18));if(scroll+3<ids.Count)ops.Add(new(StatusPrimitive.EtcSprite,316,450,19));
        if(description is not null)
        {
            ops.Add(new(StatusPrimitive.Fill,62,174,517,133));ops.Add(new(StatusPrimitive.Panel,48,160,15,3));
            Text(items.GetItem(ids[scroll+row]).Name,80,182,color:0xffff);
            for(int line=0;line<description.Length;line++)Text(description[line],80,206+line*19,0x4cc400);
        }
    }
    public void DrawPlan(IReadOnlyList<StatusDrawCommand> plan,StatusRenderFrame frame,Span<ushort> target)
    {
        if(target.Length!=640*480)throw new ArgumentException("Status target must be640x480.");
        foreach(var op in plan)
        {
            switch(op.Primitive)
            {
                case StatusPrimitive.Fill:
                    for(int y=Math.Max(0,op.Y);y<Math.Min(480,op.Y+op.B);y++)target.Slice(y*640+Math.Max(0,op.X),Math.Max(0,Math.Min(640,op.X+op.A)-Math.Max(0,op.X))).Fill(op.Color);break;
                case StatusPrimitive.Tint:UiRaster.TintHalf(target,640,480,640,op.X,op.Y,op.A,op.B,op.Color);break;
                case StatusPrimitive.Panel:UiRaster.DrawPanel(etc,op.X,op.Y,op.A,op.B,target);break;
                case StatusPrimitive.ItemSprite:case StatusPrimitive.Portrait:case StatusPrimitive.EtcSprite:
                    var sprites=op.Primitive==StatusPrimitive.ItemSprite?item:op.Primitive==StatusPrimitive.Portrait?portraits:etc;
                    if((uint)op.A>=(uint)sprites.Frames.Length)throw new InvalidDataException("Status sprite frame absent.");
                    SpriteRaster.Draw565(sprites.Frames[op.A],target,640,480,640,op.X,op.Y,SpriteBlend.Copy);break;
                case StatusPrimitive.Text:
                    var font=op.Small?small:large;
                    if(op.Palette==0)font.DrawText(op.Text.Span,target,640,480,640,op.X,op.Y,op.Color);
                    else UiRaster.DrawStyledText(font,op.Text.Span,Palette(op.Palette),frame.TextPhase,target,640,480,640,op.X,op.Y);break;
            }
        }
    }
    private static byte[] Number(int value)=>Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture));
    private static byte[] Join(params ReadOnlyMemory<byte>[] parts){var bytes=new byte[parts.Sum(p=>p.Length)];int at=0;foreach(var part in parts){part.Span.CopyTo(bytes.AsSpan(at));at+=part.Length;}return bytes;}
    private static ReadOnlySpan<ushort> Palette(int address)=>address switch
    {
        0x4cc228=>NativeUiPalette.Normal,0x4cc484=>NativeUiPalette.Selected,0x4cc370=>NativeUiPalette.Title,
        //42e3b0 converts EXE44df54 and44dfd4 RGB tables to these565 constants.
        0x4cc26c=>[0xffff,0xff1f,0xfe9f,0xfe1f,0xfd9f,0xfd1f,0xfc9f,0xfc9f,0xfb9f,0xfb1f,0xfa9f,0xfa1f,0xe19c,0xd19a,0xc198,0xb196,0xa114,0x9112,0x8110,0x710e,0x608c,0x588b,0x508a,0x4889,0x4008,0x3807,0x3006,0x2805,0x2004,0x1803,0x1002,0x0801],
        0x4cc400=>[0xffff,0xe7ff,0xd7ff,0xc7ff,0xb7ff,0xa7ff,0x97ff,0x97ff,0x77ff,0x67ff,0x57ff,0x47ff,0x371c,0x369a,0x3618,0x3596,0x2514,0x2492,0x2410,0x238e,0x130c,0x12cb,0x128a,0x1249,0x0208,0x01c7,0x0186,0x0145,0x0104,0x00c3,0x0082,0x0041],
        _=>throw new InvalidDataException("Unrecovered status text palette.")
    };
}






