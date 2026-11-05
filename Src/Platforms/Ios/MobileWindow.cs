using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atria.Core.Contracts.Input;
using Atria.Core.Contracts.Host;
using Atria.Core.Contracts.Window;
using Atria.Core.Game.Combat;
using Atria.Core.Game.Progression;
using Atria.Core.Resources;
using Atria.Core.Runtime.Input;
using Atria.Core.Runtime.Timing;

namespace Atria.Platforms.Ios;

/// <summary>Transports host input to the game thread without sharing physical and touch slots.</summary>
public sealed class MobileWindow : IGameWindow, IGamepadDevice, IInputActivitySource, IMobileUiChannel
{
    /// <summary>Number of independent physical controller slots.</summary>
    public const int PadCount = 4;
    private readonly ScanInputCapture scans = new();
    private readonly ConcurrentQueue<(byte Scan, bool Down)> keys = new();
    private readonly ConcurrentQueue<string> text = new();
    private readonly ConcurrentQueue<(int X, int Y, bool Down)> pointer = new();
    private readonly object sync = new();
    private readonly List<PadSample>[] padChanges = Enumerable.Range(0, PadCount).Select(_ => new List<PadSample>()).ToArray();
    private readonly List<MobileTouchState> touchChanges = new();
    private readonly Queue<MobileUiCommand> uiCommands = new();
    private readonly PadSample[] pads = new PadSample[PadCount];
    private readonly PadSample[] lastSubmitted = new PadSample[PadCount];
    private readonly bool[] resyncPad = new bool[PadCount];
    private MobileTouchState touchState, lastTouchState;
    private MobileUiSnapshot? uiSnapshot;
    private byte[] uiSnapshotJson = [];
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
    /// <summary>Gets the latest sequenced direct touch interaction reported by the host.</summary>
    public InputActivity LastInputActivity { get { lock (sync) return lastInputActivity; } }
    public ulong UiRevision { get { lock (sync) return uiSnapshot?.Revision ?? 0; } }
    public ulong UiContextId { get { lock (sync) return uiSnapshot?.ContextId ?? 0; } }

    /// <summary>Copies the last Core-owned UTF-8 snapshot; returns its required byte count.</summary>
    public int CopyUiSnapshot(Span<byte> destination)
    {
        lock (sync)
        {
            if (destination.Length < uiSnapshotJson.Length) return uiSnapshotJson.Length;
            uiSnapshotJson.CopyTo(destination);
            return uiSnapshotJson.Length;
        }
    }

    public void PublishUiSnapshot(MobileUiSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (sync)
        {
            if (uiSnapshot is { } current && snapshot.ContextId == current.ContextId && snapshot.Revision == current.Revision) return;
            if (uiSnapshot is { } previous && snapshot.ContextId != previous.ContextId)
            {
                touchChanges.Clear();
                touchState = lastTouchState = new(snapshot.ContextId, 0, 0, MobileTouchButtons.None);
                touchChanges.Add(touchState);
                uiCommands.Clear();
            }

            uiSnapshot = snapshot;
            uiSnapshotJson = JsonSerializer.SerializeToUtf8Bytes(snapshot, MobileUiJsonContext.Default.MobileUiSnapshot);
        }
    }

    public void SubmitUiCommand(MobileUiCommand command)
    {
        lock (sync)
        {
            if (!focused || uiSnapshot is null || command.ContextId != uiSnapshot.ContextId) return;
            if (uiCommands.Count == 128) uiCommands.Dequeue();
            uiCommands.Enqueue(command);
            RecordActivity(InputSourceKind.Touch, 0);
        }
    }

    public bool TryDequeueUiCommand(out MobileUiCommand command)
    {
        lock (sync) return uiCommands.TryDequeue(out command);
    }

    public void SubmitTouchState(MobileTouchState state)
    {
        lock (sync)
        {
            if (!focused || uiSnapshot is null || state.ContextId != uiSnapshot.ContextId || state == lastTouchState) return;
            QueueTouchSample(touchChanges, touchState, state);
            lastTouchState = state;
        }
    }

    public bool TryDequeueTouchState(out MobileTouchState state)
    {
        lock (sync)
        {
            if (touchChanges.Count == 0) { state = touchState; return false; }
            touchState = touchChanges[0];
            touchChanges.RemoveAt(0);
            state = touchState;
            return true;
        }
    }

    /// <summary>Updates drawable size and flushes input when the surface loses focus.</summary>
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
                touchChanges.Clear();
                uiCommands.Clear();
                Array.Clear(pads);
                Array.Fill(resyncPad, true);
                touchState = lastTouchState = new(uiSnapshot?.ContextId ?? 0, 0, 0, MobileTouchButtons.None);
                if (uiSnapshot is { } current) touchChanges.Add(new(current.ContextId, 0, 0, MobileTouchButtons.None));
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

    /// <summary>Records a touch contact even when no touch-control state changes.</summary>
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

    private static void QueueTouchSample(List<MobileTouchState> changes, MobileTouchState consumed, MobileTouchState sample)
    {
        if (changes.Count != 0)
        {
            var last = changes[^1];
            var before = changes.Count > 1 ? changes[^2] : consumed;
            bool modifier = (sample.Buttons & MobileTouchButtons.B) != 0;
            if (!modifier && before.Buttons == last.Buttons && last.Buttons == sample.Buttons &&
                Monotonic(before.LeftX, last.LeftX, sample.LeftX) && Monotonic(before.LeftY, last.LeftY, sample.LeftY))
            {
                changes[^1] = sample;
                return;
            }
        }
        // shortcut: keep at most 128 touch transitions, raise this if the game thread can stall during active input.
        if (changes.Count == 128) changes.RemoveAt(0);
        changes.Add(sample);
    }

    private static bool Monotonic(byte before, byte last, byte next)
        => before <= last && last <= next || before >= last && last >= next;

    private static bool Monotonic(short before, short last, short next)
        => before <= last && last <= next || before >= last && last >= next;

}

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MobileUiSnapshot))]
internal partial class MobileUiJsonContext : JsonSerializerContext;
