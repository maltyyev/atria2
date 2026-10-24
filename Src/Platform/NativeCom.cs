using System.Runtime.InteropServices;

namespace Atria.Platform;

internal static unsafe class NativeCom
{
    private const int RpcChangedMode = unchecked((int)0x80010106);
    private const uint MultithreadedApartment = 0;

    // An existing STA is usable, but must not be uninitialized by this owner.
    public static bool InitializeMultithreadedApartment()
    {
        int result = CoInitializeEx(reserved: 0, mode: MultithreadedApartment);
        if (result == RpcChangedMode)
        {
            return false;
        }

        Check(result, nameof(CoInitializeEx));
        return true;
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(nint reserved, uint mode);

    [DllImport("ole32.dll", ExactSpelling = true)]
    public static extern void CoUninitialize();

    public static nint Method(nint instance, int slot)
    {
        if (instance == 0)
        {
            throw new ObjectDisposedException("Native interface");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        return (*(nint**)instance)[slot];
    }

    public static void Release(ref nint instance)
    {
        if (instance == 0)
        {
            return;
        }

        ((delegate* unmanaged[Stdcall]<nint, uint>)Method(instance, ComSlots.Unknown.Release))(instance);
        instance = 0;
    }

    public static void Check(int result, string operation)
    {
        if (result < 0)
        {
            throw new ExternalException($"{operation} failed (0x{result:X8}).", result);
        }
    }
}
