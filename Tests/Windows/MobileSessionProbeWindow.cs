using System.Text.Json;
using Atria.Core.Contracts.Host;
using Atria.Core.Contracts.Input;
using Atria.Core.Contracts.Window;
using Atria.Platforms.Windows.Win32;

namespace Tests.Windows;

/// <summary>Combines a native Windows surface with a controllable mobile snapshot/command channel.</summary>
internal sealed class MobileSessionProbeWindow(NativeWindow native) : IGameWindow, IMobileUiChannel
{
    private readonly Queue<MobileUiCommand> commands = new();
    private readonly Queue<MobileTouchState> touch = new();
    private MobileUiSnapshot? snapshot;
    private byte[] json = [];

    public bool Closed => native.Closed;
    public bool Focused => native.Focused;
    public (int Width, int Height) ClientSize => native.ClientSize;
    public bool[] ScanKeys => native.ScanKeys;
    public bool[] ScanPressed => native.ScanPressed;
    public int MouseX => native.MouseX;
    public int MouseY => native.MouseY;
    public bool LeftButton => native.LeftButton;
    public bool RightButton => native.RightButton;
    public bool MiddleButton => native.MiddleButton;
    public bool[] MousePressed => native.MousePressed;
    public int MouseWheelDelta => native.MouseWheelDelta;
    public uint InputClockMilliseconds => native.InputClockMilliseconds;
    public ulong UiRevision => snapshot?.Revision ?? 0;
    public ulong UiContextId => snapshot?.ContextId ?? 0;

    public void Pump() => native.Pump();
    public bool TryDequeueScanInput(out TimestampedScanInput input) => native.TryDequeueScanInput(out input);
    public string TakeTextInput() => native.TakeTextInput();
    public void RequestClose() => native.RequestClose();

    public int CopyUiSnapshot(Span<byte> destination)
    {
        if (destination.Length < json.Length) return json.Length;
        json.CopyTo(destination);
        return json.Length;
    }

    public void PublishUiSnapshot(MobileUiSnapshot value)
    {
        snapshot = value;
        json = JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    // Deliberately keeps queued stale commands so the Core context guard is exercised.
    public void SubmitUiCommand(MobileUiCommand command)
    {
        if (commands.Count == 128) commands.Dequeue();
        commands.Enqueue(command);
    }

    public bool TryDequeueUiCommand(out MobileUiCommand command) => commands.TryDequeue(out command);
    public void SubmitTouchState(MobileTouchState state) => touch.Enqueue(state);
    public bool TryDequeueTouchState(out MobileTouchState state) => touch.TryDequeue(out state);
}
