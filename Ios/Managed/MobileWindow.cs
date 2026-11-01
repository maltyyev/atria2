using System.Collections.Concurrent;
using Atria.Platform;
using Atria.Platform.Input;

namespace Atria.Ios;

public sealed class MobileWindow : IGameWindow, IGamepadDevice
{
    private readonly ScanInputCapture scans = new();
    private readonly ConcurrentQueue<(byte Scan, bool Down)> keys = new();
    private readonly ConcurrentQueue<string> text = new();
    private readonly ConcurrentQueue<(int X, int Y, bool Down)> pointer = new();
    private readonly object sync = new();
    private readonly Queue<PadSample> padChanges = new();
    private PadSample pad = new(true, 0, 0, 0, 0, 0, 0, 0);
    private PadSample lastSubmitted;
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
                padChanges.Clear();
                pad = lastSubmitted = new(true, 0, 0, 0, 0, 0, 0, 0);
                keys.Clear();
                pointer.Clear();
            }
        }
    }

    public void SubmitPad(PadSample sample)
    {
        lock (sync)
        {
            if (!focused || sample == lastSubmitted) return;
            // Keep short touch press/release pairs until the game has sampled both.
            if (padChanges.Count >= 128) padChanges.Clear();
            padChanges.Enqueue(sample);
            lastSubmitted = sample;
        }
    }

    public PadSample Read(int slot)
    {
        if (slot != 0) return default;
        lock (sync)
        {
            if (padChanges.TryDequeue(out var next)) pad = next;
            return pad;
        }
    }

    // shortcut: controller rumble is deferred until physical Xbox input has been calibrated on iOS.
    public void Vibrate(int slot, ushort left, ushort right) { }
    public void SubmitKey(byte scan, bool down) { if (focused) keys.Enqueue((scan, down)); }
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
}
