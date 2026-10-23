using System.Buffers.Binary;
using Atria.Audio;
using Atria.Game.Combat;
using Atria.Game.Exploration;
using Atria.Game.State;
using Atria.Resources;

namespace Atria.Tests.Combat;

// Diagnostic observation of actual resources and connected logic; not a reference-parity assertion.
public static class CombatEncounterDiagnostic
{
    public static string Run(string root,bool diagnostic=true)
    {
        var items=ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root,"ATITEM.DAT")));
        var state=InitialGameState.Create(items,LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root,"TEXTmsg.dat"))));
        var random=new LegacyRandom(1);var held=new HashSet<byte>();uint now=1000;
        using var entry=new CombatEntryPreparation(root,new(FixedEncounterKind.Scripted,2),random,()=>123,now,_=>new Voice(),_=>{});
        var selection=new CombatPartySelection(state,1);
        selection.ApplyInput((_,key)=>key==CombatSelectionInput.Toggle,_=>{});
        selection.ApplyInput((_,key)=>key==CombatSelectionInput.Next,_=>{});
        selection.ApplyInput((_,key)=>key==CombatSelectionInput.Toggle,_=>{});
        selection.ApplyInput((_,key)=>key==CombatSelectionInput.Finish,_=>{});
        selection.Commit(state);entry.BindSelectedParty(selection,0,now);
        var bindings=ControlBindings.Parse(File.ReadAllBytes(Path.Combine(root,"BATTLE.CFG")));
        var host=new CombatExecutionHost(root,entry,state,random,bindings,new CombatScanInput(held.Contains),()=>-10000,new Transport(),()=>now,now);
        if(diagnostic)Console.WriteLine("Diagnostic: scripted scene2, native initial party, both members selected, seed123, supplied 1ms opportunities; NOT original cadence evidence.");
        var observer=new HitObserver(host,entry.Runtime,diagnostic);
        var coordinator=new CombatSerialCoordinator(observer,host.Ui.Schedule);
        held.Add(30);coordinator.OuterOpportunity(()=>now);held.Clear();
        int[] attacks=new int[entry.Runtime.Actors.Count];
        int[] previousHp=entry.Runtime.Actors.Select(a=>Read(a.Fields,0x170)).ToArray();
        bool enemyDamaged=false,partyDamaged=false;
        uint limit=diagnostic?601000u:121000u;
        for(now=1001;now<=limit && !coordinator.Completed;now++)
        {
            coordinator.OuterOpportunity(()=>now);
            for(int i=0;i<previousHp.Length;i++)
            {
                int hp=Read(entry.Runtime.Actors[i].Fields,0x170);
                if(hp<previousHp[i]) {if(i<entry.Runtime.Scene.PartyStartIndex)enemyDamaged=true;else partyDamaged=true;}
                previousHp[i]=hp;
            }
            if(!diagnostic && enemyDamaged && partyDamaged)break;
            for(int i=0;i<attacks.Length;i++)if(Read(entry.Runtime.Actors[i].Fields,0xb1)==6)attacks[i]++;
            if(!diagnostic || now%10000!=0)continue;
            Console.WriteLine($"time={now} frames={coordinator.FrameSerial} randomCalls={random.Calls} result={coordinator.TerminalResult}");
            for(int i=0;i<attacks.Length;i++)
            {
                var a=entry.Runtime.Actors[i];if(a.Binding is null)continue;byte[] f=a.Fields;
                Console.WriteLine($"actor={i} type={a.Binding.Info.Type} active={Read(f,0x224)} hp={Read(f,0x170)} x={Read(f,0x98)} y={Read(f,0x78)} ad={Read(f,0xad):X} motion={Read(f,0xb1)} frame={a.Animation!.FrameIndex} target={entry.Runtime.TargetIndices[i]} attackSamples={attacks[i]} atkDeadline={Read(f,0x329)} alive={Read(f,0x1ec)} dd={f[0xdd]}");
            }
        }
        if(diagnostic)Console.WriteLine($"Diagnostic finished time={now} result={coordinator.TerminalResult} hitCandidates={observer.Candidates} eligible={observer.Eligible}");
        if(!enemyDamaged || !partyDamaged)throw new InvalidOperationException("Actual-resource encounter did not produce both enemy and party damage through the connected animation/geometry/hit pipeline.");
        return "Actual SBATTL02 encounter: ordinary AI/animation/hit execution damages both teams; no injected damage or outcome. Cadence/reference parity remains unverified.";
    }
    private static int Read(byte[] f,int at)=>BinaryPrimitives.ReadInt32LittleEndian(f.AsSpan(at,4));
    private sealed class Transport:ICombatPresentationHost
    { public void PumpEventsAndMusic(){} public void BeginInputSample(uint milliseconds){} public void CompleteFrameInput(){} public void Present(ReadOnlySpan<ushort> pixels){} }
    private sealed class Voice:IBufferedSoundEffect
    {public void SetCurrentVolume(int value){} public void Play(){} public void StopAll(){} public void Dispose(){}}
    private sealed class HitObserver(ICombatSerialHost inner,CombatSceneRuntime runtime,bool verbose):ICombatSerialHost
    {
        public long Candidates,Eligible;
        public bool ActiveMagic=>inner.ActiveMagic;
        public void PumpEventsAndMusic()=>inner.PumpEventsAndMusic();
        public CombatFrameResult ActorPass(uint now)=>inner.ActorPass(now);
        public void HitPass(uint now)
        {
            for(int i=0;i<runtime.Actors.Count;i++)
            {
                var a=runtime.Actors[i];var f=a.Fields;
                if(a.Animation is null || f[0xdd]==0 || Read(f,0xb1)!=6)continue;
                Candidates++;
                if(verbose && Candidates<=10)Console.WriteLine($"HIT time={now} actor={i} frame={a.Animation.BoundFrameIndex} ad={Read(f,0xad):X} team={f[0xa6]} rect={Short(f,0xff)},{Short(f,0x101)},{Short(f,0x103)},{Short(f,0x105)} flags={a.Animation.CurrentFrame.FinalDword:X} priority={a.Animation.CurrentFrame.Words[9]}");
                for(int j=0;j<runtime.Actors.Count;j++)
                {
                    var d=runtime.Actors[j];if(d.Animation is null)continue;
                    if(CombatHitEligibility.CanHit(i,j,f,a.Binding!.Info.Type,a.Animation.CurrentFrame,d.Fields,d.Animation.CurrentFrame))Eligible++;
                    if(verbose && Candidates<=10 && f[0xa6]!=d.Fields[0xa6])Console.WriteLine($" target={j} hurt={Short(d.Fields,0x107)},{Short(d.Fields,0x109)},{Short(d.Fields,0x10b)},{Short(d.Fields,0x10d)} dy={Read(d.Fields,0x78)-Read(f,0x78)} priority={Read(d.Fields,0x11c)} flags={d.Animation.CurrentFrame.FinalDword:X}");
                }
            }
            inner.HitPass(now);
        }
        private static short Short(byte[] f,int at)=>BinaryPrimitives.ReadInt16LittleEndian(f.AsSpan(at,2));
        public void ProcessDebugHeldKeys(uint now)=>inner.ProcessDebugHeldKeys(now);
        public bool QueryNativePauseToggle()=>inner.QueryNativePauseToggle();
        public void ComposeLogicalFrame(uint now)=>inner.ComposeLogicalFrame(now);
        public void ProcessFrameInput(uint now,CombatRenderSchedule schedule)=>inner.ProcessFrameInput(now,schedule);
        public void PresentCachedFrame()=>inner.PresentCachedFrame();
    }
}
