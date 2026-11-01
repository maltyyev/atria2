using Atria.Platform.Input;

namespace Atria.Platform;

/// <summary>Transport state sampled by the game thread; pumping never advances simulation.</summary>
public interface IGameWindow
{
    bool Closed { get; }
    bool Focused { get; }
    (int Width, int Height) ClientSize { get; }
    bool[] ScanKeys { get; }
    bool[] ScanPressed { get; }
    int MouseX { get; }
    int MouseY { get; }
    bool LeftButton { get; }
    bool RightButton { get; }
    bool MiddleButton { get; }
    bool[] MousePressed { get; }
    int MouseWheelDelta { get; }
    uint InputClockMilliseconds { get; }
    void Pump();
    bool TryDequeueScanInput(out TimestampedScanInput input);
    string TakeTextInput();
    void RequestClose();
}
