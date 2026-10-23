using System.Runtime.InteropServices;
using static Atria.Platform.NativeCom;

namespace Atria.Audio;

public readonly record struct PcmChunk(long Offset, int Length);
public readonly record struct PcmAudioFormat(ushort Channels, uint SampleRate, ushort BitsPerSample)
{
    public ushort BlockAlign => checked((ushort)(Channels * BitsPerSample / 8));
    public uint ByteRate => checked(SampleRate * BlockAlign);
    public void Validate()
    {
        if(Channels==0 || Channels>64 || SampleRate==0 || BitsPerSample is not (8 or 16 or 24 or 32)) throw new InvalidDataException("Invalid integer PCM format.");
        _=BlockAlign; _=ByteRate;
    }
}

/// <summary>Continuous PCM read over immutable, bounded file ranges; no RIFF assumptions or extracted files.</summary>
public sealed class PcmChunkReader : IDisposable
{
    private readonly FileStream source;
    private readonly PcmChunk[] chunks;
    private int chunkIndex, chunkPosition;
    public PcmAudioFormat Format { get; }
    public long TotalBytes { get; }
    public long RemainingBytes { get; private set; }
    public PcmChunkReader(string path,IReadOnlyList<PcmChunk> chunks,PcmAudioFormat format)
    {
        ArgumentNullException.ThrowIfNull(chunks); format.Validate(); Format=format;
        this.chunks=chunks.ToArray();
        source=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.RandomAccess);
        try
        {
            long total=0;
            foreach(var chunk in this.chunks)
            {
                if(chunk.Offset<0 || chunk.Length<0 || chunk.Offset>source.Length || chunk.Length>source.Length-chunk.Offset || chunk.Length%format.BlockAlign!=0)
                    throw new InvalidDataException("PCM chunk is unaligned or outside source file.");
                total=checked(total+chunk.Length);
            }
            TotalBytes=RemainingBytes=total;
        }
        catch { source.Dispose(); throw; }
    }
    public int Read(Span<byte> destination)
    {
        if(destination.Length%Format.BlockAlign!=0) throw new ArgumentException("PCM destination must hold complete sample frames.",nameof(destination));
        int written=0;
        while(written<destination.Length && chunkIndex<chunks.Length)
        {
            var chunk=chunks[chunkIndex];
            int count=Math.Min(destination.Length-written,chunk.Length-chunkPosition);
            if(count==0) { chunkIndex++; chunkPosition=0; continue; }
            source.Position=checked(chunk.Offset+chunkPosition);
            source.ReadExactly(destination.Slice(written,count));
            chunkPosition+=count; written+=count; RemainingBytes-=count;
        }
        return written;
    }
    public void Dispose()=>source.Dispose();
}

