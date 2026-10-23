using Atria.Game.Exploration;
using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.Combat;

/// <summary>Required owning platform operations; no game-state mutation belongs in Present.</summary>
public interface ICombatPresentationHost
{
    void PumpEventsAndMusic();
    void BeginInputSample(uint milliseconds);
    void CompleteFrameInput();
    void Present(ReadOnlySpan<ushort> completedPixels);
}

/// <summary>Connected4083d0 actor/hit/UI host. The caller still supplies each simulation opportunity.</summary>
public sealed class CombatExecutionHost : ICombatSerialHost
{
    private readonly GameState state;
    private readonly CombatScanInput input;
    private readonly ICombatPresentationHost presentation;
    private readonly Func<uint> readClock,sampleInputClock;
    private readonly CombatGlobalUiRaster uiRaster;
    public CombatExecutionRuntime Execution {get;}
    public CombatGlobalUiRuntime Ui {get;}
    public CombatSerialCoordinator Coordinator {get;}

    public CombatExecutionHost(string root,CombatEntryPreparation entry,GameState state,LegacyRandom random,
        ControlBindings bindings,CombatScanInput input,Func<int> attenuation,ICombatPresentationHost presentation,
        Func<uint> readClock,uint initialRenderDeadline,int initialRenderPeriod=25,int initialVisualDetail=0)
    {
        this.state=state;this.input=input;this.presentation=presentation;this.readClock=readClock;
        ArgumentNullException.ThrowIfNull(presentation);ArgumentNullException.ThrowIfNull(readClock);
        if((uint)initialVisualDetail>3) throw new ArgumentOutOfRangeException(nameof(initialVisualDetail));
        var schedule=new CombatRenderSchedule(initialRenderDeadline,initialRenderPeriod);
        var assets=CombatGlobalUiAssets.Load(root);Ui=new(schedule,assets.Labels);uiRaster=new(assets);
        Execution=new(root,entry,state,random,bindings,input,attenuation,
            (rank,_)=>{Ui.ShowRankNotice(rank);},(item,_)=>{Ui.ShowConsumedItemNotice(item);});
        Execution.VisualDetail=initialVisualDetail;
        Coordinator=new(this,schedule);sampleInputClock=SampleInputClock;
        //4083d0 displays rank45acfc (413930 initializes3) before its first outer iteration.
        Ui.ShowRankNotice(Execution.Controller.ControlRank);
    }

    public CombatDispatchResult OuterOpportunity()=>Coordinator.OuterOpportunity(sampleInputClock);
    public CombatDispatchResult FrozenRenderOpportunity()=>Coordinator.FrozenRenderOpportunity();
    public bool PresentCached()=>Coordinator.TryPresentCached();
    private uint SampleInputClock()
    {
        uint now=readClock();presentation.BeginInputSample(now);return now;
    }

    void ICombatSerialHost.PumpEventsAndMusic()=>presentation.PumpEventsAndMusic();
    bool ICombatSerialHost.ActiveMagic=>Execution.GlobalEffectBusy;
    CombatFrameResult ICombatSerialHost.ActorPass(uint now)=>Execution.AdvanceActors(now);
    void ICombatSerialHost.HitPass(uint now)=>Execution.AdvanceHits(now);
    void ICombatSerialHost.ProcessDebugHeldKeys(uint now){if(input.Modern is null)CombatDebugInput.ApplyHeldShortcut(state,input);}
    bool ICombatSerialHost.QueryNativePauseToggle()=>input.Query(0x19);
    void ICombatSerialHost.ComposeLogicalFrame(uint now)=>Execution.AdvanceLogicalVisual(now);
    void ICombatSerialHost.ProcessFrameInput(uint now,CombatRenderSchedule schedule)
    {
        if(!ReferenceEquals(schedule,Ui.Schedule)) throw new InvalidOperationException("Battle must share one render schedule.");
        Execution.AdvanceGlobalUi(Ui,uiRaster,input,now,Coordinator.NativePause);
        presentation.CompleteFrameInput();
    }
    void ICombatSerialHost.PresentCachedFrame()=>presentation.Present(Execution.CachedFrame.Span);
}
