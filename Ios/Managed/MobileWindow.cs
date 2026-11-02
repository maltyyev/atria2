using System.Collections.Concurrent;
using Atria.Platform;
using Atria.Platform.Input;

namespace Atria.Ios;

/// <summary>Transports host input to the game thread without sharing physical and touch slots.</summary>
public sealed class MobileWindow : IGameWindow, IGamepadDevice, IVirtualGamepadSource, IInputActivitySource
{
    public const int PadCount = 4;
    private readonly ScanInputCapture scans = new();
    private readonly ConcurrentQueue<(byte Scan, bool Down)> keys = new();
    private readonly ConcurrentQueue<string> text = new();
    private readonly ConcurrentQueue<(int X, int Y, bool Down)> pointer = new();
    private readonly object sync = new();
    private readonly List<PadSample>[] padChanges = Enumerable.Range(0, PadCount).Select(_ => new List<PadSample>()).ToArray();
    private readonly List<PadSample> virtualPadChanges = new();
    private readonly PadSample[] pads = new PadSample[PadCount];
    private readonly PadSample[] lastSubmitted = new PadSample[PadCount];
    private readonly bool[] resyncPad = new bool[PadCount];
    private PadSample virtualPad, lastVirtualPad;
    private InputActivity lastInputActivity;
    private long inputSequence;
    private volatile bool focused = true;
    private volatile bool requestedFocus = true;
    private int focusReset;
    private volatile bool closed;
    private bool wasFocused = true;
    private (int Width, int Height) size = (1280, 720);

    public bool Closed => closed;
    public bool Focused => focused;
    public (int Width, int Height) ClientSize { get { lock (sync) return size; } }
    public bool[] ScanKeys => scans.Keys;
    public bool[] ScanPressed => scans.Pressed;
    public int MouseX { get; private set; } = -1;
    public int MouseY { get; private set; } = -1;
    public bool LeftButton { get; private set; }
    public bool RightButton => false;
    public bool MiddleButton => false;
    public bool[] MousePressed { get; } = new bool[3];
    public int MouseWheelDelta => 0;
    public uint InputClockMilliseconds => unchecked((uint)Environment.TickCount64);
    public InputActivity LastInputActivity { get { lock (sync) return lastInputActivity; } }

    public void SetSurface(int width, int height, bool active)
    {
        lock (sync)
        {
            size = (width, height);
            requestedFocus = active;
            if (!active)
            {
                focused = false;
                Interlocked.Exchange(ref focusReset, 1);
                foreach (var changes in padChanges) changes.Clear();
                virtualPadChanges.Clear();
                Array.Clear(pads);
                Array.Fill(resyncPad, true);
                virtualPad = lastVirtualPad = default;
                keys.Clear();
                pointer.Clear();
            }
        }
    }

    /// <summary>Queues state changes for one physical controller slot.</summary>
    public void SubmitPad(int slot, PadSample sample)
    {
        if (slot is < 0 or >= PadCount) return;
        lock (sync)
        {
            if (!focused) return;
            if (sample == lastSubmitted[slot] && !resyncPad[slot]) return;
            QueueSample(padChanges[slot], pads[slot], sample);
            resyncPad[slot] = false;
            lastSubmitted[slot] = sample;
        }
    }

    public PadSample Read(int slot)
    {
        if (slot is < 0 or >= PadCount) return default;
        lock (sync)
        {
            if (padChanges[slot].Count != 0)
            {
                pads[slot] = padChanges[slot][0];
                padChanges[slot].RemoveAt(0);
            }
            return pads[slot];
        }
    }

    /// <summary>Queues touch controls without marking a physical controller connected.</summary>
    public void SubmitVirtualPad(PadSample sample)
    {
        sample = sample with { Connected = !Neutral(sample) };
        lock (sync)
        {
            if (!focused || sample == lastVirtualPad) return;
            QueueSample(virtualPadChanges, virtualPad, sample);
            lastVirtualPad = sample;
        }
    }