/// <summary>Three fixed PCM buffers; XAudio2 sample count is the movie's audio playback clock.</summary>
public sealed unsafe class PcmChunkStream : IDisposable
{
    private const int BufferCount=3, BufferBytes=65536;
    private readonly int ownerThread=Environment.CurrentManagedThreadId;
    private readonly PcmChunkReader reader;
    private readonly nint[] buffers=new nint[BufferCount];
    private readonly object gate=new();
    private readonly AutoResetEvent wake=new(false);
    private readonly Thread worker;
    private nint voice;
    private volatile bool stopping,finished,disposed;
    private bool requested,paused,running;
    private Exception? failure;
    private long samplesPlayed;
    public PcmAudioFormat Format { get; }
    public long TotalSampleFrames { get; }
    public long SamplesPlayed=>Interlocked.Read(ref samplesPlayed);
    public bool Finished=>finished;
    public bool IsDisposed=>disposed;
    public Exception? Failure=>Volatile.Read(ref failure);
    public int BufferCapacityBytes=>BufferCount*BufferBytes;
    internal PcmChunkStream(nint engine,string path,IReadOnlyList<PcmChunk> chunks,PcmAudioFormat format,float volume,bool startPaused)
    {
        if(!float.IsFinite(volume)||volume<0||volume>1) throw new ArgumentOutOfRangeException(nameof(volume));
        reader=new PcmChunkReader(path,chunks,format); Format=format;
        TotalSampleFrames=reader.TotalBytes/format.BlockAlign;
        paused=startPaused; requested=!startPaused;
        worker=new Thread(Feed) { IsBackground=true,Name="Atria movie PCM" };
        try
        {
            WaveFormat fmt=new() { Tag=1,Channels=format.Channels,Rate=format.SampleRate,Average=format.ByteRate,Align=format.BlockAlign,Bits=format.BitsPerSample };
            fixed(nint* pv=&voice)
                Check(((delegate* unmanaged[Stdcall]<nint,nint*,WaveFormat*,uint,float,nint,nint,nint,int>)Method(engine,5))(engine,pv,&fmt,0,2,0,0,0),"CreateMoviePcmVoice");
            Check(((delegate* unmanaged[Stdcall]<nint,float,uint,int>)Method(voice,12))(voice,volume,0),"SetMoviePcmVolume");
            for(int i=0;i<BufferCount;i++) buffers[i]=(nint)NativeMemory.Alloc(BufferBytes);
            worker.Start();
        }
        catch { Dispose(); throw; }
    }
    private void CheckOwner()
    {
        if(Environment.CurrentManagedThreadId!=ownerThread) throw new InvalidOperationException("Control movie audio on the owning thread.");
        ObjectDisposedException.ThrowIf(disposed,this);
    }
    public void Start()
    {
        CheckOwner(); lock(gate) { requested=true; paused=false; } wake.Set();
    }
    public void Resume()=>Start();
    public void Pause()
    {
        CheckOwner(); lock(gate)
        {
            paused=true;
            if(running) { Check(((delegate* unmanaged[Stdcall]<nint,uint,uint,int>)Method(voice,20))(voice,0,0),"PauseMoviePcm"); running=false; }
        }
    }
    private void Feed()
    {
        try
        {
            int next=0;
            while(!stopping)
            {
                lock(gate)
                {
                    VoiceState state=default;
                    ((delegate* unmanaged[Stdcall]<nint,VoiceState*,uint,void>)Method(voice,25))(voice,&state,0);
                    // XAudio2 resets SamplesPlayed after an END_OF_STREAM buffer. Preserve our completed timeline.
                    if(reader.RemainingBytes==0 && state.Queued==0)
                    { Interlocked.Exchange(ref samplesPlayed,TotalSampleFrames); finished=true; return; }
                    Interlocked.Exchange(ref samplesPlayed,checked((long)state.SamplesPlayed));
                    if(reader.RemainingBytes>0 && state.Queued<BufferCount)
                    {
                        int capacity=BufferBytes/Format.BlockAlign*Format.BlockAlign;
                        int count=reader.Read(new Span<byte>((void*)buffers[next],capacity));
                        AudioBuffer buffer=new() { Flags=reader.RemainingBytes==0?0x40u:0,ByteCount=(uint)count,Data=buffers[next] };
                        Check(((delegate* unmanaged[Stdcall]<nint,AudioBuffer*,nint,int>)Method(voice,21))(voice,&buffer,0),"SubmitMoviePcmBuffer");
                        next=(next+1)%BufferCount; state.Queued++;
                    }
                    if(requested && !paused && !running && state.Queued>0 && (reader.RemainingBytes==0 || state.Queued==BufferCount))
                    {
                        Check(((delegate* unmanaged[Stdcall]<nint,uint,uint,int>)Method(voice,19))(voice,0,0),"StartMoviePcmVoice"); running=true;
                    }
                }
                wake.WaitOne(4);
            }
        }
        catch(Exception e) { Volatile.Write(ref failure,e); finished=true; }
    }
    public void Dispose()
    {
        if(Environment.CurrentManagedThreadId!=ownerThread) throw new InvalidOperationException("Dispose movie audio on the owning thread.");
        if(disposed) return;
        disposed=true; stopping=true; wake.Set();
        if(worker.IsAlive) worker.Join();
        if(voice!=0) { ((delegate* unmanaged[Stdcall]<nint,void>)Method(voice,18))(voice); voice=0; }
        foreach(nint buffer in buffers) NativeMemory.Free((void*)buffer);
        reader.Dispose(); wake.Dispose();
    }
    [StructLayout(LayoutKind.Sequential,Pack=2)] private struct WaveFormat { public ushort Tag,Channels; public uint Rate,Average; public ushort Align,Bits,Extra; }
    [StructLayout(LayoutKind.Sequential,Pack=1)] private struct AudioBuffer { public uint Flags,ByteCount; public nint Data; public uint PlayBegin,PlayLength,LoopBegin,LoopLength,LoopCount; public nint Context; }
    [StructLayout(LayoutKind.Sequential,Pack=1)] private struct VoiceState { public nint Current; public uint Queued; public ulong SamplesPlayed; }
}
