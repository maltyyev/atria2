using System.Numerics;

namespace Atria.Game.Progression;

public enum X87Precision
{
    Bits53 = 53,
    Bits64 = 64
}

/// <summary>4248f6/424948: round division and multiplication to native x87 precision, then truncate toward zero.</summary>
public static class ExperienceBarMath
{
    public static int Ratio(int signed155, int threshold, int multiplier, X87Precision precision)
    {
        int bits = (int)precision;
        if (bits is not (53 or 64) || signed155 is not (155 or -155) || threshold < 1000)
        {
            throw new ArgumentException("Invalid native XP bar arithmetic parameters.");
        }

        if (multiplier == 0)
        {
            return 0;
        }

        UInt128 numerator = 155, divisor = (uint)threshold;
        int logarithm = 7 - BitOperations.Log2((uint)threshold);
        if ((numerator << -logarithm) < divisor)
        {
            logarithm--;
        }

        int exponent = logarithm - (bits - 1);
        UInt128 scaled = numerator << -exponent;
        UInt128 mantissa = Round(scaled, divisor);
        if (mantissa == (UInt128)1 << bits)
        {
            mantissa >>= 1;
            exponent++;
        }

        long absolute = multiplier < 0 ? -(long)multiplier : multiplier;
        UInt128 product = mantissa * (ulong)absolute;
        int length = product >> 64 == 0 ? BitOperations.Log2((ulong)product) + 1 : BitOperations.Log2((ulong)(product >> 64)) + 65;
        int drop = Math.Max(0, length - bits);
        mantissa = drop == 0 ? product : Round(product, (UInt128)1 << drop);
        exponent += drop;
        if (mantissa == (UInt128)1 << bits)
        {
            mantissa >>= 1;
            exponent++;
        }

        UInt128 integer = exponent >= 0 ? mantissa << exponent : mantissa >> -exponent;
        long result = checked((long)integer);
        if ((signed155 < 0) != (multiplier < 0))
        {
            result = -result;
        }

        return checked((int)result);
    }

    private static UInt128 Round(UInt128 numerator, UInt128 denominator)
    {
        UInt128 quotient = numerator / denominator, remainder = numerator % denominator;
        UInt128 twice = remainder * 2;
        if (twice > denominator || (twice == denominator && (quotient & 1) != 0))
        {
            quotient++;
        }

        return quotient;
    }
}
