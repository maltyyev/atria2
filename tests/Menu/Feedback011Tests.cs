using Atria.Game;
using Atria.Game.ActionMenu;
using Atria.Game.Menu;
using Atria.Game.State;
using Atria.Game.Town;
using Atria.Render.Codecs;
using Atria.Resources;
using Atria.Script;

namespace Atria.Tests.Menu;

public static class Feedback011Tests
{
    public static string Run(string root)
    {
        var bindings=ControlBindings.Parse(File.ReadAllBytes(Path.Combine(root,"BATTLE.CFG")));
        var pressed=new HashSet<byte>();var navigation=new MenuNavigation(bindings,pressed.Remove);
        var messages=LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root,"TEXTmsg.dat")));
        var actions=new ActionMenuController();actions.Open(0,0,5,3,5,messages,420);
        foreach(byte key in new[]{bindings.Down,bindings.Next})
        {
            pressed.Add(key);actions.Input(new(Next:navigation.Next()),_=>{});
            Check(actions.Selection==1,"down/right reaches second vertical row");
            pressed.Add(bindings.Up);actions.Input(new(Previous:navigation.Previous()),_=>{});
            Check(actions.Selection==0,"up returns to first row");
        }
        pressed.Add(bindings.Previous);actions.Input(new(Previous:navigation.Previous()),_=>{});
        Check(actions.Selection==1,"original left alias still wraps");

        var items=ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root,"ATITEM.DAT")));
        var state=InitialGameState.Create(items,messages);
        var assets=TownSceneAssets.Load(root,"01mercen.sod"u8);
        var town=new TownController();int previousTown=1;
        using var scripts=BinderArchive.Open(Path.Combine(root,"SCRIPT.000"));
        var program=ScriptProgram.Parse("01MERCEN",scripts.ReadMember(scripts.FindResource("01MERCEN"u8)));
        for(int attempt=0;attempt<3;attempt++)
        {
            // Reproduce return to main menu with the same town, completed intro and a hidden action stack.
            state.Globals[501]=1;town.SetSavedSelection(1);
            actions.CloseAll();actions.OpenTown(true,messages);actions.SetRenderEnabled(false);
            previousTown=1;
            InitialGameState.ResetRecoveredCore(state,items,messages);
            NewGameSceneReset.Apply(state,town,actions,ref previousTown);
            Check(previousTown==0&&town.SavedSelection==0&&actions.Depth==-1,"new game clears same-town and hidden-menu guards");
            var commands=new List<TownCommand>();
            town.Enter(assets.Scene,assets.Objects,1,previousTown,"01mercen.sod"u8.ToArray(),default,10,commands.Add);
            Check(commands.Count(c=>c.Kind==TownEffectKind.QueueEnterScript)==1,"restarted town queues intro exactly once");
            var observer=new IntroObserver();state.Mode=400;state.ScriptActive=1;
            new ScriptMachine(program,new ScriptStateHost(state,observer)).Invoke("enter");
            Check(observer.Dialogues==11&&state.Globals[501]==1,"original intro emits eleven dialogue effects on every restart");
            var frame=town.AdvanceLogicalFrame(new(ActionMenuDepth:actions.Depth,DialogueActive:0,LabelDelay:0));
            Check(frame.DrawObjects&&frame.DrawLabel,"town marker/name eligible after restart and intro");
            commands.Clear();town.Enter(assets.Scene,assets.Objects,1,1,"01mercen.sod"u8.ToArray(),default,10,commands.Add);
            Check(commands.All(c=>c.Kind!=TownEffectKind.QueueEnterScript),"ordinary same-town re-entry still skips intro");
        }
        return "0.1.1 feedback regressions passed: vertical aliases; three new-game resets; original eleven-line intro; marker gates; same-town return.";
    }
    private static void Check(bool condition,string message)
    {if(!condition)throw new InvalidOperationException(message);}
    private sealed class IntroObserver:IScriptEffects
    {
        public int Dialogues { get; private set; }
        public void Execute(ScriptEffect effect,GameState state)
        {
            if(effect.Kind is not (ScriptEffectKind.DialogueText or ScriptEffectKind.DialogueKey))
                throw new InvalidOperationException("Unexpected effect in initial dialogue fixture.");
            Dialogues++;
        }
    }
}
