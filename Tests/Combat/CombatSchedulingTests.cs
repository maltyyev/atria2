using System.Buffers.Binary;
using Atria.Game.Combat;

namespace Atria.Tests.Combat;

public static class CombatSchedulingTests
{
    public static string Run()
    {
        void Check(bool value, string message)
        {
            if (!value)
            {
                throw new InvalidOperationException("Combat scheduling: " + message);
            }
        }

        void Reject(Action action, string message)
        {
            try
            {
                action();
            }
            catch (ArgumentException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                return;
            }
            catch (InvalidDataException)
            {
                return;
            }

            throw new InvalidOperationException("Combat scheduling accepted " + message);
        }

        var schedule = new CombatRenderSchedule(100);
        Check(!schedule.IsDue(99) && schedule.IsDue(100), "signed inclusive initial deadline");
        schedule.CompleteLogicalRender(100);
        Check(schedule.Deadline == 125 && !schedule.IsDue(124) && schedule.IsDue(125), "native25ms inclusive render period");
        schedule.CompleteLogicalRender(1000);
        Check(schedule.Deadline == 1025 && !schedule.IsDue(1000), "late observed-now reschedule produces no catch-up renders");
        var queries = new List<byte>();
        Check(schedule.PollSpeedKeys(scan =>
        {
            queries.Add(scan);
            return true;
        }, 10), "bounded speed key is visited");
        Check(queries.SequenceEqual(new byte[] { 0x12 }) && schedule.Period == 25 && schedule.SettingsNoticeDeadline == 5010, "E priority consumes R and refreshes notice even at lower bound");
        schedule.PollSpeedKeys(scan => scan == 0x13, 200);
        schedule.CompleteLogicalRender(200);
        Check(schedule.Period == 45 && schedule.Deadline == 245, "new UI period participates in the following deadline assignment");
        var slow = new CombatRenderSchedule(0, 225);
        slow.PollSpeedKeys(scan => scan == 0x13, 123);
        Check(slow.Period == 225 && slow.SettingsNoticeDeadline == 5123, "upper bound still refreshes native five-second notice");
        Reject(() => new CombatRenderSchedule(0, 26), "unreachable period26");
        Check(!new CombatRenderSchedule(0x7fffffff).IsDue(0x80000000), "signed tick transition preserves original CMP behavior");
        var wrapped = new CombatRenderSchedule(0xfffffff0);
        wrapped.CompleteLogicalRender(0xfffffff0);
        Check(wrapped.Deadline == 9 && !wrapped.IsDue(0xfffffff0) && wrapped.IsDue(9), "unchecked DWORD addition with signed due comparison");
        var host = new Host();
        host.OnActor = () => host.Magic = true;
        var coordinator = new CombatSerialCoordinator(host, new CombatRenderSchedule(100));
        Check(!coordinator.TryPresentCached(), "uninitialized cache is not presented");
        coordinator.OuterOpportunity(() =>
        {
            host.Log.Add("Clock");
            return 100;
        });
        Check(host.Log.SequenceEqual(new[] { "Pump", "Clock", "Actor100", "Hit100", "Debug100", "Pause", "Render100", "UI100", "Present" }), "exact outer actor/hit/render/UI/present ordering");
        Check(host.Hits == 1 && host.Magic, "magic created by actor pass does not retroactively suppress its hit pass");
        coordinator.OuterOpportunity(() => 100);
        Check(host.Actors == 1 && host.Hits == 1 && host.Debug == 2 && host.Renders == 1, "preexisting magic skips actor/hit only and render is separately gated");
        host.OnRender = () => host.Magic = false;
        coordinator.OuterOpportunity(() => 125);
        coordinator.OuterOpportunity(() => 126);
        Check(host.Actors == 2 && host.Hits == 2, "logical particle/magic release resumes simulation at the next supplied opportunity");
        var pauseHost = new Host();
        pauseHost.Pause.Enqueue(true);
        pauseHost.Pause.Enqueue(false);
        pauseHost.Pause.Enqueue(true);
        var paused = new CombatSerialCoordinator(pauseHost, new CombatRenderSchedule(7));
        int clockReads = 0;
        paused.OuterOpportunity(() =>
        {
            clockReads++;
            return 7;
        });
        Reject(() => paused.OuterOpportunity(() => 8), "outer call inside native frozen render loop");
        paused.FrozenRenderOpportunity();
        var unpaused = paused.FrozenRenderOpportunity();
        Check(!unpaused.FrozenRenderRequired && clockReads == 1 && pauseHost.Pumps == 1 && pauseHost.Actors == 1 && pauseHost.Hits == 1
&& pauseHost.Debug == 1, "frozen inner iterations neither pump nor resample nor run actor/hit/debug");
        Check(pauseHost.RenderTimes.SequenceEqual(new uint[] { 7, 7, 7 }) && pauseHost.Renders == 3 && pauseHost.Inputs == 3 && pauseHost.Presents == 3
&& paused.RenderSchedule.Deadline == 32, "frozen clock still advances three stateful render passes and presents the unpause iteration");
        Reject(() => paused.FrozenRenderOpportunity(), "frozen render outside pause");
        var mismatchHost = new Host();
        mismatchHost.Pause.Enqueue(true);
        var mismatch = new CombatSerialCoordinator(mismatchHost, new CombatRenderSchedule(7));
        mismatch.OuterOpportunity(() => 7);
        Reject(() => CombatOpportunityReplay.Run(mismatch, new[] { new CombatRecordedOpportunity(CombatRecordedOpportunityKind.FrozenRender, 8) }, 1), "changed frozen timestamp");
        var threadHost = new Host();
        var serial = new CombatSerialCoordinator(threadHost, new CombatRenderSchedule(0));
        Exception? threadFailure = null;
        var wrongThread = new Thread(() =>
        {
            try
            {
                serial.OuterOpportunity(() => 0);
            }
            catch (Exception error)
            {
                threadFailure = error;
            }
        });
        wrongThread.Start();
        wrongThread.Join();
        Check(threadFailure is InvalidOperationException && threadHost.Pumps == 0, "another thread is rejected before host work");
        var reentrantHost = new Host();
        var reentrant = new CombatSerialCoordinator(reentrantHost, new CombatRenderSchedule(0));
        reentrantHost.OnRender = () => reentrant.OuterOpportunity(() => 1);
        Reject(() => reentrant.OuterOpportunity(() => 0), "reentrant dispatch");
        Check(reentrantHost.Pumps == 1, "reentrancy cannot create another actor opportunity");
        var terminalHost = new Host
        {
            ActorCode = 2
        };
        var terminal = new CombatSerialCoordinator(terminalHost, new CombatRenderSchedule(0));
        IEnumerable<CombatRecordedOpportunity> TerminalRecording()
        {
            yield return new(CombatRecordedOpportunityKind.Outer, 0);
            throw new InvalidOperationException("Replay enumerated after actual terminal result.");
        }

        var ended = CombatOpportunityReplay.Run(terminal, TerminalRecording(), 10);
        Check(ended.Terminal && ended.Outcome?.ReturnCode == 2 && ended.Consumed == 1 && terminalHost.Hits == 0 && terminalHost.Debug == 0
&& terminalHost.Renders == 0, "actual terminal result returns before hit/debug/render and stops trace enumeration");
        Reject(() => terminal.OuterOpportunity(() => 1), "simulation after battle return");
        (Host Host, CombatReplaySummary Summary) ReplayWithPresentations(int interval)
        {
            var fake = new Host
            {
                Maintenance = true
            };
            Set(fake.Actor, 8, 2);
            Set(fake.Actor, 0xc, 10);
            Set(fake.Actor, 0x10, 20);
            Set(fake.Actor, 0x170, 100);
            Set(fake.Actor, 4, 130);
            var engine = new CombatSerialCoordinator(fake, new CombatRenderSchedule(0));
            var records = new List<CombatRecordedOpportunity>
            {
                new(CombatRecordedOpportunityKind.Outer, 0),
                new(CombatRecordedOpportunityKind.Outer, 0),
                new(CombatRecordedOpportunityKind.Outer, 25),
                new(CombatRecordedOpportunityKind.Outer, 100)
            };
            for (uint tick = 0; tick <= 120; tick += (uint)interval)
            {
                records.Add(new(CombatRecordedOpportunityKind.CachedPresentation, tick));
            }

            return (fake, CombatOpportunityReplay.Run(engine, records.OrderBy(x => x.Milliseconds), records.Count));
        }

        var fast = ReplayWithPresentations(8);
        var slower = ReplayWithPresentations(33);
        Check(fast.Host.Actors == 4 && fast.Host.Hits == 4 && fast.Host.Renders == 3 && fast.Host.RenderTimes.SequenceEqual(new uint[] { 0, 25, 100 }), "duplicates remain opportunities and100ms stall creates one due logical render");
        Check(fast.Host.Actor.AsSpan().SequenceEqual(slower.Host.Actor) && fast.Host.ActorRngCalls == slower.Host.ActorRngCalls
&& fast.Host.Renders == slower.Host.Renders && fast.Summary.CachedPresentations > slower.Summary.CachedPresentations, "8/33ms cached GPU presentation cadence does not alter actor state, RNG opportunities or stateful rendering");
        var strictHost = new Host
        {
            Maintenance = true
        };
        Set(strictHost.Actor, 8, 2);
        Set(strictHost.Actor, 0xc, 10);
        Set(strictHost.Actor, 0x10, 20);
        Set(strictHost.Actor, 0x170, 100);
        Set(strictHost.Actor, 4, 130);
        var strict = new CombatSerialCoordinator(strictHost, new CombatRenderSchedule(10));
        strict.OuterOpportunity(() => 10);
        strict.OuterOpportunity(() => 10);
        Check(Get(strictHost.Actor, 0x170) == 100 && Get(strictHost.Actor, 4) == 110, "two same-timestamp actor visits preserve untimed HP display changes but strict regeneration does not run at equality");
        strict.OuterOpportunity(() => 11);
        Check(Get(strictHost.Actor, 0x170) == 101 && Get(strictHost.Actor, 4) == 109 && Get(strictHost.Actor, 0xc) == 31, "native regeneration runs once after strict deadline and reschedules from observed now");
        Reject(() => CombatOpportunityReplay.Run(new(new Host(), new(0)), Enumerable.Repeat(new CombatRecordedOpportunity(CombatRecordedOpportunityKind.Outer, 0), 2), 1), "explicit replay bound overflow");
        return "Combat scheduling checks passed (signed deadlines, no catch-up, exact serial/magic/pause order, terminal return, recorded opportunities and pure cached presentation). No production cadence is selected.";
    }

