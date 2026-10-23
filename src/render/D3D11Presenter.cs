using System.Runtime.InteropServices;
using System.Text;
using Atria.Platform;
using static Atria.Platform.NativeCom;

namespace Atria.Render;

/// <summary>Nearest-neighbor GPU presentation of the original 640x480 logical framebuffer.</summary>
public sealed unsafe class D3D11Presenter : IDisposable
{
    private nint device,context,swapChain,target,texture,view,vertexShader,pixelShader;
    private nint badgeTexture,badgeView;
    private int badgeWidth;
    private int width,height,sourceWidth=640,sourceHeight=480;
    public Viewport ContentViewport { get; private set; }
    public uint FeatureLevel { get; private set; }

    public D3D11Presenter(nint window,int width,int height)
    {
        this.width=width; this.height=height;
        SwapDescription desc=new() { Width=(uint)width,Height=(uint)height,Format=87,SampleCount=1,
            Usage=0x20,BufferCount=2,Window=window,Windowed=1,SwapEffect=4 };
        uint feature=0;
        try
        {
            fixed(nint* pd=&device,pc=&context,ps=&swapChain)
                Check(D3D11CreateDeviceAndSwapChain(0,1,0,0x20,null,0,7,&desc,ps,pd,&feature,pc),"D3D11CreateDeviceAndSwapChain");
            FeatureLevel=feature;
            CreateTarget(); LimitFrameLatency();
            CreateTexture();
            CreateBuildBadge();
            vertexShader=CreateShader("VS","vs_5_0",12);
            pixelShader=CreateShader("PS","ps_5_0",15);
        }
        catch { Dispose(); throw; }
    }

    public void SetFrameSize(int frameWidth,int frameHeight)
    {
        if(frameWidth<=0 || frameHeight<=0 || frameWidth>4096 || frameHeight>4096) throw new ArgumentOutOfRangeException(nameof(frameWidth));
        if(sourceWidth==frameWidth && sourceHeight==frameHeight) return;
        nint empty=0;
        ((delegate* unmanaged[Stdcall]<nint,uint,uint,nint*,void>)Method(context,8))(context,0,1,&empty);
        Release(ref view); Release(ref texture);
        sourceWidth=frameWidth; sourceHeight=frameHeight; CreateTexture();
        ContentViewport=Viewport.Fit(width,height,sourceWidth,sourceHeight);
    }
    private void CreateTexture()
    {
        TextureDescription tex=new() { Width=(uint)sourceWidth,Height=(uint)sourceHeight,Mips=1,ArraySize=1,Format=87,
            SampleCount=1,Usage=2,BindFlags=8,CpuAccess=0x10000 };
        fixed(nint* pt=&texture)
            Check(((delegate* unmanaged[Stdcall]<nint,TextureDescription*,nint,nint*,int>)Method(device,5))(device,&tex,0,pt),"CreateTexture2D");
        fixed(nint* pv=&view)
            Check(((delegate* unmanaged[Stdcall]<nint,nint,nint,nint*,int>)Method(device,7))(device,texture,0,pv),"CreateShaderResourceView");
    }

    private void CreateBuildBadge()
    {
        var badge=BuildBadge.Create();
        badgeWidth=badge.Width;
        TextureDescription tex=new() { Width=(uint)badgeWidth,Height=BuildBadge.Height,Mips=1,ArraySize=1,Format=87,
            SampleCount=1,Usage=2,BindFlags=8,CpuAccess=0x10000 };
        fixed(nint* pt=&badgeTexture)
            Check(((delegate* unmanaged[Stdcall]<nint,TextureDescription*,nint,nint*,int>)Method(device,5))(device,&tex,0,pt),"Create build badge texture");
        fixed(nint* pv=&badgeView)
            Check(((delegate* unmanaged[Stdcall]<nint,nint,nint,nint*,int>)Method(device,7))(device,badgeTexture,0,pv),"Create build badge view");
        Mapped mapped=default;
        Check(((delegate* unmanaged[Stdcall]<nint,nint,uint,uint,uint,Mapped*,int>)Method(context,14))(context,badgeTexture,0,4,0,&mapped),"Map build badge");
        try
        {
            fixed(uint* src=badge.Pixels)
                for(int row=0;row<BuildBadge.Height;row++)
                    Buffer.MemoryCopy(src+row*badgeWidth,(byte*)mapped.Data+row*mapped.RowPitch,badgeWidth*4,badgeWidth*4);
        }
        finally { ((delegate* unmanaged[Stdcall]<nint,nint,uint,void>)Method(context,15))(context,badgeTexture,0); }
    }

