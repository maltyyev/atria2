using System.Buffers.Binary;
using System.Text;
using Atria.Game.State;
using Atria.Game.World;

namespace Atria.Game.Exploration;

public sealed record WorldMapEntry(WorldMapContext Map,int AreaIndex,ReadOnlyMemory<byte> PrimaryName,
    ReadOnlyMemory<byte> SecondaryName,ReadOnlyMemory<byte> ScriptMember,ReadOnlyMemory<byte> MapMember);
public sealed record WorldTransition(WorldMapEntry Entry,int TargetX,int TargetY);

public static class WorldMapBoundary
{
    //41b340 load/restore portion. Caller owns replacing controller/assets and executing music/background mode entry.
    public static WorldMapEntry Load(ReadOnlyMemory<byte> mapMember,WorldAreaStore areas,
        Func<ReadOnlyMemory<byte>,ReadOnlyMemory<byte>> readAmf)
    {
        var next=WorldMapContext.Parse(readAmf(mapMember)); int area=next.AreaIndex;
        if((uint)area>=8) throw new InvalidDataException("Loaded world area exceeds eight saved records.");
        if(areas.IsInitialized(area)) areas.Restore(area,next); else areas.Capture(area,next);
        byte[] inline=areas.GetInline(area);
        return new(next,area,WorldMapContext.CString(inline.AsSpan(0,50)),WorldMapContext.CString(inline.AsSpan(50,50)),
            Encoding.ASCII.GetBytes($"ground{area+1}.scr"),mapMember.ToArray());
    }
    //41c060: preserve source area before reading destination+92/+96/+128; no direct guessed mode jump.
    public static WorldTransition PrepareTransition(GameState state,WorldMapContext current,WorldAreaStore areas,
        Func<ReadOnlyMemory<byte>,ReadOnlyMemory<byte>> readAmf)
    {
        if((uint)state.WorldX>=10 || (uint)state.WorldY>=10) throw new InvalidDataException("Transition source coordinate outside world.");
        var tile=current.TileBytes.Slice((state.WorldY*10+state.WorldX)*328,328);
        int x=BinaryPrimitives.ReadInt32LittleEndian(tile[92..]),y=BinaryPrimitives.ReadInt32LittleEndian(tile[96..]);
        if((uint)x>=10 || (uint)y>=10) throw new InvalidDataException("Transition destination coordinate outside world.");
        var member=WorldMapContext.CString(tile.Slice(128,20));
        areas.Capture(current.AreaIndex,current);
        return new(Load(member,areas,readAmf),x,y);
    }
    public static void ApplyCoordinatesAndNativeResets(WorldTransition transition,GameState state)
    {
        state.WorldX=transition.TargetX; state.WorldY=transition.TargetY;
        state.ModeStackDepth=-1; state.Globals[73]=state.Globals[74]=state.Globals[75]=0;
        state.ChangedChip=0;
        // Native also resets save gate46fd54/travel5f548 and invokes429260(300): explicit host obligations.
    }
}
