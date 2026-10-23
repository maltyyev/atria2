using System.Security.Cryptography;
using System.Text;
using Atria.Game.Controls;
using Atria.Game.Interiors;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Game.World;
using Atria.Render.Codecs;
using Atria.Resources;
using Atria.Script;

namespace Atria.Tests.Persistence;

internal static class AutosaveTests
{
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException("Autosaves: "+message);}
    private static void Reject(Action action)
    {try{action();}catch(IOException){return;}catch(InvalidDataException){return;}catch(ArgumentException){return;}throw new InvalidOperationException("Autosaves: invalid operation accepted.");}
    public static string Run(string root)
    {
        var state=new GameState {Mode=300,Money=23,Year=1450,Month=3,Day=14};
        var metadata=LegacySlotMetadata.Empty([]).WithCurrent("Town"u8,"Region"u8,300,0,1,2,[]);
        var shops=LegacyShopProjection.DecodeAll(new byte[30*612]);var areas=new WorldAreaStore();
        NativeSaveImage Image()=>NativeSaveImage.Capture(state,300,areas,shops,metadata,new("fixture",1,[1]));
        string directory=Path.Combine(Path.GetFullPath(Path.Combine(root,"..")),"audit","implementation","autosave-tests",Guid.NewGuid().ToString("N"));
        var store=new NativeSaveStore(directory,root);
        Check(store.InspectLibrary(false).Length==0&&store.InspectLibrary(true).Single().IsNew,"empty save/load libraries");
        for(int i=0;i<12;i++){store.WriteNative(store.NextManualSlot(),Image());File.SetLastWriteTimeUtc(store.GetSlotPath(i),new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc).AddMinutes(i));}
        var manual=Enumerable.Range(0,12).Select(i=>SHA256.HashData(File.ReadAllBytes(store.GetSlotPath(i)))).ToArray();
        Check(store.NextManualSlot()==12&&store.InspectLibrary(false)[0].Index==11&&store.InspectLibrary(true).Length==13,"more than five manual saves, newest first and New row");
        for(int limit=1;limit<=5;limit++)
        {
            for(int i=0;i<9;i++){state.Money=limit*100+i;store.WriteAutomatic(Image(),limit);var current=store.InspectLibrary(false);Check(current.Count(s=>s.Automatic)<=limit,"bounded rotation");Check(current.Where(s=>s.Automatic).Any(s=>store.ReadSlot(s.Index)!.Native!.State.Money==state.Money),"new generation committed");}
            Check(store.InspectLibrary(false).Count(s=>s.Automatic)==limit,"configured retained count");
        }
        store.PruneAutomatic(1);Check(store.InspectLibrary(false).Count(s=>s.Automatic)==1,"decreasing limit applies immediately");
        for(int i=0;i<12;i++)Check(SHA256.HashData(File.ReadAllBytes(store.GetSlotPath(i))).AsSpan().SequenceEqual(manual[i]),"rotation cannot mutate manual saves");
        // Force failure at the next free automatic destination. Existing generation must survive.
        var before=store.InspectLibrary(false).Single(s=>s.Automatic);byte[] prior=File.ReadAllBytes(store.GetSlotPath(before.Index));
        int free=Enumerable.Range(1,6).Select(i=>-i).First(i=>i!=before.Index);Directory.CreateDirectory(store.GetSlotPath(free));
        Reject(()=>store.WriteAutomatic(Image(),1));Check(File.ReadAllBytes(store.GetSlotPath(before.Index)).AsSpan().SequenceEqual(prior),"failed commit does not prune old checkpoint");
        Directory.Delete(store.GetSlotPath(free));
        File.WriteAllBytes(store.GetSlotPath(12),[0,1,2]);File.WriteAllBytes(Path.Combine(directory,"atria1.sav"),[0]);
        Check(store.InspectLibrary(false).Count(s=>s.Status==SaveSlotStatus.Invalid)==1,"corrupt save remains visible and noncanonical name ignored");
        var menu=new SaveLoadMenuController();var rows=store.InspectLibrary(true);menu.Enter(SaveLoadMenuKind.Save,0,_=>{});menu.SetCount(rows.Length);
        for(int i=0;i<11;i++)menu.Input(new(Down:true),rows.Select(r=>r.Metadata).ToArray(),_=>{});
        Check(menu.Selection==11&&menu.Top==7,"scroll reaches rows beyond first screen");menu.PointerSelect(150,100);Check(menu.Selection==7,"pointer resolves scrolled row");
        menu.Enter(SaveLoadMenuKind.Load,0,_=>{});menu.Input(new(Accept:true),[],_=>{});Check(!menu.Confirmation&&!menu.EmptyNotice,"empty load list has no invalid selection");
        var bindings=ControlBindings.Parse(File.ReadAllBytes(Path.Combine(root,"BATTLE.CFG")));var profile=ControlProfile.Defaults(bindings);
        string old=Encoding.UTF8.GetString(ControlProfileStore.Encode(profile)).Replace("\"autosaveLimit\": 3,","");
        Check(ControlProfileStore.Decode(Encoding.UTF8.GetBytes(old)).AutosaveLimit==3,"old settings migrate to three autosaves");
        foreach(int n in new[]{1,5}){profile.AutosaveLimit=n;Check(profile.Clone().AutosaveLimit==n,"settings retain count");}
        foreach(int n in new[]{0,6}){profile.AutosaveLimit=n;Reject(()=>ControlProfileStore.Decode(ControlProfileStore.Encode(profile)));}
        Raster(root,rows);Replay(root);
        return "Autosave checks passed: unlimited manual list/scroll, 1..5 rotation, failed write preservation, corrupt entries, settings migration, colored labels and scenario checkpoint replay before/after battles without duplicate effects.";
    }
    private static void Raster(string root,SaveSlotInfo[] rows)
    {
        using var archive=BinderArchive.Open(Path.Combine(root,"AT2SPR.000"));
        var renderer=new SaveLoadMenuRenderer(SpriteFile.Decode(archive.ReadMember(archive.FindResource("ETC"u8))),BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root,"ENG1.FNT")),true),BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root,"ENG.FNT")),false),LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root,"TEXTmsg.dat"))));
        var plan=renderer.BuildPlan(new(SaveLoadMenuKind.Save,0,false,0,false,0,0),rows);
        Check(plan.Any(p=>p.Color==0x354a)&&plan.Any(p=>p.Color==0x443e),"automatic/manual tag colors");
        ushort[] pixels=new ushort[640*480];renderer.Draw(plan,pixels);Check(pixels.Contains((ushort)0x354a)&&pixels.Contains((ushort)0x443e),"actual fonts render both tags");
    }
    private static void Replay(string root)
    {
        // Native host mutations, nested locals, branch-dependent results and two battles.
        byte[] synthetic=Encoding.ASCII.GetBytes("int g; helper(int x){int i;i=0;while(i<2){get_money(x);i=i+1;} battle_start2(x);return check_money();} run(){int x;x=check_money();get_item(2);g=helper(x+1);if(g>5){battle_start3(g);get_money(7);}set_eventstatus(4,4);get_xp(19);script_end();}");
        using var archive=BinderArchive.Open(Path.Combine(root,"SCRIPT.000"));
        foreach(var scenario in new[]{(Member:"fixture.scr",Function:"run",Source:synthetic,Expected:2),(Member:"01_BAR",Function:"enter",Source:archive.ReadMember(archive.FindResource("01_BAR"u8)),Expected:1)})
        {
        var program=ScriptProgram.Parse(scenario.Member,scenario.Source);var state=new GameState {Mode=410,Money=4,ScriptActive=1};var effects=new BattleEffects();
        var host=new CheckpointScriptHost(new ScriptStateHost(state,effects));
        var checkpoints=new List<(bool Before,byte[] State,ScriptCheckpoint Point,int Battles,int Dialogues)>();
        void Capture(bool before)=>checkpoints.Add((before,NativeGameStateCodec.Encode(state),new(scenario.Member,scenario.Function,Convert.ToHexString(SHA256.HashData(scenario.Source)),0,0,host.Capture()),effects.Count,effects.Dialogues));
        host.BeforeCall=c=>{if(c.Function.StartsWith("battle_start"))Capture(true);};host.AfterCall=c=>{if(c.Function.StartsWith("battle_start"))Capture(false);};
        new ScriptMachine(program,host).Invoke(scenario.Function);byte[] expected=NativeGameStateCodec.Encode(state);
        Check(checkpoints.Count==scenario.Expected*2,"all battle boundaries captured "+scenario.Member);
        foreach(var point in checkpoints)
        {
            CheckpointScriptHost.Validate(program,point.Point);
            var restored=NativeGameStateCodec.Decode(point.State);var remaining=new BattleEffects();
            var replay=new CheckpointScriptHost(new ScriptStateHost(restored,remaining),point.Point.Calls);
            new ScriptMachine(program,replay).Invoke(scenario.Function);replay.VerifyReplayComplete();
            Check(NativeGameStateCodec.Encode(restored).AsSpan().SequenceEqual(expected),"resumed full state matches uninterrupted run");
            Check(remaining.Count==effects.Count-point.Battles&&remaining.Dialogues==effects.Dialogues-point.Dialogues,"already completed battles/dialogues not repeated");
        }
        var broken=checkpoints[1].Point;var calls=broken.Calls.ToArray();calls[0]=calls[0] with {Offset=calls[0].Offset+1};
        Reject(()=>CheckpointScriptHost.Validate(program,broken with {Calls=calls}));
        }
    }
    private sealed class BattleEffects:IScriptEffects
    {
        public int Count,Dialogues;
        public void Execute(ScriptEffect effect,GameState state)
        {
            if(effect.Kind is ScriptEffectKind.DialogueKey or ScriptEffectKind.DialogueText){Dialogues++;return;}
            if(effect.Kind is not (ScriptEffectKind.BattleWithPostAction or ScriptEffectKind.ForcedBattleWithPostAction))throw new InvalidOperationException("Unexpected test effect.");
            Count++;state.Money+=31;state.EndingResetFlag=0;
        }
    }
}
