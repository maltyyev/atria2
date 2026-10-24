using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Game.Status;
using Atria.Resources;
using Atria.Script;

namespace Atria.Tests.Status;

public static class StoryQuestTests
{
    public static void Run(string root)
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Story journal: " + message);
        }
        using var archive = BinderArchive.Open(Path.Combine(root, "SCRIPT.000"));
        var state = new GameState();
        var effects = new Outcomes();
        void Run(string member, string function)
        {
            var entry = archive.Entries.Single(entry => entry.Name == member);
            new ScriptMachine(ScriptProgram.Parse(member, archive.ReadMember(entry.Index)), new ScriptStateHost(state, effects)).Invoke(function);
        }
        bool Has(int id, QuestMilestones milestone) => state.Journal.Progress(state, id).HasFlag(milestone);
        Check(!StoryQuests.Ids.Any(id => QuestJournalText.IsVisible(state, id)), "empty state has no fabricated quests");
        foreach (int branch in new[] { 0, 1 })
        {
            state = new();
            state.Events[81] = branch;
            Run("08_JACKA", "knowall_dialog");
            Check(Has(1000, QuestMilestones.Started) && !Has(1000, QuestMilestones.Completed), "both book offers start without completion");
            Run("GROUND1", "gnd1_e7");
            Check(Has(1000, QuestMilestones.ObjectiveReached) && !Has(1000, QuestMilestones.Completed), "book battle returns objective only");
            Run("08_JACKA", "knowall_dialog");
            Check(Has(1000, QuestMilestones.Completed) && QuestJournalText.Description(state, 1000).Split("\n\n").Length == 3, "both book returns complete three paragraphs");
            Check(new StatusTextList(state, 4).Ids.Contains(1000), "story IDs included in actual journal list");
            state = NativeGameStateCodec.Decode(NativeGameStateCodec.Encode(state));
            Check(Has(1000, QuestMilestones.Completed), "story history persists");
        }
        state = new();
        effects.Answer = 1;
        Run("08_KENNE", "kenneth_dialog");
        Check(!QuestJournalText.IsVisible(state, 1001), "declined escort stays absent");
        effects.Answer = 2;
        Run("08_KENNE", "kenneth_dialog");
        Check(Has(1001, QuestMilestones.Started), "accepted escort appears");
        byte[] accepted = NativeGameStateCodec.Encode(state);
        Run("09AHNRAN", "enter");
        Check(Has(1001, QuestMilestones.Completed) && !Has(1001, QuestMilestones.Stopped), "destination is successful escort");
        state = NativeGameStateCodec.Decode(accepted);
        Run("14SINOV", "pre_enter");
        Check(Has(1001, QuestMilestones.Stopped) && !Has(1001, QuestMilestones.Completed), "wrong destination and penalty are not success");
        state = new();
        state.Events[91] = 1;
        Run("09_NOMA", "enter");
        Check(Has(1005, QuestMilestones.Started), "letter pickup recorded");
        Run("10_REIDE", "raiden_dialog");
        Check(Has(1005, QuestMilestones.Completed) && state.GetQuestItemFlag(13) == 0, "consumed letter retains journal outcome");
        state = NativeGameStateCodec.Decode(NativeGameStateCodec.Encode(state));
        Check(Has(1005, QuestMilestones.Completed), "consumed letter history survives save");
        state = new();
        state.Events[91] = 1;
        Check(!Has(1005, QuestMilestones.Started), "old save without letter or history does not fabricate delivery");
        Run("21_CHIEF", "chief_talk");
        Check(Has(1013, QuestMilestones.Started), "unflagged request is recorded by exact dialogue call");
        Run("MD7", "md7_ed");
        Check(Has(1013, QuestMilestones.Completed), "Duncan outcome completes accepted request");
        int revision = state.Journal.CompletionRevision;
        Run("21_CHIEF", "chief_talk");
        Check(state.Journal.CompletionRevision == revision, "repeat conversation does not retrigger completion autosave");
        state = new();
        state.Globals[905] = 2;
        Check(Has(1000, QuestMilestones.ObjectiveReached) && !Has(1000, QuestMilestones.Completed), "old book save derives only proven stage");
        state.Events[83] = state.Events[84] = 1;
        state.Globals[936] = 1;
        Check(Has(1001, QuestMilestones.Stopped) && !Has(1001, QuestMilestones.Completed), "old failed escort is recognized");
        Console.WriteLine("Story quests: real book branches, escort acceptance/refusal/success/failure, consumed letter, Duncan, old saves and completion revisions passed.");
    }

    private sealed class Outcomes : IScriptEffects
    {
        public int Answer { get; set; } = 1;
        public void Execute(ScriptEffect effect, GameState state)
        {
            if (effect.Kind is ScriptEffectKind.QuestionKey or ScriptEffectKind.QuestionText) state.Globals[10] = (short)Answer;
            if (effect.Kind is ScriptEffectKind.Battle or ScriptEffectKind.BattleWithPostAction or ScriptEffectKind.ForcedBattleWithPostAction) state.Globals[61] = 1;
        }
    }
}
