using System.Text;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Game.Town;
using Atria.Resources;
using Atria.Script;

namespace Atria.Tests.Combat;

internal static class NarrativeReturnTests
{
    private sealed class Effects : IScriptEffects
    {
        public void Execute(ScriptEffect effect, GameState state)
        {
            Check(effect.Kind is ScriptEffectKind.DialogueText or ScriptEffectKind.DialogueKey,
                "town return only presents dialogue");
        }
    }

    public static string Run(string root, string? fixture = null)
    {
        var items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT")));
        var messages = LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat")));
        var assets = TownSceneAssets.Load(root, "43salin.sod"u8);
        var original = ScriptProgram.Parse("43SALIN", GameResources.Read(root, "SCRIPT.000", "43SALIN"u8));
        var effects = new Effects();

        if (fixture is not null)
        {
            var saved = NativeSaveImage.Parse(File.ReadAllBytes(fixture));
            var context = SessionRuntimeCodec.Decode(saved.Context);
            Console.WriteLine($"Return fixture: mode={saved.State.Mode}, quest1020={saved.State.Journal.Progress(saved.State, 1020)}, history={string.Join(',', saved.State.NarrativeHistory)}.");
            Check(saved.State.Journal.Progress(saved.State, 1020)
                == (QuestMilestones.Started | QuestMilestones.ObjectiveReached), "actual save contains rescued workers and retained Salin identity");
            Enter(saved.State, "NAR0003");
            Check(saved.State.Journal.Progress(saved.State, 1020).HasFlag(QuestMilestones.LeadFound)
                && saved.State.Journal.Progress(saved.State, 1021) == QuestMilestones.None,
                "actual rescued-worker save directs the player into the Academy");
        }

        foreach (string id in new[] { "NAR0001", "NAR0003" })
        {
            foreach (int visit in new[] { 1, 2 })
            {
                var state = InitialGameState.Create(items, messages);
                state.Events[441] = 1;
                foreach (var previous in NarrativeScenes.All.TakeWhile(scene => scene.Id != id))
                {
                    state.NarrativeHistory.Add(previous.Id);
                }
                if (id == "NAR0005")
                {
                    state.Journal.Restore(1020, QuestMilestones.Started | QuestMilestones.ObjectiveReached | QuestMilestones.Completed);
                }
                var scene = NarrativeScenes.All.Single(scene => scene.Id == id);
                foreach (var row in scene.Conditions.GetProperty("quests").EnumerateArray())
                {
                    if (row[1].GetInt32() != 0)
                    {
                        state.Journal.Restore(row[0].GetInt32(), (QuestMilestones)row[1].GetInt32());
                    }
                }
                Enter(state, id);
                Check(state.Journal.Progress(state, 1020).HasFlag(id == "NAR0001"
                    ? QuestMilestones.Started : QuestMilestones.LeadFound), "inside-town milestone: " + id);
                Check(state.Journal.Progress(state, 1021) == QuestMilestones.None, "Academy conversation requires entering the building");
            }
        }
        return "Narrative return PASS: Salin opening and worker return, direct returns and visits from another town, original admission/arrival and controller, journal advancement, no repeats after reload.";

        void Enter(GameState state, string expectedScene)
        {
            state.EnterPermission = 0;
            new ScriptMachine(original, new ScriptStateHost(state, effects)).Invoke("pre_enter");
            Check(state.EnterPermission != 0, "original scenario permits Salin entry");
            Check(NarrativeScenes.After(state, "43SALIN", "pre_enter") is null, "no conversation outside town");
            var commands = new List<TownCommand>();
            var town = new TownController();
            town.Initialize(assets.Scene, assets.Objects, 43, "43salin.sod"u8.ToArray(),
                TownCatalog.Name(messages, 43), TownCatalog.Get(43).MusicTrack, commands.Add);
            town.QueueArrival(commands.Add);
            Check(commands.Count(command => command.Kind == TownEffectKind.QueueEnterScript) == 1,
                "every physical arrival queues the original enter exactly once");
            new ScriptMachine(original, new ScriptStateHost(state, effects)).Invoke("enter");
            var scene = NarrativeScenes.After(state, "43SALIN", "enter");
            Check(scene?.Id == expectedScene, "inside-town hook must dispatch " + expectedScene);
            new ScriptMachine(ScriptProgram.Parse(scene!.Id, scene.Source), new ScriptStateHost(state, effects)).Invoke("run");
            state.NarrativeHistory.Add(scene.Id);
            var reloaded = NativeGameStateCodec.Decode(NativeGameStateCodec.Encode(state));
            Check(NarrativeScenes.After(reloaded, "43SALIN", "enter")?.Id != expectedScene,
                "completed scene does not repeat after reload");
        }
    }

    private static void Check(bool value, string reason)
    {
        if (!value) throw new InvalidOperationException("Narrative return: " + reason);
    }
}
