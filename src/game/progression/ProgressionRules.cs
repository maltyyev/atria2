using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.Progression;

public static class ProgressionRules
{
    // Original44d834, bounded before adjacent start.wav. Rank13 terminal behavior is an approved repair.
    public static ReadOnlySpan<int> Thresholds => [1000, 2000, 4000, 8000, 16000, 32000, 48000, 64000, 80000, 96000, 112000, 128000, 999999];
    public static ReadOnlySpan<int> RankQuestItemIds => [3, 8, 12, 17, 20, 24, 29, 34, 40, 45, 46, 47];

    public static void ValidateRecord(GameState state, int actor)
    {
        int level = state.GetPartyInt(actor, 200), threshold = state.GetPartyInt(actor, 196);
        if (level is < 1 or > 13) throw new InvalidDataException("Party level is outside recovered ranks1..13; no implicit legacy-save normalization.");
        if (threshold != Thresholds[level - 1]) throw new InvalidDataException("Party next-XP threshold does not match the recovered level table.");
        if ((uint)state.GetPartyInt(actor, 64) >= 6) throw new InvalidDataException("Unknown progression class.");
    }

    internal static void ApplyAward(ExperienceAllocationSession session)
    {
        if (!session.Finished) throw new InvalidOperationException("Actual XP allocation confirmation is required.");
        GameState party = session.Working;
        for (int i = 0; i < 6; i++)
        {
            if (party.GetMemberStatus(i) == 0) continue;
            int xp = unchecked(party.GetPartyInt(i, 192) + session.ConsumeAllocation(i));
            int level = party.GetPartyInt(i, 200);
            party.SetPartyInt(i, 192, xp);
            if (level < 13 && xp >= party.GetPartyInt(i, 196))
            {
                party.SetPartyInt(i, 196, Thresholds[level]);
                party.SetPartyInt(i, 256, 1);
                party.SetPartyInt(i, 200, ++level);
            }
            party.SetPartyInt(i, 88, (level - 1) / 2 + 1);
            party.SetPartyInt(i, 224, level / 3 + 1);
        }
    }

    // 403ec0: equipment capacity first, then per-pending actor403cc0 and full recalculation each time.
    internal static void FinalizeStats(GameState working, GameState rankEvidence, ItemCatalog items)
    {
        for (int i = 0; i < 6; i++)
        {
            int level = working.GetPartyInt(i, 200), type = working.GetPartyInt(i, 64);
            if ((uint)type < 6) working.SetPartyInt(i, 252, InitialGameState.EquipmentCapacity(type, level));
        }
        for (int i = 0; i < 6; i++)
        {
            if (working.GetPartyInt(i, 256) == 0) continue;
            ValidateRecord(working, i);
            int level = working.GetPartyInt(i, 200);
            if (level == 13)
            {
                bool itemsComplete = true, questsComplete = true;
                foreach (int id in RankQuestItemIds) if (rankEvidence.GetQuestItemFlag(id) == 0) { itemsComplete = false; break; }
                for (int id = 20; id < 40; id++) if (rankEvidence.EventStatuses[id] != 4) { questsComplete = false; break; }
                if (itemsComplete) Bonus(i, 2, 2, 1);
                if (questsComplete) Bonus(i, itemsComplete ? 4 : 2, itemsComplete ? 5 : 2, itemsComplete ? 2 : 1);
            }
            else
            {
                int row = (working.GetPartyInt(i, 64) * 13 + level) * 6;
                for (int n = 0; n < 3; n++)
                {
                    int code = LevelGainTable.Values[row + n];
                    if (code == 99) continue;
                    int offset = code == 23 ? 184 : 100 + code * 4;
                    if ((uint)offset > GameState.PartyRecordSize - 4) throw new InvalidDataException("Level stat target exceeds party record.");
                    working.SetPartyInt(i, offset, unchecked(working.GetPartyInt(i, offset) + LevelGainTable.Values[row + n + 3]));
                }
            }
            PartyDerivedStats.Recalculate(working, items);
            working.SetPartyInt(i, 256, 0);
        }
        void Bonus(int actor, int attribute, int extra, int rank)
        {
            for (int offset = 100; offset <= 120; offset += 4)
                working.SetPartyInt(actor, offset, unchecked(working.GetPartyInt(actor, offset) + attribute));
            working.SetPartyInt(actor, 184, unchecked(working.GetPartyInt(actor, 184) + extra));
            working.SetPartyInt(actor, 276, rank);
        }
    }
}
