using Atria.Resources;

namespace Atria.Game.State;

/// <summary>Recovered state-only reset subset, excluding world allocations/platform/scene loading.</summary>
public static class InitialGameState
{
    public static GameState Create(ItemCatalog items, IReadOnlyList<ReadOnlyMemory<byte>> messages)
    {
        var state = new GameState();
        ResetRecoveredCore(state, items, messages);
        return state;
    }

    public static void ResetRecoveredCore(GameState state, ItemCatalog items, IReadOnlyList<ReadOnlyMemory<byte>> messages)
    {
        if (messages.Count < 260) throw new InvalidDataException("TEXTmsg must contain initial names and event-item labels.");
        Array.Clear(state.OwnedItems, 0, 100); Array.Clear(state.AvailableItems, 0, 100);
        foreach (int item in new[] { 0, 18, 25, 26 }) state.OwnedItems[item] = 1;
        for (int i = 1; i <= 50; i++)
        {
            state.SetQuestItemFlag(i, 0);
            CopyName(messages[209 + i].Span, state.QuestItemRecords.AsSpan(i * 74 + 4, 30), false);
            CopyName(messages[209 + i].Span, state.QuestItemRecords.AsSpan(i * 74 + 34, 30), false);
        }
        state.InitializedPartyCount = 6;
        for (int i = 0; i < 6; i++)
        {
            state.PartyRecords.AsSpan(i * 280, 60).Clear();
            CopyName(messages[i].Span, state.PartyRecords.AsSpan(i * 280, 30), false);
            int[] writes = InitialPartyConstants.Records[i];
            for (int p = 0; p < writes.Length; p += 2) state.SetPartyInt(i, writes[p], writes[p + 1]);
            state.SetPartyInt(i, 252, EquipmentCapacity(state.GetPartyInt(i, 64), state.GetPartyInt(i, 200)));
        }
        // 402c60 invokes equipment-effect routine415920 before derived recalculation.
        // All actual initial active items have zero modifiers. Reject an altered catalog
        // here until the unrelated general equip/unequip effect path is implemented.
        for (int actor = 0; actor < 6; actor++)
        {
            if (state.GetMemberStatus(actor) == 0) continue;
            for (int slot = 0; slot < state.GetPartyInt(actor, 252); slot++)
            {
                int id = state.GetPartyInt(actor, 228 + slot * 4);
                if (id != -1 && ReadItem(items, id, 58) != 0)
                    throw new NotSupportedException("Initial equipped-item effect path needs recovery for modified item catalogs.");
            }
        }
        PartyDerivedStats.Recalculate(state, items);
        state.WorldIndex = 0; state.WorldX = 4; state.WorldY = 4;
        state.ResourceCounterDc88 = 20; state.Money = 200; state.Score = 0; state.ResourceCounterFc9a = 50;
        Array.Clear(state.Events); Array.Clear(state.EventStatuses); Array.Clear(state.Globals);
        state.EventStatuses[0] = 4;
        state.Mode = 200; state.ModeStackDepth = 0; state.EndingResetFlag = 0; state.EnterPermission = 0;
        state.EventCounter = 1; state.DungeonEntryPending = 0; state.DayCounterStorage = 0;
        state.Year = 1450; state.Month = 3; state.Day = 14;
        state.DungeonFog = 1; state.ExperiencePending = 0; state.NewCharacterPending = 0; state.NewCharacterId = 0;
    }

