using Atria.Game.State;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Game.Interiors;

public readonly record struct InteriorEventRange(int Enabled,int Variant,int First,int Last);
public enum InteriorCommandKind { SetEventRange, SetScriptContext, QueueEnter, InitializeObjectSprites, InitializeItemSprites,
    SelectEventOffer, OpenActionMenu, Sound, OpenTalkMenu, BeginBuy, BeginSell, ReleaseItemSprites, LeaveWithPreOut }
public readonly record struct InteriorCommand(InteriorCommandKind Kind,int Value=0,int Value2=0,ReadOnlyMemory<byte> Bytes=default);
public readonly record struct InteriorRenderFrame(bool DrawObject,int ObjectIndex,int X,int Y,bool DrawBuy,bool DrawSell);
public sealed record InteriorSnapshot(int Mode,InteriorEventRange EventRange,bool FirstEntry,int SelectedObject);

/// <summary>421410/41ddf0/42f1b0 entry plus exact first-entry/confirm action gating.</summary>
public sealed class InteriorController
{
    public int Mode { get; private set; }
    public SodV2? Scene { get; private set; }
    public InteriorEventRange EventRange { get; private set; }
    public bool FirstEntry { get; private set; }
    public int SelectedObject { get; set; }
    public bool Buying { get; private set; }
    public bool Selling { get; private set; }
    public InteriorSnapshot CaptureRuntime()
    {if(Buying||Selling)throw new NotSupportedException("Active trade cannot be snapshotted.");return new(Mode,EventRange,FirstEntry,SelectedObject);}
    public void RestoreRuntime(InteriorSnapshot saved,SodV2 scene)
    {
        if(saved.Mode is not (410 or 420 or 430)||saved.SelectedObject<0||(scene.Count>0&&saved.SelectedObject>=scene.Count))throw new InvalidDataException("Invalid interior snapshot.");
        Mode=saved.Mode;EventRange=saved.EventRange;FirstEntry=saved.FirstEntry;SelectedObject=saved.SelectedObject;Scene=scene;Buying=Selling=false;
    }
    public void Enter(int mode,SodObject outerObject,SodV2 scene,Action<InteriorCommand> emit)
    {
        if(mode is not (410 or 420 or 430)) throw new ArgumentOutOfRangeException(nameof(mode));
        ArgumentNullException.ThrowIfNull(scene);ArgumentNullException.ThrowIfNull(emit);
        if(outerObject.GetString(0).Length>=16) throw new InvalidDataException("Interior reference exceeds recovered16-byte destination.");
        Mode=mode;Scene=scene;FirstEntry=true;SelectedObject=0;Buying=Selling=false;
        EventRange=new(outerObject.GetInteger(26),outerObject.GetInteger(27),outerObject.GetInteger(28),outerObject.GetInteger(29));
        emit(new(InteriorCommandKind.SetEventRange));
        emit(new(InteriorCommandKind.SetScriptContext,Bytes:scene.ScriptReference.ToArray()));
        if(mode!=420 && scene.Count>0) emit(new(InteriorCommandKind.InitializeObjectSprites,Bytes:scene.SpriteReference.ToArray()));
        emit(new(InteriorCommandKind.QueueEnter,Bytes:"enter"u8.ToArray()));
        if(mode==430) emit(new(InteriorCommandKind.InitializeItemSprites,Bytes:"item.spr"u8.ToArray()));
    }
    public void SetEventOfferEnabled(int value)=>EventRange=EventRange with { Enabled=value };
    public void HandleInput(bool confirmPressed,Action<InteriorCommand> emit)
    {
        if(Scene is null) throw new InvalidOperationException("Interior not loaded.");
        if(FirstEntry)
        {
            if(EventRange.Enabled!=0) emit(new(InteriorCommandKind.SelectEventOffer));
            // ASM41df21 writes1, unlike410/430 clearing their entry latch.
            FirstEntry=Mode==420;
            OpenAction(emit);
            if(Mode!=410) return;
        }
        if(Mode==430 && (Buying||Selling)) return; // ShopSession owns its active input branch.
        if(confirmPressed) {emit(new(InteriorCommandKind.Sound,3));OpenAction(emit);}
    }
    public void ChooseAction(int choice,Action<InteriorCommand> emit)
    {
        if(Mode==430)
        {
            switch(choice)
            {
                case 0:Buying=true;Selling=false;emit(new(InteriorCommandKind.BeginBuy));break;
                case 1:Selling=true;Buying=false;emit(new(InteriorCommandKind.BeginSell));break;
                case 2:emit(new(InteriorCommandKind.OpenTalkMenu));break;
                case 3:emit(new(InteriorCommandKind.ReleaseItemSprites));emit(new(InteriorCommandKind.LeaveWithPreOut));break;
            }
        }
        else if(choice==0) emit(new(InteriorCommandKind.OpenTalkMenu));
        else if(choice==1) emit(new(InteriorCommandKind.LeaveWithPreOut));
    }
    public void ReturnFromTrade(Action<InteriorCommand> emit) {Buying=Selling=false;OpenAction(emit);}
    public InteriorRenderFrame BuildRenderFrame(bool hasObjectSprites)
    {
        var scene=Scene??throw new InvalidOperationException("Interior not loaded.");
        bool draw=Mode!=420 && hasObjectSprites;
        if(!draw) return new(false,SelectedObject,0,0,Buying,Selling);
        if((uint)SelectedObject>=(uint)scene.Count) throw new InvalidDataException("Interior selected object exceeds scene records.");
        var obj=scene.GetObject(SelectedObject);
        return new(true,SelectedObject,obj.GetInteger(0),obj.GetInteger(1),Buying,Selling);
    }
    private void OpenAction(Action<InteriorCommand> emit)=>emit(new(InteriorCommandKind.OpenActionMenu,Mode==430?5:3,Mode switch {410=>3,420=>5,_=>6}));
}

public sealed record EventOffer(int EventId,ReadOnlyMemory<byte> ScriptMember,ReadOnlyMemory<byte> Function,int Status);
public static class InteriorEventOffers
{
    /// <summary>42c120 bounded status search. Null clears offer flags; no quest outcome or handler is invented.</summary>
    public static EventOffer? Select(GameState state,InteriorEventRange range)
    {
        if((uint)range.First>=GameState.EventStatusCount || (uint)range.Last>=GameState.EventStatusCount || range.First>range.Last)
            throw new InvalidDataException("Interior event range is outside status bank.");
        for(int id=range.First;id<=range.Last;id++)
        {
            state.EventCounter=id;
            int status=state.EventStatuses[id];
            if(status==5) return null;
            if(status is 3 or 4) continue;
            if(status is not (0 or 1 or 2)) throw new InvalidDataException("Unknown native event status would stall offer selection.");
            if(range.Variant==2 && status!=1) continue;
            string prefix=range.Variant switch {1=>"sub_",2=>"suba_",_=>throw new InvalidDataException("Unrecovered event-offer variant.")};
            return new(id,"subevent.scr"u8.ToArray(),System.Text.Encoding.ASCII.GetBytes(prefix+id.ToString("D3",System.Globalization.CultureInfo.InvariantCulture)),status);
        }
        state.EventCounter=range.Last+1;return null;
    }
}

