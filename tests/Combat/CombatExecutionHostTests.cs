using Atria.Audio;
using Atria.Game.Combat;
using Atria.Game.Exploration;
using Atria.Game.State;
using Atria.Resources;

namespace Atria.Tests.Combat;

public static class CombatExecutionHostTests
{
    public static string Run(string root)
    {
        var sparse=Replay(root,0);var frequent=Replay(root,7);
        Check(sparse.State.SequenceEqual(frequent.State) && sparse.Pixels.SequenceEqual(frequent.Pixels) &&
            sparse.RandomState==frequent.RandomState && sparse.RandomCalls==frequent.RandomCalls,
            "Extra presentations cannot alter the connected battle state, RNG or completed logical image.");
        Check(frequent.Presents>sparse.Presents,"Comparison actually exercised additional presentations.");
        return "Connected combat host: real actor/hit/UI pipeline, initial rank notice, held shortcut, pause clock, magic gate and presentation invariance passed.";
    }

    private static Snapshot Replay(string root,int cachedCopies)
    {
        var items=ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root,"ATITEM.DAT")));
        var state=InitialGameState.Create(items,LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root,"TEXTmsg.dat"))));
        var random=new LegacyRandom(1);var held=new HashSet<byte>();uint now=1000;int clockReads=0;
        using var entry=new CombatEntryPreparation(root,new(FixedEncounterKind.Scripted,2),random,()=>123,now,
            _=>new Voice(),_=>{});
        var selection=new CombatPartySelection(state,1);
        selection.ApplyInput((_,key)=>key is CombatSelectionInput.Toggle or CombatSelectionInput.Finish,_=>{});
        selection.Commit(state);entry.BindSelectedParty(selection,0,now);
        var transport=new Transport();var input=new CombatScanInput(held.Contains);
        var bindings=ControlBindings.Parse(File.ReadAllBytes(Path.Combine(root,"BATTLE.CFG")));
        var host=new CombatExecutionHost(root,entry,state,random,bindings,input,()=>-10000,transport,
            ()=>{clockReads++;return now;},now);
        Check(host.Ui.NoticePhase==1 && host.Ui.NoticeX==410 && host.Execution.Controller.ControlRank==3,
            "Battle entry creates original rank3 notice before the first outer iteration.");
        var first=host.OuterOpportunity();Check(first.LogicalFrameProduced && !first.Terminal,"Actual scene starts a live battle.");
        CopyExtra();
        int originalXp=state.GetPartyInt(0,192);
        held.Add(3);held.Add(9);
        //Same timestamp is a distinct actual simulation opportunity; no arbitrary deduplication.
        var second=host.OuterOpportunity();
        Check(!second.LogicalFrameProduced && state.GetPartyInt(0,192)==originalXp+10,
            "Untimed held shortcut runs after actors even when no logical draw is due.");
        CopyExtra();held.Clear();
        now=1025;held.Add(0x13);held.Add(0x11);host.OuterOpportunity();
        Check(host.Ui.Schedule.Period==45 && host.Execution.VisualDetail==1,
            "Actual global UI changes shared render schedule and next-frame detail.");
        CopyExtra();held.Clear();now=1070;held.Add(0x19);var paused=host.OuterOpportunity();
        Check(paused.FrozenRenderRequired && host.Coordinator.NativePause,"Pause begins after the current actor pass.");
        int pumpCount=transport.Pumps,inputSamples=transport.Samples,reads=clockReads;
        held.Clear();host.FrozenRenderOpportunity();CopyExtra();
        held.Add(0x19);var resumed=host.FrozenRenderOpportunity();held.Clear();
        Check(!resumed.FrozenRenderRequired && resumed.SampledMilliseconds==1070 && clockReads==reads &&
            transport.Pumps==pumpCount && transport.Samples==inputSamples,
            "Frozen render retains sampled time and performs no outer pump/input sampling/clock read.");
        //Frozen visual stages may mutate visual counters; they must not run the raw held XP shortcut.
        Check(state.GetPartyInt(0,192)==originalXp+10,"Frozen visual loop cannot advance the outer held shortcut.");
        int party=entry.Runtime.Scene.PartyStartIndex;
        host.Execution.ApplyFrameEffect(party,106,10,1070);
        now=1071;held.Add(3);held.Add(9);var magic=host.OuterOpportunity();
        Check(magic.Actors is null && state.GetPartyInt(0,192)==originalXp+20 && transport.Samples==inputSamples+1,
            "Active magic suppresses actors but retains sampled input and outer debug shortcut.");
        held.Clear();now=1115;host.OuterOpportunity();CopyExtra();
        int[] particleState=host.Execution.Particles.Particles.SelectMany(p=>p.Fields).ToArray();
        byte[] snapshot=entry.Runtime.Actors.SelectMany(a=>a.Fields).Concat(state.PartyRecords)
            .Concat(state.Globals.SelectMany(v=>BitConverter.GetBytes(v)))
            .Concat(particleState.SelectMany(v=>BitConverter.GetBytes(v)))
            .Concat(host.Execution.Magic.GlobalStatus).ToArray();
        var retained=new CombatExecutionHost(root,entry,state,random,bindings,input,()=>-10000,new Transport(),
            ()=>now,now,205,3);
        Check(retained.Ui.Schedule.Period==205 && retained.Execution.VisualDetail==3,
            "A later battle receives retained E/R speed and Q/W detail rather than reinitializing application defaults.");
        return new(snapshot,transport.Pixels,random.State,random.Calls,transport.Presents);

        void CopyExtra(){for(int i=0;i<cachedCopies;i++)Check(host.PresentCached(),"A completed frame must be available.");}
    }
    private sealed record Snapshot(byte[] State,ushort[] Pixels,uint RandomState,ulong RandomCalls,int Presents);
    private sealed class Transport:ICombatPresentationHost
    {
        public int Pumps,Samples,Presents;
        public ushort[] Pixels=new ushort[640*480];
        public void PumpEventsAndMusic()=>Pumps++;
        public void BeginInputSample(uint milliseconds)=>Samples++;
        public void CompleteFrameInput(){}
        public void Present(ReadOnlySpan<ushort> completedPixels){completedPixels.CopyTo(Pixels);Presents++;}
    }
    private sealed class Voice:IBufferedSoundEffect
    {public void SetCurrentVolume(int value){} public void Play(){} public void StopAll(){} public void Dispose(){}}
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
