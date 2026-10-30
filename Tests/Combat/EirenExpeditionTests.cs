using System.Text;
using Atria.Game;
using Atria.Game.Dungeon;
using Atria.Game.Exploration;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Game.World;
using Atria.Resources;
using Atria.Script;

namespace Atria.Tests.Combat;

internal static class EirenExpeditionTests
{
    private sealed class Outcomes(bool defeated = false) : IScriptEffects
    {
        public void Execute(ScriptEffect effect, GameState state)
        {
            if (effect.Kind != ScriptEffectKind.ForcedBattleWithPostAction) return;
            state.PendingBattle = state.EndingResetFlag = 0;
            if (defeated) state.Mode = GameMode.Defeat;
        }
    }

    internal static GameState CompleteChain(string root, GameState state, IScriptEffects effects)
    {
        // Follow physical visits, including leaving a dungeon unfinished and returning.
        string[] route = ["NAR0001", "NAR0002", "NAR0003", "NAR0012", "NAR0004", "NAR0015",
            "NAR0004", "NAR0016", "NAR0005", "NAR0006", "NAR0007", "NAR0013", "NAR0014",
            "NAR0008", "NAR0017", "NAR0008", "NAR0018", "NAR0019", "NAR0020"];
        foreach (string id in route)
        {
            var scene = NarrativeScenes.All.Single(scene => scene.Id == id);
            int? area = null;
            if (scene.Conditions.TryGetProperty("world", out var point))
            {
                area = point[0].GetInt32();
                state.WorldX = point[1].GetInt32();
                state.WorldY = point[2].GetInt32();
            }
            var dispatched = scene.Replaces ? NarrativeScenes.Replacement(state, scene.AfterMember, scene.AfterFunction)
                : NarrativeScenes.After(state, scene.AfterMember, scene.AfterFunction, area);
            Check(dispatched?.Id == id, "reachable visit: " + id);
            new ScriptMachine(ScriptProgram.Parse(id, scene.Source), new ScriptStateHost(state, effects)).Invoke("run");
            state.NarrativeHistory.Add(id);
            state = NativeGameStateCodec.Decode(NativeGameStateCodec.Encode(state));
            if (!scene.Repeatable)
                Check((scene.Replaces ? NarrativeScenes.Replacement(state, scene.AfterMember, scene.AfterFunction)
                    : NarrativeScenes.After(state, scene.AfterMember, scene.AfterFunction, area))?.Id != id,
                    "completed scene does not replay after reload: " + id);
        }
        Check(state.HasHero("eiren_wald") && state.Journal.Progress(state, 1023).HasFlag(QuestMilestones.Completed),
            "chain recruits Eiren and finishes without resetting earlier quests");
        return state;
    }

