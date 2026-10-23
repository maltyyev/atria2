using System.Buffers.Binary;
using Atria.Game.World;

namespace Atria.Game.Dungeon;

/// <summary>Mutable AMF dungeon tile fields; offsets are relative to each328-byte tile.</summary>
public sealed class DungeonMap
{

    public WorldMapContext Context { get; }
    public DungeonMap(WorldMapContext context)
    {
        Context=context;
        // Retired tile type is ordinary floor in both resource loads and old snapshots.
        for(int i=0;i<100;i++)if(Read(i,88)==5)Write(i,88,0);
    }
    public int Read(int index,int offset)
    {
        if((uint)index>=100 || (uint)offset>324) throw new ArgumentOutOfRangeException(nameof(index));
        return BinaryPrimitives.ReadInt32LittleEndian(Context.TileBytes.Slice(index*328+offset,4));
    }
    public void Write(int index,int offset,int value)
    {
        if((uint)index>=100 || (uint)offset>324) throw new ArgumentOutOfRangeException(nameof(index));
        BinaryPrimitives.WriteInt32LittleEndian(Context.TileBytes.Slice(index*328+offset,4),value);
    }
    public ReadOnlyMemory<byte> Member(int index)
    {
        if((uint)index>=100) throw new ArgumentOutOfRangeException(nameof(index));
        var bytes=Context.TileBytes.Slice(index*328+128,20); int end=bytes.IndexOf((byte)0);
        if(end<0) throw new InvalidDataException("Dungeon tile member lacks terminator.");
        return bytes[..end].ToArray();
    }
    public (int X,int Y)? FindEntrance()
    {
        for(int i=0;i<100;i++) if(Read(i,88)==1) return(i%10,i/10);
        return null; //Native leaves incoming coordinates unchanged when no entrance exists.
    }
    public bool CanEnter(int x,int y)
    {
        if((uint)x>=(uint)Context.Width || (uint)y>=(uint)Context.Height) return false;
        int index=y*Context.Width+x;
        if(Read(index,88)==4) return Read(index,96)!=Read(Read(index,92),92);
        return Read(index,4) is not (16 or 64);
    }
    public byte[] GetTiles()=>Context.GetTiles();
}

public readonly record struct DungeonTorch(bool Active,int X,int Y,int Cost,int Remaining);

/// <summary>Exact21-cell local visibility and20-slot placed-torch lifetime banks41df70/41e2a0..41e4c0.</summary>
public sealed partial class DungeonVisibility
{
    private static readonly (int X,int Y)[] local=[(0,-2),(-1,-1),(0,-1),(1,-1),(-2,0),(-1,0),(0,0),(1,0),(2,0),(-1,1),(0,1),(1,1),(0,2),(-1,-2),(1,-2),(-2,-1),(2,-1),(-2,1),(2,1),(-1,2),(1,2)];
    private readonly int[] values=new int[100];
    private readonly DungeonTorch[] torches=new DungeonTorch[20];
    private int nextTorch;
    private readonly int[] scratchNeighbors=new int[9];
    public int ScratchNeighbor(int index)=>scratchNeighbors[index];
    public int[] GetScratchNeighbors()=>(int[])scratchNeighbors.Clone();
    public ReadOnlySpan<int> Values=>values;
    public ReadOnlySpan<DungeonTorch> Torches=>torches;
    public DungeonTorch[] GetTorchBank()=>(DungeonTorch[])torches.Clone();
    public int NextTorch=>nextTorch;
    public DungeonVisibility(int retainedNextTorch=0,ReadOnlyMemory<int> retainedScratch=default,ReadOnlyMemory<DungeonTorch> retainedTorches=default) {if((uint)retainedNextTorch>=20) throw new ArgumentOutOfRangeException(nameof(retainedNextTorch));nextTorch=retainedNextTorch;if(!retainedTorches.IsEmpty) {if(retainedTorches.Length!=20) throw new ArgumentException("Torch bank requires20 records.");retainedTorches.Span.CopyTo(torches);for(int i=0;i<20;i++) torches[i]=torches[i] with {Active=false};}if(!retainedScratch.IsEmpty) {if(retainedScratch.Length!=9) throw new ArgumentException("Scratch bank requires9 integers.");retainedScratch.Span.CopyTo(scratchNeighbors);}}
    public void Reset() {Array.Clear(values); for(int i=0;i<20;i++) torches[i]=torches[i] with {Active=false};}
    public void MarkLocal(int x,int y,int width)
    {
        foreach(var delta in local) Mark(x+delta.X,y+delta.Y,width,1);
    }
    public void Place(int x,int y,int cost)
    {
        torches[nextTorch]=new(true,x,y,cost,unchecked(cost*4)); nextTorch=(nextTorch+1)%20;
    }
    public void Arrival(int x,int y,int width)
    {
        Array.Clear(values); MarkLocal(x,y,width);
        for(int i=0;i<20;i++)
        {
            var t=torches[i]; int remaining=unchecked(t.Remaining-1);
            torches[i]=t with {Remaining=remaining<1?0:remaining,Active=remaining<1?false:t.Active};
        }
        foreach(var t in torches) if(t.Active)
            for(int yy=-1;yy<=1;yy++) for(int xx=-1;xx<=1;xx++) Mark(t.X+xx,t.Y+yy,width,2);
    }
    private void Mark(int x,int y,int width,int value)
    {
        //Native tests hard-coded10 bounds even though flattening uses AMF width.
        if((uint)x>=10 || (uint)y>=10) return;
        int index=checked(y*width+x);
        if((uint)index>=100) throw new InvalidDataException("Dungeon visibility bank index exceeds100.");
        values[index]=value;
    }
}
