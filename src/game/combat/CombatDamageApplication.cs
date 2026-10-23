using System.Buffers.Binary;

namespace Atria.Game.Combat;

public readonly record struct CombatDeathReaction(int Field0, int Field1, int Field2, int Field3, sbyte AttackerFacing);
public readonly record struct CombatDamageResult(int AppliedAmount, bool Lethal);

/// <summary>406b40/406cc0 HP and recovery-field application only; attack calculation/reactions remain separate.</summary>
public static class CombatDamageApplication
{
    public static CombatDamageResult Apply(byte[] attacker, int attackerType, byte[] target, int targetType, int incomingAmount,
        Action<CombatDeathReaction> applyActualDeathReaction)
    {
        if (attacker.Length != CombatSceneDefinition.RuntimeRecordSize || target.Length != CombatSceneDefinition.RuntimeRecordSize)
            throw new ArgumentException("Incomplete combat damage actor records.");
        ArgumentNullException.ThrowIfNull(applyActualDeathReaction);
        if (Read(target, 4) < Read(target, 0x170)) Write(target, 4, Read(target, 0x170));
        int amount = ScaleByControlRank(attacker, attackerType, incomingAmount);
        amount = ScaleByControlRank(target, targetType, amount);
        int hp = unchecked(Read(target, 0x170) - amount);
        Write(target, 0x170, hp);
        if (hp < 1)
        {
            Write(target, 0x170, 0); Write(target, 8, 0);
            applyActualDeathReaction(new(15, 15, 6, 4, unchecked((sbyte)attacker[0xfa])));
            return new(amount, true);
        }
        unchecked
        {
            int recovery = Read(target, 8);
            recovery = recovery == 0 ? amount >> 1 : recovery + (amount >> 1) - (amount >> 2);
            if (HasEquipment(target, 40)) recovery += amount / 10;
            if (HasEquipment(target, 41)) recovery += amount / 5;
            if (HasEquipment(target, 42)) recovery += amount / 3;
            Write(target, 8, recovery);
            int timer = Read(target, 0x180);
            if (HasEquipment(target, 58)) timer -= 300;
            else if (HasEquipment(target, 59)) timer -= 200;
            else if (HasEquipment(target, 60)) timer -= 100;
            Write(target, 0x10, timer < 0 ? 50 : timer);
        }
        return new(amount, false);
    }

    public static int ScaleByControlRank(ReadOnlySpan<byte> actor, int type, int amount)
    {
        if (actor.Length != CombatSceneDefinition.RuntimeRecordSize) throw new ArgumentException("Incomplete combat actor record.");
        if (type >= 8 || actor[0xa4] != 0) return amount;
        return Read(actor, 0x22c) switch
        {
            1 => unchecked(amount * 10) / 15,
            2 => amount >> 1,
            3 => amount >> 2,
            4 => 0,
            _ => amount
        };
    }
    private static bool HasEquipment(ReadOnlySpan<byte> actor, int id)
    { for (int i = 0; i < 6; i++) if (Read(actor, 0x208 + i * 4) == id) return true; return false; }
    private static int Read(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
    private static void Write(Span<byte> bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes[offset..], value);
}
