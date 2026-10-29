using Atria.Game.Progression;

namespace Atria.Game.State;

internal static class HeroRecruitment
{
    public static void Join(GameState state, string id)
    {
        int index = state.HeroCatalog.Find(id);
        if (index < 0)
        {
            throw new InvalidDataException($"Unknown recruit: {id}");
        }

        var member = state.Heroes[index];
        if (member.MemberStatus != 0)
        {
            return;
        }

        int[] levels = state.Heroes.Where(hero => hero.MemberStatus != 0).Select(hero => hero.Level).Order().ToArray();
        int level = levels.Length == 0 ? 1 : (levels[(levels.Length - 1) / 2] + levels[levels.Length / 2]) / 2;
        for (int rank = member.Level + 1; rank <= level && rank < ProgressionRules.Thresholds.Length; rank++)
        {
            ProgressionCatalog.Apply(member, rank);
        }

        member.Level = level;
        member.Experience = level == 1 ? 0 : ProgressionRules.Thresholds[level - 2];
        member.NextExperience = ProgressionRules.Thresholds[level - 1];
        member.MagicRank = (level - 1) / 2 + 1;
        member.CombatForm = level / 3 + 1;
        member.SelectedForBattle = 1;
        state.NewCharacterId = state.HeroCatalog[index].StorageIndex;
        state.NewCharacterPending = 1;
        // Mark immediately: replayed recruitment must never grant equipment twice.
        member.MemberStatus = 1;
    }
}
