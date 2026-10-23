using System.Buffers.Binary;
using Atria.Game.State;
using Atria.Game.World;

namespace Atria.Game.Exploration;

public static class ExplorationRules
{
    /// <summary>41c1a0:30-day calendar; only byte0 of saved day counter changes.</summary>
    public static void AdvanceTravelDay(GameState state)
    {
        state.Day=unchecked(state.Day+1);
        if(state.Day==31) { state.Day=1; state.Month=unchecked(state.Month+1); }
        else if(state.Day==32) { state.Day=2; state.Month=unchecked(state.Month+1); }
        if(state.Month==13) { state.Month=1; state.Year=unchecked(state.Year+1); }
        state.DayCounterStorage=unchecked((state.DayCounterStorage&~255)|((state.DayCounterStorage+1)&255));
    }
    internal static int Read(ReadOnlySpan<byte> tile,int at)=>BinaryPrimitives.ReadInt32LittleEndian(tile.Slice(at,4));
    internal static void Write(Span<byte> tile,int at,int value)=>BinaryPrimitives.WriteInt32LittleEndian(tile.Slice(at,4),value);
    /// <summary>41bf80 ordered OR: a matching first event0 can suppress the second event.</summary>
    public static int MatchingEvent(ReadOnlySpan<byte> tile,ReadOnlySpan<int> statuses)
    {
        if(tile.Length!=328) throw new ArgumentException("Expected one AMF tile.",nameof(tile));
        int first=Read(tile,112),second=Read(tile,120);
        if(first==0 && second==0) return -1;
        if((uint)first>=(uint)statuses.Length) throw new InvalidDataException("Tile first event index exceeds status bank.");
        if(statuses[first]==Read(tile,116)) return first;
        if((uint)second>=(uint)statuses.Length) throw new InvalidDataException("Tile second event index exceeds status bank.");
        return statuses[second]==Read(tile,124)?second:-1;
    }
    /// <summary>41b7b0/41b750/41b930 retain flattened-neighbor wrapping and exclusion of index0.</summary>
    public static void MarkArrival(WorldMapContext map,int x,int y)
    {
        if((uint)x>=10 || (uint)y>=10) throw new InvalidDataException("World position exceeds10x10 map.");
        Span<byte> tiles=map.TileBytes; int current=y*10+x;
        Write(tiles.Slice(current*328,328),44,1); tiles[current*328]=1;
        for(int i=0;i<100;i++) tiles[i*328+1]=0;
        foreach(int index in new[] {current-11,current-10,current-9,current-1,current,current+1,current+9,current+10,current+11})
            if(index>0 && index<100) tiles[index*328+1]=1;
        for(int i=0;i<100;i++) tiles[i*328+3]=tiles[i*328]==1?(byte)0:(byte)2;
    }
    public static bool CanReveal(WorldMapContext map,int currentX,int currentY,int targetX,int targetY)
    {
        if((uint)currentX>=10||(uint)currentY>=10||(uint)targetX>=10||(uint)targetY>=10) return false;
        int current=currentY*10+currentX,target=targetY*10+targetX;
        var tile=map.TileBytes.Slice(target*328,328);
        return tile[0]!=1 && Read(tile,4)!=16 && (target==current-1||target==current+1||target==current-10||target==current+10);
    }
    public static bool Reveal(WorldMapContext map,GameState state,int x,int y)
    {
        if(!CanReveal(map,state.WorldX,state.WorldY,x,y)) throw new InvalidOperationException("Tile is not an eligible native reveal target.");
        if(state.ResourceCounterDc88<1) return false;
        map.TileBytes[(y*10+x)*328]=1; state.ResourceCounterDc88=unchecked(state.ResourceCounterDc88-1); return true;
    }
}

public enum ExplorationEffectKind { RunCurrentScriptFunction, RunAreaFunction, StartEventScript, EnterDungeon330, Message, BattleQueued }
public sealed record ExplorationEffect(ExplorationEffectKind Kind,int Value=0,ReadOnlyMemory<byte> Member=default,ReadOnlyMemory<byte> Function=default);

