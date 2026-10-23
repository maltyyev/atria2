using System.Text;
using Atria.Game.Interiors;
using Atria.Game.State;
using Atria.Game.Town;
using Atria.Resources;
using Atria.Script;

namespace Atria.Tests.Interiors;

public static class GeographerTests
{
    public static string Run(string root)
    {
        static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException("Geographer: "+message);}
        using var scenes=BinderArchive.Open(Path.Combine(root,"AT2SOD.000"));
        using var scripts=BinderArchive.Open(Path.Combine(root,"SCRIPT.000"));
        var town=SodV2.Parse(scenes.ReadMember(scenes.FindResource("03uga.sod"u8)));
        var outer=town.GetObject(4);
        Check(outer.GetString(0).Span.SequenceEqual("03_eve1.sod"u8)&&outer.GetString(1).Span.SequenceEqual("03_eve1.scr"u8),"actual town doorway identity");
        byte[] original=scenes.ReadMember(scenes.FindResource(outer.GetString(0).Span));
        Check(SodV2.Parse(original).ScriptReference.Span.SequenceEqual("03_zero.scr"u8)&&scripts.FindResource("03_zero.scr"u8)<0,"original dangling header reproduced");
        byte[] corrected=TownSceneRepairs.Normalize("03_EVE1",original);
        Check(!ReferenceEquals(corrected,original)&&corrected.AsSpan(0,94).SequenceEqual(original.AsSpan(0,94))&&corrected.AsSpan(107).SequenceEqual(original.AsSpan(107)),"correction owns its bytes and changes only script field");
        Check(ReferenceEquals(TownSceneRepairs.Normalize("OTHER",original),original),"member identity gate");
        byte[] altered=(byte[])original.Clone();altered[119]^=1;
        Check(ReferenceEquals(TownSceneRepairs.Normalize("03_EVE1",altered),altered),"source hash gate");
        byte[] truncated=original[..119];
        Check(ReferenceEquals(TownSceneRepairs.Normalize("03_EVE1",truncated),truncated),"length gate");
        foreach(var entry in scenes.Entries)
        {
            if(entry.Name=="03_EVE1")continue;
            byte[] other=scenes.ReadMember(entry.Index);
            Check(ReferenceEquals(TownSceneRepairs.Normalize(entry.Name,other),other),"unrelated scene unchanged: "+entry.Name);
        }
        var assets=TownSceneAssets.Load(root,outer.GetString(0).Span);
        var controller=new InteriorController();var commands=new List<InteriorCommand>();
        controller.Enter(410,outer,assets.Scene,commands.Add);
        var context=commands.Single(c=>c.Kind==InteriorCommandKind.SetScriptContext).Bytes;
        int index=scripts.FindResource(context.Span);
        Check(index>=0,"queued interior scenario must exist (0.3.1 crashes here)");
        Check(context.Span.SequenceEqual(outer.GetString(1).Span),"interior and doorway use the same existing lifecycle script");
        var program=ScriptProgram.Parse(scripts.Entries[index].Name,scripts.ReadMember(index));
        var state=new GameState();var machine=new ScriptMachine(program,new ScriptStateHost(state));
        foreach(string function in new[]{"pre_enter","enter","pre_out","out"})
        {
            state.ScriptActive=1;machine.Invoke(function);
            Check(state.ScriptActive==0,"lifecycle terminates: "+function);
            if(function.StartsWith("pre_",StringComparison.Ordinal))Check(state.EnterPermission==1,"door permission: "+function);
        }
        Check(controller.EventRange==new InteriorEventRange(1,1,13,17),"geographer's original quest range preserved");
        var subevent=ScriptProgram.Parse("SUBEVENT",scripts.ReadMember(scripts.FindResource("subevent.scr"u8)));
        var messages=LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root,"TEXTmsg.dat")));
        for(int id=13;id<=17;id++)
        {
            var offer=InteriorEventOffers.Select(state,controller.EventRange);
            Check(offer is not null&&offer.EventId==id,"next original quest available");
            var talk=InteriorTalkMenu.Build(state,context,offer,messages);
            Check(talk.Choices.Count==1&&talk.Select(0).IsEvent&&subevent.Functions.ContainsKey(Encoding.ASCII.GetString(talk.Select(0).Function.Span)),"quest dialogue still dispatches to real SUBEVENT handler");
            state.EventStatuses[id]=4;
        }
        Check(InteriorEventOffers.Select(state,controller.EventRange) is null,"completed range has no invented extra quest");
        Check(scenes.ReadMember(scenes.FindResource(outer.GetString(0).Span)).AsSpan().SequenceEqual(original),"original member remains immutable");
        return "Geographer: original dangling reference reproduced; real scene/controller, all lifecycle handlers and five original quest offers pass.";
    }
}
