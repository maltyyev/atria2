using System.Buffers.Binary;
using Atria.Game.Exploration;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Game.Status;
using Atria.Resources;

namespace Atria.Tests.Status;

public static class TrackedQuestTests
{
    public static void Run(string root)
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Tracked quest: " + message);
        }
        var state = new GameState();
        state.Journal.Record(state, 63, QuestMilestones.Started);
        var runtime = new StatusMenuRuntime();
        var items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT")));
        var journal = QuestJournalCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "DANGEL.DAT")));
        runtime.Enter(_ => { });
        void Input(StatusMenuInput input) => runtime.Input(input, state, items, default, journal, _ => { });
        Input(default);
        Input(default);
        for (int i = 0; i < 4; i++) Input(new(NextPage: true));
        Input(new(Accept: true));
        Input(new(Accept: true));
        Check(state.Journal.ActiveQuest(state) == null, "opening description does not silently select");
        Input(new(Accept: true));
        Check(state.Journal.ActiveQuest(state) == 63 && runtime.TextList!.DescriptionOpen, "second confirm selects without closing");
        var pixels = Enumerable.Repeat((ushort)0x1234, 640 * 480).ToArray();
        WorldHudRenderer.DrawActiveQuest(state, pixels);
        Check(pixels.Any(pixel => pixel != 0x1234), "HUD renders selected title");
        Check(pixels.Where((_, i) => i / 640 < 60 || i / 640 > 90).All(pixel => pixel == 0x1234), "HUD remains in strip below top panel");
        state = NativeGameStateCodec.Decode(NativeGameStateCodec.Encode(state));
        Check(state.Journal.ActiveQuest(state) == 63, "selection survives save/load");
        byte[] current = NativeGameStateCodec.Encode(state);
        int extension = current.Length - (12 + state.Journal.Entries.Count * 5 + sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(current.AsSpan(extension + sizeof(uint)), 1);
        var previous = NativeGameStateCodec.Decode(current.AsSpan(0, current.Length - sizeof(int)));
        Check(previous.Journal.ActiveQuest(previous) == null && previous.Journal.Entries.ContainsKey(63), "v1 journal loads without tracking field");
        Input(new(Accept: true));
        Check(state.Journal.ActiveQuest(state) == null, "repeat confirm untracks");
        Input(new(Accept: true));
        Input(new(Cancel: true));
        Check(!runtime.TextList!.DescriptionOpen && state.Journal.ActiveQuest(state) == 63, "cancel closes description and retains selection");
        state.Journal.Record(state, 63, QuestMilestones.Completed);
        Check(state.Journal.ActiveQuest(state) == null, "completion removes tracking");
        state.Journal.ToggleTracking(state, 63);
        state.Journal.ToggleTracking(state, 0);
        Check(state.Journal.ActiveQuest(state) == null, "closed and information entries cannot be tracked");
        state.Globals[905] = 1;
        state.Journal.ToggleTracking(state, 1000);
        Check(state.Journal.ActiveQuest(state) == 1000, "story quest can be tracked from old-save flags");
        var copy = new GameState();
        LegacySnapshotBridge.CopyRecoveredState(state, copy);
        Check(copy.Journal.ActiveQuest(copy) == 1000, "transactional load copies selection");
        state.Journal.Clear();
        Check(state.Journal.ActiveQuest(state) == null, "new game clears tracking");
        try
        {
            state.Journal.RestoreTracking(9999);
            throw new Exception("Invalid selection accepted.");
        }
        catch (InvalidDataException) { }
        Console.WriteLine("Tracked quest: actual journal input, HUD bounds, save v1/v2, transactional copy, completion and reset passed.");
    }
}
