using Atria.Resources;

namespace Atria.Game.State;

/// <summary>Recovered state-only reset subset, excluding world allocations/platform/scene loading.</summary>
public static class InitialGameState
{
    public static GameState Create(ItemCatalog items, IReadOnlyList<ReadOnlyMemory<byte>> messages, HeroCatalog? heroCatalog = null)
    {
        var state = new GameState(heroCatalog);
        ResetRecoveredCore(state, items, messages);
        return state;
    }

    public static void ResetRecoveredCore(GameState state, ItemCatalog items, IReadOnlyList<ReadOnlyMemory<byte>> messages)
    {
        state.Journal.Clear();
        if (messages.Count < 260)
        {
            throw new InvalidDataException("TEXTmsg must contain initial names and event-item labels.");
        }

        Array.Clear(state.OwnedItems, 0, 100);
        Array.Clear(state.AvailableItems, 0, 100);
        foreach (int item in new[]
        {
            0,
            18,
            25,
            26
        }
        )
        {
            state.OwnedItems[item] = 1;
        }

        for (int i = 1; i <= 50; i++)
        {
            state.SetQuestItemFlag(i, 0);
            CopyName(messages[209 + i].Span, state.QuestItemRecords.AsSpan(i * 74 + 4, 30), false);
            CopyName(messages[209 + i].Span, state.QuestItemRecords.AsSpan(i * 74 + 34, 30), false);
        }

        InitialPartyDefinition.Apply(state.Party);
        state.HeroCatalog.InitializeAdditionalHeroes(state);
        state.InitializedPartyCount = state.Heroes.Length;
        for (int i = 0; i < state.Heroes.Length; i++)
        {
            state.Heroes[i].Name.AsSpan().Clear();
            state.Heroes[i].LegacyName.AsSpan().Clear();
            CopyName(messages[state.HeroCatalog[i].Template].Span, state.Heroes[i].Name, false);

            state.Heroes[i].EquipmentCapacity = EquipmentCapacity(state.Heroes[i].Class, state.Heroes[i].Level);
        }

        // 402c60 invokes equipment-effect routine415920 before derived recalculation.
        // All actual initial active items have zero modifiers. Reject an altered catalog
        // here until the unrelated general equip/unequip effect path is implemented.
        for (int actor = 0; actor < state.Heroes.Length; actor++)
        {
            if (state.Heroes[actor].MemberStatus == 0)
            {
                continue;
            }

            for (int slot = 0; slot < state.Heroes[actor].EquipmentCapacity; slot++)
            {
                int id = state.Heroes[actor].Equipment[slot];
                if (id != -1 && items.GetItem(id).ModifierCount != 0)
                {
                    throw new NotSupportedException("Initial equipped-item effect path needs recovery for modified item catalogs.");
                }
            }
        }

        PartyDerivedStats.Recalculate(state, items);
        state.WorldIndex = 0;
        state.WorldX = 4;
        state.WorldY = 4;
        state.ResourceCounterDc88 = 20;
        state.Money = 200;
        state.Score = 0;
        state.ResourceCounterFc9a = 50;
        Array.Clear(state.Events);
        Array.Clear(state.EventStatuses);
        Array.Clear(state.Globals);
        state.EventStatuses[0] = 4;
        state.Mode = 200;
        state.ModeStackDepth = 0;
        state.EndingResetFlag = 0;
        state.EnterPermission = 0;
        state.EventCounter = 1;
        state.DungeonEntryPending = 0;
        state.DayCounterStorage = 0;
        state.Year = 1450;
        state.Month = 3;
        state.Day = 14;
        state.DungeonFog = 1;
        state.ExperiencePending = 0;
        state.NewCharacterPending = 0;
        state.NewCharacterId = 0;
    }

    private static void CopyName(ReadOnlySpan<byte> source, Span<byte> target, bool clear)
    {
        int nul = source.IndexOf((byte)0);
        if (nul >= 0)
        {
            source = source[..nul];
        }

        if (source.Length >= target.Length)
        {
            throw new InvalidDataException("Localized name exceeds original destination.");
        }

        if (clear)
        {
            target.Clear();
        }

        source.CopyTo(target);
        target[source.Length] = 0;
    }

    public static int EquipmentCapacity(int classId, int level) => ClassCatalog.Get(classId).CapacityAt(level);

}
