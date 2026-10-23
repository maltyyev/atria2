using Atria.Game.Exploration;
using Atria.Game.State;
using Atria.Game.World;

namespace Atria.Game.Dungeon;

public enum DungeonEntrySource { WorldTile, WorldScript, TownScript }
public sealed record DungeonEntryPlan(DungeonEntrySource Source,ReadOnlyMemory<byte> MapMember,ReadOnlyMemory<byte> ScriptMember,int ReturnX,int ReturnY,int? CaptureArea);
public static class DungeonEntry
{
    //41e4e0 copies world state BEFORE41eb60 replaces the active map and locates its first entrance.
    public static ReadOnlyMemory<byte> ResolveScript(DungeonEntryPlan plan,Atria.Resources.AmfMap loaded)=>plan.Source==DungeonEntrySource.WorldTile
        ?loaded.EmbeddedScene?.ScriptReference??ReadOnlyMemory<byte>.Empty:plan.ScriptMember;
    public static DungeonEntryPlan Prepare(GameState state,WorldMapContext world,bool worldScriptOverride)
    {
        if(state.DungeonEntryPending!=0)
            return new(DungeonEntrySource.TownScript,state.PendingDungeonMap.ToArray(),state.PendingDungeonScript.ToArray(),state.WorldX,state.WorldY,null);
        if(worldScriptOverride)
            return new(DungeonEntrySource.WorldScript,state.PendingDungeonMap.ToArray(),state.PendingDungeonScript.ToArray(),state.WorldX,state.WorldY,world.AreaIndex);
        var tile=world.SelectScene(state.WorldX,state.WorldY);
        return new(DungeonEntrySource.WorldTile,tile.SceneMember.ToArray(),ReadOnlyMemory<byte>.Empty,state.WorldX,state.WorldY,world.AreaIndex);
    }
}

public enum DungeonArrivalEffectKind { ExitPending, TreasureMoney, TreasureItem, TreasureTorch, TreasureRetained, ScriptPending, BattlePending }
public sealed record DungeonArrivalEffect(DungeonArrivalEffectKind Kind,int Value=0,int Value2=0,ReadOnlyMemory<byte> Member=default);

/// <summary>41f440/41f5a0/41f7f0/41f850/41f8a0/41f8f0 arrival mutations in native order.</summary>
public sealed partial class DungeonArrival
{
    private readonly GameState state;
    private readonly DungeonMap map;
    private readonly DungeonVisibility visibility;
    public int FireCountdown {get;private set;}=2;
    public DungeonArrival(GameState state,DungeonMap map,DungeonVisibility visibility)
    {this.state=state;this.map=map;this.visibility=visibility;}
    public IReadOnlyList<DungeonArrivalEffect> Complete()
    {
        List<DungeonArrivalEffect> effects=[]; int index=state.WorldX+state.WorldY*10;
        switch(map.Read(index,88))
        {
            case 2: effects.Add(new(DungeonArrivalEffectKind.ExitPending));break;
            case 6:
                if(map.Read(index,104)==0) break;
                int kind=map.Read(index,92),value=map.Read(index,96),amount=map.Read(index,100);
                if(kind==1) {state.Money=unchecked(state.Money+value);effects.Add(new(DungeonArrivalEffectKind.TreasureMoney,value));}
                else if(kind==2)
                {
                    if((uint)value>=state.OwnedItems.Length) throw new InvalidDataException("Dungeon treasure item exceeds bank.");
                    state.OwnedItems[value]=unchecked(state.OwnedItems[value]+amount);state.AvailableItems[value]=unchecked(state.AvailableItems[value]+amount);
                    effects.Add(new(DungeonArrivalEffectKind.TreasureItem,value,amount));
                }
                else if(kind==3) {state.ResourceCounterFc9a=unchecked(state.ResourceCounterFc9a+value);effects.Add(new(DungeonArrivalEffectKind.TreasureTorch,value));}
                else if(kind==4) effects.Add(new(DungeonArrivalEffectKind.ScriptPending,8,Member:map.Member(index)));
                else effects.Add(new(DungeonArrivalEffectKind.TreasureRetained)); //41f71e retains message/type/value banks, opens6 and clears availability.
                map.Write(index,104,0);break;
            case 7:effects.Add(new(DungeonArrivalEffectKind.ScriptPending,Member:map.Member(index)));map.Write(index,88,0);break;
            case 8:
                state.PendingBattle=map.Read(index,92);state.BattleVariant=unchecked((ushort)map.Read(index,96));state.EndingResetFlag=1;
                effects.Add(new(DungeonArrivalEffectKind.BattlePending,state.PendingBattle,state.BattleVariant));map.Write(index,88,0);break;
            case 9:effects.Add(new(DungeonArrivalEffectKind.ScriptPending,Member:map.Member(index)));break;
        }
        FireCountdown=unchecked(FireCountdown-1);
        if(FireCountdown==0)
        {
            state.ResourceCounterFc9a=unchecked(state.ResourceCounterFc9a-1);
            if(state.ResourceCounterFc9a<0)
            {
                state.ResourceCounterFc9a=0;
                effects.Add(new(DungeonArrivalEffectKind.ScriptPending,Member:"dungeon_nofire"u8.ToArray()));
                effects.Add(new(DungeonArrivalEffectKind.ExitPending));
            }
            FireCountdown=2;
        }
        state.DungeonFogDirty=1;visibility.Arrival(state.WorldX,state.WorldY,map.Context.Width);
        return effects;
    }
}
