using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.Progression;

internal static class ProgressionCatalog
{
    private enum GrowthStat
    {
        Strength, Intelligence, Endurance, Spirit, Dexterity, Luck, Immunity, Leadership
    }
    private readonly record struct Gain(GrowthStat Stat, int Amount);
    private static readonly Gain[][][] classes;
    private static readonly int[] thresholds;
    private static readonly int[] rankQuestItems;
    public static ReadOnlySpan<int> Thresholds => thresholds;
    public static ReadOnlySpan<int> RankQuestItems => rankQuestItems;

    static ProgressionCatalog()
    {
        using var document = GameData.Open("Progression");
        var root = document.RootElement;
        thresholds = root.GetProperty("Thresholds").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        rankQuestItems = root.GetProperty("RankQuestItems").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        var rows = root.GetProperty("Classes");
        classes = new Gain[rows.GetArrayLength()][][];
        int index = 0;
        foreach (var row in rows.EnumerateArray())
        {
            if (row.GetProperty("Class").GetInt32() != index)
            {
                throw new InvalidDataException("Unordered progression classes.");
            }

            var levels = row.GetProperty("Levels");
            if (levels.GetArrayLength() != thresholds.Length)
            {
                throw new InvalidDataException("Inconsistent progression levels.");
            }

            classes[index++] = levels.EnumerateArray().Select(level => level.EnumerateObject().Select(property =>
            {
                if (!Enum.TryParse<GrowthStat>(property.Name, out var stat) || !Enum.IsDefined(stat))
                {
                    throw new InvalidDataException($"Unknown progression stat {property.Name}.");
                }

                return new Gain(stat, property.Value.GetInt32());
            }).ToArray()).ToArray();
        }
    }

    public static void Apply(PartyMember member, int newLevel)
    {
        if ((uint)member.Class >= classes.Length || (uint)newLevel >= classes[member.Class].Length)
        {
            throw new InvalidDataException("Unknown class or level in progression data.");
        }

        foreach (var gain in classes[member.Class][newLevel])
        {
            switch (gain.Stat)
            {
                case GrowthStat.Immunity:
                    member.Immunity = unchecked(member.Immunity + gain.Amount);
                    break;
                case GrowthStat.Leadership:
                    member.Leadership = unchecked(member.Leadership + gain.Amount);
                    break;
                default:
                    member.AddAttribute((int)gain.Stat, gain.Amount);
                    break;
            }
        }
    }
}
