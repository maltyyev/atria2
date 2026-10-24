using Atria.Resources;

namespace Atria.Game.Combat;

internal readonly record struct CombatRegionGrowth(int Health, int Attack, int Defense);
internal sealed record CombatAttributeDefinition(int Health, int Attack, int Defense, int CriticalChance,
    int RecoveryInterval, int MagicRank, int AwardBonus, IReadOnlyList<CombatRegionGrowth> RegionGrowth);

internal static class CombatAttributeCatalog
{
    private static readonly CombatAttributeDefinition[] definitions = Load();
    public static int Count => definitions.Length;
    public static CombatAttributeDefinition Get(int type) => definitions[type];

    private static CombatAttributeDefinition[] Load()
    {
        using var data = GameData.Open("CombatAttributes");
        var result = new List<CombatAttributeDefinition>();
        foreach (var row in data.RootElement.GetProperty("Actors").EnumerateArray())
        {
            if (row.GetProperty("Type").GetInt32() != result.Count)
            {
                throw new InvalidDataException("Combat attribute types must be contiguous and ordered.");
            }

            int Value(string name) => row.GetProperty(name).GetInt32();
            var growth = row.GetProperty("RegionGrowth").EnumerateArray().Select(g => new CombatRegionGrowth(
                g.GetProperty("Health").GetInt32(), g.GetProperty("Attack").GetInt32(), g.GetProperty("Defense").GetInt32())).ToArray();
            result.Add(new(Value("Health"), Value("Attack"), Value("Defense"), Value("CriticalChance"),
                Value("RecoveryInterval"), Value("MagicRank"), Value("AwardBonus"), Array.AsReadOnly(growth)));
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException("Combat attribute catalog is empty.");
        }

        return result.ToArray();
    }
}