    private void LimitFrameLatency()
    {
        Guid iid=new("77db970f-6276-48ba-ba28-070143b4392c"); nint dxgi=0;
        int hr=((delegate* unmanaged[Stdcall]<nint,Guid*,nint*,int>)Method(device,0))(device,&iid,&dxgi);
        Check(hr,"Query IDXGIDevice1");
        try { Check(((delegate* unmanaged[Stdcall]<nint,uint,int>)Method(dxgi,12))(dxgi,1),"SetMaximumFrameLatency"); }
        finally { Release(ref dxgi); }
    }

    private void CreateTarget()
    {
        Guid iid=new("6f15aaf2-d208-4e89-9ab4-489535d34f9c"); nint back=0;
        Check(((delegate* unmanaged[Stdcall]<nint,uint,Guid*,nint*,int>)Method(swapChain,9))(swapChain,0,&iid,&back),"GetBuffer");
        try
        {
            fixed(nint* p=&target)
                Check(((delegate* unmanaged[Stdcall]<nint,nint,nint,nint*,int>)Method(device,9))(device,back,0,p),"CreateRenderTargetView");
        }
        finally { Release(ref back); }
        ContentViewport=Viewport.Fit(width,height,sourceWidth,sourceHeight);
    }

    public void Resize(int newWidth,int newHeight)
    {
        if(newWidth<=0 || newHeight<=0 || (newWidth==width && newHeight==height)) return;
        ((delegate* unmanaged[Stdcall]<nint,uint,nint*,nint,void>)Method(context,33))(context,0,null,0);
        Release(ref target);
        Check(((delegate* unmanaged[Stdcall]<nint,uint,uint,uint,uint,uint,int>)Method(swapChain,13))
            (swapChain,0,(uint)newWidth,(uint)newHeight,0,0),"ResizeBuffers");
        width=newWidth; height=newHeight; CreateTarget();
    }

    public void Present(ReadOnlySpan<uint> pixels)
    {
        if(pixels.Length!=sourceWidth*sourceHeight) throw new ArgumentException("Framebuffer dimensions do not match the selected source size.",nameof(pixels));
        Mapped mapped=default;
        Check(((delegate* unmanaged[Stdcall]<nint,nint,uint,uint,uint,Mapped*,int>)Method(context,14))(context,texture,0,4,0,&mapped),"Map framebuffer");
        try
        {
            fixed(uint* src=pixels)
                for(int row=0;row<sourceHeight;row++)
                    Buffer.MemoryCopy(src+row*sourceWidth,(byte*)mapped.Data+row*mapped.RowPitch,sourceWidth*4,sourceWidth*4);
        }
        finally { ((delegate* unmanaged[Stdcall]<nint,nint,uint,void>)Method(context,15))(context,texture,0); }
        nint t=target, v=view;
        ((delegate* unmanaged[Stdcall]<nint,uint,nint*,nint,void>)Method(context,33))(context,1,&t,0);
        float* clear=stackalloc float[4] { 0,0,0,1 };
        ((delegate* unmanaged[Stdcall]<nint,nint,float*,void>)Method(context,50))(context,target,clear);
        Viewport fit=ContentViewport;
        GpuViewport vp=new() { X=fit.X,Y=fit.Y,Width=fit.Width,Height=fit.Height,MaxDepth=1 };
        ((delegate* unmanaged[Stdcall]<nint,uint,GpuViewport*,void>)Method(context,44))(context,1,&vp);
        ((delegate* unmanaged[Stdcall]<nint,uint,void>)Method(context,24))(context,4);
        ((delegate* unmanaged[Stdcall]<nint,nint,nint,uint,void>)Method(context,11))(context,vertexShader,0,0);
        ((delegate* unmanaged[Stdcall]<nint,nint,nint,uint,void>)Method(context,9))(context,pixelShader,0,0);
        ((delegate* unmanaged[Stdcall]<nint,uint,uint,nint*,void>)Method(context,8))(context,0,1,&v);
        ((delegate* unmanaged[Stdcall]<nint,uint,uint,void>)Method(context,13))(context,3,0);
        // Draw last in physical window coordinates, including over movies and letterboxing.
        float badgeScale=Math.Min(1f,Math.Min(width/(float)(badgeWidth+24),height/(float)(BuildBadge.Height+24)));
        vp=new() { X=12*badgeScale,Y=height-(BuildBadge.Height+12)*badgeScale,
            Width=badgeWidth*badgeScale,Height=BuildBadge.Height*badgeScale,MaxDepth=1 };
        v=badgeView;
        ((delegate* unmanaged[Stdcall]<nint,uint,GpuViewport*,void>)Method(context,44))(context,1,&vp);
        ((delegate* unmanaged[Stdcall]<nint,uint,uint,nint*,void>)Method(context,8))(context,0,1,&v);
        ((delegate* unmanaged[Stdcall]<nint,uint,uint,void>)Method(context,13))(context,3,0);
        Check(((delegate* unmanaged[Stdcall]<nint,uint,uint,int>)Method(swapChain,8))(swapChain,1,0),"Present");
    }

