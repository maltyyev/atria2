using System.Buffers.Binary;
using Atria.Game.State;
using Atria.Game.World;
namespace Atria.Game.ActionMenu;

public enum ActionMenuEffect { Sound, InvokeTownChoice, InvokeChoice, LeaveInterior, ReturnInteriorAction }
public readonly record struct ActionMenuCommand(ActionMenuEffect Kind,int Value,int MenuType=0);
public readonly record struct ActionMenuInput(bool Previous=false,bool Next=false,bool Cancel=false,bool Accept=false);
public sealed record ActionMenuFrame(int X,int Y,int HorizontalTiles,int VerticalTiles,int Type,int Selection,int Flash,
    ReadOnlyMemory<byte>[] Labels);

public sealed class ActionMenuController
{
    private sealed record Entry(int X,int Y,int Horizontal,int Vertical,int Type,ReadOnlyMemory<byte>[] Labels)
    { public int Selection; }
    private readonly List<Entry> stack=new(4);
    private int flash;
    public int Depth=>stack.Count-1;
    public bool RenderEnabled { get; private set; }
    public int Selection=>stack.Count==0?throw new InvalidOperationException("Action menu is closed."):stack[^1].Selection;
    public void SetRenderEnabled(bool value)=>RenderEnabled=value;
    public void PointerSelect(int x,int y)
    {
        if(stack.Count==0||!RenderEnabled)return;var e=stack[^1];
        int top=e.Y+((e.Labels.Length&1)==0?52:38);
        if(x<e.X+30||x>e.X+(e.Horizontal+2)*32-12||y<top||y>=top+e.Labels.Length*32)return;
        e.Selection=(y-top)/32;
    }

    //0x427600/426ea0. Type0 regular town,66 empty town; other handlers remain explicit unsupported work.
    public void OpenTown(bool hasObjects,IReadOnlyList<ReadOnlyMemory<byte>> messages) =>
        Open(0,0,5,hasObjects?5:3,hasObjects?0:66,messages);

    public void Open(int x,int y,int horizontalTiles,int verticalTiles,int type,IReadOnlyList<ReadOnlyMemory<byte>> messages,int currentMode=400)
    {
        if((type is 0 or 66) ? currentMode!=400 : !((type==3&&currentMode==410)||(type==5&&currentMode==420)||(type==6&&currentMode==430))) throw new NotSupportedException("Action-menu type/mode pair has no recovered handler.");
        if(type is not (0 or 66 or 3 or 5 or 6)) throw new NotSupportedException("Action menu type has no recovered runtime handler.");
        if(stack.Count>=4) throw new InvalidOperationException("Native four-entry action menu stack capacity exceeded.");
        if(horizontalTiles<0 || verticalTiles<0) throw new ArgumentOutOfRangeException(nameof(horizontalTiles));
        if(messages.Count<(type is 0 or 66?275:294)) throw new InvalidDataException("TEXTMSG action labels are absent.");
        // Native427600 makes the vertical tile count odd before centering.
        if((verticalTiles&1)==0) verticalTiles=checked(verticalTiles+1);
        if(x==0 && y==0)
        { x=checked(320-(horizontalTiles+2)*16); y=checked(240-(verticalTiles+2)*16); }
        int first=type switch {0=>271,66=>272,3=>286,5=>288,6=>290,_=>throw new InvalidOperationException()};
        int count=type is 0 or 6?4:type==66?3:2;
        var labels=new ReadOnlyMemory<byte>[count];
        for(int i=0;i<count;i++)
        {
            if(messages[first+i].Length>=64) throw new InvalidDataException("Action label exceeds native64-byte slot.");
            labels[i]=messages[first+i].ToArray();
        }
        stack.Add(new(x,y,horizontalTiles,verticalTiles,type,labels));
        RenderEnabled=true;
        // Native push does not reset global c69a0 flash; retain it across nesting.
    }

    public void Input(ActionMenuInput input,Action<ActionMenuCommand> emit)
    {
        ArgumentNullException.ThrowIfNull(emit);
        if(stack.Count==0) throw new InvalidOperationException("Action menu is closed.");
        Entry top=stack[^1];
        //4279d0 has exclusive priority: previous, next, cancel, accept.
        if(input.Previous || input.Next)
        {
            emit(new(ActionMenuEffect.Sound,1)); flash=2;
            top.Selection=(top.Selection+(input.Previous?top.Labels.Length-1:1))%top.Labels.Length;
            return;
        }
        if(input.Cancel)
        {
            emit(new(ActionMenuEffect.Sound,4));
            if(top.Type is 3 or 5 or 6) emit(new(ActionMenuEffect.LeaveInterior,0,top.Type));
            else if(top.Type==8) emit(new(ActionMenuEffect.ReturnInteriorAction,0,8));
            else CloseTop();
            return;
        }
        if(input.Accept)
        {
            emit(new(ActionMenuEffect.Sound,3));
            // Execute synchronously through TownController.ChooseAction; that host decides close-all vs retained map-return stack.
            if(top.Type==88) { CloseAll();return; }
            emit(new(top.Type is 0 or 66?ActionMenuEffect.InvokeTownChoice:ActionMenuEffect.InvokeChoice,top.Selection,top.Type));
        }
    }

