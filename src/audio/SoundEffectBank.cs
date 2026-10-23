using System.Runtime.InteropServices;
using Atria.Resources;
using static Atria.Platform.NativeCom;

namespace Atria.Audio;

public interface ISoundEffectVoice : IDisposable
{
    void PlayIfIdle();
    void StopAndReset();
    void SetVolume(float amplitude);
}

/// <summary>Recovered eight-slot rotation. Playback completion belongs to each backend voice.</summary>
public sealed class SoundEffectRotation : IDisposable
{
    private readonly ISoundEffectVoice[] voices;
    public int Cursor { get; private set; }
    public bool IsDisposed { get; private set; }
    public SoundEffectRotation(ISoundEffectVoice[] voices)
    {
        ArgumentNullException.ThrowIfNull(voices);
        if (voices.Length != 8 || voices.Any(static v => v is null)) throw new ArgumentException("Exactly eight voices are required.", nameof(voices));
        this.voices = (ISoundEffectVoice[])voices.Clone();
    }
    public void Play()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        voices[Cursor].PlayIfIdle(); Cursor = (Cursor + 1) & 7;
    }
    public void SetCurrentVolume(int hundredthsDb)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        voices[Cursor].SetVolume(Amplitude(hundredthsDb));
    }
    public static float Amplitude(int hundredthsDb)
    {
        if (hundredthsDb is < -10000 or > 0) throw new ArgumentOutOfRangeException(nameof(hundredthsDb));
        return hundredthsDb == -10000 ? 0 : MathF.Pow(10, hundredthsDb / 2000f);
    }
    public void StopAll()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        foreach (var voice in voices) voice.StopAndReset();
        // 436010 does not write the separate rotation cursor at record+32.
    }
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        foreach (var voice in voices) voice.Dispose();
    }
}

