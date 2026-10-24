using System.Runtime.InteropServices;

namespace Atria.Platform.Win32;

[Flags]
internal enum XInputButtons : ushort
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

public sealed class XInputDevice : IGamepadDevice
{
    public PadSample Read(int slot)
    {
        uint result = XInputGetState((uint)slot, out var s);
        return result == 0 ? new(true, s.Buttons, s.LT, s.RT, s.LX, s.LY, s.RX, s.RY) : default;
    }

    public void Vibrate(int slot, ushort left, ushort right)
    {
        var v = new Vibration
        {
            Left = left,
            Right = right
        };
        XInputSetState((uint)slot, ref v);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct State
    {
        public uint Packet;
        public ushort Buttons;
        public byte LT, RT;
        public short LX, LY, RX, RY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Vibration
    {
        public ushort Left, Right;
    }

    [DllImport("xinput1_4.dll", ExactSpelling = true)]
    private static extern uint XInputGetState(uint index, out State state);
    [DllImport("xinput1_4.dll", ExactSpelling = true)]
    private static extern uint XInputSetState(uint index, ref Vibration state);
}
