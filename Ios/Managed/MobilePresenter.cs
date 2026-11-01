using Atria.Render;

namespace Atria.Ios;

public sealed class MobilePresenter : IFramePresenter, IDisposable
{
    public const int MaximumPixels = 2560 * 1440;
    private readonly object sync = new();
    private readonly AutoResetEvent consumed = new(false);
    private uint[] frame = [];
    private int width = 640, height = 480, sequence;

    public void Resize(int width, int height) { }
    public void SetFrameSize(int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > MaximumPixels)
            throw new ArgumentOutOfRangeException(nameof(width));
        lock (sync)
        {
            this.width = width;
            this.height = height;
        }
    }

    public void Present(ReadOnlySpan<uint> pixels)
    {
        lock (sync)
        {
            if (pixels.Length != width * height) throw new ArgumentException("Frame dimensions do not match pixels.");
            if (frame.Length != pixels.Length) frame = new uint[pixels.Length];
            pixels.CopyTo(frame);
            sequence++;
        }
        // Windows Present waits for v-sync; bound this producer even while iOS is suspended.
        consumed.WaitOne(17);
    }

    public int CopyFrame(Span<uint> destination, out int w, out int h)
    {
        lock (sync)
        {
            w = width;
            h = height;
            if (frame.Length == 0 || frame.Length != w * h) return 0;
            if (destination.Length < frame.Length) return -1;
            frame.CopyTo(destination);
            consumed.Set();
            return sequence;
        }
    }

    public void Dispose() => consumed.Dispose();
}
