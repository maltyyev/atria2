using Atria.Game.Combat;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Resources;
using Atria.Script;

namespace Atria.Tests;

public static class Quest63RecoveryTests
{
    public static void Run(string root,ScriptProgram program)
    {
        Check(program.AppliedRepairs.Contains("QUEST-063-PATCH-161-162-RU-TRANSLATION"),"recovery must be identified explicitly");
        Check(ScriptSyntax.Analyze(program).Errors.Count==0,"recovered two functions parse with actual engine grammar");
        GameState Checkpoint(GameState value)
        {
            byte[] blank=new byte[LegacySaveImage.Size];"ATRIA-II SAVE FILE (c)Jamie system development\0"u8.CopyTo(blank);
            var loaded=LegacySaveImage.Parse(LegacySaveImage.Parse(blank).WithRecoveredState(value).Encode()).ReadRecoveredState();
            Check(loaded.Globals[901]==value.Globals[901]&&new[]{63,161,162}.All(i=>loaded.EventStatuses[i]==value.EventStatuses[i]),"checkpoint retains quest stage and allthree eventstates through recovered legacy layout");
            return loaded;
        }
        var state=new GameState();var effects=new Outcomes(1,1);var machine=new ScriptMachine(program,new ScriptStateHost(state,effects));
        machine.Invoke("sub_063");Check(state.EventStatuses[63]==1&&state.EventStatuses[161]==1&&state.EventStatuses[162]==1,"acceptance retains original map triggers");
        Check(new Atria.Game.Status.StatusTextList(state,4).Ids.Contains(63),"actual accepted quest appears in journal before completion");
        machine.Invoke("suba_063");Check(state.Globals[901]==1&&effects.Battles.Count==0,"original first meeting establishes stage1 without invented battle");
        state=Checkpoint(state);machine=new(program,new ScriptStateHost(state,effects));
        machine.Invoke("suba_161");Check(state.Globals[901]==2&&effects.Battles.SequenceEqual(new[]{154})&&effects.StagesAtBattle.SequenceEqual(new[]{2}),"stage1->2 precedes actual forced encounter154");
        Check(state.EventStatuses[63]==1,"161 never fabricates completion or reward");
        int dialogues=effects.Dialogues;machine.Invoke("suba_161");Check(effects.Dialogues==dialogues&&effects.Battles.Count==1,"revisiting161 after stage2 does not replay encounter");
        state=Checkpoint(state);machine=new(program,new ScriptStateHost(state,effects));
        machine.Invoke("suba_162");Check(state.Globals[901]==3&&effects.Battles.SequenceEqual(new[]{154,155})&&effects.StagesAtBattle.SequenceEqual(new[]{2,3}),"stage2->3 precedes forced encounter155");
        Check(state.EventStatuses[63]==2&&state.EventStatuses[161]==2&&state.EventStatuses[162]==2&&state.Money==0&&state.PendingExperience==0,"only victory155 completes allthree events, reward remains original callback");
        state=Checkpoint(state);machine=new(program,new ScriptStateHost(state,effects));
        machine.Invoke("sub_063");Check(state.Money==500&&state.PendingExperience==12000&&state.ExperiencePending==1&&state.EventStatuses[63]==4,"original reward500gold12000XP exactlyonce");
        machine.Invoke("sub_063");machine.Invoke("suba_162");Check(state.Money==500&&state.PendingExperience==12000&&effects.Battles.Count==2,"repeat completion/reward callbacks do not duplicate rewards/battles");
        foreach(int stage in new[]{0,1,3})
        {var skipped=new GameState();skipped.Globals[901]=(short)stage;var fixture=new Outcomes();new ScriptMachine(program,new ScriptStateHost(skipped,fixture)).Invoke("suba_162");Check(fixture.Battles.Count==0&&fixture.Dialogues==0,"162 exact guard excludes unrelated stage");}
        var lost=new GameState();lost.Globals[901]=1;lost.EventStatuses[63]=lost.EventStatuses[161]=lost.EventStatuses[162]=1;
        var losses=new Outcomes(0,0);var loseMachine=new ScriptMachine(program,new ScriptStateHost(lost,losses));
        loseMachine.Invoke("suba_161");Check(lost.Globals[901]==2&&lost.EventStatuses[63]==1,"authentic patch advances stage before loss154; no rollback invented");
        loseMachine.Invoke("suba_162");Check(lost.Globals[901]==3&&lost.EventStatuses[63]==1&&lost.EventStatuses[161]==1&&lost.EventStatuses[162]==1,"loss155 does not complete quest; prebattle stage3 remains native behavior");
        var declined=new GameState();var decline=new Outcomes(){Accept=false};new ScriptMachine(program,new ScriptStateHost(declined,decline)).Invoke("sub_063");Check(declined.EventStatuses[63]==4&&declined.EventStatuses[161]==0&&declined.EventStatuses[162]==0,"native decline closes63 at4 and leaves encounter triggers disabled");
        using var infos=BinderArchive.Open(Path.Combine(root,"INF.000"));using var animations=BinderArchive.Open(Path.Combine(root,"ANI.000"));
        using var backgrounds=BinderArchive.Open(Path.Combine(root,"BMP.000"));using var sprites=BinderArchive.Open(Path.Combine(root,"SPR.000"));
        foreach(int id in new[]{154,155})
        {
            var scene=CombatSceneDefinition.Load(root,new(FixedEncounterKind.Forced,id));
            Check(scene.Source.Count==16&&scene.Actors.Count==15&&scene.PartySpawns.Count==6,"supplied quest battle has real15 enemies and party marker");
            Check(backgrounds.FindResource(scene.Source.BackgroundReference.Span)>=0,"supplied quest battle background exists");
            int[] types=scene.Actors.Select(a=>a.Type).ToArray();
            Check(id==154?types.SequenceEqual(new[]{26,31,31,31,31,31,31,31,30,30,30,30,30,30,30}):types.SequenceEqual(Enumerable.Repeat(18,14).Append(38)),"recovered encounter matches original actor declarations");
            foreach(var actor in scene.Actors.DistinctBy(a=>a.Type))
            {
                int infoIndex=infos.FindResource(System.Text.Encoding.ASCII.GetBytes(actor.InfoResource));Check(infoIndex>=0,"actual quest enemy INF exists");
                foreach(var character in CombatInfoDefinition.Parse(infos.ReadMember(infoIndex)).Characters)
                {
                    int ani=animations.FindResource(character.AnimationResource.Span);Check(ani>=0,"quest enemy ANI exists");
                    var animation=Atria.Render.Codecs.AnimationFile.Decode(animations.ReadMember(ani));Check(sprites.FindResource(animation.SpriteReference)>=0,"quest enemy SPR exists");
                }
            }
        }
    }
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException("Quest63 recovery: "+message);}
    // Only an explicitly scripted test outcome. This does not implement, win or replace real combat.
    private sealed class Outcomes(params int[] battleResults):IScriptEffects
    {
        private int outcome;
        public bool Accept {get;init;}=true;
        public int Dialogues {get;private set;}
        public List<int> Battles {get;}=[];
        public List<int> StagesAtBattle {get;}=[];
        public void Execute(ScriptEffect effect,GameState state)
        {
            if(effect.Kind==ScriptEffectKind.ForcedBattleWithPostAction)
            {
                Check(outcome<battleResults.Length,"fixture has no predeclared combat outcome");
                Check(state.PendingBattle==effect.Call.Arguments[0].Integer&&state.BattleVariant==1&&state.EndingResetFlag==1,"battle callback sees actual request before outcome");
                Battles.Add(state.PendingBattle);StagesAtBattle.Add(state.Globals[901]);state.Globals[61]=(short)battleResults[outcome++];state.EndingResetFlag=0;return;
            }
            Check(effect.Kind is ScriptEffectKind.DialogueText or ScriptEffectKind.DialogueKey or ScriptEffectKind.QuestionText or ScriptEffectKind.QuestionKey,"unexpected recovered effect");
            Dialogues++;
            if(effect.Kind is ScriptEffectKind.QuestionText or ScriptEffectKind.QuestionKey)state.Globals[10]=(short)(Accept?1:2);
        }
    }
}