    private static void Set(object actor, int at, int value) => LegacyActorFixture.WriteInt32LittleEndian(actor.FixtureSpan(at, 4), value);
    private static int Get(object actor, int at) => LegacyActorFixture.ReadInt32LittleEndian(actor.FixtureSpan(at, 4));
    private sealed class Host : ICombatSerialHost
    {
        public readonly List<string> Log = [];
        public readonly List<uint> RenderTimes = [];
        public readonly Queue<bool> Pause = [];
        public readonly CombatActorState Actor = new();
        public int Pumps, Actors, Hits, Debug, Renders, Inputs, Presents, ActorCode, ActorRngCalls;
        public bool Magic, Maintenance;
        public Action? OnActor, OnRender;
        public bool ActiveMagic => Magic;

        public void PumpEventsAndMusic()
        {
            Pumps++;
            Log.Add("Pump");
        }

        public CombatFrameResult ActorPass(uint now)
        {
            Actors++;
            ActorRngCalls++;
            Log.Add("Actor" + now);
            if (Maintenance)
            {
                CombatActorMaintenance.RegenerateAndUpdateDisplay(Actor, now);
            }

            OnActor?.Invoke();
            return new(ActorCode, ActorCode == 1, 1, 1, 0, 0);
        }

        public void HitPass(uint now)
        {
            Hits++;
            Log.Add("Hit" + now);
        }

        public void ProcessDebugHeldKeys(uint now)
        {
            Debug++;
            Log.Add("Debug" + now);
        }

        public bool QueryNativePauseToggle()
        {
            Log.Add("Pause");
            return Pause.TryDequeue(out bool value) && value;
        }

        public void ComposeLogicalFrame(uint now)
        {
            Renders++;
            RenderTimes.Add(now);
            Log.Add("Render" + now);
            OnRender?.Invoke();
        }

        public void ProcessFrameInput(uint now, CombatRenderSchedule schedule)
        {
            Inputs++;
            Log.Add("UI" + now);
        }

        public void PresentCachedFrame()
        {
            Presents++;
            Log.Add("Present");
        }
    }
}
