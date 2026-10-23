using System.Buffers.Binary;
using Atria.Audio;

namespace Atria.Video;

public readonly record struct AviVideoPacket(long Offset,int Length);

/// <summary>Bounded RIFF AVI/IV32 + PCM demux. Indexes offsets, never loads the whole movie.</summary>
public sealed class AviMovie : IDisposable
{
    private readonly FileStream source;
    private readonly List<AviVideoPacket> frames=[];
    private readonly List<PcmChunk> audio=[];
    private int streamCount,videoStream=-1,audioStream=-1;
    private uint declaredFrames,declaredAudioFrames;
    public string Path { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public uint FrameScale { get; private set; }
    public uint FrameRate { get; private set; }
    public PcmAudioFormat AudioFormat { get; private set; }
    public IReadOnlyList<AviVideoPacket> Frames { get; }
    public IReadOnlyList<PcmChunk> AudioChunks { get; }
    public int MaximumPacketBytes { get; private set; }
    public long AudioByteCount { get; private set; }
    public double DurationSeconds => (double)frames.Count*FrameScale/FrameRate;

    public AviMovie(string path)
    {
        Path=System.IO.Path.GetFullPath(path);
        source=new FileStream(Path,FileMode.Open,FileAccess.Read,FileShare.Read);
        Frames=frames.AsReadOnly(); AudioChunks=audio.AsReadOnly();
        try
        {
            byte[] header=ReadAt(0,12);
            if(!header.AsSpan(0,4).SequenceEqual("RIFF"u8) || !header.AsSpan(8,4).SequenceEqual("AVI "u8))
                throw new InvalidDataException("Expected an original RIFF AVI movie.");
            long end=8L+U32(header,4);
            if(end!=source.Length) throw new InvalidDataException("AVI RIFF size mismatch.");
            Scan(12,end,false,0);
            if(videoStream<0 || audioStream<0 || streamCount!=2 || Width<=0 || Height<=0 || Width>4096 || Height>4096 ||
                FrameScale==0 || FrameRate==0 || frames.Count!=declaredFrames || frames.Count==0)
                throw new InvalidDataException("Unsupported AVI stream layout or frame count.");
            if(AudioByteCount!=(long)declaredAudioFrames*AudioFormat.BlockAlign)
                throw new InvalidDataException("AVI PCM length differs from its stream header.");
        }
        catch { source.Dispose(); throw; }
    }

    private byte[] ReadAt(long offset,int length)
    {
        if(offset<0 || length<0 || offset>source.Length-length) throw new InvalidDataException("Truncated AVI data.");
        byte[] bytes=new byte[length]; source.Position=offset; source.ReadExactly(bytes); return bytes;
    }
    private void Scan(long start,long end,bool movie,int depth)
    {
        if(depth>4) throw new InvalidDataException("Excessive AVI list nesting.");
        for(long offset=start;offset<end;)
        {
            if(end-offset<8) throw new InvalidDataException("Truncated AVI chunk header.");
            byte[] h=ReadAt(offset,8); uint size=U32(h,4);
            long data=offset+8,next=checked(data+size+(size&1));
            if(next>end) throw new InvalidDataException("AVI chunk exceeds its parent.");
            ReadOnlySpan<byte> id=h.AsSpan(0,4);
            if(id.SequenceEqual("LIST"u8))
            {
                if(size<4) throw new InvalidDataException("Invalid AVI list.");
                byte[] type=ReadAt(data,4);
                if(type.AsSpan().SequenceEqual("strl"u8)) ReadStream(data+4,data+size);
                else if(type.AsSpan().SequenceEqual("hdrl"u8) || type.AsSpan().SequenceEqual("movi"u8) || type.AsSpan().SequenceEqual("rec "u8))
                    Scan(data+4,data+size,movie || type.AsSpan().SequenceEqual("movi"u8),depth+1);
            }
            else if(movie && !id.SequenceEqual("JUNK"u8))
            {
                if(id[0]<'0' || id[0]>'9' || id[1]<'0' || id[1]>'9') throw new InvalidDataException("Unsupported AVI media chunk.");
                int stream=(id[0]-'0')*10+id[1]-'0';
                if(stream==videoStream && id[2]=='d' && id[3]=='c')
                {
                    if(size>16*1024*1024 || frames.Count>=1000000) throw new InvalidDataException("AVI video bounds exceeded.");
                    frames.Add(new(data,(int)size)); MaximumPacketBytes=Math.Max(MaximumPacketBytes,(int)size);
                }
                else if(stream==audioStream && id[2]=='w' && id[3]=='b')
                {
                    if(size>int.MaxValue || audio.Count>=1000000 || size%AudioFormat.BlockAlign!=0)
                        throw new InvalidDataException("Invalid AVI PCM chunk.");
                    audio.Add(new(data,(int)size)); AudioByteCount+=size;
                }
                else throw new InvalidDataException("Unrecognized AVI stream chunk.");
            }
            offset=next;
        }
    }
    private void ReadStream(long start,long end)
    {
        byte[]? header=null,format=null;
        for(long offset=start;offset<end;)
        {
            if(end-offset<8) throw new InvalidDataException("Truncated AVI stream header.");
            byte[] chunk=ReadAt(offset,8); uint size=U32(chunk,4);
            long next=checked(offset+8+size+(size&1));
            if(next>end) throw new InvalidDataException("AVI stream chunk exceeds its list.");
            if(chunk.AsSpan(0,4).SequenceEqual("strh"u8) || chunk.AsSpan(0,4).SequenceEqual("strf"u8))
            {
                if(size>4096) throw new InvalidDataException("Oversized AVI stream format.");
                byte[] bytes=ReadAt(offset+8,(int)size);
                if(chunk[3]=='h') header=bytes; else format=bytes;
            }
            offset=next;
        }
        if(header is null || header.Length<48 || format is null || U32(header,28)!=0)
            throw new InvalidDataException("Unsupported AVI stream header/start time.");
        int index=streamCount++;
        if(header.AsSpan(0,4).SequenceEqual("vids"u8))
        {
            if(videoStream!=-1 || format.Length<40 || !header.AsSpan(4,4).SequenceEqual("IV32"u8) ||
                !format.AsSpan(16,4).SequenceEqual("IV32"u8)) throw new InvalidDataException("Expected exactly one IV32 video stream.");
            videoStream=index; Width=checked((int)U32(format,4)); Height=checked((int)U32(format,8));
            FrameScale=U32(header,20); FrameRate=U32(header,24); declaredFrames=U32(header,32);
        }
        else if(header.AsSpan(0,4).SequenceEqual("auds"u8))
        {
            if(audioStream!=-1 || format.Length<16 || U16(format,0)!=1) throw new InvalidDataException("Expected exactly one PCM stream.");
            audioStream=index; AudioFormat=new(U16(format,2),U32(format,4),U16(format,14));
            if(AudioFormat.Channels is not (1 or 2) || AudioFormat.BitsPerSample is not (8 or 16) ||
                AudioFormat.SampleRate==0 || AudioFormat.SampleRate>192000 || U16(format,12)!=AudioFormat.BlockAlign ||
                U32(format,8)!=AudioFormat.ByteRate || U32(header,20)!=AudioFormat.BlockAlign || U32(header,24)!=AudioFormat.ByteRate)
                throw new InvalidDataException("Invalid AVI PCM format/time base.");
            declaredAudioFrames=U32(header,32);
        }
        else throw new InvalidDataException("Unsupported AVI stream kind.");
    }
    public int ReadFrame(int index,Span<byte> buffer)
    {
        AviVideoPacket packet=frames[index];
        if(buffer.Length<packet.Length) throw new ArgumentException("Packet buffer too small.",nameof(buffer));
        source.Position=packet.Offset; source.ReadExactly(buffer[..packet.Length]); return packet.Length;
    }
    private static uint U32(ReadOnlySpan<byte> data,int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    private static ushort U16(ReadOnlySpan<byte> data,int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
    public void Dispose() => source.Dispose();
}
