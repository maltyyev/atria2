namespace Atria.Resources;

/// <summary>ATITEM modifier identifiers. Gaps are intentionally unsupported retired effects.</summary>
public enum ItemModifierKind
{
    Strength = 0,
    Intelligence = 1,
    Endurance = 2,
    Spirit = 3,
    Dexterity = 4,
    Luck = 5,
    Health = 6,
    Mana = 7,
    CriticalChance = 8,
    Accuracy = 9,
    MagicAffinity = 10,
    MagicDefense = 11,
    ReservedDerivedStat = 13,
    Immunity = 14,
    Defense = 15,
    Attack = 16,
    AllAttributes = 17,
    MaximumHealth = 18,
    MaximumMana = 19,
    MagicRank = 20,
    RecoveryInterval = 21,
    LegacyParameter96 = 22
}

public readonly record struct ItemModifier(ItemModifierKind Kind, int Amount);
