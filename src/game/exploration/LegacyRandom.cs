namespace Atria.Game.Exploration;

/// <summary>43e7a7/43e7b1: one shared original CRT state, explicit seed and call order.</summary>
public sealed partial class LegacyRandom
{
    public const uint ProcessInitialSeed = 1; // raw original DWORD4509b0
    public uint State { get; private set; }
    public ulong Calls { get; private set; }
    public LegacyRandom(uint seed) => State=seed;
    public void Seed(uint seed) { State=seed; Calls=0; }
    public int Next()
    {
        State=unchecked(State*214013u+2531011u); Calls++;
        return (int)((State>>16)&32767);
    }
}

