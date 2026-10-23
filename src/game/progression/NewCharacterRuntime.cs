using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.Progression;

/// <summary>4219b0 ->41aaf0: deferred state transition, with no native modal or sound.</summary>
public static class NewCharacterRuntime
{
    //415920 reads45ce68. Its target is deliberately explicit;41aaf0 does not set it per actor.
    public static bool ProcessPending(GameState state, ItemCatalog items, int selectedEquipmentActor)
    {
        if (state.NewCharacterPending == 0) return false;
        if ((uint)state.NewCharacterId >= GameState.PartyCount || (uint)selectedEquipmentActor >= 6)
            throw new InvalidDataException("New-character index or equipment effect target exceeds native party storage.");
        var working = new GameState();
        state.PartyRecords.CopyTo(working.PartyRecords, 0);
        state.OwnedItems.CopyTo(working.OwnedItems, 0);
        int joining = state.NewCharacterId;
        working.SetMemberStatus(joining, 1);
        for (int slot = 0; slot < 6; slot++)
        {
            int id = working.GetPartyInt(joining, 228 + slot * 4);
            if (id == -1) continue;
            if ((uint)id >= GameState.InventoryBankCount) throw new InvalidDataException("Joining equipment exceeds inventory storage.");
            working.OwnedItems[id] = unchecked(working.OwnedItems[id] + 1);
        }
        for (int actor = 0; actor < 6; actor++)
        {
            int kind = working.GetPartyInt(actor, 64);
            if ((uint)kind < 6)
                working.SetPartyInt(actor, 252, InitialGameState.EquipmentCapacity(kind, working.GetPartyInt(actor, 200)));
        }
        for (int actor = 0; actor < 6; actor++)
        {
            if (working.GetMemberStatus(actor) != 1) continue;
            int capacity = working.GetPartyInt(actor, 252);
            if ((uint)capacity > 6) throw new InvalidDataException("Joining equipment capacity exceeds six slots.");
            for (int slot = 0; slot < capacity; slot++)
            {
                int id = working.GetPartyInt(actor, 228 + slot * 4);
                if (id == -1) continue;
                ApplyEquipment(working, items, selectedEquipmentActor, id);
            }
        }
        PartyDerivedStats.Recalculate(working, items);
        working.PartyRecords.CopyTo(state.PartyRecords, 0);
        working.OwnedItems.CopyTo(state.OwnedItems, 0);
        // AvailableItems and NewCharacterId are intentionally retained by41aaf0.
        state.NewCharacterPending = 0;
        return true;
    }

    internal static void ApplyEquipment(GameState state, ItemCatalog items, int actor, int id)
    {
        if ((uint)id >= ItemCatalog.Count) throw new InvalidDataException("Joining equipment has no item definition.");
        var item = items.GetItem(id);
        int count = item.GetInteger(7); // packed item58
        if (count > 3) throw new InvalidDataException("Equipment modifier count exceeds three native fields.");
        void Add(int offset, int amount) => state.SetPartyInt(actor, offset, unchecked(state.GetPartyInt(actor, offset) + amount));
        for (int i = 0; i < count; i++)
        {
            int code = item.GetInteger(8 + i), amount = item.GetInteger(11 + i);
            if ((uint)code < 6) Add(100 + code * 4, amount);
            else if (code == 17) { for (int j = 0; j < 6; j++) Add(100 + j * 4, amount); }
            else if (code == 18) { Add(72, amount); Add(76, amount); }
            else if (code == 19) { Add(80, amount); Add(84, amount); }
            else
            {
                int offset = code switch
                { 6 => 76, 7 => 84, 8 => 188, >= 9 and <= 11 or 13 or 14 => 148 + (code - 9) * 4,
                  15 => 204, 16 => 208, 20 => 88, 21 => 92, 22 => 96, _ => -1 };
                if (offset >= 0) Add(offset, amount);
            }
        }
    }
}
