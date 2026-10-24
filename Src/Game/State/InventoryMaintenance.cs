namespace Atria.Game.State;

/// <summary>Repair the three per-battle supplies whose old copyback lost the final ownership decrement.</summary>
public static class InventoryMaintenance
{
    public static void ReconcileBattleSupplies(GameState state)
    {
        for (int item = 58; item <= 60; item++)
        {
            int total = Math.Max(0, state.AvailableItems[item]);
            for (int actor = 0; actor < 6; actor++)
            {
                int capacity = state.Party[actor].EquipmentCapacity;
                if ((uint)capacity > 6)
                {
                    throw new InvalidDataException("Equipment capacity exceeds six slots.");
                }

                for (int slot = 0; slot < capacity; slot++)
                {
                    if (state.Party[actor].Equipment[slot] == item)
                    {
                        total = checked(total + 1);
                    }
                }
            }

            state.AvailableItems[item] = Math.Max(0, state.AvailableItems[item]);
            state.OwnedItems[item] = total;
        }
    }
}
