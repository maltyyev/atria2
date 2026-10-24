namespace Atria.Platform;

// ABI slots are grouped by their declaring native interface. Do not renumber.
internal static class ComSlots
{
    internal static class Unknown
    {
        public const int QueryInterface = 0;
        public const int Release = 2;
    }

    internal static class D3DDevice
    {
        public const int CreateTexture2D = 5;
        public const int CreateShaderResourceView = 7;
        public const int CreateRenderTargetView = 9;
        public const int CreateVertexShader = 12;
        public const int CreatePixelShader = 15;
    }

    internal static class D3DContext
    {
        public const int SetPixelShaderResources = 8;
        public const int SetPixelShader = 9;
        public const int SetVertexShader = 11;
        public const int Draw = 13;
        public const int Map = 14;
        public const int Unmap = 15;
        public const int SetPrimitiveTopology = 24;
        public const int SetRenderTargets = 33;
        public const int SetViewports = 44;
        public const int ClearRenderTarget = 50;
        public const int ClearState = 110;
    }

    internal static class SwapChain
    {
        public const int Present = 8;
        public const int GetBuffer = 9;
        public const int ResizeBuffers = 13;
    }

    internal static class DxgiDevice
    {
        public const int SetMaximumFrameLatency = 12;
    }

    internal static class Blob
    {
        public const int GetBufferPointer = 3;
        public const int GetBufferSize = 4;
    }

    internal static class AudioEngine
    {
        public const int CreateSourceVoice = 5;
        public const int CreateMasteringVoice = 7;
    }

    internal static class AudioVoice
    {
        public const int SetVolume = 12;
        public const int DestroyVoice = 18;
        public const int Start = 19;
        public const int Stop = 20;
        public const int SubmitSourceBuffer = 21;
        public const int FlushSourceBuffers = 22;
        public const int GetState = 25;
    }
}
