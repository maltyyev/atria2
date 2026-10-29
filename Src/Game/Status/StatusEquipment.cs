using Atria.Game.Progression;
using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.Status;

public enum StatusEquipResult
{
    Changed,
    EmptySlot,
    Unavailable,
    AlreadyEquipped,
    ConflictingAccessory
}

/// <summary>415720/415780 candidate filtering,415840 conflicts,415f60 equip and416070 remove.</summary>
public static class StatusEquipment
{
    public static int Category(int kind, int slot)
    {
        if ((uint)kind >= ClassCatalog.Count || (uint)slot >= 6)
        {
            throw new InvalidDataException("Unknown status class/slot.");
        }

        return ClassCatalog.Get(kind).EquipmentCategories[slot];
    }

    public static int[] Candidates(GameState state, ItemCatalog items, int actor, int slot)
    {
        ValidateSlot(state, actor, slot);
        int kind = state.Heroes[actor].Class;
        if ((uint)kind >= ClassCatalog.Count)
        {
            throw new InvalidDataException("Unknown equipment class.");
        }

        int category = ClassCatalog.Get(kind).EquipmentCategories[slot];
        return Enumerable.Range(0, 88).Where(id => state.OwnedItems[id] > 0 && state.AvailableItems[id] > 0
&& (items.GetItem(id).Category == category || category == 5 && items.GetItem(id).Category == 6) && !(state.HeroCatalog[actor].Id == "swallow" && category == 0 && id >= 6)).ToArray();
    }

    public static StatusEquipResult Equip(GameState state, ItemCatalog items, int actor, int slot, int id)
    {
        ValidateSlot(state, actor, slot);
        if ((uint)id >= 88)
        {
            throw new InvalidDataException("Status equipment item exceeds88 definitions.");
        }

        if (state.AvailableItems[id] < 1)
        {
            return StatusEquipResult.Unavailable;
        }

        int old = state.Heroes[actor].Equipment[slot];
        if (items.GetItem(id).Category == 7 && Conflicts(state, actor, slot, id))
        {
            return StatusEquipResult.ConflictingAccessory;
        }

        if (old == id)
        {
            return StatusEquipResult.AlreadyEquipped;
        }

        var working = Copy(state);
        if (old != -1)
        {
            ValidateItem(old);
            working.AvailableItems[old] = unchecked(working.AvailableItems[old] + 1);
            RemoveEffects(working, items, actor, old);
        }

        working.Heroes[actor].Equipment[slot] = id;
        NewCharacterRuntime.ApplyEquipment(working, items, actor, id);
        PartyDerivedStats.Recalculate(working, items);
        working.AvailableItems[id] = unchecked(working.AvailableItems[id] - 1);
        Commit(working, state);
        return StatusEquipResult.Changed;
    }

    public static StatusEquipResult Remove(GameState state, ItemCatalog items, int actor, int slot)
    {
        ValidateSlot(state, actor, slot);
        int old = state.Heroes[actor].Equipment[slot];
        if (old == -1)
        {
            return StatusEquipResult.EmptySlot;
        }

        ValidateItem(old);
        var working = Copy(state);
        working.Heroes[actor].Equipment[slot] = -1;
        working.AvailableItems[old] = unchecked(working.AvailableItems[old] + 1);
        RemoveEffects(working, items, actor, old);
        PartyDerivedStats.Recalculate(working, items);
        Commit(working, state);
        return StatusEquipResult.Changed;
    }

    private static bool Conflicts(GameState state, int actor, int slot, int id)
    {
        int Group(int value) => value switch
        {
            >= 37 and <= 39 => 1,
            >= 40 and <= 43 => 2,
            44 or 45 => 3,
            46 or 47 => 4,
            _ => 0
        };
        int group = Group(id);
        if (group == 0)
        {
            return false;
        }

        for (int i = 0; i < state.Heroes[actor].EquipmentCapacity; i++)
        {
            if (i != slot && Group(state.Heroes[actor].Equipment[i]) == group)
            {
                return true;
            }
        }

        return false;
    }

    private static void RemoveEffects(GameState state, ItemCatalog items, int actor, int id)
    {
        EquipmentEffects.Apply(state.Heroes[actor], items.GetItem(id), removing: true);
    }

    private static void ValidateSlot(GameState state, int actor, int slot)
    {
        if ((uint)actor >= state.Heroes.Length || (uint)state.Heroes[actor].EquipmentCapacity > 6 || (uint)slot >= (uint)state.Heroes[actor].EquipmentCapacity)
        {
            throw new InvalidDataException("Status equipment actor/slot exceeds recovered bounds.");
        }
    }

    private static void ValidateItem(int id)
    {
        if ((uint)id >= 88)
        {
            throw new InvalidDataException("Equipped item exceeds88 definitions.");
        }
    }

    private static GameState Copy(GameState state)
    {
        var copy = new GameState(state.HeroCatalog);
        PartyCodec.Copy(state.Party, copy.Party);
        state.AvailableItems.CopyTo(copy.AvailableItems, 0);
        return copy;
    }

    private static void Commit(GameState source, GameState target)
    {
        PartyCodec.Copy(source.Party, target.Party);
        source.AvailableItems.CopyTo(target.AvailableItems, 0);
    }
}
