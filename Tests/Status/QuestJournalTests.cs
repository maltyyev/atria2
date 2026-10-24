using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Game.Status;
using Atria.Localization;
using Atria.Resources;
using Atria.Script;

namespace Atria.Tests.Status;

public static class QuestJournalTests
{
    public static string Run(string root)
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Quest journal: " + message);
        }
        var state = new GameState();
        var host = new ScriptStateHost(state);
        void Status(int id, int value) => host.Invoke(new("TEST", "set_eventstatus", 0, [new(id), new(value)]));
        int authored = 0;
        for (int id = 1; id < QuestJournalText.OriginalEntryCount; id++)
        {
            if (GameText.Get($"quests.{id:D3}.heading.0").Length == 0) continue;
            string prefix = $"quests.{id:D3}";
            string start = GameText.Get(prefix + ".start.text");
            Check(start.Length > 20, "every named entry has an authored start");
            Status(id, 1);
            Check(QuestJournalText.Description(state, id) == start, "acceptance does not leak completion");
            Status(id, 2);
            bool hasStep = GameText.Current.TryGet(prefix + ".progress.text", out var step);
            Check(QuestJournalText.Description(state, id) == start + (hasStep ? "\n\n" + step : ""), "objective reveals only reached paragraph");
            Status(id, 4);
            string complete = QuestJournalText.Description(state, id);
            Check(complete == start + (hasStep ? "\n\n" + step : "") + "\n\n" + GameText.Get(prefix + ".description"), "history order and blank separators");
            Check(QuestJournalText.Description(NativeGameStateCodec.Decode(NativeGameStateCodec.Encode(state)), id) == complete, "history survives native save");
            authored++;
        }
        state = new GameState();
        host = new(state);
        Status(2, 3);
        Check(!new StatusTextList(state, 4).Ids.Contains(2), "declining an offer creates no accepted quest");
        Status(86, 1);
        Status(86, 3);
        Check(QuestJournalText.IsVisible(state, 86) && QuestJournalText.Description(state, 86).Contains(GameText.Get("ui.quests.stoppedDescription")), "failed accepted quest retains history without false success");

        using var archive = BinderArchive.Open(Path.Combine(root, "SCRIPT.000"));
        foreach (var group in QuestJournalTriggers.All.GroupBy(rule => rule.Member))
        {
            var entry = archive.Entries.Single(entry => entry.Name == group.Key);
            var program = ScriptProgram.Parse(entry.Name, archive.ReadMember(entry.Index));
            foreach (var rule in group)
            {
                Check(program.Tokens.Any(token => token.Offset == rule.Offset && token.Text == rule.Function), "trigger matches actual repaired scenario call");
            }
        }
        var subevent = archive.Entries.Single(entry => entry.Name == "SUBEVENT");
        var script = ScriptProgram.Parse(subevent.Name, archive.ReadMember(subevent.Index));
        var outcomes = new Outcomes();
        state = new GameState();
        var machine = new ScriptMachine(script, new ScriptStateHost(state, outcomes));
        machine.Invoke("sub_063");
        Check(QuestJournalText.Description(state, 63) == GameText.Get("quests.063.start.text"), "real acceptance has no future pursuit");
        machine.Invoke("suba_063");
        Check(state.Journal.Progress(state, 63).HasFlag(QuestMilestones.LeadFound), "first meeting recorded at scenario return");
        state = NativeGameStateCodec.Decode(NativeGameStateCodec.Encode(state));
        machine = new(script, new ScriptStateHost(state, outcomes));
        machine.Invoke("suba_161");
        Check(state.Journal.Progress(state, 63).HasFlag(QuestMilestones.PursuitContinued), "successful chase recorded after battle");
        machine.Invoke("suba_162");
        machine.Invoke("sub_063");
        string[] paragraphs = QuestJournalText.Description(state, 63).Split("\n\n");
        Check(paragraphs.Length == 5 && paragraphs[1] == GameText.Get("quests.063.lead.text") && paragraphs[2] == GameText.Get("quests.063.pursuit.text"), "five real quest stages retained in order");
        foreach (int id in new[] { 61, 62, 63 })
        {
            var declined = new GameState();
            declined.EventStatuses[60] = 4;
            new ScriptMachine(script, new ScriptStateHost(declined, new Outcomes { Answer = 2 })).Invoke($"sub_{id:D3}");
            Check(!QuestJournalText.IsVisible(declined, id), "status4 decline does not fabricate completion");
            Check(!QuestJournalText.IsVisible(NativeGameStateCodec.Decode(NativeGameStateCodec.Encode(declined)), id), "decline persists");
        }
        var old = new GameState();
        old.EventStatuses[2] = 4;
        Check(NativeGameStateCodec.Decode(NativeGameStateCodec.Encode(old)).Journal.Entries.Count == 0, "old state without extension remains readable");
        Check(QuestJournalText.Description(old, 2) == GameText.Get("quests.002.start.text") + "\n\n" + GameText.Get("quests.002.description"), "old completed save does not invent an unrecorded intermediate stage");
        byte[] corrupt = NativeGameStateCodec.Encode(state);
        corrupt[^1] = byte.MaxValue;
        try
        {
            NativeGameStateCodec.Decode(corrupt);
            throw new Exception("Corrupt journal flags were accepted.");
        }
        catch (InvalidDataException) { }
        var items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT")));
        var messages = LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat")));
        var runtime = new StatusMenuRuntime();
        var journal = QuestJournalCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "DANGEL.DAT")));
        runtime.Enter(_ => { });
        void Input(StatusMenuInput input) => runtime.Input(input, state, items, default, journal, _ => { });
        Input(default);
        Input(default);
        for (int i = 0; i < 4; i++) Input(new(NextPage: true));
        Input(new(Accept: true));
        Input(new(Accept: true));
        Check(runtime.Description.Length > 5, "full quest history exceeds one description page");
        for (int i = 0; i < 100; i++) Input(new(NextPage: true));
        Check(runtime.DescriptionOffset == runtime.Description.Length - 5 && runtime.TextList!.SelectedId == 63, "scroll reaches final paragraph without changing selected quest");
        for (int i = 0; i < 100; i++) Input(new(PreviousPage: true));
        Check(runtime.DescriptionOffset == 0, "description scroll clamps at start");
        InitialGameState.ResetRecoveredCore(state, items, messages);
        Check(state.Journal.Entries.Count == 0, "new game clears journal history");
        return $"Quest journal: {authored} authored starts, ordered milestones, real five-stage quest63, refusal/failure branches, save compatibility and reset passed.";
    }

    private sealed class Outcomes : IScriptEffects
    {
        public int Answer { get; init; } = 1;
        public void Execute(ScriptEffect effect, GameState state)
        {
            if (effect.Kind is ScriptEffectKind.QuestionKey or ScriptEffectKind.QuestionText) state.Globals[10] = (short)Answer;
            if (effect.Kind is ScriptEffectKind.Battle or ScriptEffectKind.BattleWithPostAction or ScriptEffectKind.ForcedBattleWithPostAction) state.Globals[61] = 1;
        }
    }
}
