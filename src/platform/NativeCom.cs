using System.Runtime.InteropServices;
namespace Atria.Platform;

internal static unsafe class NativeCom
{
    public static nint Method(nint instance,int slot) => (*(nint**)instance)[slot];
    public static void Release(ref nint instance)
    {
        if(instance==0) return;
        ((delegate* unmanaged[Stdcall]<nint,uint>)Method(instance,2))(instance); instance=0;
    }
    public static void Check(int result,string operation)
    {
        if(result<0) throw new ExternalException($"{operation} failed (0x{result:X8}).",result);
    }
}
