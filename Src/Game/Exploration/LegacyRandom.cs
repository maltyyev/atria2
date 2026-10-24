namespace Atria.Game.Exploration;

/// <summary>43e7a7/43e7b1: one shared original CRT state, explicit seed and call order.</summary>
public sealed partial class LegacyRandom
{
    private const uint Multiplier = 214013;
    private const uint Increment = 2531011;
    private const int OutputShift = 16;
    public const int MaximumSample = short.MaxValue;

    public static void ValidateSample(int sample)
    {
        if ((uint)sample > MaximumSample)
        {
            throw new InvalidDataException("Expected a nonnegative 15-bit game RNG sample.");
        }
    }

    public const uint ProcessInitialSeed = 1; // raw original DWORD4509b0
    public uint State
    {
        get; private set;
    }
    public ulong Calls
    {
        get; private set;
    }

    public LegacyRandom(uint seed) => State = seed;
    public void Seed(uint seed)
    {
        State = seed;
        Calls = 0;
    }

    public int Next()
    {
        State = unchecked(State * Multiplier + Increment);
        Calls++;
        return (int)((State >> OutputShift) & MaximumSample);
    }
}
