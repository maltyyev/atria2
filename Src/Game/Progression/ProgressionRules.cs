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
        int level = state.Heroes[actor].Level, threshold = state.Heroes[actor].NextExperience;
        if (level is < 1 or > 13)
        {
            throw new InvalidDataException("Party level is outside recovered ranks1..13; no implicit legacy-save normalization.");
        }

        if (threshold != Thresholds[level - 1])
        {
            throw new InvalidDataException("Party next-XP threshold does not match the recovered level table.");
        }

        if ((uint)state.Heroes[actor].Class >= ClassCatalog.Count)
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
        for (int i = 0; i < party.Heroes.Length; i++)
        {
            if (party.Heroes[i].MemberStatus == 0)
            {
                continue;
            }

            int xp = unchecked(party.Heroes[i].Experience + session.ConsumeAllocation(i));
            int level = party.Heroes[i].Level;
            party.Heroes[i].Experience = xp;
            if (level < 13 && xp >= party.Heroes[i].NextExperience)
            {
                party.Heroes[i].NextExperience = Thresholds[level];
                party.Heroes[i].PendingAction = 1;
                party.Heroes[i].Level = ++level;
            }

            party.Heroes[i].MagicRank = (level - 1) / 2 + 1;
            party.Heroes[i].CombatForm = level / 3 + 1;
        }
    }

    // 403ec0: equipment capacity first, then per-pending actor403cc0 and full recalculation each time.
    internal static void FinalizeStats(GameState working, GameState rankEvidence, ItemCatalog items)
    {
        for (int i = 0; i < working.Heroes.Length; i++)
        {
            int level = working.Heroes[i].Level, type = working.Heroes[i].Class;
            if ((uint)type < ClassCatalog.Count)
            {
                working.Heroes[i].EquipmentCapacity = InitialGameState.EquipmentCapacity(type, level);
            }
        }

        for (int i = 0; i < working.Heroes.Length; i++)
        {
            if (working.Heroes[i].PendingAction == 0)
            {
                continue;
            }

            ValidateRecord(working, i);
            int level = working.Heroes[i].Level;
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
                ProgressionCatalog.Apply(working.Heroes[i], level);
            }

            PartyDerivedStats.Recalculate(working, items);
            working.Heroes[i].PendingAction = 0;
        }

        void Bonus(int actor, int attribute, int extra, int rank)
        {
            for (int index = 0; index < PartyMember.AttributeCount; index++)
            {
                working.Heroes[actor].AddAttribute(index, attribute);
            }

            working.Heroes[actor].Leadership = unchecked(working.Heroes[actor].Leadership + extra);
            working.Heroes[actor].AchievementRank = rank;
        }
    }
}
