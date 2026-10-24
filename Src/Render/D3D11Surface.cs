using System.Runtime.InteropServices;
using System.Text;
using Atria.Platform;
using static Atria.Platform.NativeCom;

namespace Atria.Render;

/// <summary>Nearest-neighbor GPU presentation of the original 640x480 logical framebuffer.</summary>
internal sealed unsafe class D3D11Surface : IDisposable
{
    private const uint Bgra8Unorm = 87;
    private const uint RenderTargetOutputUsage = 0x20;
    private const uint FlipDiscard = 4;
    private const uint HardwareDriver = 1;
    private const uint BgraDeviceSupport = 0x20;
    private const uint D3D11SdkVersion = 7;
    private const uint DynamicTextureUsage = 2;
    private const uint ShaderResourceBinding = 8;
    private const uint CpuWriteAccess = 0x10000;
    private const uint MapWriteDiscard = 4;
    private const uint TriangleList = 4;
    private const uint ShaderOptimizationLevel3 = 0x8000;
    private const int BadgeMarginPixels = 12;

    private nint device, context, swapChain, target, texture, view, vertexShader, pixelShader;
    private nint badgeTexture, badgeView;
    private int badgeWidth;
    private int width, height, sourceWidth = 640, sourceHeight = 480;
    public Viewport ContentViewport
    {
        get; private set;
    }
    public uint FeatureLevel
    {
        get; private set;
    }

