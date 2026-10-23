using Atria.Game.Progression;
using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.Status;

public enum StatusEquipResult { Changed, EmptySlot, Unavailable, AlreadyEquipped, ConflictingAccessory }
/// <summary>415720/415780 candidate filtering,415840 conflicts,415f60 equip and416070 remove.</summary>
public static class StatusEquipment
{
    private static readonly int[][] Categories=[[0,5,7,8,7,9],[1,5,7,8,7,7],[3,6,9,8,8,9],[0,6,8,8,7,9],[3,6,8,8,7,8],[2,8,8,7,8,7]];
    public static int Category(int kind,int slot){if((uint)kind>=6||(uint)slot>=6)throw new InvalidDataException("Unknown status class/slot.");return Categories[kind][slot];}
    public static int[] Candidates(GameState state,ItemCatalog items,int actor,int slot)
    {
        ValidateSlot(state,actor,slot);int kind=state.GetPartyInt(actor,64);
        if((uint)kind>=6)throw new InvalidDataException("Status equipment class exceeds native six class records.");
        int category=Categories[kind][slot];
        return Enumerable.Range(0,88).Where(id=>state.OwnedItems[id]>0 && state.AvailableItems[id]>0 &&
            (items.GetItem(id).Category==category || category==5&&items.GetItem(id).Category==6) &&
            !(actor==2&&category==0&&id>=6)).ToArray();
    }
    public static StatusEquipResult Equip(GameState state,ItemCatalog items,int actor,int slot,int id)
    {
        ValidateSlot(state,actor,slot);if((uint)id>=88)throw new InvalidDataException("Status equipment item exceeds88 definitions.");
        if(state.AvailableItems[id]<1)return StatusEquipResult.Unavailable;
        int old=state.GetPartyInt(actor,228+slot*4);
        if(items.GetItem(id).Category==7&&Conflicts(state,actor,slot,id))return StatusEquipResult.ConflictingAccessory;
        if(old==id)return StatusEquipResult.AlreadyEquipped;
        var working=Copy(state);
        if(old!=-1){ValidateItem(old);working.AvailableItems[old]=unchecked(working.AvailableItems[old]+1);RemoveEffects(working,items,actor,old);}
        working.SetPartyInt(actor,228+slot*4,id);NewCharacterRuntime.ApplyEquipment(working,items,actor,id);
        PartyDerivedStats.Recalculate(working,items);working.AvailableItems[id]=unchecked(working.AvailableItems[id]-1);Commit(working,state);
        return StatusEquipResult.Changed;
    }
    public static StatusEquipResult Remove(GameState state,ItemCatalog items,int actor,int slot)
    {
        ValidateSlot(state,actor,slot);int old=state.GetPartyInt(actor,228+slot*4);if(old==-1)return StatusEquipResult.EmptySlot;
        ValidateItem(old);var working=Copy(state);working.SetPartyInt(actor,228+slot*4,-1);
        working.AvailableItems[old]=unchecked(working.AvailableItems[old]+1);RemoveEffects(working,items,actor,old);
        PartyDerivedStats.Recalculate(working,items);Commit(working,state);return StatusEquipResult.Changed;
    }
    private static bool Conflicts(GameState state,int actor,int slot,int id)
    {
        int Group(int value)=>value switch {>=37 and <=39=>1,>=40 and <=43=>2,44 or 45=>3,46 or 47=>4,_=>0};
        int group=Group(id);if(group==0)return false;
        for(int i=0;i<state.GetPartyInt(actor,252);i++)if(i!=slot&&Group(state.GetPartyInt(actor,228+i*4))==group)return true;
        return false;
    }
    private static void RemoveEffects(GameState state,ItemCatalog items,int actor,int id)
    {
        var item=items.GetItem(id);int count=item.GetInteger(7);if(count>3)throw new InvalidDataException("Equipment modifier count exceeds three native fields.");
        void Subtract(int at,int amount)=>state.SetPartyInt(actor,at,unchecked(state.GetPartyInt(actor,at)-amount));
        for(int i=0;i<count;i++)
        {
            int code=item.GetInteger(8+i),amount=item.GetInteger(11+i);
            if((uint)code<6)Subtract(100+code*4,amount);
            else if(code==17){for(int j=0;j<6;j++)Subtract(100+j*4,amount);}
            else
            {
                int at=code switch {6=>76,7=>84,8=>188,>=9 and <=11 or 13 or 14=>148+(code-9)*4,15=>204,16=>208,18=>72,19=>80,20=>88,21=>92,22=>96,_=>-1};
                if(at>=0)Subtract(at,amount);
            }
        }
    }
    private static void ValidateSlot(GameState state,int actor,int slot)
    {if((uint)actor>=6||(uint)state.GetPartyInt(actor,252)>6||(uint)slot>=(uint)state.GetPartyInt(actor,252))throw new InvalidDataException("Status equipment actor/slot exceeds recovered bounds.");}
    private static void ValidateItem(int id){if((uint)id>=88)throw new InvalidDataException("Equipped item exceeds88 definitions.");}
    private static GameState Copy(GameState state){var copy=new GameState();state.PartyRecords.CopyTo(copy.PartyRecords,0);state.AvailableItems.CopyTo(copy.AvailableItems,0);return copy;}
    private static void Commit(GameState source,GameState target){source.PartyRecords.CopyTo(target.PartyRecords,0);source.AvailableItems.CopyTo(target.AvailableItems,0);}
}

