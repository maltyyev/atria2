using System.Runtime.InteropServices;

namespace Atria.Platform.Win32;

using Atria.Platform.Input;

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
