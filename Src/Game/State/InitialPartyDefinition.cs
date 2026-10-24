using Atria.Resources;

namespace Atria.Game.State;

internal static class InitialPartyDefinition
{
    public static void Apply(IReadOnlyList<PartyMember> party)
    {
        using var document = GameData.Open("InitialParty");
        var members = document.RootElement.GetProperty("Members");
        if (members.GetArrayLength() > party.Count)
        {
            throw new InvalidDataException("Initial party exceeds storage.");
        }

        int index = 0;
        foreach (var row in members.EnumerateArray())
        {
            var member = party[index++];
            foreach (var property in row.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "MemberStatus":
                        member.MemberStatus = property.Value.GetInt32();
                        break;
                    case "Class":
                        member.Class = property.Value.GetInt32();
                        break;
                    case "LegacyParameter68":
                        member.LegacyParameter68 = property.Value.GetInt32();
                        break;
                    case "MaximumHealth":
                        member.MaximumHealth = property.Value.GetInt32();
                        break;
                    case "Health":
                        member.Health = property.Value.GetInt32();
                        break;
                    case "MaximumMana":
                        member.MaximumMana = property.Value.GetInt32();
                        break;
                    case "Mana":
                        member.Mana = property.Value.GetInt32();
                        break;
                    case "MagicRank":
                        member.MagicRank = property.Value.GetInt32();
                        break;
                    case "RecoveryInterval":
                        member.RecoveryInterval = property.Value.GetInt32();
                        break;
                    case "LegacyParameter96":
                        member.LegacyParameter96 = property.Value.GetInt32();
                        break;
                    case "Strength":
                        member.Strength = property.Value.GetInt32();
                        break;
                    case "Intelligence":
                        member.Intelligence = property.Value.GetInt32();
                        break;
                    case "Endurance":
                        member.Endurance = property.Value.GetInt32();
                        break;
                    case "Spirit":
                        member.Spirit = property.Value.GetInt32();
                        break;
                    case "Dexterity":
                        member.Dexterity = property.Value.GetInt32();
                        break;
                    case "Luck":
                        member.Luck = property.Value.GetInt32();
                        break;
                    case "Accuracy":
                        member.Accuracy = property.Value.GetInt32();
                        break;
                    case "MagicAffinity":
                        member.MagicAffinity = property.Value.GetInt32();
                        break;
                    case "MagicDefense":
                        member.MagicDefense = property.Value.GetInt32();
                        break;
                    case "ReservedDerivedStat":
                        member.ReservedDerivedStat = property.Value.GetInt32();
                        break;
                    case "Immunity":
                        member.Immunity = property.Value.GetInt32();
                        break;
                    case "PreferredAttribute1":
                        member.PreferredAttribute1 = property.Value.GetInt32();
                        break;
                    case "PreferredAttribute2":
                        member.PreferredAttribute2 = property.Value.GetInt32();
                        break;
                    case "PreferredAttribute3":
                        member.PreferredAttribute3 = property.Value.GetInt32();
                        break;
                    case "Leadership":
                        member.Leadership = property.Value.GetInt32();
                        break;
                    case "CriticalChance":
                        member.CriticalChance = property.Value.GetInt32();
                        break;
                    case "Experience":
                        member.Experience = property.Value.GetInt32();
                        break;
                    case "NextExperience":
                        member.NextExperience = property.Value.GetInt32();
                        break;
                    case "Level":
                        member.Level = property.Value.GetInt32();
                        break;
                    case "Defense":
                        member.Defense = property.Value.GetInt32();
                        break;
                    case "Attack":
                        member.Attack = property.Value.GetInt32();
                        break;
                    case "MagicPower":
                        member.MagicPower = property.Value.GetInt32();
                        break;
                    case "LegacyParameter216":
                        member.LegacyParameter216 = property.Value.GetInt32();
                        break;
                    case "LegacyParameter220":
                        member.LegacyParameter220 = property.Value.GetInt32();
                        break;
                    case "CombatForm":
                        member.CombatForm = property.Value.GetInt32();
                        break;
                    case "EquipmentCapacity":
                        member.EquipmentCapacity = property.Value.GetInt32();
                        break;
                    case "PendingAction":
                        member.PendingAction = property.Value.GetInt32();
                        break;
                    case "SelectedForBattle":
                        member.SelectedForBattle = property.Value.GetInt32();
                        break;
                    case "LegacyParameter264":
                        member.LegacyParameter264 = property.Value.GetInt32();
                        break;
                    case "LegacyParameter268":
                        member.LegacyParameter268 = property.Value.GetInt32();
                        break;
                    case "LegacyParameter272":
                        member.LegacyParameter272 = property.Value.GetInt32();
                        break;
                    case "AchievementRank":
                        member.AchievementRank = property.Value.GetInt32();
                        break;
                    case "Equipment":
                        if (property.Value.GetArrayLength() != member.Equipment.Length)
                        {
                            throw new InvalidDataException("Invalid initial equipment.");
                        }

                        int slot = 0;
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            member.Equipment[slot++] = item.GetInt32();
                        }

                        break;
                    default:
                        throw new InvalidDataException($"Unknown initial party field {property.Name}.");
                }
            }
        }
    }
}
