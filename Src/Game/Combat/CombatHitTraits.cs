using Atria.Resources;

namespace Atria.Game.Combat;

internal sealed record CombatHitTraits(bool SlowsTarget, bool PoisonsTarget, bool AllowsStatusOnlyHit,
    bool IgnoresNormalReaction, bool Undead, bool MayResistReaction, bool VulnerableToSilver)
{
    private static readonly CombatHitTraits[] definitions = Load();
    private static readonly CombatHitTraits ordinary = new(false, false, false, false, false, false, false);
    public static CombatHitTraits For(int type) => (uint)type < definitions.Length ? definitions[type] : ordinary;

    private static CombatHitTraits[] Load()
    {
        using var document = GameData.Open("CombatHitTraits");
        var rows = document.RootElement.GetProperty("Actors");
        var result = new CombatHitTraits[rows.GetArrayLength()];
        int index = 0;
        foreach (var row in rows.EnumerateArray())
        {
            if (row.GetProperty("Type").GetInt32() != index)
            {
                throw new InvalidDataException("Unordered hit traits.");
            }

            bool Read(string name) => row.GetProperty(name).GetBoolean();
            result[index++] = new(Read("SlowsTarget"), Read("PoisonsTarget"), Read("AllowsStatusOnlyHit"), Read("IgnoresNormalReaction"),
                Read("Undead"), Read("MayResistReaction"), Read("VulnerableToSilver"));
        }
        return result;
    }
}
