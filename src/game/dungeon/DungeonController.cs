using Atria.Game.Exploration;
using Atria.Game.State;

namespace Atria.Game.Dungeon;

public enum DungeonCommandKind { Sound,RebuildObjects,RefreshVisibility,RunPendingScript,Leave,OpenActionMenu,WalkStarted,ToggleHud,Arrival }
public sealed record DungeonCommand(DungeonCommandKind Kind,int Value=0,DungeonArrivalEffect? Arrival=null);
public readonly record struct DungeonBindings(byte Left,byte Right,byte Up,byte Down,byte Confirm,byte Cancel,byte Hud);

/// <summary>41ef90 priority and modal input. Invoke once per scheduled logical opportunity.</summary>
public sealed partial class DungeonController
{
    private readonly GameState state;
    private readonly DungeonMap map;
    private readonly LegacyRandom random;
    private readonly DungeonArrival arrival;
    public DungeonMotion Motion {get;}
    public DungeonVisibility Visibility {get;}
    public int Modal {get;private set;}
    public int Selection {get;private set;}
    public int TorchCost {get;private set;}
    public bool ExitPending {get;private set;}
    public bool ScriptPending {get;private set;}=true;
    public bool SaveGate {get;private set;}
    public bool HudVisible {get;private set;}=true;
    public bool ActionTileIsSwitch {get;private set;}
    public int TreasureKind {get;private set;}
    public int TreasureValue {get;private set;}
    public int TreasureAmount {get;private set;}
    public int TreasureItem {get;private set;}
    public ReadOnlyMemory<byte> PendingFunction {get;private set;}="enter"u8.ToArray();
    public int AdjustmentCounter=>torchAdjustCalls;
    private int ambientCalls,randomAmbientCalls,torchAdjustCalls;
    public DungeonController(GameState state,DungeonMap map,Atria.Render.Codecs.AnimationFile animation,LegacyRandom random,bool retainedHudVisible=true,int retainedNextTorch=0,ReadOnlyMemory<int> retainedScratch=default,int retainedTorchCost=0,int retainedAdjustmentCounter=0,ReadOnlyMemory<DungeonTorch> retainedTorches=default,int retainedTreasureKind=0,bool initializeState=true)
    {
        this.state=state;this.map=map;this.random=random;TreasureKind=retainedTreasureKind;TorchCost=retainedTorchCost;torchAdjustCalls=retainedAdjustmentCounter;
        HudVisible=retainedHudVisible;Visibility=new(retainedNextTorch,retainedScratch,retainedTorches);Visibility.MarkLocal(state.WorldX,state.WorldY,map.Context.Width);
        Motion=new(state,map,animation);arrival=new(state,map,Visibility);
        if(initializeState) {state.ModeStackDepth=-1;state.DungeonFogDirty=1;}
    }
    public void OpenSwitch() {Modal=3;Selection=map.Read(state.WorldX+state.WorldY*10,92)!=1?1:0;}
    public void OpenTorch() {Modal=99;TorchCost=1;}
    public void RequestExit()=>ExitPending=true;
    public IReadOnlyList<DungeonCommand> Input(DungeonBindings binding,Func<byte,bool> query,Func<byte,bool> held)
    {
        List<DungeonCommand> result=[];
        void Sound(int id)=>result.Add(new(DungeonCommandKind.Sound,id));
        if(++ambientCalls>200) {Sound(9);ambientCalls=0;}
        if(++randomAmbientCalls>350) {Sound(random.Next()%2==0?11:10);randomAmbientCalls=0;}
        if(Motion.Moving) return result;
        if(ExitPending&&!ScriptPending) {result.Add(new(DungeonCommandKind.Leave));return result;}
        if(!SaveGate) {SaveGate=true;result.Add(new(DungeonCommandKind.RebuildObjects));return result;}
        if(state.DungeonFogDirty!=0) {result.Add(new(DungeonCommandKind.RefreshVisibility));return result;}
        if(ScriptPending) {state.ScriptActive=1;ScriptPending=false;result.Add(new(DungeonCommandKind.RunPendingScript));return result;}
        if(Modal==0)
        {
            if(query(binding.Confirm)) {ActionTileIsSwitch=map.Read(state.WorldX+state.WorldY*10,88)==3;result.Add(new(DungeonCommandKind.OpenActionMenu,9));Sound(3);return result;}
            WorldDirection? direction=held(binding.Left)?WorldDirection.Left:held(binding.Right)?WorldDirection.Right:held(binding.Up)?WorldDirection.Up:held(binding.Down)?WorldDirection.Down:null;
            if(direction is {} d) {if(Motion.Begin(d)) {Sound(12);result.Add(new(DungeonCommandKind.WalkStarted));}return result;}
            if(query(binding.Hud)||query(15)) {HudVisible=!HudVisible;result.Add(new(DungeonCommandKind.ToggleHud,HudVisible?1:0));}
        }
        else if(Modal==3)
        {
            if(query(binding.Left)) {if(Selection!=0) {Selection=0;Sound(1);}return result;}
            if(query(binding.Right)) {if(Selection!=1) {Selection=1;Sound(1);}return result;}
            if(query(binding.Confirm)) {Sound(3);map.Write(state.WorldX+state.WorldY*10,92,Selection==0?1:0);result.Add(new(DungeonCommandKind.RebuildObjects));Modal=0;return result;}
            if(query(binding.Cancel)) {Sound(4);Modal=0;}
        }
        else if(Modal==6)
        {if(query(binding.Confirm)||query(binding.Cancel)) {Sound(3);Modal=0;}}
        else if(Modal==99)
        {
            if(query(binding.Confirm))
            {
                Sound(3);Modal=0;
                if(state.ResourceCounterFc9a>0) {Visibility.Place(state.WorldX,state.WorldY,TorchCost);state.ResourceCounterFc9a=unchecked(state.ResourceCounterFc9a-TorchCost);}
                return result;
            }
            if(query(binding.Cancel)) {Sound(4);Modal=0;return result;}
            if(held(binding.Up))
            {if(state.ResourceCounterFc9a>0&&TorchCost<state.ResourceCounterFc9a&&TorchCost<20&&++torchAdjustCalls>1) {TorchCost++;torchAdjustCalls=0;Sound(5);}}
            else if(held(binding.Down)&&state.ResourceCounterFc9a>0&&TorchCost>1&&++torchAdjustCalls>1) {TorchCost--;torchAdjustCalls=0;Sound(5);}
        }
        return result;
    }
    public (DungeonMotionStep Motion,IReadOnlyList<DungeonCommand> Commands) Advance(Func<uint> clock)
    {
        var step=Motion.Advance(clock);List<DungeonCommand> result=[];
        if(step.PlayWalkSound) result.Add(new(DungeonCommandKind.Sound,12));
        if(step.Arrived) foreach(var effect in arrival.Complete())
        {
            switch(effect.Kind)
            {
                case DungeonArrivalEffectKind.BattlePending:result.Add(new(DungeonCommandKind.Sound,3));break;
                case DungeonArrivalEffectKind.ExitPending:ExitPending=true;break;
                case DungeonArrivalEffectKind.ScriptPending:if(!effect.Member.Span.SequenceEqual("dungeon_nofire"u8)) result.Add(new(DungeonCommandKind.Sound,effect.Value==8?8:3));PendingFunction=effect.Member;ScriptPending=true;Modal=0;break;
                case DungeonArrivalEffectKind.TreasureRetained:result.Add(new(DungeonCommandKind.Sound,8));Modal=6;break;
                case DungeonArrivalEffectKind.TreasureMoney:result.Add(new(DungeonCommandKind.Sound,8));Modal=6;TreasureKind=1;TreasureValue=effect.Value;break;
                case DungeonArrivalEffectKind.TreasureItem:result.Add(new(DungeonCommandKind.Sound,8));Modal=6;TreasureKind=2;TreasureItem=effect.Value;TreasureValue=effect.Value;TreasureAmount=effect.Value2;break;
                case DungeonArrivalEffectKind.TreasureTorch:result.Add(new(DungeonCommandKind.Sound,8));Modal=6;TreasureKind=2;TreasureAmount=effect.Value;TreasureValue=effect.Value;break;
            }
            result.Add(new(DungeonCommandKind.Arrival,Arrival:effect));
        }
        return(step,result);
    }
}
