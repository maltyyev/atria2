using System.Runtime.InteropServices;
using static Atria.Platform.NativeCom;

namespace Atria.Platform.Win32;

// Windows SDK xaudio2.h ABI. Keep native signatures and defaults out of playback policy.
internal static unsafe class XAudio2Native
{
    private const uint NoFlags = 0;
    private const uint AnyProcessor = uint.MaxValue;
    private const uint DefaultChannels = 0;
    private const uint DefaultSampleRate = 0;
    private const uint CommitNow = 0;
    private const uint EndOfStream = 0x0040;
    private const float DefaultMaximumFrequencyRatio = 2;
    private const ushort WaveFormatPcm = 1;
    private const int GameMediaCategory = 7;

    public static nint CreateEngine()
    {
        nint engine = 0;
        Check(XAudio2Create(&engine, flags: NoFlags, processor: AnyProcessor), nameof(XAudio2Create));
        return engine;
    }

    public static nint CreateMasteringVoice(nint engine)
    {
        nint voice = 0;
        nint defaultOutputDevice = 0;
        nint noEffectChain = 0;
        var create = (delegate* unmanaged[Stdcall]<nint, nint*, uint, uint, uint, nint, nint, int, int>)
            Method(engine, ComSlots.AudioEngine.CreateMasteringVoice);

        Check(create(engine, &voice, DefaultChannels, DefaultSampleRate, NoFlags,
            defaultOutputDevice, noEffectChain, GameMediaCategory), nameof(CreateMasteringVoice));
        return voice;
    }

    public static nint CreatePcmVoice(nint engine, ushort channels, uint sampleRate, ushort bitsPerSample)
    {
        ushort blockAlign = checked((ushort)(channels * bitsPerSample / 8));
        WaveFormat format = new()
        {
            Tag = WaveFormatPcm,
            Channels = channels,
            SampleRate = sampleRate,
            ByteRate = checked(sampleRate * blockAlign),
            BlockAlign = blockAlign,
            BitsPerSample = bitsPerSample
        };

        nint voice = 0;
        nint noCallback = 0;
        nint defaultOutput = 0;
        nint noEffectChain = 0;
        var create = (delegate* unmanaged[Stdcall]<nint, nint*, WaveFormat*, uint, float, nint, nint, nint, int>)
            Method(engine, ComSlots.AudioEngine.CreateSourceVoice);

        Check(create(engine, &voice, &format, NoFlags, DefaultMaximumFrequencyRatio,
            noCallback, defaultOutput, noEffectChain), nameof(CreatePcmVoice));
        return voice;
    }

    public static void SetVolume(nint voice, float amplitude)
    {
        var setVolume = (delegate* unmanaged[Stdcall]<nint, float, uint, int>)
            Method(voice, ComSlots.AudioVoice.SetVolume);
        Check(setVolume(voice, amplitude, CommitNow), nameof(SetVolume));
    }

    public static VoiceState GetState(nint voice)
    {
        VoiceState state = default;
        var getState = (delegate* unmanaged[Stdcall]<nint, VoiceState*, uint, void>)
            Method(voice, ComSlots.AudioVoice.GetState);
        getState(voice, &state, NoFlags);
        return state;
    }

    // The caller owns PCM storage until the queue drains or DestroyVoice returns.
    public static void SubmitBuffer(nint voice, nint samples, uint byteCount, bool endOfStream)
    {
        AudioBuffer buffer = new()
        {
            Flags = endOfStream ? EndOfStream : NoFlags,
            ByteCount = byteCount,
            Data = samples
        };
        nint noWmaPacketData = 0;
        var submit = (delegate* unmanaged[Stdcall]<nint, AudioBuffer*, nint, int>)
            Method(voice, ComSlots.AudioVoice.SubmitSourceBuffer);
        Check(submit(voice, &buffer, noWmaPacketData), nameof(SubmitBuffer));
    }

    public static void Start(nint voice)
    {
        var start = (delegate* unmanaged[Stdcall]<nint, uint, uint, int>)
            Method(voice, ComSlots.AudioVoice.Start);
        Check(start(voice, NoFlags, CommitNow), nameof(Start));
    }

    public static void Stop(nint voice)
    {
        var stop = (delegate* unmanaged[Stdcall]<nint, uint, uint, int>)
            Method(voice, ComSlots.AudioVoice.Stop);
        Check(stop(voice, NoFlags, CommitNow), nameof(Stop));
    }

    public static void FlushBuffers(nint voice)
    {
        var flush = (delegate* unmanaged[Stdcall]<nint, int>)
            Method(voice, ComSlots.AudioVoice.FlushSourceBuffers);
        Check(flush(voice), nameof(FlushBuffers));
    }

    public static void DestroyVoice(ref nint voice)
    {
        if (voice == 0)
        {
            return;
        }

        var destroy = (delegate* unmanaged[Stdcall]<nint, void>)
            Method(voice, ComSlots.AudioVoice.DestroyVoice);
        destroy(voice);
        voice = 0;
    }

    [DllImport("xaudio2_9.dll", ExactSpelling = true)]
    private static extern int XAudio2Create(nint* engine, uint flags, uint processor);

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public ushort Tag;
        public ushort Channels;
        public uint SampleRate;
        public uint ByteRate;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraBytes;
    }

    // xaudio2.h uses one-byte packing even for pointer-bearing records on x64.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct AudioBuffer
    {
        public uint Flags;
        public uint ByteCount;
        public nint Data;
        public uint PlayBegin;
        public uint PlayLength;
        public uint LoopBegin;
        public uint LoopLength;
        public uint LoopCount;
        public nint Context;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct VoiceState
    {
        public nint CurrentBufferContext;
        public uint Queued;
        public ulong SamplesPlayed;
    }
}
