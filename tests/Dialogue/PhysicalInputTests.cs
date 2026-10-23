using Atria.Game.Dialogue;
using Atria.Platform;

namespace Atria.Tests.Dialogue;

public static class PhysicalInputTests
{
    public static void Run()
    {
        Check(PhysicalScanCode.Decode(0x004d0000)==77 && PhysicalScanCode.Decode(0x014d0000)==205,"physical keypad/right distinction");
        Check(PhysicalScanCode.Decode(0x001d0000)==29 && PhysicalScanCode.Decode(0x011d0000)==157,"physical left/right control distinction");
        Check(PhysicalScanCode.Decode(0,0xe04d)==205 && PhysicalScanCode.Decode(0,0x2e)==46,"scanless injected mapping only");
        Check(PhysicalScanCode.Decode(0x004d0000,0xe04d)==77,"fallback must never override supplied physical scan");

        var capture=new ScanInputCapture();
        capture.Down(10,46,false); capture.Up(30,46); // Both messages delivered by one delayed Pump.
        var scheduled=new ScheduledScanInput(0,0,new ScanCodeEdges());
        Transfer(capture,scheduled);
        scheduled.BeginLogicalFrame(21); Check(scheduled.Query(46) && scheduled.IsHeld(46),"historical down reaches its own scheduled tick");
        Check(!scheduled.Query(46),"same-frame repeated query does not repeat held press");
        scheduled.BeginLogicalFrame(42); Check(!scheduled.Query(46) && !scheduled.IsHeld(46),"historical up reaches later tick");

        capture.Down(45,46,false); capture.Up(46,46); Transfer(capture,scheduled);
        scheduled.BeginLogicalFrame(63);
        Check(!scheduled.IsHeld(46) && scheduled.Query(46) && !scheduled.Query(46),"short down/up between polls yields exactly one real press, never held state");
        capture.Down(65,46,false); capture.Up(66,46); Transfer(capture,scheduled);
        scheduled.BeginLogicalFrame(84); // Higher-priority flow never queried this released key.
        scheduled.BeginLogicalFrame(105); Check(!scheduled.Query(46),"unqueried released tap expires next logical opportunity across screens");

        capture.Down(110,46,false); capture.Down(111,46,true); Transfer(capture,scheduled);
        scheduled.BeginLogicalFrame(126); Check(scheduled.Query(46) && !scheduled.Query(46),"autorepeat does not queue an extra edge");
        scheduled.BeginLogicalFrame(147); Check(!scheduled.Query(46),"held key remains consumed on later poll");
        capture.Up(150,46); capture.Down(151,46,false); Transfer(capture,scheduled);
        scheduled.BeginLogicalFrame(168); Check(scheduled.Query(46) && !scheduled.Query(46),"physical release/repress between polls yields one new edge");

        // Resume drops past presses but leaves a held key suppressed until physical release/new down.
        capture.Down(170,205,false); Transfer(capture,scheduled); scheduled.Resume(175);
        capture.Down(176,205,true); Transfer(capture,scheduled);
        scheduled.BeginLogicalFrame(196); Check(!scheduled.Query(205),"movie resume does not synthesize held/auto-repeat rising edge");
        capture.Up(200,205); capture.Down(201,205,false); Transfer(capture,scheduled);
        scheduled.BeginLogicalFrame(217); Check(scheduled.Query(205),"post-resume physical repress works");

        capture.Reset(220); capture.Down(221,205,true); Transfer(capture,scheduled);
        scheduled.BeginLogicalFrame(238); Check(!scheduled.Query(205) && !scheduled.IsHeld(205),"focus reset clears held and consumed state without inventing auto-repeat press");
        capture.Up(240,205); capture.Down(241,205,false); Transfer(capture,scheduled);
        scheduled.BeginLogicalFrame(259); Check(scheduled.Query(205),"focus-restored fresh down is accepted");

        var mapped=new ScheduledScanInput(1000,0,new ScanCodeEdges());
        mapped.Enqueue(new(1010,46,ScanInputEventKind.Down)); mapped.Enqueue(new(1030,46,ScanInputEventKind.Up));
        mapped.BeginLogicalFrame(21); Check(mapped.Query(46),"Win32 time converts to shared monotonic origin");
        mapped.BeginLogicalFrame(42); Check(!mapped.Query(46),"converted release ordering");
        var wrapped=new ScheduledScanInput(uint.MaxValue-10,0,new ScanCodeEdges());
        wrapped.Enqueue(new(2,46,ScanInputEventKind.Down)); wrapped.Enqueue(new(20,46,ScanInputEventKind.Up));
        wrapped.BeginLogicalFrame(21); Check(wrapped.Query(46),"event clock wrap maps down to13ms");
        wrapped.BeginLogicalFrame(42); Check(!wrapped.Query(46),"event clock wrap maps up to31ms");

        var nativeEdges=new ScanCodeEdges(); bool[] raw=new bool[256]; raw[46]=true;
        Check(nativeEdges.Query(raw,46) && !nativeEdges.Query(raw,46),"native per-query held latch unchanged");
        raw[46]=false; nativeEdges.Query(raw,46); raw[46]=true;
        Check(nativeEdges.Query(raw,46),"native queried-up reset unchanged");
        raw[205]=true; nativeEdges.Query(raw,205); nativeEdges.Reset(46);
        Check(nativeEdges.Query(raw,46) && !nativeEdges.Query(raw,205),"native reveal reset affects only selected direction latch");
        CheckReplayAcrossPresentRates();
    }
    private static void CheckReplayAcrossPresentRates()
    {
        var a=Replay(33);
        var b=Replay(7);
        Check(a.SequenceEqual(b) && a.SequenceEqual(new uint[]{21,84}),"one-Pump short taps replay on identical scheduled deadlines at30/144Hz");
    }
    private static List<uint> Replay(uint presentInterval)
    {
        TimestampedScanInput[] source=[new(10,46,ScanInputEventKind.Down),new(11,46,ScanInputEventKind.Up),
            new(70,46,ScanInputEventKind.Down),new(71,46,ScanInputEventKind.Up)];
        int cursor=0; uint now=0,last=0;
        var input=new ScheduledScanInput(0,0,new ScanCodeEdges()); var answers=new List<uint>();
        while(last<126)
        {
            now+=presentInterval;
            while(cursor<source.Length && source[cursor].Time<=now) input.Enqueue(source[cursor++]);
            while(now-last>=21)
            { last+=21; input.BeginLogicalFrame(last); if(input.Query(46)) answers.Add(last); }
        }
        return answers;
    }
    private static void Transfer(ScanInputCapture capture,ScheduledScanInput scheduled)
    { while(capture.TryDequeue(out var input)) scheduled.Enqueue(input); }
    private static void Check(bool result,string message) { if(!result) throw new InvalidOperationException("Physical input: "+message); }
}
