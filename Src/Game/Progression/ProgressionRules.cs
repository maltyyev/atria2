using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.Progression;

public static class ProgressionRules
{
    // Original44d834, bounded before adjacent start.wav. Rank13 terminal behavior is an approved repair.
    public static ReadOnlySpan<int> Thresholds => ProgressionCatalog.Thresholds;
    public static ReadOnlySpan<int> RankQuestItemIds => ProgressionCatalog.RankQuestItems;

    public static void ValidateRecord(GameState state, int actor)
    {
        int level = state.Party[actor].Level, threshold = state.Party[actor].NextExperience;
        if (level is < 1 or > 13)
        {
            throw new InvalidDataException("Party level is outside recovered ranks1..13; no implicit legacy-save normalization.");
        }

        if (threshold != Thresholds[level - 1])
        {
            throw new InvalidDataException("Party next-XP threshold does not match the recovered level table.");
        }

        if ((uint)state.Party[actor].Class >= 6)
        {
            throw new InvalidDataException("Unknown progression class.");
        }
    }

    internal static void ApplyAward(ExperienceAllocationSession session)
    {
        if (!session.Finished)
        {
            throw new InvalidOperationException("Actual XP allocation confirmation is required.");
        }

        GameState party = session.Working;
        for (int i = 0; i < 6; i++)
        {
            if (party.GetMemberStatus(i) == 0)
            {
                continue;
            }

            int xp = unchecked(party.Party[i].Experience + session.ConsumeAllocation(i));
            int level = party.Party[i].Level;
            party.Party[i].Experience = xp;
            if (level < 13 && xp >= party.Party[i].NextExperience)
            {
                party.Party[i].NextExperience = Thresholds[level];
                party.Party[i].PendingAction = 1;
                party.Party[i].Level = ++level;
            }

            party.Party[i].MagicRank = (level - 1) / 2 + 1;
            party.Party[i].CombatForm = level / 3 + 1;
        }
    }

    // 403ec0: equipment capacity first, then per-pending actor403cc0 and full recalculation each time.
    internal static void FinalizeStats(GameState working, GameState rankEvidence, ItemCatalog items)
    {
        for (int i = 0; i < 6; i++)
        {
            int level = working.Party[i].Level, type = working.Party[i].Class;
            if ((uint)type < 6)
            {
                working.Party[i].EquipmentCapacity = InitialGameState.EquipmentCapacity(type, level);
            }
        }

        for (int i = 0; i < 6; i++)
        {
            if (working.Party[i].PendingAction == 0)
            {
                continue;
            }

            ValidateRecord(working, i);
            int level = working.Party[i].Level;
            if (level == 13)
            {
                bool itemsComplete = true, questsComplete = true;
                foreach (int id in RankQuestItemIds)
                {
                    if (rankEvidence.GetQuestItemFlag(id) == 0)
                    {
                        itemsComplete = false;
                        break;
                    }
                }

                for (int id = 20; id < 40; id++)
                {
                    if (rankEvidence.EventStatuses[id] != 4)
                    {
                        questsComplete = false;
                        break;
                    }
                }

                if (itemsComplete)
                {
                    Bonus(i, 2, 2, 1);
                }

                if (questsComplete)
                {
                    Bonus(i, itemsComplete ? 4 : 2, itemsComplete ? 5 : 2, itemsComplete ? 2 : 1);
                }
            }
            else
            {
                ProgressionCatalog.Apply(working.Party[i], level);
            }

            PartyDerivedStats.Recalculate(working, items);
            working.Party[i].PendingAction = 0;
        }

        void Bonus(int actor, int attribute, int extra, int rank)
        {
            for (int index = 0; index < PartyMember.AttributeCount; index++)
            {
                working.Party[actor].AddAttribute(index, attribute);
            }

            working.Party[actor].Leadership = unchecked(working.Party[actor].Leadership + extra);
            working.Party[actor].AchievementRank = rank;
        }
    }
}