    public void OpenWorld(WorldMapContext map,GameState state,Func<int,bool> isSceneReferenceBlocked,IReadOnlyList<ReadOnlyMemory<byte>> messages)
    {
        if((uint)state.WorldX>=10||(uint)state.WorldY>=10)throw new InvalidDataException("World menu position exceeds10x10 map.");

        int Read(int at)=>BinaryPrimitives.ReadInt32LittleEndian(map.TileBytes.Slice((state.WorldY*10+state.WorldX)*328+at,4));
        bool blocked=isSceneReferenceBlocked(Read(92))&&Read(8)!=0;
        bool scene=Read(24)==1&&Read(112)==0&&Read(120)==0;
        var labels=new List<ReadOnlyMemory<byte>> {messages[275],messages[276],messages[277]};
        if(blocked)labels.Add(messages[278]);
        if(scene)
        {
            //426ea0 writes slot3 again and increments count again if both conditions hold.
            if(blocked){labels[3]=messages[279];labels.Add(ReadOnlyMemory<byte>.Empty);}else labels.Add(messages[279]);
        }
        PushOwned(5,3+(blocked?1:0)+(scene?1:0),1,labels);
    }
    public void OpenDungeon(bool isSwitch,IReadOnlyList<ReadOnlyMemory<byte>> messages)=>PushOwned(5,3,9,isSwitch?[messages[283],messages[284],messages[285]]:[messages[283],messages[284]]);
    public void OpenWorldReveal(IReadOnlyList<ReadOnlyMemory<byte>> messages)=>PushOwned(5,3,2,[messages[275],messages[276],messages[277]]);
    public void OpenWorldMessage(int type,IReadOnlyList<ReadOnlyMemory<byte>> messages,WorldMapContext? map=null,GameState? state=null,Func<int,bool>? isSceneReferenceBlocked=null)
    {
        int first=type switch {77=>296,88=>360,99=>294,_=>throw new ArgumentOutOfRangeException(nameof(type))};
        int vertical=type==88?1:2;
        if(map is not null)
        {
            if(state is null||isSceneReferenceBlocked is null)throw new ArgumentException("World layout requires state and scene predicate.");
            if((uint)state.WorldX>=10||(uint)state.WorldY>=10)throw new InvalidDataException("World menu position exceeds10x10 map.");
            int Read(int at)=>BinaryPrimitives.ReadInt32LittleEndian(map.TileBytes.Slice((state.WorldY*10+state.WorldX)*328+at,4));
            if(isSceneReferenceBlocked(Read(92))&&Read(8)!=0)vertical++;
            if(Read(24)==1&&Read(112)==0&&Read(120)==0)vertical++;
        }
        PushOwned(9,vertical,type,type==88?[messages[first]]:[messages[first],messages[first+1]]);
    }
    private void PushOwned(int horizontal,int vertical,int type,IReadOnlyList<ReadOnlyMemory<byte>> labels)
    {
        if(stack.Count>=4)throw new InvalidOperationException("Native four-entry action menu stack capacity exceeded.");
        if((vertical&1)==0)vertical++;
        var owned=new ReadOnlyMemory<byte>[labels.Count];
        for(int i=0;i<labels.Count;i++){if(labels[i].Length>=64)throw new InvalidDataException("Action label exceeds64-byte native slot.");owned[i]=labels[i].ToArray();}
        stack.Add(new(320-(horizontal+2)*16,240-(vertical+2)*16,horizontal,vertical,type,owned));RenderEnabled=true;
    }
    public void OpenInterior(int mode,IReadOnlyList<ReadOnlyMemory<byte>> messages)=>Open(0,0,5,mode==430?5:3,mode switch {410=>3,420=>5,430=>6,_=>throw new ArgumentOutOfRangeException(nameof(mode))},messages,mode);
    public void OpenTalk(IReadOnlyList<ReadOnlyMemory<byte>> labels,bool hasEventOffer,int currentMode)
    {
        if(currentMode is not (410 or 420 or 430))throw new ArgumentOutOfRangeException(nameof(currentMode));
        if(labels.Count is <1 or >21)throw new InvalidDataException("Talk menu requires1..21 actual choices.");
        if(stack.Count>=4)throw new InvalidOperationException("Native four-entry action menu stack capacity exceeded.");
        int horizontal=hasEventOffer?7:5,vertical=(labels.Count&1)==0?labels.Count+1:labels.Count;
        var owned=new ReadOnlyMemory<byte>[labels.Count];
        for(int i=0;i<labels.Count;i++){if(labels[i].Length>=64)throw new InvalidDataException("Talk label exceeds64-byte slot.");owned[i]=labels[i].ToArray();}
        stack.Add(new(320-(horizontal+2)*16,240-(vertical+2)*16,horizontal,vertical,8,owned));RenderEnabled=true;
    }
    public void SetSelection(int selection)
    {
        if(stack.Count==0||(uint)selection>=(uint)stack[^1].Labels.Length)throw new ArgumentOutOfRangeException(nameof(selection));
        stack[^1].Selection=selection;
    }
    public void CloseTop()
    {
        if(stack.Count==0) throw new InvalidOperationException("Action menu stack is empty.");
        stack.RemoveAt(stack.Count-1); //427940 zeroes all labels/selection of the removed entry.
    }
    public void CloseAll() { stack.Clear(); RenderEnabled=false; }

    //428d70 draws only the top entry. Freeze once per original logical draw, redraw freely afterward.
    public ActionMenuFrame? AdvanceLogicalFrame()
    {
        if(stack.Count==0 || !RenderEnabled) return null;
        Entry top=stack[^1];
        var frame=new ActionMenuFrame(top.X,top.Y,top.Horizontal,top.Vertical,top.Type,top.Selection,flash,
            (ReadOnlyMemory<byte>[])top.Labels.Clone());
        if(flash!=0) flash--;
        return frame;
    }
}




