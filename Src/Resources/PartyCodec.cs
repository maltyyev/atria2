using System.Buffers.Binary;
using Atria.Game.State;

namespace Atria.Resources;

/// <summary>Original party save layout. Unknown bytes survive round trips without entering gameplay calculations.</summary>
public static class PartyCodec
{
    public const int RecordSize = 280;
    public static byte[] Encode(IReadOnlyList<PartyMember> party)
    {
        var bytes = new byte[checked(party.Count * RecordSize)];
        for (int i = 0; i < party.Count; i++)
        {
            EncodeMember(party[i]).CopyTo(bytes, i * RecordSize);
        }

        return bytes;
    }

    public static void Decode(ReadOnlySpan<byte> bytes, IReadOnlyList<PartyMember> party)
    {
        if (bytes.Length != checked(party.Count * RecordSize))
        {
            throw new InvalidDataException("Invalid party image size.");
        }

        for (int i = 0; i < party.Count; i++)
        {
            DecodeMember(bytes.Slice(i * RecordSize, RecordSize), party[i]);
        }
    }

    public static void Copy(IReadOnlyList<PartyMember> source, IReadOnlyList<PartyMember> target) => Decode(Encode(source), target);

    public static void DecodeMember(ReadOnlySpan<byte> bytes, PartyMember member)
    {
        if (bytes.Length != RecordSize)
        {
            throw new InvalidDataException("Invalid party record size.");
        }

        member.UnmappedLegacyData = bytes.ToArray();
        bytes[..30].CopyTo(member.Name);
        bytes.Slice(30, 30).CopyTo(member.LegacyName);
        member.MemberStatus = BinaryPrimitives.ReadInt32LittleEndian(bytes[60..]);
        member.Class = BinaryPrimitives.ReadInt32LittleEndian(bytes[64..]);
        member.LegacyParameter68 = BinaryPrimitives.ReadInt32LittleEndian(bytes[68..]);
        member.MaximumHealth = BinaryPrimitives.ReadInt32LittleEndian(bytes[72..]);
        member.Health = BinaryPrimitives.ReadInt32LittleEndian(bytes[76..]);
        member.MaximumMana = BinaryPrimitives.ReadInt32LittleEndian(bytes[80..]);
        member.Mana = BinaryPrimitives.ReadInt32LittleEndian(bytes[84..]);
        member.MagicRank = BinaryPrimitives.ReadInt32LittleEndian(bytes[88..]);
        member.RecoveryInterval = BinaryPrimitives.ReadInt32LittleEndian(bytes[92..]);
        member.LegacyParameter96 = BinaryPrimitives.ReadInt32LittleEndian(bytes[96..]);
        member.Strength = BinaryPrimitives.ReadInt32LittleEndian(bytes[100..]);
        member.Intelligence = BinaryPrimitives.ReadInt32LittleEndian(bytes[104..]);
        member.Endurance = BinaryPrimitives.ReadInt32LittleEndian(bytes[108..]);
        member.Spirit = BinaryPrimitives.ReadInt32LittleEndian(bytes[112..]);
        member.Dexterity = BinaryPrimitives.ReadInt32LittleEndian(bytes[116..]);
        member.Luck = BinaryPrimitives.ReadInt32LittleEndian(bytes[120..]);
        member.Accuracy = BinaryPrimitives.ReadInt32LittleEndian(bytes[148..]);
        member.MagicAffinity = BinaryPrimitives.ReadInt32LittleEndian(bytes[152..]);
        member.MagicDefense = BinaryPrimitives.ReadInt32LittleEndian(bytes[156..]);
        member.ReservedDerivedStat = BinaryPrimitives.ReadInt32LittleEndian(bytes[164..]);
        member.Immunity = BinaryPrimitives.ReadInt32LittleEndian(bytes[168..]);
        member.PreferredAttribute1 = BinaryPrimitives.ReadInt32LittleEndian(bytes[172..]);
        member.PreferredAttribute2 = BinaryPrimitives.ReadInt32LittleEndian(bytes[176..]);
        member.PreferredAttribute3 = BinaryPrimitives.ReadInt32LittleEndian(bytes[180..]);
        member.Leadership = BinaryPrimitives.ReadInt32LittleEndian(bytes[184..]);
        member.CriticalChance = BinaryPrimitives.ReadInt32LittleEndian(bytes[188..]);
        member.Experience = BinaryPrimitives.ReadInt32LittleEndian(bytes[192..]);
        member.NextExperience = BinaryPrimitives.ReadInt32LittleEndian(bytes[196..]);
        member.Level = BinaryPrimitives.ReadInt32LittleEndian(bytes[200..]);
        member.Defense = BinaryPrimitives.ReadInt32LittleEndian(bytes[204..]);
        member.Attack = BinaryPrimitives.ReadInt32LittleEndian(bytes[208..]);
        member.MagicPower = BinaryPrimitives.ReadInt32LittleEndian(bytes[212..]);
        member.LegacyParameter216 = BinaryPrimitives.ReadInt32LittleEndian(bytes[216..]);
        member.LegacyParameter220 = BinaryPrimitives.ReadInt32LittleEndian(bytes[220..]);
        member.CombatForm = BinaryPrimitives.ReadInt32LittleEndian(bytes[224..]);
        member.EquipmentCapacity = BinaryPrimitives.ReadInt32LittleEndian(bytes[252..]);
        member.PendingAction = BinaryPrimitives.ReadInt32LittleEndian(bytes[256..]);
        member.SelectedForBattle = BinaryPrimitives.ReadInt32LittleEndian(bytes[260..]);
        member.LegacyParameter264 = BinaryPrimitives.ReadInt32LittleEndian(bytes[264..]);
        member.LegacyParameter268 = BinaryPrimitives.ReadInt32LittleEndian(bytes[268..]);
        member.LegacyParameter272 = BinaryPrimitives.ReadInt32LittleEndian(bytes[272..]);
        member.AchievementRank = BinaryPrimitives.ReadInt32LittleEndian(bytes[276..]);
        for (int i = 0; i < member.Equipment.Length; i++)
        {
            member.Equipment[i] = BinaryPrimitives.ReadInt32LittleEndian(bytes[(228 + i * sizeof(int))..]);
        }
    }

