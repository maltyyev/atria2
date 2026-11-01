using Atria.Platform.Input;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atria.Platform.Win32;

/// <summary>Owned Win32 window. No simulation is advanced by the message pump.</summary>
public sealed unsafe class NativeWindow : IGameWindow, IDisposable
{
    private const int VirtualKeyCount = 256;
    private const uint PopupWindowStyle = 0x80000000;
    private const uint VisibleWindowStyle = 0x10000000;
    private const int ArrowCursorId = 32512;
    private const int PerMonitorV2DpiContext = -4;
    private const long PreviousKeyStateMask = 1L << 30;
    private const long HardwareScanCodeMask = 0x7f0000;
    private const uint VirtualKeyToExtendedScanCode = 4;

    private enum WindowMessage : uint
    {
        Destroy = 0x0002,
        Close = 0x0010,
        EraseBackground = 0x0014,
        ActivateApp = 0x001c,
        KeyDown = 0x0100,
        KeyUp = 0x0101,
        Character = 0x0102,
        SystemKeyDown = 0x0104,
        SystemKeyUp = 0x0105,
        MouseMove = 0x0200,
        LeftButtonDown = 0x0201,
        LeftButtonUp = 0x0202,
        RightButtonDown = 0x0204,
        RightButtonUp = 0x0205,
        MiddleButtonDown = 0x0207,
        MiddleButtonUp = 0x0208,
        MouseWheel = 0x020a
    }

    private static NativeWindow? active;
    public nint Handle
    {
        get; private set;
    }
    public bool Closed
    {
        get; private set;
    }
    public bool Focused { get; private set; } = true;
    public bool[] Keys { get; } = new bool[VirtualKeyCount];
    public bool[] Pressed { get; } = new bool[VirtualKeyCount];
    private readonly System.Text.StringBuilder textInput = new();
    public string TakeTextInput()
    {
        string value = textInput.ToString();
        textInput.Clear();
        return value;
    }
    public bool[] ScanKeys => scanInput.Keys;
    public bool[] ScanPressed => scanInput.Pressed;
    public long KeyDownCount
    {
        get; private set;
    }
    public int LastVirtualKey
    {
        get; private set;
    }
    public int LastScanCode
    {
        get; private set;
    }
    public int MouseX
    {
        get; private set;
    }
    public int MouseY
    {
        get; private set;
    }
    public bool LeftButton
    {
        get; private set;
    }
    public bool RightButton
    {
        get; private set;
    }
    public bool MiddleButton
    {
        get; private set;
    }
    public bool[] MousePressed { get; } = new bool[3];
    public int MouseWheelDelta
    {
        get; private set;
    }

    private readonly nint instance;
    private ushort windowClass;
    private readonly ScanInputCapture scanInput = new();
    private bool dispatching;
    private uint dispatchMessageId, dispatchMessageTime;
    public static uint MessageClockMilliseconds => GetTickCount();
    public uint InputClockMilliseconds => MessageClockMilliseconds;