    /// <summary>Returns one pending touch sample while retaining its current state.</summary>
    public PadSample ReadVirtualGamepad()
    {
        lock (sync)
        {
            if (virtualPadChanges.Count != 0)
            {
                virtualPad = virtualPadChanges[0];
                virtualPadChanges.RemoveAt(0);
            }
            return virtualPad;
        }
    }

    /// <summary>Records a touch contact even when it does not change any virtual control.</summary>
    public void SubmitTouchActivity(int identity)
    {
        lock (sync)
        {
            if (focused) RecordActivity(InputSourceKind.Touch, identity);
        }
    }

    // shortcut: controller rumble is deferred until physical Xbox input has been calibrated on iOS.
    public void Vibrate(int slot, ushort left, ushort right) { }
    public void SubmitKey(byte scan, bool down)
    {
        lock (sync)
        {
            if (focused) keys.Enqueue((scan, down));
        }
    }
    public void SubmitText(string value) { if (focused) text.Enqueue(value); }
    public void SubmitPointer(int x, int y, bool down) { if (focused) pointer.Enqueue((x, y, down)); }
    public void Pump()
    {
        Array.Clear(scans.Pressed);
        Array.Clear(MousePressed);
        bool lostFocus = Interlocked.Exchange(ref focusReset, 0) != 0;
        focused = requestedFocus && !lostFocus;
        if (wasFocused != focused || lostFocus)
        {
            scans.Reset(InputClockMilliseconds);
            LeftButton = false;
            MouseX = MouseY = -1;
            wasFocused = focused;
        }
        if (pointer.TryDequeue(out var position))
        {
            MouseX = position.X;
            MouseY = position.Y;
            MousePressed[0] = position.Down && !LeftButton;
            LeftButton = position.Down;
        }
        while (keys.TryDequeue(out var key))
        {
            if (key.Down) scans.Down(InputClockMilliseconds, key.Scan, false);
            else scans.Up(InputClockMilliseconds, key.Scan);
        }
    }

    public bool TryDequeueScanInput(out TimestampedScanInput input) => scans.TryDequeue(out input);
    public string TakeTextInput()
    {
        var result = new System.Text.StringBuilder();
        while (text.TryDequeue(out var value)) result.Append(value);
        return result.ToString();
    }

    public void RequestClose() => closed = true;

    private void RecordActivity(InputSourceKind kind, int identity)
        => lastInputActivity = new InputActivity(++inputSequence, kind, identity);

    private static void QueueSample(List<PadSample> changes, PadSample consumed, PadSample sample)
    {
        if (changes.Count != 0)
        {
            var last = changes[^1];
            var before = changes.Count > 1 ? changes[^2] : consumed;
            // Retain digital edges and trigger extrema so any configured trigger threshold still crosses.
            if (before.Connected == last.Connected && last.Connected == sample.Connected &&
                before.Buttons == last.Buttons && last.Buttons == sample.Buttons &&
                Monotonic(before.LeftTrigger, last.LeftTrigger, sample.LeftTrigger) &&
                Monotonic(before.RightTrigger, last.RightTrigger, sample.RightTrigger))
            {
                changes[^1] = sample;
                return;
            }
        }
        // shortcut: retain at most 128 digital transitions; revisit if host polling can stall for seconds.
        if (changes.Count == 128) changes.RemoveAt(0);
        changes.Add(sample);
    }

    private static bool Monotonic(byte before, byte last, byte next)
        => before <= last && last <= next || before >= last && last >= next;

    // Touch controls are digital full-scale values; physical deadzones remain in InputRouter.
    private static bool Neutral(PadSample sample)
        => sample.Buttons == 0 && sample.LeftTrigger <= 32 && sample.RightTrigger <= 32 &&
            Math.Abs((int)sample.LX) <= 1800 && Math.Abs((int)sample.LY) <= 1800 &&
            Math.Abs((int)sample.RX) <= 1800 && Math.Abs((int)sample.RY) <= 1800;
}