    internal static int ReadItem(ItemCatalog items, int id, int offset)
    {
        if ((uint)id >= ItemCatalog.Count) throw new InvalidDataException("Party equipment refers to an invalid item.");
        return items.GetItem(id).GetInteger((offset - 30) / 4);
    }
    private static void CopyName(ReadOnlySpan<byte> source, Span<byte> target, bool clear)
    {
        int nul = source.IndexOf((byte)0); if (nul >= 0) source = source[..nul];
        if (source.Length >= target.Length) throw new InvalidDataException("Localized name exceeds original destination.");
        if (clear) target.Clear();
        source.CopyTo(target); target[source.Length] = 0;
    }
    public static int EquipmentCapacity(int classId, int level) => classId switch
    {
        0 => level < 4 ? 3 : level < 6 ? 4 : level <= 9 ? 5 : 6,
        1 or 2 or 5 => level < 8 ? 4 : level <= 9 ? 5 : 6,
        3 => level < 6 ? 3 : level < 8 ? 4 : level <= 11 ? 5 : 6,
        4 => level < 6 ? 4 : level <= 9 ? 5 : 6,
        _ => throw new InvalidDataException("Unknown party class.")
    };
}

/// <summary>0x402860 and0x4029c0. Recalculation resets active current HP/MP as the original does.</summary>
public static class PartyDerivedStats
{
    // Class114-byte table fields82,86,90,94,98,102,106,110 from4141f0.
    private static readonly int[][] ClassConstants =
    [
        [20,15,10,10,15,10,8,8], [20,5,10,10,10,15,10,4],
        [5,20,20,10,10,5,4,10], [15,5,10,20,15,5,6,6],
        [10,15,15,5,10,10,8,10], [15,15,10,10,5,10,8,8]
    ];
    public static void Recalculate(GameState state, ItemCatalog items)
    {
        for (int actor = 0; actor < 6; actor++)
        {
            if (state.GetMemberStatus(actor) == 0) continue;
            int R(int offset) => state.GetPartyInt(actor, offset);
            void W(int offset, int value) => state.SetPartyInt(actor, offset, value);
            int classId = R(64);
            if ((uint)classId >= ClassConstants.Length) throw new InvalidDataException("Unknown class in derived-stat calculation.");
            int[] c = ClassConstants[classId];
            int capacity = R(252);
            if ((uint)capacity > 6) throw new InvalidDataException("Equipment capacity exceeds six slots.");
            int Item(int id, int offset) => InitialGameState.ReadItem(items, id, offset);
            int Modifier(int code)
            {
                int value = 0;
                for (int slot = 0; slot < capacity; slot++)
                {
                    int id = R(228 + slot * 4); if (id == -1) continue;
                    int count = Item(id, 58);
                    if ((uint)count > 3) throw new InvalidDataException("Item modifier count exceeds its original three fields.");
                    for (int j = 0; j < count; j++) if (Item(id, 62 + j * 4) == code) value = unchecked(value + Item(id, 74 + j * 4));
                }
                return value;
            }
            unchecked
            {
                W(148, (R(100) + R(120) + R(116)) / 3 + c[0] + Modifier(9));
                W(152, R(104) + c[1] + Modifier(10));
                W(156, (R(112) + R(104) + R(120)) / 3 + c[2] + Modifier(11));
                W(164, 0);
                W(168, (R(108) + R(104) + R(120)) / 3 + c[5] + Modifier(14));
                int hp = c[6] * (R(200) + 10) + R(108) * 10 + Modifier(18);
                int mp = c[7] * (R(200) + 10) + (classId == 5 ? R(112) : R(104)) * 10 + Modifier(19);
                W(72, hp); W(76, hp); W(80, mp); W(84, mp);
                int attack = classId == 1 ? 3 : classId == 5 ? 5 : 1;
                int weapon = R(228);
                if (weapon != -1)
                {
                    int chance = Math.Min(Item(weapon, 54) + R(200) * 5 + R(148), 120);
                    attack += (Item(weapon, 50) * chance + 50) / 100;
                }
                W(208, attack);
                int armor = classId == 5 ? 3 : 0;
                for (int slot = 0; slot < 6; slot++)
                {
                    int id = R(228 + slot * 4); if (id == -1) continue;
                    if (id > 24 && id < 48) armor += Item(id, 50);
                    for (int j = 0; j < 3; j++) if (Item(id, 62 + j * 4) == 15) armor += Item(id, 74 + j * 4);
                }
                W(204, armor); W(212, R(152) + R(104));
            }
        }
    }
}
