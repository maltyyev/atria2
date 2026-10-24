using Atria.Resources;

namespace Atria.Game.State;

/// <summary>0x402860 and0x4029c0. Recalculation resets active current HP/MP as the original does.</summary>
public static class PartyDerivedStats
{
    private const int VitalityLevelOffset = 10;
    private const int VitalityAttributeScale = 10;
    private const int WeaponAccuracyPerLevel = 5;
    private const int MaximumWeaponAccuracy = 120;
    private const int PercentageScale = 100;
    private const int PercentageRounding = PercentageScale / 2;
    private const int FirstArmorItem = 25;
    private const int LastArmorItem = 47;
    public static void Recalculate(GameState state, ItemCatalog items)
    {
        for (int actor = 0; actor < 6; actor++)
        {
            if (state.GetMemberStatus(actor) == 0)
            {
                continue;
            }

            var member = state.Party[actor];
            int classId = member.Class;
            var definition = ClassCatalog.Get(classId);
            int capacity = member.EquipmentCapacity;
            if ((uint)capacity > 6)
            {
                throw new InvalidDataException("Equipment capacity exceeds six slots.");
            }

            int Modifier(ItemModifierKind kind)
            {
                int value = 0;
                for (int slot = 0; slot < capacity; slot++)
                {
                    int id = member.Equipment[slot];
                    if (id == -1)
                    {
                        continue;
                    }

                    var item = items.GetItem(id);
                    int count = item.ModifierCount;
                    if ((uint)count > 3)
                    {
                        throw new InvalidDataException("Item modifier count exceeds its original three fields.");
                    }

                    for (int j = 0; j < count; j++)
                    {
                        if (item.GetModifier(j).Kind == kind)
                        {
                            value = unchecked(value + item.GetModifier(j).Amount);
                        }
                    }
                }

                return value;
            }

            unchecked
            {
                member.Accuracy = (member.Strength + member.Luck + member.Dexterity) / 3 + definition.AccuracyBonus + Modifier(ItemModifierKind.Accuracy);
                member.MagicAffinity = member.Intelligence + definition.MagicAffinityBonus + Modifier(ItemModifierKind.MagicAffinity);
                member.MagicDefense = (member.Spirit + member.Intelligence + member.Luck) / 3 + definition.MagicDefenseBonus + Modifier(ItemModifierKind.MagicDefense);
                member.ReservedDerivedStat = 0;
                member.Immunity = (member.Endurance + member.Intelligence + member.Luck) / 3 + definition.ImmunityBonus + Modifier(ItemModifierKind.Immunity);
                int hp = definition.HealthPerLevel * (member.Level + VitalityLevelOffset) + member.Endurance * VitalityAttributeScale + Modifier(ItemModifierKind.MaximumHealth);
                int mp = definition.ManaPerLevel * (member.Level + VitalityLevelOffset) + (definition.UsesSpiritForMana ? member.Spirit : member.Intelligence) * VitalityAttributeScale + Modifier(ItemModifierKind.MaximumMana);
                member.MaximumHealth = hp;
                member.Health = hp;
                member.MaximumMana = mp;
                member.Mana = mp;
                int attack = definition.UnarmedAttack;
                int weapon = member.Equipment[0];
                if (weapon != -1)
                {
                    int chance = Math.Min(items.GetItem(weapon).Accuracy + member.Level * WeaponAccuracyPerLevel + member.Accuracy, MaximumWeaponAccuracy);
                    attack += (items.GetItem(weapon).Power * chance + PercentageRounding) / PercentageScale;
                }

                member.Attack = attack;
                int armor = definition.UnarmoredDefense;
                for (int slot = 0; slot < 6; slot++)
                {
                    int id = member.Equipment[slot];
                    if (id == -1)
                    {
                        continue;
                    }

                    if (id >= FirstArmorItem && id <= LastArmorItem)
                    {
                        armor += items.GetItem(id).Power;
                    }

                    for (int j = 0; j < ItemRecord.ModifierCapacity; j++)
                    {
                        if (items.GetItem(id).GetModifier(j).Kind == ItemModifierKind.Defense)
                        {
                            armor += items.GetItem(id).GetModifier(j).Amount;
                        }
                    }
                }

                member.Defense = armor;
                member.MagicPower = member.MagicAffinity + member.Intelligence;
            }
        }
    }
}
