using Atria.Resources;

namespace Atria.Game.State;

public static class EquipmentEffects
{
    public static void Apply(PartyMember member, ItemRecord item, bool removing)
    {
        for (int i = 0; i < item.ModifierCount; i++)
        {
            var modifier = item.GetModifier(i);
            int amount = removing ? unchecked(-modifier.Amount) : modifier.Amount;
            if ((uint)modifier.Kind < PartyMember.AttributeCount)
            {
                member.AddAttribute((int)modifier.Kind, amount);
                continue;
            }

            unchecked
            {
                switch (modifier.Kind)
                {
                    case ItemModifierKind.Health:
                        member.Health += amount;
                        break;
                    case ItemModifierKind.Mana:
                        member.Mana += amount;
                        break;
                    case ItemModifierKind.CriticalChance:
                        member.CriticalChance += amount;
                        break;
                    case ItemModifierKind.Accuracy:
                        member.Accuracy += amount;
                        break;
                    case ItemModifierKind.MagicAffinity:
                        member.MagicAffinity += amount;
                        break;
                    case ItemModifierKind.MagicDefense:
                        member.MagicDefense += amount;
                        break;
                    case ItemModifierKind.ReservedDerivedStat:
                        member.ReservedDerivedStat += amount;
                        break;
                    case ItemModifierKind.Immunity:
                        member.Immunity += amount;
                        break;
                    case ItemModifierKind.Defense:
                        member.Defense += amount;
                        break;
                    case ItemModifierKind.Attack:
                        member.Attack += amount;
                        break;
                    case ItemModifierKind.AllAttributes:
                        for (int attribute = 0; attribute < PartyMember.AttributeCount; attribute++)
                        {
                            member.AddAttribute(attribute, amount);
                        }

                        break;
                    case ItemModifierKind.MaximumHealth:
                        member.MaximumHealth += amount;
                        // Equipping changes both values; removal changes only the maximum before recalculation.
                        if (!removing)
                        {
                            member.Health += amount;
                        }

                        break;
                    case ItemModifierKind.MaximumMana:
                        member.MaximumMana += amount;
                        if (!removing)
                        {
                            member.Mana += amount;
                        }

                        break;
                    case ItemModifierKind.MagicRank:
                        member.MagicRank += amount;
                        break;
                    case ItemModifierKind.RecoveryInterval:
                        member.RecoveryInterval += amount;
                        break;
                    case ItemModifierKind.LegacyParameter96:
                        member.LegacyParameter96 += amount;
                        break;
                }
            }
        }
    }
}