    public void RequestClose()
    {
        if (PostMessageW(Handle, 0x0010, 0, 0) == 0)
            throw new IOException("Cannot post game window close.");
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int PostMessageW(nint window, uint message, nuint wParam, nint lParam);

    // One consumer: the shared game/dialogue host. Pump does not discard event history.
    public bool TryDequeueScanInput(out TimestampedScanInput input) => scanInput.TryDequeue(out input);
    public NativeWindow(string title, int width, int height)
    {
        if (active is not null)
        {
            throw new InvalidOperationException("Only one game window is supported.");
        }

        instance = GetModuleHandleW(null);
        // Per-monitor-v2; failure can mean the process context was already set.
        SetProcessDpiAwarenessContext(PerMonitorV2DpiContext);
        active = this;
        fixed (char* name = "AtriaNativeWindow")
        {
            WindowClass cls = new()
            {
                Size = (uint)sizeof(WindowClass),
                Proc = &WindowProc,
                Instance = instance,
                Cursor = LoadCursorW(0, ArrowCursorId),
                ClassName = name
            };
            windowClass = RegisterClassExW(&cls);
            if (windowClass == 0)
            {
                active = null;
                throw new System.ComponentModel.Win32Exception();
            }

            fixed (char* caption = title)
            {
                Handle = CreateWindowExW(0, name, caption, PopupWindowStyle | VisibleWindowStyle, 0, 0, width, height, 0, 0, instance, 0);
            }
        }

        if (Handle == 0)
        {
            Dispose();
            throw new System.ComponentModel.Win32Exception();
        }
    }

    public (int Width, int Height) ClientSize
    {
        get
        {
            Rect r;
            if (!GetClientRect(Handle, &r))
            {
                return (0, 0);
            }

            return (r.Right - r.Left, r.Bottom - r.Top);
        }
    }

    public void Pump()
    {
        Array.Clear(Pressed);
        textInput.Clear();
        Array.Clear(ScanPressed);
        Array.Clear(MousePressed);
        MouseWheelDelta = 0;
        Message msg;
        while (PeekMessageW(&msg, 0, 0, 0, 1))
        {
            if (msg.Id == 0x12)
            {
                Closed = true;
                break;
            }

            dispatching = true;
            dispatchMessageId = msg.Id;
            dispatchMessageTime = msg.Time;
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
            dispatching = false;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProc(nint hwnd, uint msg, nuint wp, nint lp)
    {
        NativeWindow? w = active;
        if (w is not null)
        {
            uint inputTime = w.dispatching && w.dispatchMessageId == msg ? w.dispatchMessageTime : GetTickCount();
            switch ((WindowMessage)msg)
            {
                case WindowMessage.Close:
                    w.Closed = true;
                    return 0; // WM_CLOSE; owner destroys after device shutdown
                case WindowMessage.Destroy:
                    w.Closed = true;
                    PostQuitMessage(0);
                    return 0;
                case WindowMessage.ActivateApp:
                    w.Focused = wp != 0;
                    if (!w.Focused)
                    {
                        Array.Clear(w.Keys);
                        Array.Clear(w.Pressed);
                        w.LeftButton = w.RightButton = w.MiddleButton = false;
                        w.scanInput.Reset(inputTime);
                    }

                    return 0;
                case WindowMessage.KeyDown:
                case WindowMessage.SystemKeyDown:
                    if (wp < VirtualKeyCount)
                    {
                        if (!w.Keys[(int)wp])
                        {
                            w.Pressed[(int)wp] = true;
                        }

                        w.Keys[(int)wp] = true;
                    }

                    int downScan = GetScanCode(wp, lp);
                    w.KeyDownCount++;
                    w.LastVirtualKey = (int)wp;
                    w.LastScanCode = downScan;
                    w.scanInput.Down(inputTime, (byte)downScan, ((long)lp & PreviousKeyStateMask) != 0);
                    break;
                case WindowMessage.Character:
                    if (w.Focused && wp >= 32 && wp <= char.MaxValue)
                        w.textInput.Append((char)wp);
                    return 0;
                case WindowMessage.KeyUp:
                case WindowMessage.SystemKeyUp:
                    if (wp < VirtualKeyCount)
                    {
                        w.Keys[(int)wp] = false;
                    }

                    int upScan = GetScanCode(wp, lp);
                    w.scanInput.Up(inputTime, (byte)upScan);
                    break;
                case WindowMessage.MouseMove:
                    w.MouseX = (short)(long)lp;
                    w.MouseY = (short)((long)lp >> 16);
                    return 0;
                case WindowMessage.LeftButtonDown:
                    if (!w.LeftButton)
                    {
                        w.MousePressed[0] = true;
                    }

                    w.LeftButton = true;
                    return 0;
                case WindowMessage.LeftButtonUp:
                    w.LeftButton = false;
                    return 0;
                case WindowMessage.RightButtonDown:
                    if (!w.RightButton)
                    {
                        w.MousePressed[1] = true;
                    }

                    w.RightButton = true;
                    return 0;
                case WindowMessage.RightButtonUp:
                    w.RightButton = false;
                    return 0;
                case WindowMessage.MiddleButtonDown:
                    if (!w.MiddleButton)
                    {
                        w.MousePressed[2] = true;
                    }

                    w.MiddleButton = true;
                    return 0;
                case WindowMessage.MiddleButtonUp:
                    w.MiddleButton = false;
                    return 0;
                case WindowMessage.MouseWheel:
                    w.MouseWheelDelta += (short)((long)wp >> 16);
                    return 0;
                case WindowMessage.EraseBackground:
                    return 1; // background is cleared by GPU
            }
        }

        return DefWindowProcW(hwnd, msg, wp, lp);
    }

    private static int GetScanCode(nuint virtualKey, nint keyData)
    {
        // Virtual-key injection can legitimately omit lParam's hardware scan code.
        // Use Windows' current keyboard mapping only for that case; physical scans remain authoritative.
        uint mapped = ((long)keyData & HardwareScanCodeMask) == 0 && virtualKey < VirtualKeyCount ? MapVirtualKeyW((uint)virtualKey, VirtualKeyToExtendedScanCode) : 0;
        return PhysicalScanCode.Decode((long)keyData, mapped);
    }

    public void Dispose()
    {
        if (Handle != 0)
        {
            DestroyWindow(Handle);
            Handle = 0;
        }

        if (windowClass != 0)
        {
            UnregisterClassW((nint)windowClass, instance);
            windowClass = 0;
        }

        if (ReferenceEquals(active, this))
        {
            active = null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        public uint Size, Style;
        public delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint> Proc;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public char* MenuName;
        public char* ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetTickCount();
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint LoadCursorW(nint instance, nint name);
    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern ushort RegisterClassExW(WindowClass* cls);
    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint exStyle,
        char* className,
        char* title,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter
    );
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint DefWindowProcW(nint hwnd, uint msg, nuint wp, nint lp);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool PeekMessageW(Message* msg, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool TranslateMessage(Message* msg);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint DispatchMessageW(Message* msg);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetClientRect(nint hwnd, Rect* rect);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool UnregisterClassW(nint name, nint instance);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint MapVirtualKeyW(uint code, uint mapType);
}
