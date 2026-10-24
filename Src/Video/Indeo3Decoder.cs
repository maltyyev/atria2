using System.Runtime.InteropServices;
using System.Text;

namespace Atria.Video;

/// <summary>Owned native IV32 codec, with no FFmpeg struct layouts exposed to managed code.</summary>
public sealed unsafe class Indeo3Decoder : IDisposable
{
    private nint context;
    private readonly int pixelCount;
    public Indeo3Decoder(int width, int height)
    {
        if (width <= 0 || width > 4096 || height <= 0 || height > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        pixelCount = checked(width * height);
        context = atria_video_open(width, height);
        if (context == 0)
        {
            throw new InvalidOperationException("Could not initialize the Indeo 3 video decoder.");
        }
    }

    public void Decode(ReadOnlySpan<byte> packet, Span<uint> destination)
    {
        ObjectDisposedException.ThrowIf(context == 0, this);
        if (destination.Length != pixelCount)
        {
            throw new ArgumentException("Wrong video output dimensions.", nameof(destination));
        }

        if (packet.Length == 0)
        {
            return; // AVI dropped frame: retain pixels and advance timeline.
        }

        int result;
        fixed (byte* input = packet)
        fixed (uint* output = destination)
        {
            result = atria_video_decode(context, input, packet.Length, (byte*)output, checked(destination.Length * 4));
        }

        if (result < 0)
        {
            Span<byte> error = stackalloc byte[256];
            fixed (byte* message = error)
            {
                atria_video_error(result, message, error.Length);
            }

            int end = error.IndexOf((byte)0);
            throw new InvalidDataException($"Indeo 3 decode failed: {Encoding.UTF8.GetString(error[..(end < 0 ? error.Length : end)])} ({result}).");
        }
    }

    public void Dispose()
    {
        if (context != 0)
        {
            atria_video_close(context);
            context = 0;
        }
    }

    [DllImport("atria_video.dll", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint atria_video_open(int width, int height);
    [DllImport("atria_video.dll", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int atria_video_decode(nint context, byte* packet, int size, byte* output, int outputSize);
    [DllImport("atria_video.dll", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern void atria_video_close(nint context);
    [DllImport("atria_video.dll", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int atria_video_error(int code, byte* buffer, int size);
}
