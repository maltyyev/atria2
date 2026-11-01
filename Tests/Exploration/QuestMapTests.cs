using Atria.Game.Exploration;
using Atria.Game.State;
using Atria.Game.World;
using Atria.Resources;

namespace Atria.Tests.Exploration;

public static class QuestMapTests
{
    public static void Run(string root)
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Quest map: " + message);
        }
        var index = new QuestMapIndex(root);
        var state = new GameState();
        Check(index.Marker(state, 0) == null, "unselected quest has no marker");
        state.Globals[905] = 1;
        state.Journal.ToggleTracking(state, 1000);
        Check(index.Objective(state) == new QuestMapPoint(0, 9, 1), "book points at actual gnd1_e7 tile");
        Check(index.Marker(state, 1) == new QuestMapPoint(1, 0, 3), "other area points at actual return exit");
        state.Globals[905] = 2;
        Check(index.Objective(state) == new QuestMapPoint(0, 8, 3), "book return points at Loose Hamlet");
        state.Globals[905] = 3;
        Check(index.Marker(state, 0) == null, "completed quest has no marker");
        state = new();
        state.Events[83] = 1;
        state.Journal.ToggleTracking(state, 1001);
        Check(index.Marker(state, 0) == new QuestMapPoint(0, 9, 3), "Kenneth points east to next region");
        Check(index.Marker(state, 1) == new QuestMapPoint(1, 1, 3), "Kenneth points at Anrang within destination region");
        state = new();
        state.Events[353] = 1;
        state.Journal.ToggleTracking(state, 1007);
        Check(index.Marker(state, 3) == new QuestMapPoint(3, 0, 2), "multi-area route follows first actual directed exit");
        state.Events[422] = 1;
        Check(index.Objective(state) == null && Enumerable.Range(0, 8).All(area => index.Marker(state, area) == null), "destroyed Hyohyan removes hostage objective and every regional route marker");
        state = new();
        state.EventStatuses[63] = state.EventStatuses[161] = state.EventStatuses[162] = 1;
        state.Journal.ToggleTracking(state, 63);
        Check(index.Objective(state) == new QuestMapPoint(2, 3, 3), "first runaway-son meeting");
        state.Journal.Record(state, 63, QuestMilestones.LeadFound);
        Check(index.Objective(state) == new QuestMapPoint(2, 2, 6), "second pursuit destination");
        state.Journal.Record(state, 63, QuestMilestones.PursuitContinued);
        Check(index.Objective(state) == new QuestMapPoint(2, 6, 3), "final pursuit destination");
        state.EventStatuses[63] = 2;
        Check(index.Objective(state) == new QuestMapPoint(2, 2, 4), "return to Diyar after rescue");
        state = new();
        state.EventStatuses[86] = 1;
        state.Journal.ToggleTracking(state, 86);
        Check(index.Objective(state) == new QuestMapPoint(4, 1, 7), "escort destination wins over four possible ambushes");
        state = new();
        state.Globals[641] = 1;
        state.Journal.ToggleTracking(state, 1011);
        Check(index.Marker(state, 0) == null, "unknown medallion whereabouts not revealed");
        using var archive = BinderArchive.Open(Path.Combine(root, "AT2AMF.000"));
        byte[] raw = archive.ReadMember(archive.FindResource("ground1.amf"u8));
        var map = WorldMapContext.Parse(raw);
        byte[] before = map.GetTiles();
        var renderer = new WorldSceneRenderer(WorldSceneAssets.Load(root, map, raw));
        renderer.RebuildBase(map, state);
        ushort[] plain = new ushort[640 * 480], marked = new ushort[640 * 480];
        renderer.Draw(new(3, 0, 288, 182), null, plain);
        renderer.Draw(new(3, 0, 288, 182), null, marked, new(9 * 64, 48, 38));
        Check(!plain.SequenceEqual(marked), "original reveal-chip sprite renders objective");
        Check(before.SequenceEqual(map.GetTiles()), "marker does not reveal fog or mutate map events");
        Console.WriteLine("Quest markers: actual AMF/SOD destinations, stage changes, directed multi-region routes, no spoilers, reveal sprite and immutable fog passed.");
    }
}
