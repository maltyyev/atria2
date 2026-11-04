using Atria.Core.Game.Progression;
namespace Atria.Core.Contracts.Input;

[Flags]
public enum GamepadButtons : ushort
{
    DpadUp = 0x0001,
    DpadDown = 0x0002,
    DpadLeft = 0x0004,
    DpadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftThumb = 0x0040,
    RightThumb = 0x0080,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000
}

public readonly record struct PadSample(bool Connected, ushort Buttons, byte LeftTrigger, byte RightTrigger, short LX, short LY, short RX, short RY);
public interface IGamepadDevice
{
    PadSample Read(int slot);
    void Vibrate(int slot, ushort left, ushort right);
}

/// <summary>Names the device family that produced the latest meaningful user activity.</summary>
public enum InputSourceKind
{
    Keyboard,
    Mouse,
    Gamepad,
    Touch
}

/// <summary>Identifies one meaningful input activity without allocating an event object.</summary>
public readonly record struct InputActivity(long Sequence, InputSourceKind Kind, int Identity);

/// <summary>Exposes meaningful platform activity such as direct touch gestures.</summary>
public interface IInputActivitySource
{
    /// <summary>Gets the latest activity; increment Sequence only for a new intentional action.</summary>
    InputActivity LastInputActivity { get; }
}
