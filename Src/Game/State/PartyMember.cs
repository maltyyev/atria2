namespace Atria.Game.State;

public sealed class PartyMember
{
    public const int AttributeCount = 6;
    public const int EquipmentSlotCount = 6;
    public byte[] Name { get; } = new byte[30];
    public byte[] LegacyName { get; } = new byte[30];
    public int[] Equipment { get; } = new int[EquipmentSlotCount];
    internal byte[]? UnmappedLegacyData
    {
        get; set;
    }
    public int MemberStatus
    {
        get; set;
    }
    public int Class
    {
        get; set;
    }
    public int LegacyParameter68
    {
        get; set;
    }
    public int MaximumHealth
    {
        get; set;
    }
    public int Health
    {
        get; set;
    }
    public int MaximumMana
    {
        get; set;
    }
    public int Mana
    {
        get; set;
    }
    public int MagicRank
    {
        get; set;
    }
    public int RecoveryInterval
    {
        get; set;
    }
    public int LegacyParameter96
    {
        get; set;
    }
    public int Strength
    {
        get; set;
    }
    public int Intelligence
    {
        get; set;
    }
    public int Endurance
    {
        get; set;
    }
    public int Spirit
    {
        get; set;
    }
    public int Dexterity
    {
        get; set;
    }
    public int Luck
    {
        get; set;
    }
    public int Accuracy
    {
        get; set;
    }
    public int MagicAffinity
    {
        get; set;
    }
    public int MagicDefense
    {
        get; set;
    }
    public int ReservedDerivedStat
    {
        get; set;
    }
    public int Immunity
    {
        get; set;
    }
    public int PreferredAttribute1
    {
        get; set;
    }
    public int PreferredAttribute2
    {
        get; set;
    }
    public int PreferredAttribute3
    {
        get; set;
    }
    public int Leadership
    {
        get; set;
    }
    public int CriticalChance
    {
        get; set;
    }
    public int Experience
    {
        get; set;
    }
    public int NextExperience
    {
        get; set;
    }
    public int Level
    {
        get; set;
    }
    public int Defense
    {
        get; set;
    }
    public int Attack
    {
        get; set;
    }
    public int MagicPower
    {
        get; set;
    }
    public int LegacyParameter216
    {
        get; set;
    }
    public int LegacyParameter220
    {
        get; set;
    }
    public int CombatForm
    {
        get; set;
    }
    public int EquipmentCapacity
    {
        get; set;
    }
    public int PendingAction
    {
        get; set;
    }
    public int SelectedForBattle
    {
        get; set;
    }
    public int LegacyParameter264
    {
        get; set;
    }
    public int LegacyParameter268
    {
        get; set;
    }
    public int LegacyParameter272
    {
        get; set;
    }
    public int AchievementRank
    {
        get; set;
    }

    public int GetAttribute(int index) => index switch
    {
        0 => Strength,
        1 => Intelligence,
        2 => Endurance,
        3 => Spirit,
        4 => Dexterity,
        5 => Luck,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    public void AddAttribute(int index, int amount)
    {
        switch (index)
        {
            case 0:
                Strength = unchecked(Strength + amount);
                break;
            case 1:
                Intelligence = unchecked(Intelligence + amount);
                break;
            case 2:
                Endurance = unchecked(Endurance + amount);
                break;
            case 3:
                Spirit = unchecked(Spirit + amount);
                break;
            case 4:
                Dexterity = unchecked(Dexterity + amount);
                break;
            case 5:
                Luck = unchecked(Luck + amount);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}
