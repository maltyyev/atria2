using System.Runtime.InteropServices;

namespace Atria.Render;

/// <summary>Owns presentation lifetime; a lost GPU surface does not own or destroy the game session.</summary>
public sealed class D3D11Presenter : IDisposable
{
    internal const int MaximumFrameDimension = 4096;
    private const int DxgiDeviceRemoved = unchecked((int)0x887A0005);
    private const int DxgiDeviceReset = unchecked((int)0x887A0007);
    private readonly nint window;
    private D3D11Surface? surface;
    private int width, height;
    private int sourceWidth = 640, sourceHeight = 480;
    public Viewport ContentViewport => Surface.ContentViewport;
    public uint FeatureLevel => Surface.FeatureLevel;
    private D3D11Surface Surface => surface ?? throw new ObjectDisposedException(nameof(D3D11Presenter));

    public D3D11Presenter(nint window, int width, int height)
    {
        this.window = window;
        this.width = width;
        this.height = height;
        surface = new(window, width, height);
    }

    public void Resize(int newWidth, int newHeight)
    {
        if (newWidth <= 0 || newHeight <= 0)
        {
            return;
        }

        width = newWidth;
        height = newHeight;
        try
        {
            Surface.Resize(width, height);
        }
        catch (ExternalException error) when (IsDeviceLost(error.ErrorCode))
        {
            RecreateDevice();
        }
    }

    public void SetFrameSize(int frameWidth, int frameHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0 || frameWidth > MaximumFrameDimension || frameHeight > MaximumFrameDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(frameWidth));
        }

        sourceWidth = frameWidth;
        sourceHeight = frameHeight;
        try
        {
            Surface.SetFrameSize(frameWidth, frameHeight);
        }
        catch (ExternalException error) when (IsDeviceLost(error.ErrorCode))
        {
            RecreateDevice();
        }
    }

    public void Present(ReadOnlySpan<uint> pixels)
    {
        try
        {
            Surface.Present(pixels);
        }
        catch (ExternalException error) when (IsDeviceLost(error.ErrorCode))
        {
            RecreateDevice();
            Surface.Present(pixels);
        }
    }

    internal static bool IsDeviceLost(int errorCode) => errorCode is DxgiDeviceRemoved or DxgiDeviceReset;
    internal void RecreateDevice()
    {
        _ = Surface;
        surface!.Dispose();
        surface = null;
        var replacement = new D3D11Surface(window, width, height);
        try
        {
            replacement.SetFrameSize(sourceWidth, sourceHeight);
        }
        catch
        {
            replacement.Dispose();
            throw;
        }

        surface = replacement;
        Console.WriteLine("D3D11 presentation device recreated.");
    }

    public void Dispose()
    {
        surface?.Dispose();
        surface = null;
    }
}