    public static byte[] EncodeMember(PartyMember member)
    {
        var bytes = member.UnmappedLegacyData?.ToArray() ?? new byte[RecordSize];
        member.Name.CopyTo(bytes, 0);
        member.LegacyName.CopyTo(bytes, 30);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(60), member.MemberStatus);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(64), member.Class);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(68), member.LegacyParameter68);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(72), member.MaximumHealth);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(76), member.Health);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(80), member.MaximumMana);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(84), member.Mana);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(88), member.MagicRank);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(92), member.RecoveryInterval);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(96), member.LegacyParameter96);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(100), member.Strength);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(104), member.Intelligence);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(108), member.Endurance);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(112), member.Spirit);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(116), member.Dexterity);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(120), member.Luck);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(148), member.Accuracy);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(152), member.MagicAffinity);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(156), member.MagicDefense);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(164), member.ReservedDerivedStat);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(168), member.Immunity);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(172), member.PreferredAttribute1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(176), member.PreferredAttribute2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(180), member.PreferredAttribute3);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(184), member.Leadership);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(188), member.CriticalChance);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(192), member.Experience);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(196), member.NextExperience);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(200), member.Level);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(204), member.Defense);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(208), member.Attack);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(212), member.MagicPower);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(216), member.LegacyParameter216);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(220), member.LegacyParameter220);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(224), member.CombatForm);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(252), member.EquipmentCapacity);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(256), member.PendingAction);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(260), member.SelectedForBattle);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(264), member.LegacyParameter264);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(268), member.LegacyParameter268);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(272), member.LegacyParameter272);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(276), member.AchievementRank);
        for (int i = 0; i < member.Equipment.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(228 + i * sizeof(int)), member.Equipment[i]);
        }

        return bytes;
    }
}
