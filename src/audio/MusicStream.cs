using System.Runtime.InteropServices;
using Atria.Resources;
using static Atria.Platform.NativeCom;

namespace Atria.Audio;

/// <summary>Three bounded PCM buffers, file I/O on a worker. Does not choose tracks or invent looping rules.</summary>
public sealed unsafe class MusicStream : IDisposable
{
    private const int BufferCount=3, BufferBytes=65536;
    private readonly FileStream source;
    private readonly nint[] buffers=new nint[BufferCount];
    private readonly Thread worker;
    private nint voice;
    private volatile bool stopping,finished;
    private Exception? failure;
    private long samplesPlayed;
    internal bool IsDisposed { get; private set; }
    public PcmWaveInfo Format { get; }
    public bool Finished => finished;
    public Exception? Failure => Volatile.Read(ref failure);
    public long SamplesPlayed => Interlocked.Read(ref samplesPlayed);

    public void SetVolume(float volume)
    {
        ObjectDisposedException.ThrowIf(IsDisposed,this);
        if(!float.IsFinite(volume)||volume<0||volume>1) throw new ArgumentOutOfRangeException(nameof(volume));
        Check(((delegate* unmanaged[Stdcall]<nint,float,uint,int>)Method(voice,12))(voice,volume,0),"SetMusicVolume");
    }

    internal MusicStream(nint engine,string path,float volume)
    {
        if(!float.IsFinite(volume) || volume<0 || volume>1) throw new ArgumentOutOfRangeException(nameof(volume));
        source=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,BufferBytes,FileOptions.SequentialScan);
        worker=new Thread(Feed) { IsBackground=true,Name="Atria PCM stream" };
        try
        {
            Format=PcmWaveInfo.Read(source); source.Position=Format.DataOffset;
            WaveFormat fmt=new() { Tag=1,Channels=Format.Channels,Rate=Format.SampleRate,Average=Format.ByteRate,Align=Format.BlockAlign,Bits=Format.BitsPerSample };
            fixed(nint* pv=&voice)
                Check(((delegate* unmanaged[Stdcall]<nint,nint*,WaveFormat*,uint,float,nint,nint,nint,int>)Method(engine,5))
                    (engine,pv,&fmt,0,2,0,0,0),"CreateSourceVoice");
            Check(((delegate* unmanaged[Stdcall]<nint,float,uint,int>)Method(voice,12))(voice,volume,0),"SetVolume");
            for(int i=0;i<BufferCount;i++) buffers[i]=(nint)NativeMemory.Alloc(BufferBytes);
            worker.Start();
        }
        catch { Dispose(); throw; }
    }

    private void Feed()
    {
        try
        {
            long remaining=Format.DataLength; int next=0; bool started=false;
            while(!stopping)
            {
                VoiceState state=default;
                ((delegate* unmanaged[Stdcall]<nint,VoiceState*,uint,void>)Method(voice,25))(voice,&state,0);
                if(remaining==0 && state.Queued==0)
                { Interlocked.Exchange(ref samplesPlayed,Format.DataLength/Format.BlockAlign); finished=true; return; }
                Interlocked.Exchange(ref samplesPlayed,(long)state.SamplesPlayed);
                if(remaining>0 && state.Queued<BufferCount)
                {
                    int size=(int)Math.Min(remaining,BufferBytes/Format.BlockAlign*Format.BlockAlign);
                    source.ReadExactly(new Span<byte>((void*)buffers[next],size));
                    remaining-=size;
                    AudioBuffer buffer=new() { Flags=remaining==0?0x40u:0,ByteCount=(uint)size,Data=buffers[next] };
                    Check(((delegate* unmanaged[Stdcall]<nint,AudioBuffer*,nint,int>)Method(voice,21))(voice,&buffer,0),"SubmitSourceBuffer");
                    next=(next+1)%BufferCount;
                    if(!started && (remaining==0 || state.Queued+1>=BufferCount))
                    {
                        Check(((delegate* unmanaged[Stdcall]<nint,uint,uint,int>)Method(voice,19))(voice,0,0),"StartSourceVoice"); started=true;
                    }
                }
                else Thread.Sleep(4);
            }
        }
        catch(Exception e) { Volatile.Write(ref failure,e); finished=true; }
    }

    public void Dispose()
    {
        if(IsDisposed) return;
        IsDisposed=true;
        stopping=true;
        if(worker.IsAlive) worker.Join();
        if(voice!=0)
        {
            ((delegate* unmanaged[Stdcall]<nint,uint,uint,int>)Method(voice,20))(voice,0,0);
            ((delegate* unmanaged[Stdcall]<nint,int>)Method(voice,22))(voice);
            ((delegate* unmanaged[Stdcall]<nint,void>)Method(voice,18))(voice); voice=0;
        }
        for(int i=0;i<BufferCount;i++) { NativeMemory.Free((void*)buffers[i]); buffers[i]=0; }
        source.Dispose();
    }

    [StructLayout(LayoutKind.Sequential,Pack=2)] private struct WaveFormat { public ushort Tag,Channels; public uint Rate,Average; public ushort Align,Bits,Extra; }
    // xaudio2.h explicitly uses #pragma pack(push,1), including x64 pointer-bearing records.
    [StructLayout(LayoutKind.Sequential,Pack=1)] private struct AudioBuffer { public uint Flags,ByteCount; public nint Data; public uint PlayBegin,PlayLength,LoopBegin,LoopLength,LoopCount; public nint Context; }
    [StructLayout(LayoutKind.Sequential,Pack=1)] private struct VoiceState { public nint Current; public uint Queued; public ulong SamplesPlayed; }
}
