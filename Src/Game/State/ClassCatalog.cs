using Atria.Resources;

namespace Atria.Game.State;

public sealed record ClassDefinition(
    int Id, string Name, int AccuracyBonus, int MagicAffinityBonus, int MagicDefenseBonus, int ImmunityBonus,
    int HealthPerLevel, int ManaPerLevel, bool UsesSpiritForMana, int UnarmedAttack, int UnarmoredDefense,
    IReadOnlyList<int> EquipmentCategories, IReadOnlyList<EquipmentCapacityStep> EquipmentCapacity)
{
    public int CapacityAt(int level)
    {
        int capacity = EquipmentCapacity[0].Slots;
        foreach (var step in EquipmentCapacity)
        {
            if (level < step.Level)
            {
                break;
            }

            capacity = step.Slots;
        }
        return capacity;
    }
}

public readonly record struct EquipmentCapacityStep(int Level, int Slots);

public static class ClassCatalog
{
    private static readonly ClassDefinition[] definitions = Load();
    public static int Count => definitions.Length;
    public static ClassDefinition Get(int id) => (uint)id < definitions.Length ? definitions[id] : throw new InvalidDataException("Unknown party class.");

    private static ClassDefinition[] Load()
    {
        using var document = GameData.Open("Classes");
        var rows = document.RootElement.GetProperty("Classes");
        var result = new ClassDefinition[rows.GetArrayLength()];
        int index = 0;
        foreach (var row in rows.EnumerateArray())
        {
            int Read(string name) => row.GetProperty(name).GetInt32();
            int[] categories = row.GetProperty("EquipmentCategories").EnumerateArray().Select(value => value.GetInt32()).ToArray();
            var capacities = row.GetProperty("EquipmentCapacity").EnumerateArray()
                .Select(value => new EquipmentCapacityStep(value.GetProperty("Level").GetInt32(), value.GetProperty("Slots").GetInt32())).ToArray();
            if (Read("Id") != index || categories.Length != PartyMember.EquipmentSlotCount || capacities.Length == 0)
            {
                throw new InvalidDataException("Invalid class definition.");
            }

            for (int i = 0; i < capacities.Length; i++)
            {
                if ((uint)capacities[i].Slots > PartyMember.EquipmentSlotCount || i > 0 && capacities[i].Level <= capacities[i - 1].Level)
                {
                    throw new InvalidDataException("Invalid equipment capacity progression.");
                }
            }
            result[index++] = new(Read("Id"), row.GetProperty("Name").GetString()!, Read("AccuracyBonus"), Read("MagicAffinityBonus"),
                Read("MagicDefenseBonus"), Read("ImmunityBonus"), Read("HealthPerLevel"), Read("ManaPerLevel"), row.GetProperty("UsesSpiritForMana").GetBoolean(),
                Read("UnarmedAttack"), Read("UnarmoredDefense"), Array.AsReadOnly(categories), Array.AsReadOnly(capacities));
        }
        return result;
    }
}
