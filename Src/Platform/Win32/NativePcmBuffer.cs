using System.Runtime.InteropServices;

namespace Atria.Platform.Win32;

// XAudio2 retains sample pointers; managed arrays cannot move while playback owns them.
internal sealed unsafe class NativePcmBuffer : IDisposable
{
    private nint memory;
    private readonly int length;

    public NativePcmBuffer(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        this.length = length;
        memory = (nint)NativeMemory.Alloc((nuint)length);
    }

    public nint Pointer
    {
        get
        {
            ObjectDisposedException.ThrowIf(memory == 0, this);
            return memory;
        }
    }

    public Span<byte> Bytes => new((void*)Pointer, length);

    // Owners must destroy the voice before releasing buffers it may still reference.
    public void Dispose()
    {
        NativeMemory.Free((void*)memory);
        memory = 0;
    }
}