    // HLSL compilation is GPU shader compilation, not generation/execution of managed IL.
    private nint CreateShader(string entry,string profile,int method)
    {
        byte[] source=Encoding.ASCII.GetBytes("struct O{float4 p:SV_Position;float2 uv:TEXCOORD0;}; O VS(uint i:SV_VertexID){O o;float2 uv=float2((i<<1)&2,i&2);o.p=float4(uv*float2(2,-2)+float2(-1,1),0,1);o.uv=uv;return o;} Texture2D<float4> img:register(t0);float4 PS(O o):SV_Target{uint w,h;img.GetDimensions(w,h);int2 size=int2(w,h);return float4(img.Load(int3(clamp(int2(o.uv*size),int2(0,0),size-1),0)).rgb,1);}");
        nint blob=0,errors=0,shader=0;
        try
        {
            fixed(byte* p=source)
            {
                int result=D3DCompile(p,(nuint)source.Length,0,0,0,entry,profile,0x8000,0,&blob,&errors);
                if(result<0)
                {
                    string text=errors==0 ? "No compiler message" : Marshal.PtrToStringAnsi(BlobPointer(errors))??"";
                    throw new InvalidOperationException($"GPU shader compilation failed: {text}");
                }
            }
            nuint size=((delegate* unmanaged[Stdcall]<nint,nuint>)Method(blob,4))(blob);
            Check(((delegate* unmanaged[Stdcall]<nint,nint,nuint,nint,nint*,int>)Method(device,method))(device,BlobPointer(blob),size,0,&shader),"CreateShader");
            return shader;
        }
        finally { Release(ref blob); Release(ref errors); }
    }
    private static nint BlobPointer(nint blob) => ((delegate* unmanaged[Stdcall]<nint,nint>)Method(blob,3))(blob);

    public void Dispose()
    {
        if(context!=0) ((delegate* unmanaged[Stdcall]<nint,void>)Method(context,110))(context);
        Release(ref pixelShader); Release(ref vertexShader); Release(ref view); Release(ref texture);
        Release(ref badgeView); Release(ref badgeTexture);
        Release(ref target); Release(ref swapChain); Release(ref context); Release(ref device);
    }

    [StructLayout(LayoutKind.Sequential)] private struct SwapDescription
    { public uint Width,Height,RefreshNumerator,RefreshDenominator,Format,Scanline,Scaling,SampleCount,SampleQuality,Usage,BufferCount; public nint Window; public int Windowed; public uint SwapEffect,Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct TextureDescription
    { public uint Width,Height,Mips,ArraySize,Format,SampleCount,SampleQuality,Usage,BindFlags,CpuAccess,Misc; }
    [StructLayout(LayoutKind.Sequential)] private struct Mapped { public nint Data; public uint RowPitch,DepthPitch; }
    [StructLayout(LayoutKind.Sequential)] private struct GpuViewport { public float X,Y,Width,Height,MinDepth,MaxDepth; }
    [DllImport("d3d11.dll",ExactSpelling=true)] private static extern int D3D11CreateDeviceAndSwapChain(nint adapter,uint driver,nint software,uint flags,uint* levels,uint count,uint sdk,SwapDescription* description,nint* swap,nint* device,uint* feature,nint* context);
    [DllImport("d3dcompiler_47.dll",ExactSpelling=true,CharSet=CharSet.Ansi)] private static extern int D3DCompile(byte* data,nuint size,nint name,nint defines,nint include,string entry,string target,uint flags,uint flags2,nint* code,nint* errors);
}