    public D3D11Surface(nint window, int width, int height)
    {
        this.width = width;
        this.height = height;
        SwapDescription desc = new()
        {
            Width = (uint)width,
            Height = (uint)height,
            Format = Bgra8Unorm,
            SampleCount = 1,
            Usage = RenderTargetOutputUsage,
            BufferCount = 2,
            Window = window,
            Windowed = 1,
            SwapEffect = FlipDiscard
        };
        uint feature = 0;
        try
        {
            fixed (nint* pd = &device, pc = &context, ps = &swapChain)
            {
                Check(D3D11CreateDeviceAndSwapChain(
                    adapter: 0, driver: HardwareDriver, software: 0, flags: BgraDeviceSupport,
                    levels: null, count: 0, sdk: D3D11SdkVersion, description: &desc,
                    swap: ps, device: pd, feature: &feature, context: pc), "D3D11CreateDeviceAndSwapChain");
            }

            FeatureLevel = feature;
            CreateTarget();
            LimitFrameLatency();
            CreateTexture();
            CreateBuildBadge();
            vertexShader = CreateShader("VS", "vs_5_0", ComSlots.D3DDevice.CreateVertexShader);
            pixelShader = CreateShader("PS", "ps_5_0", ComSlots.D3DDevice.CreatePixelShader);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void SetFrameSize(int frameWidth, int frameHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0 || frameWidth > D3D11Presenter.MaximumFrameDimension || frameHeight > D3D11Presenter.MaximumFrameDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(frameWidth));
        }

        if (sourceWidth == frameWidth && sourceHeight == frameHeight)
        {
            return;
        }

        nint empty = 0;
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, void>)Method(context, ComSlots.D3DContext.SetPixelShaderResources))(context, 0, 1, &empty);
        Release(ref view);
        Release(ref texture);
        sourceWidth = frameWidth;
        sourceHeight = frameHeight;
        CreateTexture();
        ContentViewport = Viewport.Fit(width, height, sourceWidth, sourceHeight);
    }

    private void CreateTexture() => CreateDynamicTexture(sourceWidth, sourceHeight, out texture, out view);

    private void CreateBuildBadge()
    {
        var badge = BuildBadge.Create();
        badgeWidth = badge.Width;
        CreateDynamicTexture(badgeWidth, BuildBadge.Height, out badgeTexture, out badgeView);
        UploadPixels(badgeTexture, badge.Pixels, badgeWidth, BuildBadge.Height);
    }

    private void CreateDynamicTexture(int textureWidth, int textureHeight, out nint image, out nint resourceView)
    {
        TextureDescription description = new()
        {
            Width = (uint)textureWidth,
            Height = (uint)textureHeight,
            Mips = 1,
            ArraySize = 1,
            Format = Bgra8Unorm,
            SampleCount = 1,
            Usage = DynamicTextureUsage,
            BindFlags = ShaderResourceBinding,
            CpuAccess = CpuWriteAccess
        };
        image = resourceView = 0;
        var createTexture = (delegate* unmanaged[Stdcall]<nint, TextureDescription*, nint, nint*, int>)
            Method(device, ComSlots.D3DDevice.CreateTexture2D);
        var createView = (delegate* unmanaged[Stdcall]<nint, nint, nint, nint*, int>)
            Method(device, ComSlots.D3DDevice.CreateShaderResourceView);
        nint noInitialData = 0;
        nint defaultViewDescription = 0;
        fixed (nint* result = &image)
        {
            Check(createTexture(device, &description, noInitialData, result), "Create dynamic texture");
        }

        fixed (nint* result = &resourceView)
        {
            Check(createView(device, image, defaultViewDescription, result), "Create texture view");
        }
    }

    private void UploadPixels(nint image, ReadOnlySpan<uint> pixels, int pixelWidth, int pixelHeight)
    {
        Mapped mapped = default;
        const uint firstSubresource = 0;
        const uint noMapFlags = 0;
        var map = (delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, Mapped*, int>)
            Method(context, ComSlots.D3DContext.Map);
        Check(map(context, image, firstSubresource, MapWriteDiscard, noMapFlags, &mapped), "Map texture");
        try
        {
            int rowBytes = pixelWidth * sizeof(uint);
            fixed (uint* source = pixels)
            {
                for (int row = 0; row < pixelHeight; row++)
                {
                    Buffer.MemoryCopy(source + row * pixelWidth, (byte*)mapped.Data + row * mapped.RowPitch, rowBytes, rowBytes);
                }
            }
        }
        finally
        {
            var unmap = (delegate* unmanaged[Stdcall]<nint, nint, uint, void>)
                Method(context, ComSlots.D3DContext.Unmap);
            unmap(context, image, firstSubresource);
        }
    }

    private void LimitFrameLatency()
    {
        Guid iid = new("77db970f-6276-48ba-ba28-070143b4392c");
        nint dxgi = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Method(device, ComSlots.Unknown.QueryInterface))(device, &iid, &dxgi);
        Check(hr, "Query IDXGIDevice1");
        try
        {
            Check(((delegate* unmanaged[Stdcall]<nint, uint, int>)Method(dxgi, ComSlots.DxgiDevice.SetMaximumFrameLatency))(dxgi, 1), "SetMaximumFrameLatency");
        }
        finally
        {
            Release(ref dxgi);
        }
    }

    private void CreateTarget()
    {
        Guid iid = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        nint back = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, uint, Guid*, nint*, int>)Method(swapChain, ComSlots.SwapChain.GetBuffer))(swapChain, 0, &iid, &back), "GetBuffer");
        try
        {
            fixed (nint* p = &target)
            {
                Check(((delegate* unmanaged[Stdcall]<nint, nint, nint, nint*, int>)Method(device, ComSlots.D3DDevice.CreateRenderTargetView))(device, back, 0, p), "CreateRenderTargetView");
            }
        }
        finally
        {
            Release(ref back);
        }

        ContentViewport = Viewport.Fit(width, height, sourceWidth, sourceHeight);
    }

    public void Resize(int newWidth, int newHeight)
    {
        if (newWidth <= 0 || newHeight <= 0 || (newWidth == width && newHeight == height))
        {
            return;
        }

        ((delegate* unmanaged[Stdcall]<nint, uint, nint*, nint, void>)Method(context, ComSlots.D3DContext.SetRenderTargets))(context, 0, null, 0);
        Release(ref target);
        Check(((delegate* unmanaged[Stdcall]<nint, uint, uint, uint, uint, uint, int>)Method(swapChain, ComSlots.SwapChain.ResizeBuffers))(swapChain, 0, (uint)newWidth, (uint)newHeight, 0, 0), "ResizeBuffers");
        width = newWidth;
        height = newHeight;
        CreateTarget();
    }

    public void Present(ReadOnlySpan<uint> pixels)
    {
        if (pixels.Length != sourceWidth * sourceHeight)
        {
            throw new ArgumentException("Framebuffer dimensions do not match the selected source size.", nameof(pixels));
        }

        UploadPixels(texture, pixels, sourceWidth, sourceHeight);

        nint t = target, v = view;
        ((delegate* unmanaged[Stdcall]<nint, uint, nint*, nint, void>)Method(context, ComSlots.D3DContext.SetRenderTargets))(context, 1, &t, 0);
        float* clear = stackalloc float[4]
        {
            0,
            0,
            0,
            1
        };
        ((delegate* unmanaged[Stdcall]<nint, nint, float*, void>)Method(context, ComSlots.D3DContext.ClearRenderTarget))(context, target, clear);
        Viewport fit = ContentViewport;
        GpuViewport vp = new()
        {
            X = fit.X,
            Y = fit.Y,
            Width = fit.Width,
            Height = fit.Height,
            MaxDepth = 1
        };
        ((delegate* unmanaged[Stdcall]<nint, uint, GpuViewport*, void>)Method(context, ComSlots.D3DContext.SetViewports))(context, 1, &vp);
        ((delegate* unmanaged[Stdcall]<nint, uint, void>)Method(context, ComSlots.D3DContext.SetPrimitiveTopology))(context, TriangleList);
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, uint, void>)Method(context, ComSlots.D3DContext.SetVertexShader))(context, vertexShader, 0, 0);
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, uint, void>)Method(context, ComSlots.D3DContext.SetPixelShader))(context, pixelShader, 0, 0);
        DrawTexture(v);
        // Draw last in physical window coordinates, including over movies and letterboxing.
        float badgeScale = Math.Min(1f, Math.Min(width / (float)(badgeWidth + 2 * BadgeMarginPixels), height / (float)(BuildBadge.Height + 2 * BadgeMarginPixels)));
        vp = new()
        {
            X = BadgeMarginPixels * badgeScale,
            Y = height - (BuildBadge.Height + BadgeMarginPixels) * badgeScale,
            Width = badgeWidth * badgeScale,
            Height = BuildBadge.Height * badgeScale,
            MaxDepth = 1
        };
        v = badgeView;
        ((delegate* unmanaged[Stdcall]<nint, uint, GpuViewport*, void>)Method(context, ComSlots.D3DContext.SetViewports))(context, 1, &vp);
        DrawTexture(v);
        Check(((delegate* unmanaged[Stdcall]<nint, uint, uint, int>)Method(swapChain, ComSlots.SwapChain.Present))(swapChain, 1, 0), "Present");
    }

    private void DrawTexture(nint resourceView)
    {
        const uint firstTextureSlot = 0;
        const uint textureCount = 1;
        const uint triangleVertexCount = 3;
        const uint firstVertex = 0;
        var bind = (delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, void>)
            Method(context, ComSlots.D3DContext.SetPixelShaderResources);
        var draw = (delegate* unmanaged[Stdcall]<nint, uint, uint, void>)
            Method(context, ComSlots.D3DContext.Draw);
        bind(context, firstTextureSlot, textureCount, &resourceView);
        draw(context, triangleVertexCount, firstVertex);
    }

    // HLSL compilation is GPU shader compilation, not generation/execution of managed IL.
    private nint CreateShader(string entry, string profile, int method)
    {
        byte[] source = Encoding.ASCII.GetBytes("struct O{float4 p:SV_Position;float2 uv:TEXCOORD0;}; O VS(uint i:SV_VertexID){O o;float2 uv=float2((i<<1)&2,i&2);o.p=float4(uv*float2(2,-2)+float2(-1,1),0,1);o.uv=uv;return o;} Texture2D<float4> img:register(t0);float4 PS(O o):SV_Target{uint w,h;img.GetDimensions(w,h);int2 size=int2(w,h);return float4(img.Load(int3(clamp(int2(o.uv*size),int2(0,0),size-1),0)).rgb,1);}");
        nint blob = 0, errors = 0, shader = 0;
        try
        {
            fixed (byte* p = source)
            {
                int result = D3DCompile(p, (nuint)source.Length, name: 0, defines: 0, include: 0,
                    entry: entry, target: profile, flags: ShaderOptimizationLevel3, flags2: 0, code: &blob, errors: &errors);
                if (result < 0)
                {
                    string text = errors == 0 ? "No compiler message" : Marshal.PtrToStringAnsi(BlobPointer(errors)) ?? "";
                    throw new InvalidOperationException($"GPU shader compilation failed: {text}");
                }
            }

            nuint size = ((delegate* unmanaged[Stdcall]<nint, nuint>)Method(blob, ComSlots.Blob.GetBufferSize))(blob);
            Check(((delegate* unmanaged[Stdcall]<nint, nint, nuint, nint, nint*, int>)Method(device, method))(device, BlobPointer(blob), size, 0, &shader), "CreateShader");
            return shader;
        }
        finally
        {
            Release(ref blob);
            Release(ref errors);
        }
    }

    private static nint BlobPointer(nint blob) => ((delegate* unmanaged[Stdcall]<nint, nint>)Method(blob, ComSlots.Blob.GetBufferPointer))(blob);
    public void Dispose()
    {
        if (context != 0)
        {
            ((delegate* unmanaged[Stdcall]<nint, void>)Method(context, ComSlots.D3DContext.ClearState))(context);
        }

        Release(ref pixelShader);
        Release(ref vertexShader);
        Release(ref view);
        Release(ref texture);
        Release(ref badgeView);
        Release(ref badgeTexture);
        Release(ref target);
        Release(ref swapChain);
        Release(ref context);
        Release(ref device);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SwapDescription
    {
        public uint Width, Height, RefreshNumerator, RefreshDenominator, Format, Scanline, Scaling, SampleCount, SampleQuality, Usage, BufferCount;
        public nint Window;
        public int Windowed;
        public uint SwapEffect, Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TextureDescription
    {
        public uint Width, Height, Mips, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccess, Misc;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Mapped
    {
        public nint Data;
        public uint RowPitch, DepthPitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GpuViewport
    {
        public float X, Y, Width, Height, MinDepth, MaxDepth;
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDeviceAndSwapChain(
        nint adapter,
        uint driver,
        nint software,
        uint flags,
        uint* levels,
        uint count,
        uint sdk,
        SwapDescription* description,
        nint* swap,
        nint* device,
        uint* feature,
        nint* context
    );
    [DllImport("d3dcompiler_47.dll", ExactSpelling = true, CharSet = CharSet.Ansi)]
    private static extern int D3DCompile(
        byte* data,
        nuint size,
        nint name,
        nint defines,
        nint include,
        string entry,
        string target,
        uint flags,
        uint flags2,
        nint* code,
        nint* errors
    );
}
