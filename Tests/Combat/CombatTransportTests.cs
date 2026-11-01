using Atria.Platform.Input;
using Atria.Game.Combat;
using Atria.Game.Dialogue;
using Atria.Platform;
using Atria.Platform.Win32;

namespace Atria.Tests.Combat;

public static class CombatTransportTests
{
    public static string Run()
    {
        bool[] scheduled = new bool[256], physical = new bool[256];
        int scheduledReads = 0, physicalReads = 0;
        var source = new CombatHeldInputSource(scan =>
        {
            scheduledReads++;
            return scheduled[scan];
        }, scan =>
        {
            physicalReads++;
            return physical[scan];
        });
        var input = new CombatScanInput(source.IsHeld);
        scheduled[25] = true;
        physical[25] = true;
        Check(input.Query(25) && !input.Query(25), "Normal pause toggle consumes its original query latch.");
        int oldScheduledReads = scheduledReads;
        source.UseFrozenTransport();
        physical[25] = false;
        Check(!input.Query(25), "Physical release clears the native query latch while the sampled game timestamp and scheduled key remain frozen.");
        physical[25] = true;
        Check(input.Query(25) && !input.Query(25) && scheduledReads == oldScheduledReads && physicalReads > 0, "Physical repress resumes the frozen pause loop without advancing or reading scheduled keyboard state.");
        //Unqueried release/repress retains the native edge latch rather than synthesizing a queued edge.
        physical[25] = false;
        physical[25] = true;
        Check(!input.Query(25), "Frozen adapter preserves per-query native latch semantics for missed short releases.");
        source.UseScheduledInput();
        scheduled[25] = false;
        physical[25] = true;
        Check(!input.Query(25), "Next outer sample returns to shared scheduled held state.");
        input.Joystick = new(0, 0, true, false, false, false);
        source.UseFrozenTransport();
        Check(input.IsHeld(46), "Frozen input transport retains the last outer-loop joystick sample; it does not repoll or discard it.");
        Check(new CombatTransportState(false, true).Ready && !new CombatTransportState(true, true).Ready && !new CombatTransportState(false, false).Ready, "Close and inactive focus prevent rendering without inventing a combat result.");
        SharedModalTransitions();
        SlowerGlobalUiPoll();
        return "Combat frozen held transport and shared selection/combat/XP input checks passed.";
    }

    private static void SharedModalTransitions()
    {
        var edges = new ScanCodeEdges();
        var queue = new ScheduledScanInput(0, 0, edges);
        var clock = new Clock
        {
            Now = 10
        };
        var gate = new ModalFrameGate(clock, 0);
        var combat = new CombatScanInput(queue.IsHeld, queue.QueryWithHeld);
        queue.Enqueue(new(10, 46, ScanInputEventKind.Down));
        queue.BeginLogicalFrame(10);
        Check(queue.Query(46), "Selection consumes the initial confirm edge.");
        clock.Now = 1000;
        uint generation = gate.ResumeGeneration;
        gate.RebaseSchedule();
        Check(gate.LastFrameMilliseconds == 1000 && gate.ResumeGeneration == generation && !gate.IsDue, "Battle ownership rebases modal debt without triggering the host's input-reset generation.");
        queue.BeginLogicalFrame(1000);
        Check(combat.IsHeld(46) && !combat.Query(46), "Held selection key remains held without creating a second battle edge.");
        queue.Enqueue(new(1001, 46, ScanInputEventKind.Up));
        queue.BeginLogicalFrame(1001);
        Check(!combat.Query(46), "Queried release rearms the shared latch.");
        queue.Enqueue(new(1002, 46, ScanInputEventKind.Down));
        queue.BeginLogicalFrame(1002);
        Check(combat.Query(46) && !queue.Query(46), "A combat-consumed edge cannot be reused by the XP modal.");
        //Modern preservation policy: down+up before one sampled opportunity is consumed once, across owners.
        queue.Enqueue(new(1003, 25, ScanInputEventKind.Down));
        queue.Enqueue(new(1004, 25, ScanInputEventKind.Up));
        queue.BeginLogicalFrame(1004);
        Check(!combat.IsHeld(25) && combat.Query(25) && !queue.Query(25), "A short pause tap survives one sample and has one shared consumer.");
        queue.BeginLogicalFrame(1005);
        Check(!combat.Query(25), "The frozen-loop tap cannot replay when normal outer sampling resumes.");
        queue.Enqueue(new(1006, 25, ScanInputEventKind.Down));
        queue.Enqueue(new(1007, 25, ScanInputEventKind.Up));
        queue.BeginLogicalFrame(1007);
        queue.BeginLogicalFrame(1008);
        Check(!combat.Query(25), "An unqueried short tap does not become an unlimited cross-screen backlog.");
        gate.Resume();
        queue.Resume(clock.Now);
        Check(gate.ResumeGeneration == generation + 1 && !queue.IsHeld(46), "Explicit focus resume still resets held input.");
    }

    private sealed class Clock : IModalClock
    {
        public uint Now;
        public uint Milliseconds => Now;

        public void Yield()
        {
        }
    }

    private static void SlowerGlobalUiPoll()
    {
        var queue = new ScheduledScanInput(0, 0, new ScanCodeEdges());
        queue.DeferTapsUntilPoll([0x19, 0x12, 0x13, 0x10, 0x11]);
        queue.Enqueue(new(1, 0x19, ScanInputEventKind.Down));
        queue.Enqueue(new(2, 0x19, ScanInputEventKind.Up));
        queue.BeginLogicalFrame(2);
        for (uint t = 3; t <= 25; t++)
        {
            queue.BeginLogicalFrame(t);
        }

        Check(!queue.IsHeld(0x19) && queue.Query(0x19) && !queue.Query(0x19), "A pause tap between renders survives actor samples and is consumed once at the actual UI poll.");
        queue.CompleteDeferredPoll();
        queue.BeginLogicalFrame(26);
        Check(!queue.Query(0x19), "A consumed pause tap cannot toggle again in the frozen loop.");
        queue.Enqueue(new(27, 0x12, ScanInputEventKind.Down));
        queue.Enqueue(new(27, 0x12, ScanInputEventKind.Up));
        queue.Enqueue(new(27, 0x13, ScanInputEventKind.Down));
        queue.Enqueue(new(27, 0x13, ScanInputEventKind.Up));
        queue.BeginLogicalFrame(27);
        queue.BeginLogicalFrame(50);
        var schedule = new CombatRenderSchedule(0, 45);
        schedule.PollSpeedKeys(queue.Query, 50);
        queue.CompleteDeferredPoll();
        Check(schedule.Period == 25 && !queue.Query(0x13), "E retains lazy priority and the unqueried R token expires at the same UI opportunity.");
        queue.Enqueue(new(51, 0x11, ScanInputEventKind.Down));
        queue.Enqueue(new(51, 0x11, ScanInputEventKind.Up));
        queue.BeginLogicalFrame(51);
        Check(queue.Query(0x11), "A bound actor key can consume the shared W token before global detail input.");
        queue.BeginLogicalFrame(52);
        Check(!queue.Query(0x11), "Deferred transport cannot duplicate an actor-consumed key.");
        queue.Enqueue(new(53, 0x19, ScanInputEventKind.Down));
        queue.Enqueue(new(53, 0x19, ScanInputEventKind.Up));
        queue.BeginLogicalFrame(53);
        queue.StopDeferringTaps();
        queue.BeginLogicalFrame(54);
        Check(!queue.Query(0x19), "Ending combat ownership discards an unconsumed deferred tap before XP/modal input.");
    }

    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
