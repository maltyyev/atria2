using Atria.Game.ActionMenu;
using Atria.Game.Interiors;
using Atria.Game.State;
using Atria.Resources;
using Atria.Script;

namespace Atria.Tests.Interiors;

public static class GathaGuildTests
{
    public static string Run(string root)
    {
        static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException("Gatha guild: "+message);}
        using var scripts=BinderArchive.Open(Path.Combine(root,"SCRIPT.000"));
        byte[] source=scripts.ReadMember(scripts.FindResource("02_eve1.scr"u8));
        var raw=ScriptProgram.Parse("02_EVE1",source,false);
        var state=new GameState {Mode=410,ScriptActive=1};
        new ScriptMachine(raw,new ScriptStateHost(state)).Invoke("enter");
        Check(state.ScriptActive==1&&state.People.Count==0,"original empty enter leaves script flag active");
        var fixedProgram=ScriptProgram.Parse("02_EVE1",source);
        Check(fixedProgram.AppliedRepairs.SequenceEqual(new[]{"GATHA-GUILD-002-LIFECYCLE-END"}),"exact repair recorded");
        foreach(string function in new[]{"pre_enter","enter","pre_out","out"})
        {
            state.ScriptActive=1;new ScriptMachine(fixedProgram,new ScriptStateHost(state)).Invoke(function);
            Check(state.ScriptActive==0&&state.People.Count==0,"lifecycle ends without inventing people: "+function);
            if(function.StartsWith("pre_",StringComparison.Ordinal))Check(state.EnterPermission==1,"original entry/exit permission retained");
        }
        byte[] altered=(byte[])source.Clone();altered[0]=(byte)'P';
        Check(ScriptProgram.Parse("02_EVE1",altered).AppliedRepairs.Count==0&&ScriptProgram.Parse("OTHER",source).AppliedRepairs.Count==0&&raw.AppliedRepairs.Count==0,"changed hash/name and disabled repair gates");
        using var scenes=BinderArchive.Open(Path.Combine(root,"AT2SOD.000"));
        var town=SodV2.Parse(scenes.ReadMember(scenes.FindResource("02gatha.sod"u8)));
        var scene=SodV2.Parse(scenes.ReadMember(scenes.FindResource("02_eve1.sod"u8)));
        var interior=new InteriorController();interior.Enter(410,town.GetObject(3),scene,_=>{});
        Check(interior.EventRange==new InteriorEventRange(1,1,7,12),"original quest range");
        for(int id=7;id<=12;id++)state.EventStatuses[id]=4;
        var menus=new ActionMenuController();int executed=0,left=0;
        var messages=LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root,"TEXTmsg.dat")));
        var adapter=new InteriorActionMenuAdapter(menus,interior,state,messages,()=>"02_eve1.scr"u8.ToArray(),_=>executed++,()=>left++,_=>{},()=>{},_=>{});
        adapter.OpenAction();
        for(int attempt=0;attempt<8;attempt++)
        {
            menus.Input(new(Accept:true),adapter.HandleCommand);
            Check(menus.Depth==0&&menus.AdvanceLogicalFrame()!.Type==3&&executed==0,"empty Talk safely returns to one action menu without fabricated dialogue");
        }
        menus.Input(new(Cancel:true),adapter.HandleCommand);Check(left==1,"leave still works");
        // The same room with an available quest must retain a real selectable handler.
        interior.Enter(410,town.GetObject(3),scene,_=>{});state.EventStatuses[12]=0;
        menus.CloseAll();adapter.OpenAction();menus.Input(new(Accept:true),adapter.HandleCommand);
        Check(menus.AdvanceLogicalFrame()!.Type==8&&menus.AdvanceLogicalFrame()!.Labels.Length==1,"uncompleted quest remains available");
        menus.Input(new(Accept:true),adapter.HandleCommand);Check(executed==1,"real quest choice still dispatches");
        state.EventStatuses[12]=4;
        state.People=[new("Person"u8.ToArray(),"Person"u8.ToArray(),"dialogue"u8.ToArray())];
        menus.CloseAll();adapter.OpenAction();menus.Input(new(Accept:true),adapter.HandleCommand);
        Check(menus.AdvanceLogicalFrame()!.Type==8&&menus.AdvanceLogicalFrame()!.Labels.Length==1,"ordinary NPC retained without available quest");
        menus.Input(new(Accept:true),adapter.HandleCommand);Check(executed==2,"ordinary NPC still dispatches");
        return "Gatha guild: raw repeated-entry defect reproduced; exhausted quests/empty Talk, repeated attempts, leave and available-quest dispatch verified.";
    }
}
