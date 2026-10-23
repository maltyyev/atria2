using System.Buffers.Binary;
using Atria.Render.Codecs;

namespace Atria.Game.Combat;

/// <summary>Isolated arithmetic stages406ee9..406f64 and4072a9..4072e1; not the full hit handler.</summary>
public static class CombatAttackDamageMath
{
    // firstRoll is the actual saved406e8e rand result; native visual/status effects run around these stages.
    public static int BaseAmount(ReadOnlySpan<byte> attacker, int attackerType, AnimationFrame attackFrame,
        ReadOnlySpan<byte> target, int firstRoll, Func<int> nextNativeRandom)
    {
        Validate(attacker, target, attackFrame); CheckRandom(firstRoll);
        int amount = unchecked(attackFrame.Words[8] - I(target, 0x2a0) - I(target, 0x1f0) + I(attacker, 0x2a4) + I(attacker, 0x1f4));
        if (amount < 1) amount = 1;
        //406f37 JGE uses a SIGNED low byte. Values128..255 also satisfy the less-than100 branch.
        if (attackerType == 2 && unchecked((sbyte)firstRoll) < 100 && target[0xfa] == attacker[0xfa])
        {
            int rank = I(attacker, 0x1ec);
            if (rank == 0) throw new InvalidDataException("Native class2 attack divisor is zero.");
            int roll = nextNativeRandom(); CheckRandom(roll);
            amount = unchecked(amount + roll % rank * I(attacker, 0x1f4));
        }
        return amount;
    }

    public static int CriticalAmount(ReadOnlySpan<byte> attacker, AnimationFrame attackFrame, int amount, Func<int> nextNativeRandom)
    {
        Validate(attacker, attacker, attackFrame);
        if (attackFrame.Words[23] <= 6) return amount;
        int roll = nextNativeRandom(); CheckRandom(roll);
        int odds = unchecked(I(attacker, 0x29c) + I(attacker, 0x1e0));
        return (roll & 255) < odds ? unchecked(((odds >> 4) + 1) * amount) : amount;
    }
    private static void Validate(ReadOnlySpan<byte> attacker, ReadOnlySpan<byte> target, AnimationFrame frame)
    {
        if (attacker.Length != CombatSceneDefinition.RuntimeRecordSize || target.Length != CombatSceneDefinition.RuntimeRecordSize || frame.Words.Length != 28)
            throw new ArgumentException("Incomplete native damage calculation records.");
    }
    private static void CheckRandom(int value)
    { if ((uint)value > 32767) throw new InvalidDataException("Expected native15-bit random sample."); }
    private static int I(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
}

