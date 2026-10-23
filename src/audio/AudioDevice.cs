using System.Runtime.InteropServices;
using Atria.Platform;
using static Atria.Platform.NativeCom;

namespace Atria.Audio;

/// <summary>Native XAudio2 ownership; instantiate/dispose on the same thread.</summary>
public sealed unsafe class AudioDevice : IDisposable
{
    internal nint Engine { get; private set; }
    private nint master;
    private readonly int threadId=Environment.CurrentManagedThreadId;
    private bool comInitialized;
    private readonly List<MusicStream> streams=[];
    private readonly List<SoundEffectBank> effects=[];
    private readonly List<PcmChunkStream> chunkStreams=[];
    public AudioDevice()
    {
        int co=CoInitializeEx(0,0);
        if(co<0 && co!=unchecked((int)0x80010106)) Check(co,"CoInitializeEx");
        comInitialized=co>=0;
        try
        {
            nint engine=0;
            Check(XAudio2Create(&engine,0,0xFFFFFFFF),"XAudio2Create"); Engine=engine;
            fixed(nint* pm=&master)
                Check(((delegate* unmanaged[Stdcall]<nint,nint*,uint,uint,uint,nint,nint,int,int>)Method(engine,7))(engine,pm,0,0,0,0,0,7),"CreateMasteringVoice");
        }
        catch { Dispose(); throw; }
    }
    public MusicStream PlayFile(string path,float volume=1)
    {
        if(Engine==0) throw new ObjectDisposedException(nameof(AudioDevice));
        streams.RemoveAll(static stream => stream.IsDisposed);
        var stream=new MusicStream(Engine,path,volume); streams.Add(stream); return stream;
    }
    public PcmChunkStream PlayPcmChunks(string path,IReadOnlyList<PcmChunk> chunks,PcmAudioFormat format,float volume=1,bool startPaused=false)
    {
        if(Engine==0) throw new ObjectDisposedException(nameof(AudioDevice));
        if(Environment.CurrentManagedThreadId!=threadId) throw new InvalidOperationException("Create movie audio on the owning thread.");
        chunkStreams.RemoveAll(static stream=>stream.IsDisposed);
        var stream=new PcmChunkStream(Engine,path,chunks,format,volume,startPaused); chunkStreams.Add(stream); return stream;
    }
    public SoundEffectBank LoadEffect(ReadOnlyMemory<byte> wave)
    {
        if(Engine==0) throw new ObjectDisposedException(nameof(AudioDevice));
        if(Environment.CurrentManagedThreadId!=threadId) throw new InvalidOperationException("Create effects on the owning audio thread.");
        effects.RemoveAll(static effect => effect.IsDisposed);
        var effect=new SoundEffectBank(this,wave); effects.Add(effect); return effect;
    }
    public void Dispose()
    {
        if(Environment.CurrentManagedThreadId!=threadId) throw new InvalidOperationException("Dispose audio device on its owning thread.");
        foreach(var s in streams) s.Dispose(); streams.Clear();
        foreach(var s in chunkStreams) s.Dispose(); chunkStreams.Clear();
        foreach(var effect in effects) effect.Dispose(); effects.Clear();
        if(master!=0) { ((delegate* unmanaged[Stdcall]<nint,void>)Method(master,18))(master); master=0; }
        nint engine=Engine; Release(ref engine); Engine=0;
        if(comInitialized) { CoUninitialize(); comInitialized=false; }
    }
    [DllImport("xaudio2_9.dll",ExactSpelling=true)] private static extern int XAudio2Create(nint* engine,uint flags,uint processor);
    [DllImport("ole32.dll",ExactSpelling=true)] private static extern int CoInitializeEx(nint reserved,uint mode);
    [DllImport("ole32.dll",ExactSpelling=true)] private static extern void CoUninitialize();
}

