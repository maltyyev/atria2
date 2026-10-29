using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.Progression;

/// <summary>4219b0 ->41aaf0: deferred state transition, with no native modal or sound.</summary>
public static class NewCharacterRuntime
{
    //415920 reads45ce68. Its target is deliberately explicit;41aaf0 does not set it per actor.
    public static bool ProcessPending(GameState state, ItemCatalog items, int selectedEquipmentActor)
    {
        if (state.NewCharacterPending == 0)
        {
            return false;
        }

        if ((uint)state.NewCharacterId >= state.Party.Length || (uint)selectedEquipmentActor >= (uint)state.Heroes.Length)
        {
            throw new InvalidDataException("New-character index or equipment effect target exceeds native party storage.");
        }

        var working = new GameState(state.HeroCatalog);
        PartyCodec.Copy(state.Party, working.Party);
        state.OwnedItems.CopyTo(working.OwnedItems, 0);
        int joining = state.NewCharacterId;
        working.SetMemberStatus(joining, 1);
        for (int slot = 0; slot < 6; slot++)
        {
            int id = working.Party[joining].Equipment[slot];
            if (id == -1)
            {
                continue;
            }

            if ((uint)id >= GameState.InventoryBankCount)
            {
                throw new InvalidDataException("Joining equipment exceeds inventory storage.");
            }

            working.OwnedItems[id] = unchecked(working.OwnedItems[id] + 1);
        }

        for (int actor = 0; actor < working.Heroes.Length; actor++)
        {
            int kind = working.Heroes[actor].Class;
            if ((uint)kind < ClassCatalog.Count)
            {
                working.Heroes[actor].EquipmentCapacity = InitialGameState.EquipmentCapacity(kind, working.Heroes[actor].Level);
            }
        }

        for (int actor = 0; actor < working.Heroes.Length; actor++)
        {
            if (working.Heroes[actor].MemberStatus != 1)
            {
                continue;
            }

            int capacity = working.Heroes[actor].EquipmentCapacity;
            if ((uint)capacity > 6)
            {
                throw new InvalidDataException("Joining equipment capacity exceeds six slots.");
            }

            for (int slot = 0; slot < capacity; slot++)
            {
                int id = working.Heroes[actor].Equipment[slot];
                if (id == -1)
                {
                    continue;
                }

                ApplyEquipment(working, items, selectedEquipmentActor, id);
            }
        }

        PartyDerivedStats.Recalculate(working, items);
        PartyCodec.Copy(working.Party, state.Party);
        working.OwnedItems.CopyTo(state.OwnedItems, 0);
        // AvailableItems and NewCharacterId are intentionally retained by41aaf0.
        state.NewCharacterPending = 0;
        return true;
    }

    internal static void ApplyEquipment(GameState state, ItemCatalog items, int actor, int id)
    {
        if ((uint)id >= ItemCatalog.Count)
        {
            throw new InvalidDataException("Joining equipment has no item definition.");
        }

        EquipmentEffects.Apply(state.Heroes[actor], items.GetItem(id), removing: false);
    }
}
