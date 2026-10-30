using Atria.Game.Exploration;
using Atria.Game.Navigation;
using Atria.Game.Persistence;
using Atria.Game.State;

namespace Atria.Game;

public sealed partial class GameSession
{
    private void VerifyMovingSave()
    {
        bool world = state.Mode == GameMode.World;
        bool started = false;
        for (int y = 0; y < 10 && !started; y++)
        {
            for (int x = 0; x < 10 && !started; x++)
            {
                state.WorldX = x;
                state.WorldY = y;
                for (int d = 0; d < 4 && !started; d++)
                    started = world
                        ? exploration!.BeginTravel((WorldDirection)d) == TravelStartResult.Started
                        : dungeon!.Motion.Begin((WorldDirection)d);
            }
        }
        Check(started, "real map has a walkable step");

        uint tick = gate.LastFrameMilliseconds + 1;
        if (world)
            exploration!.AdvanceAnimation(() => tick);
        else
            dungeon!.Advance(() => tick);
        var movingWorld = exploration?.CaptureRuntime();
        var movingDungeon = dungeon?.CaptureRuntime();
        OpenScreen(UiScreen.MainMenu);
        for (int frame = 0; frame < 100; frame++)
            gate.CommitScheduled();
        OpenScreen(UiScreen.Save);
        SaveSessionSlot(40);
        var image = PersistenceStore.ReadSlot(40)!.Native!;
        var snapshot = SessionRuntimeCodec.Decode(image.Context);
        Check(snapshot.MovementClock == worldScreenOpenedAt, "save uses frozen world time beneath nested menus");
        Check(world ? exploration!.CaptureRuntime() == movingWorld : dungeon!.Motion.CaptureRuntime() == movingDungeon!.Motion,
            "saving does not finish or restart live movement");

        var loader = new SessionSnapshotLoader(root, state, random, smallFont, messages, items, musicTransport.ValidateTrack);
        uint deadline = world ? movingWorld!.MovementDeadline : movingDungeon!.Motion.Deadline;
        foreach (uint resumedClock in new uint[] { 0, 50000 })
        {
            var prepared = loader.Prepare(image.Context, image.State, resumedClock);
            uint actual = world ? prepared.World!.CaptureRuntime().MovementDeadline : prepared.Dungeon!.Motion.CaptureRuntime().Deadline;
            Check(actual == resumedClock + deadline - snapshot.MovementClock!.Value, "deadline rebased to a different process clock");
        }
        var overdue = loader.Prepare(SessionRuntimeCodec.Encode(snapshot with { MovementClock = deadline + 100 }), image.State);
        Check((world ? overdue.World!.CaptureRuntime().MovementDeadline : overdue.Dungeon!.Motion.CaptureRuntime().Deadline) == 0,
            "overdue movement remains due instead of wrapping into a long wait");
        bool rejected = false;
        try { SessionRuntimeCodec.Encode(snapshot with { MovementClock = null }); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected, "moving snapshot without timing evidence rejected");

        CloseScreen();
        CloseScreen();
        string[] uninterrupted = FinishStep();
        byte[] expectedState = NativeGameStateCodec.Encode(state);
        var expectedRandom = random.CaptureRuntime();
        LoadSessionSlot(40);
        string[] resumed = FinishStep();
        Check(uninterrupted.SequenceEqual(resumed), "resumed animation frames and arrival effects match uninterrupted step");
        Check(NativeGameStateCodec.Encode(state).AsSpan().SequenceEqual(expectedState)
            && random.CaptureRuntime() == expectedRandom, "same coordinates, gold, date, events and random state after one arrival");
        Console.WriteLine($"Moving save passed: {(world ? "world" : "dungeon")}, nested menu/manual save/load, rebased deadline, single arrival and identical state.");

        string[] FinishStep()
        {
            var trace = new List<string>();
            uint now = gate.LastFrameMilliseconds;
            int arrivals = 0;
            for (int frame = 0; frame < 200 && (world ? exploration!.Moving : dungeon!.Motion.Moving); frame++)
            {
                now += 31;
                if (world)
                {
                    var step = exploration!.AdvanceAnimation(() => now);
                    trace.Add(step.ToString());
                    if (step.Arrived) arrivals++;
                }
                else
                {
                    var step = dungeon!.Advance(() => now);
                    trace.Add(step.Motion + ":" + string.Join(";", step.Commands));
                    if (step.Motion.Arrived) arrivals++;
                }
            }
            Check(arrivals == 1, "step finishes exactly once");
            return trace.ToArray();
        }

        static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException("Moving save: " + message);
        }
    }
}