/// <summary>Arrival half of41c220. External effects stay explicit, including unresolved battles and event scripts.</summary>
public sealed class ExplorationArrival
{
    private readonly GameState state;
    private readonly WorldMapContext map;
    private readonly LegacyRandom random;
    public int TravelSteps { get; private set; }
    public ExplorationArrival(GameState state,WorldMapContext map,LegacyRandom random,int travelSteps=0)
    { this.state=state;this.map=map;this.random=random;TravelSteps=travelSteps; }
    public ExplorationEffect? CompleteStep(Func<int,bool> isSceneReferenceBlocked)
    {
        ArgumentNullException.ThrowIfNull(isSceneReferenceBlocked);
        int x=state.WorldX,y=state.WorldY;
        if((uint)x>=10||(uint)y>=10) throw new InvalidDataException("World position exceeds map.");
        int area=map.AreaIndex;
        if((uint)area>=8) throw new InvalidDataException("Area index exceeds eight native records.");
        Span<byte> tile=map.TileBytes.Slice((y*10+x)*328,328);
        TravelSteps=unchecked(TravelSteps+1); ExplorationRules.AdvanceTravelDay(state); ExplorationRules.MarkArrival(map,x,y);
        int kind=ExplorationRules.Read(tile,88);
        if(kind==99) { state.ScriptActive=1;return new(ExplorationEffectKind.RunCurrentScriptFunction,Function:Text(tile.Slice(148,20))); }
        int eventId=ExplorationRules.MatchingEvent(tile,state.EventStatuses);
        int sceneFlag=ExplorationRules.Read(tile,24);
        if(eventId>0)
            return sceneFlag==0?new(ExplorationEffectKind.StartEventScript,eventId):new(ExplorationEffectKind.EnterDungeon330,eventId);
        if(sceneFlag==1) return ExplorationRules.Read(tile,116)!=1?new(ExplorationEffectKind.Message,77):null;
        if(isSceneReferenceBlocked(ExplorationRules.Read(tile,92)) && ExplorationRules.Read(tile,8)!=0) return new(ExplorationEffectKind.Message,99);
        if(kind==7)
        {
            state.ScriptActive=1;
            return new(ExplorationEffectKind.RunAreaFunction,Member:System.Text.Encoding.ASCII.GetBytes($"ground{area+1}.scr"),Function:Text(tile.Slice(128,20)));
        }
        if(kind==8)
        {
            state.PendingBattle=ExplorationRules.Read(tile,92);state.Globals[70]=unchecked((short)ExplorationRules.Read(tile,96));state.EndingResetFlag=1;
            ExplorationRules.Write(tile,88,0);ExplorationRules.Write(tile,92,0);return new(ExplorationEffectKind.BattleQueued,state.PendingBattle);
        }
        if(TravelSteps==area+7)
        {
            if(area>6) { TravelSteps=0;return null; }
            if((random.Next()&1)!=0) { TravelSteps=0;return QueueRandomBattle(tile,area); }
        }
        if(TravelSteps>=area+10) { TravelSteps=0;if(area<7) return QueueRandomBattle(tile,area); }
        return null;
    }
    private ExplorationEffect? QueueRandomBattle(ReadOnlySpan<byte> tile,int area)
    {
        int terrain=ExplorationRules.Read(tile,4);
        int category=terrain switch {1 or 8=>0,2=>1,4=>2,_=>throw new InvalidDataException("Native encounter terrain default returns undefined register data; unsupported until proven unreachable.")};
        int global=73+category,limit=category==0?11:6;
        if(state.Globals[global]>=limit) return null;
        state.Globals[global]=unchecked((short)(state.Globals[global]+1));
        state.Globals[70]=2;state.Globals[71]=unchecked((short)area);state.Globals[72]=unchecked((short)category);
        state.PendingBattle=1;state.EndingResetFlag=1;return new(ExplorationEffectKind.BattleQueued,1);
    }
    private static byte[] Text(ReadOnlySpan<byte> bytes)=>WorldMapContext.CString(bytes);
}