/// <summary>One immutable PCM asset, eight independent XAudio2 voices and one shared sample allocation.</summary>
public sealed unsafe class SoundEffectBank : IBufferedSoundEffect
{
    public static IReadOnlyList<string> UiMemberNames { get; } = Array.AsReadOnly(new[] { "MAINMENU", "POPMENU", "CHIPINS", "SELECT", "CANCEL", "DIAL", "BUYITEM", "OUTSHOP", "TREASURE", "DUNGEON1", "DUNGEON2", "DUNGEON3", "WALK1", "EQUIP" });
    public static ReadOnlySpan<int> UiAttenuation => [-10000, -1500, -1000, -500, 0];
    private readonly int threadId = Environment.CurrentManagedThreadId;
    private readonly AudioDevice device;
    private SoundEffectRotation? rotation;
    private nint samples;
    public PcmWaveInfo Format { get; }
    public bool IsDisposed { get; private set; }
    public int Cursor => rotation?.Cursor ?? 0;
    internal SoundEffectBank(AudioDevice device, ReadOnlyMemory<byte> wave)
    {
        this.device = device;
        var pcm = PcmWave.Parse(wave); Format = pcm.Info;
        if (pcm.Samples.Length == 0) throw new InvalidDataException("An effect requires sample frames.");
        if (device.Engine == 0) throw new ObjectDisposedException(nameof(AudioDevice));
        var voices = new ISoundEffectVoice[8];
        try
        {
            samples = (nint)NativeMemory.Alloc((nuint)pcm.Samples.Length);
            pcm.Samples.Span.CopyTo(new Span<byte>((void*)samples, pcm.Samples.Length));
            for (int i = 0; i < voices.Length; i++) { var voice = new NativeVoice(device.Engine, Format, samples); voices[i] = voice; ownedVoices.Add(voice); }
            rotation = new SoundEffectRotation(voices);
        }
        catch
        {
            foreach (var voice in voices) voice?.Dispose();
            NativeMemory.Free((void*)samples); samples = 0;
            throw;
        }
    }
    private void CheckOwner()
    {
        if (Environment.CurrentManagedThreadId != threadId) throw new InvalidOperationException("Use effects on the owning audio thread.");
        ObjectDisposedException.ThrowIf(IsDisposed || device.Engine == 0, this);
    }
    public void Play() { CheckOwner(); rotation!.Play(); }
    public void SetCurrentVolume(int hundredthsDb) { CheckOwner(); rotation!.SetCurrentVolume(hundredthsDb); }
    public void StopAll() { CheckOwner(); rotation!.StopAll(); }
    public uint QueuedVoiceCount
    {
        get { CheckOwner(); uint count = 0; foreach (var voice in ownedVoices) if (voice.IsQueued) count++; return count; }
    }
    private readonly List<NativeVoice> ownedVoices = [];

    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != threadId) throw new InvalidOperationException("Dispose effects on the owning audio thread.");
        if (IsDisposed) return;
        IsDisposed = true;
        rotation?.Dispose(); rotation = null;
        NativeMemory.Free((void*)samples); samples = 0;
    }
    private sealed class NativeVoice : ISoundEffectVoice
    {
        private nint voice;
        private readonly nint samples;
        private readonly uint length;
        public NativeVoice(nint engine, PcmWaveInfo info, nint samples)
        {
            this.samples = samples; length = info.DataLength;
            WaveFormat fmt = new() { Tag = 1, Channels = info.Channels, Rate = info.SampleRate, Average = info.ByteRate, Align = info.BlockAlign, Bits = info.BitsPerSample };
            fixed (nint* pv = &voice)
                Check(((delegate* unmanaged[Stdcall]<nint,nint*,WaveFormat*,uint,float,nint,nint,nint,int>)Method(engine,5))(engine,pv,&fmt,0,2,0,0,0), "CreateEffectVoice");
        }
        public bool IsQueued
        {
            get { VoiceState state = default; ((delegate* unmanaged[Stdcall]<nint,VoiceState*,uint,void>)Method(voice,25))(voice,&state,0); return state.Queued != 0; }
        }
        public void PlayIfIdle()
        {
            if (IsQueued) return; // DirectSound Play while playing continues, never restarts or queues another copy.
            AudioBuffer buffer = new() { Flags = 0x40, ByteCount = length, Data = samples };
            Check(((delegate* unmanaged[Stdcall]<nint,AudioBuffer*,nint,int>)Method(voice,21))(voice,&buffer,0), "SubmitEffectBuffer");
            Check(((delegate* unmanaged[Stdcall]<nint,uint,uint,int>)Method(voice,19))(voice,0,0), "StartEffectVoice");
        }
        public void SetVolume(float amplitude) => Check(((delegate* unmanaged[Stdcall]<nint,float,uint,int>)Method(voice,12))(voice,amplitude,0), "SetEffectVolume");
        public void StopAndReset()
        {
            Check(((delegate* unmanaged[Stdcall]<nint,uint,uint,int>)Method(voice,20))(voice,0,0), "StopEffectVoice");
            Check(((delegate* unmanaged[Stdcall]<nint,int>)Method(voice,22))(voice), "FlushEffectBuffers");
        }
        public void Dispose()
        {
            if (voice == 0) return;
            ((delegate* unmanaged[Stdcall]<nint,void>)Method(voice,18))(voice); voice = 0;
        }
    }
    [StructLayout(LayoutKind.Sequential, Pack=2)] private struct WaveFormat { public ushort Tag,Channels; public uint Rate,Average; public ushort Align,Bits,Extra; }
    [StructLayout(LayoutKind.Sequential, Pack=1)] private struct AudioBuffer { public uint Flags,ByteCount; public nint Data; public uint PlayBegin,PlayLength,LoopBegin,LoopLength,LoopCount; public nint Context; }
    [StructLayout(LayoutKind.Sequential, Pack=1)] private struct VoiceState { public nint Current; public uint Queued; public ulong SamplesPlayed; }
}



