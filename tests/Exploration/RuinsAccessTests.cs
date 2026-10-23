using Atria.Game.ActionMenu;
using Atria.Game.Exploration;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Game.World;
using Atria.Resources;
using Atria.Script;

namespace Atria.Tests.Exploration;

public static class RuinsAccessTests
{
    public static string Run(string root,string? fixture=null)
    {
        static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException("Ruins access: "+message);}
        using var archive=BinderArchive.Open(Path.Combine(root,"AT2AMF.000"));
        byte[] raw=archive.ReadMember(archive.FindResource("ground1.amf"u8));
        var map=WorldMapContext.Parse(raw);var assets=WorldSceneAssets.Load(root,map,raw);
        var renderer=new WorldSceneRenderer(assets);
        var state=new GameState();state.Events[21]=1;state.Events[41]=1;
        if(fixture is not null)
        {
            var saved=NativeSaveImage.Parse(File.ReadAllBytes(fixture));state=saved.State;
            var world=SessionRuntimeCodec.Decode(saved.Context).Exploration??throw new InvalidDataException("World context missing.");
            Check(world.Map.Member.AsSpan().SequenceEqual("ground1.amf"u8),"user save region");
            world.Map.Header.CopyTo(raw,0);world.Map.Tiles.CopyTo(raw,313);map=WorldMapContext.Parse(raw);
            renderer.RestoreRuntime(world.Renderer!);
            Check(state.Events[21]==1&&state.Events[41]==1&&state.Events[51]==0,"actual user prerequisite flags");
        }
        state.Mode=300;state.WorldX=1;state.WorldY=7;
        Check(map.SelectScene(1,7).SceneMember.Span.SequenceEqual("05ancien.sod"u8)&&map.SelectScene(4,7).SceneMember.Span.SequenceEqual("02gatha.sod"u8),"ruins three cells west of Gatha");
        renderer.RebuildBase(map,state);
        Check(renderer.IsSceneReferenceBlocked(5),"native425c90 unlocks scene object5 when event(2,1)==1; 0.3.3 reverses this condition");
        var arrival=new ExplorationArrival(state,map,new LegacyRandom(1)).CompleteStep(renderer.IsSceneReferenceBlocked);
        Check(arrival is {Kind:ExplorationEffectKind.Message,Value:99},"arrival offers actual enter prompt");
        var messages=LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root,"TEXTmsg.dat")));
        var menu=new ActionMenuController();menu.OpenWorld(map,state,renderer.IsSceneReferenceBlocked,messages);
        Check(menu.AdvanceLogicalFrame()!.Labels.Length==4,"standing on tile also offers manual entry");
        using var scripts=BinderArchive.Open(Path.Combine(root,"SCRIPT.000"));
        ScriptProgram Program(string name)=>ScriptProgram.Parse(name,scripts.ReadMember(scripts.FindResource(System.Text.Encoding.ASCII.GetBytes(name))));
        var effects=new RecordEffects();var host=new ScriptStateHost(state,effects);
        state.ScriptActive=1;state.EnterPermission=0;
        new ScriptMachine(Program("05ANCIEN"),host).Invoke("pre_enter");
        Check(state.EnterPermission==1&&state.ScriptActive==0&&effects.Calls.Count==2,"existing ruins story gate permits entry with actual prerequisite state");
        new ScriptMachine(Program("05_IN"),host).Invoke("pre_enter");
        Check(state.DungeonEntryPending==1&&state.PendingDungeonMap.AsSpan().SequenceEqual("md2.amf"u8),"ruins entrance routes into MD2");
        return "Ruins: real world tile/renderer, retained save condition, arrival prompt, manual entry and original05ANCIEN/05_IN gates reach MD2; no source save modified.";
    }
    private sealed class RecordEffects:IScriptEffects
    {
        public List<ScriptEffect> Calls {get;}=[];
        public void Execute(ScriptEffect effect,GameState state)=>Calls.Add(effect);
    }
}
