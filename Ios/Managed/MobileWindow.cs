using System.Collections.Concurrent;
using Atria.Platform;
using Atria.Platform.Input;

namespace Atria.Ios;

public sealed class MobileWindow : IGameWindow, IGamepadDevice, IVirtualGamepadSource, IInputActivitySource
{
    public const int PadCount = 4;
    private readonly ScanInputCapture scans = new();
    private readonly ConcurrentQueue<(byte Scan, bool Down)> keys = new();
    private readonly ConcurrentQueue<string> text = new();
    private readonly ConcurrentQueue<(int X, int Y, bool Down)> pointer = new();
    private readonly object sync = new();
    private readonly Queue<PadSample>[] padChanges = Enumerable.Range(0, PadCount).Select(_ => new Queue<PadSample>()).ToArray();
    private readonly Queue<PadSample> virtualPadChanges = new();
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
            // Keep short press/release pairs until the game has sampled both states.
            var changes = padChanges[slot];
            if (changes.Count >= 128) changes.Clear();
            changes.Enqueue(sample);
            resyncPad[slot] = false;
            lastSubmitted[slot] = sample;
        }
    }

    public PadSample Read(int slot)
    {
        if (slot is < 0 or >= PadCount) return default;
        lock (sync)
        {
            if (padChanges[slot].TryDequeue(out var next)) pads[slot] = next;
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
            if (virtualPadChanges.Count >= 128) virtualPadChanges.Clear();
            virtualPadChanges.Enqueue(sample);
            lastVirtualPad = sample;
        }
    }

    /// <summary>Returns one pending touch sample while retaining its current state.</summary>
    public PadSample ReadVirtualGamepad()
    {
        lock (sync)
        {
            if (virtualPadChanges.TryDequeue(out var next)) virtualPad = next;
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

    private static bool Neutral(PadSample sample)
        => sample.Buttons == 0 && sample.LeftTrigger <= 32 && sample.RightTrigger <= 32 &&
            Math.Abs((int)sample.LX) <= 1800 && Math.Abs((int)sample.LY) <= 1800 &&
            Math.Abs((int)sample.RX) <= 1800 && Math.Abs((int)sample.RY) <= 1800;
}
