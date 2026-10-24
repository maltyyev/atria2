namespace Atria.Game.Combat;

public readonly record struct CombatMotion(int Animation, int StepX, int StepY, int FrameInterval, int MovementInterval);
public readonly record struct CombatJump(int Animation, int Parameter, int Flags, int FrameInterval, int MovementInterval);
public readonly record struct CombatAttack(int Animation, int RequiredFlags, int ResultFlags, int ChainWindow, int Direction, int FrameInterval);
public readonly record struct CombatReaction(int Animation, int Flags, int Parameter);
public readonly record struct CombatCommand(int Animation, int ManaCost, int Icon, int Category, int Parameter);

/// <summary>Character actions decoded from INF. Shared per binding; attack delay is reduced once per spawned enemy.</summary>
public sealed record CombatInfoCharacter
{
    public required ReadOnlyMemory<byte> AnimationResource
    {
        get; init;
    }
    public int AnimationOrdinal
    {
        get; init;
    }
    public int ReactionStartFrame
    {
        get; init;
    }
    public int Type
    {
        get; set;
    }
    public int HeaderParameter
    {
        get; init;
    }
    public int GuardFailureThreshold
    {
        get; init;
    }
    public int AttackDelay
    {
        get; set;
    }
    public int TrailingHeaderParameter
    {
        get; init;
    }
    public required IReadOnlyList<CombatMotion> Motions
    {
        get; init;
    }
    public required IReadOnlyList<CombatJump> Jumps
    {
        get; init;
    }
    public required IReadOnlyList<CombatAttack> Attacks
    {
        get; init;
    }
    public required IReadOnlyList<CombatReaction> Reactions
    {
        get; init;
    }
    public required IReadOnlyList<CombatCommand> Commands
    {
        get; init;
    }
    public required IReadOnlyList<int> Sounds
    {
        get; init;
    }
    public required IReadOnlyList<int> CommandCounts
    {
        get; init;
    }
    public int BasicAttackCount
    {
        get; init;
    }
    public int FirstVariation
    {
        get; init;
    }
    public int VariationEnd
    {
        get; init;
    }
    public int DeclaredSoundCount
    {
        get; init;
    }

    public int HitSound => Sounds[0];
    public int KnockdownSound => Sounds[1];
    public int DeathSound => Sounds[2];
    public int CommandSound => Sounds[4];
    public CombatAttack Guard => Attacks[FirstVariation];

    public static bool IsDirectional(int direction) => (CombatDirection)direction is
        CombatDirection.DownBack or CombatDirection.Down or CombatDirection.DownForward or CombatDirection.Back
        or CombatDirection.Forward or CombatDirection.UpBack or CombatDirection.Up or CombatDirection.UpForward;

    public IEnumerable<int> DirectionalVariations(int flags)
    {
        var directions = new HashSet<int>();
        for (int i = FirstVariation; i < VariationEnd; i++)
        {
            var attack = Attacks[i];
            // Dispatch uses the first row for a state/direction, even when the file contains duplicates.
            if (attack.RequiredFlags == flags && IsDirectional(attack.Direction) && directions.Add(attack.Direction))
            {
                yield return i;
            }
        }
    }
}