    public static string Run(string root)
    {
        var items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT")));
        var messages = LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat")));
        var state = InitialGameState.Create(items, messages);
        using var definitions = GameData.Open("AuthoredDungeons");
        foreach (var definition in definitions.RootElement.EnumerateObject())
        {
            byte[] member = Encoding.ASCII.GetBytes(definition.Name + ".amf");
            byte[] bytes = GameResources.Read(root, "AT2AMF.000", member);
            var context = WorldMapContext.Parse(bytes);
            var map = new DungeonMap(context);
            _ = DungeonSceneAssets.Load(root, context, bytes);
            Check(LocationTitle.Dungeon(AmfMap.Parse(bytes)).ResourceKey == definition.Value.GetProperty("titleKey").GetString(),
                "authored title has a stable localization ID");
            var start = map.FindEntrance()!.Value;
            var reached = new HashSet<(int X, int Y)> { start };
            var pending = new Queue<(int X, int Y)>();
            pending.Enqueue(start);
            while (pending.TryDequeue(out var at))
            {
                foreach (var step in new[] { (0, -1), (0, 1), (-1, 0), (1, 0) })
                {
                    var next = (X: at.X + step.Item1, Y: at.Y + step.Item2);
                    if (map.CanEnter(next.X, next.Y) && reached.Add(next)) pending.Enqueue(next);
                }
            }
            var exit = definition.Value.GetProperty("exit");
            Check(reached.Contains((exit[0].GetInt32(), exit[1].GetInt32())), "exit is reachable");
            var program = ScriptProgram.Parse(definition.Name, GameResources.Read(root, "SCRIPT.000", member));
            foreach (string handler in new[] { "enter", "dungeon_nofire" })
                new ScriptMachine(program, new ScriptStateHost(state, new Outcomes())).Invoke(handler);
            foreach (var point in definition.Value.GetProperty("points").EnumerateArray())
            {
                var at = point.GetProperty("at");
                int x = at[0].GetInt32(), y = at[1].GetInt32();
                Check(reached.Contains((x, y)), "objective is reachable: " + definition.Name + "/" + point.GetProperty("function"));
                Check(program.Tokens.Any(token => token.Text == point.GetProperty("function").GetString()), "tile handler exists");
                state.WorldX = x;
                state.WorldY = y;
                var arrival = new DungeonArrival(state, map, new DungeonVisibility()).Complete();
                Check(arrival.Any(effect => effect.Kind == DungeonArrivalEffectKind.ScriptPending), "walking on objective dispatches a script");
                Check(map.Read(y * 10 + x, 88) == 9, "attempt alone cannot consume a fight or objective");
                state.NarrativeHistory.Add(point.GetProperty("completedScene").GetString()!);
                Check(AuthoredDungeons.ApplyProgress(map, state) && map.Read(y * 10 + x, 88) == 0, "completed objective removes its marker");
            }
            var reentry = new DungeonMap(WorldMapContext.Parse(GameResources.Read(root, "AT2AMF.000", member)));
            Check(AuthoredDungeons.ApplyProgress(reentry, NativeGameStateCodec.Decode(NativeGameStateCodec.Encode(state))),
                "completed guards remain defeated on reentry after saving");
        }

        // Only the quest visit can bypass the original closed monastery admission.
        state = InitialGameState.Create(items, messages);
        state.Events[452] = 1;
        Check(NarrativeScenes.Replacement(state, "45ROOST", "pre_enter") is null, "unrelated monastery behavior unchanged");
        state.Journal.Restore(1022, QuestMilestones.Started);
        var entry = NarrativeScenes.Replacement(state, "45ROOST", "pre_enter")!;
        new ScriptMachine(ScriptProgram.Parse(entry.Id, entry.Source), new ScriptStateHost(state)).Invoke("run");
        Check(state.EnterPermission == 1 && state.Events[452] == 1, "quest admits entry without replaying medal reward");
        Check(NarrativeScenes.Replacement(state, "45ROOST", "enter")?.Id == "NAR0006", "search replaces the old descent prompt");
        state.Journal.Record(state, 1022, QuestMilestones.LeadFound);
        Check(NarrativeScenes.Replacement(state, "45ROOST", "pre_enter") is null, "bypass ends after the search");

        var index = new QuestMapIndex(root);
        foreach (var (phase, target) in new[] { (49, new QuestMapPoint(6, 8, 3)), (51, new QuestMapPoint(6, 8, 4)) })
        {
            state.Journal.Clear();
            state.Journal.Restore(1022, (QuestMilestones)phase);
            state.Journal.RestoreTracking(1022);
            Check(index.Objective(state) == target, "cemetery journey and house return have distinct markers");
        }
        var world = new DungeonMap(WorldMapContext.Parse(GameResources.Read(root, "AT2AMF.000", "ground7.amf"u8)));
        Check(world.CanEnter(8, 3), "cemetery tile north of the settlement is passable");
        foreach (int[] order in new int[][] { [17, 18, 19], [17, 19, 18], [18, 17, 19], [18, 19, 17], [19, 17, 18], [19, 18, 17] })
        {
            state = InitialGameState.Create(items, messages);
            state.Journal.Restore(1023, QuestMilestones.Started);
            for (int i = 0; i < order.Length; i++)
            {
                var scene = NarrativeScenes.All.Single(scene => scene.Id == $"NAR{order[i]:D4}");
                new ScriptMachine(ScriptProgram.Parse(scene.Id, scene.Source), new ScriptStateHost(state, new Outcomes())).Invoke("run");
                state.NarrativeHistory.Add(scene.Id);
                Check(state.Journal.Progress(state, 1023).HasFlag(QuestMilestones.ObjectiveReached) == (i == 2),
                    "finale unlocks only after all three seals, in every visit order");
            }
        }
        state = InitialGameState.Create(items, messages);
        state.Journal.Restore(1023, QuestMilestones.Started | QuestMilestones.ObjectiveReached);
        var migrated = new DungeonMap(WorldMapContext.Parse(GameResources.Read(root, "AT2AMF.000", "EIRCRYPT.amf"u8)));
        AuthoredDungeons.ApplyProgress(migrated, state);
        Check(migrated.Read(30, 88) == 0 && migrated.Read(39, 88) == 0 && migrated.Read(2, 88) == 0 && migrated.Read(6, 88) == 9,
            "old all-seals-complete saves keep their progress without individual guard histories");
        state = InitialGameState.Create(items, messages);
        state.Journal.Restore(1021, QuestMilestones.Started);
        var diary = NarrativeScenes.All.Single(scene => scene.Id == "NAR0016");
        new ScriptMachine(ScriptProgram.Parse(diary.Id, diary.Source), new ScriptStateHost(state, new Outcomes(true))).Invoke("run");
        Check(state.Journal.Progress(state, 1021) == QuestMilestones.Started, "losing the diary fight cannot award its objective");
        return "Eiren expedition PASS: two real maps/artwork, reachable objectives/exits, repeatable failed attempts, persistent defeated guards, Rust access and cemetery markers.";
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Eiren expedition: " + message);
    }
}
