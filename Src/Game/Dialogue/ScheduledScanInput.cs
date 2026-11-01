using Atria.Platform.Input;
using Atria.Platform;
using Atria.Platform.Win32;

namespace Atria.Game.Dialogue;

/// <summary>Converts Win32 event timestamps once into the shared logical clock, then replays each scheduled poll.</summary>
public sealed class ScheduledScanInput
{
    private readonly Queue<TimestampedScanInput> pending = new();
    private readonly bool[] keys = new bool[256];
    private readonly bool[] pressed = new bool[256];
    private readonly bool[] deferredTapPoll = new bool[256];
    private readonly ScanCodeEdges edges;
    private readonly uint clockOffset;
    private uint resetThrough;
    private bool hasReset;
    public ScheduledScanInput(uint messageClockMilliseconds, uint logicalClockMilliseconds, ScanCodeEdges edges)
    {
        clockOffset = unchecked(logicalClockMilliseconds - messageClockMilliseconds);
        this.edges = edges;
    }

    public void Enqueue(TimestampedScanInput input)
    {
        uint logicalTime = unchecked(input.Time + clockOffset);
        if (hasReset && unchecked((int)(logicalTime - resetThrough)) <= 0)
        {
            return;
        }

        pending.Enqueue(input with
        {
            Time = logicalTime
        });
    }

    public void BeginLogicalFrame(uint scheduledMilliseconds)
    {
        // Intentional short-tap preservation: one eligible logical opportunity, not an unlimited screen-crossing backlog.
        for (int scan = 0; scan < pressed.Length; scan++)
        {
            if (!deferredTapPoll[scan])
            {
                pressed[scan] = false;
            }
        }

        while (pending.TryPeek(out var input) && unchecked((int)(scheduledMilliseconds - input.Time)) >= 0)
        {
            pending.Dequeue();
            if (input.Kind == ScanInputEventKind.Reset)
            {
                Array.Clear(keys);
                Array.Clear(pressed);
                edges.Reset();
            }
            else
            {
                keys[input.Scan] = input.Kind == ScanInputEventKind.Down;
                if (input.Kind == ScanInputEventKind.Down)
                {
                    pressed[input.Scan] = true;
                }
            }
        }
    }

    public bool Query(byte scan) => QueryWithHeld(scan, IsHeld);
    //All modal/combat queries share both the native edge bank and this one short-tap token.
    public bool QueryWithHeld(byte scan, Func<byte, bool> held)
    {
        bool physicalPress = pressed[scan];
        pressed[scan] = false;
        return edges.Query(held, scan) || physicalPress;
    }

    public bool IsHeld(byte scan) => keys[scan];
    //A slower input consumer may retain one tap until its NEXT poll, while actor sampling continues.
    //All consumers still share the same token/latch; an earlier actor query consumes it normally.
    public void DeferTapsUntilPoll(ReadOnlySpan<byte> scans)
    {
        foreach (byte scan in scans)
        {
            deferredTapPoll[scan] = true;
        }
    }

    public void CompleteDeferredPoll()
    {
        for (int scan = 0; scan < pressed.Length; scan++)
        {
            if (deferredTapPoll[scan])
            {
                pressed[scan] = false;
            }
        }
    }

    public void StopDeferringTaps()
    {
        CompleteDeferredPoll();
        Array.Clear(deferredTapPoll);
    }

    public void ClearQueryLatch(byte scan) => edges.Reset(scan);
    // Explicit focus/movie pause policy. Drop past input, retain future events, never synthesize held downs.
    public void Resume(uint resumedMilliseconds)
    {
        Array.Clear(keys);
        Array.Clear(pressed);
        edges.Reset();
        resetThrough = resumedMilliseconds;
        hasReset = true;
        while (pending.TryPeek(out var input) && unchecked((int)(resumedMilliseconds - input.Time)) >= 0)
        {
            pending.Dequeue();
        }
    }
}
