namespace Atria.Game.Combat;

[Flags]
public enum CombatActionFlags
{
    Ready = 0,
    Moving = 1,
    CanChain = 0x20,
    Busy = 0x40,
    Guarding = 0x80,
    OffsetMotion = 0x100,
    Command = Busy | OffsetMotion,
    Launched = 0x200,
    Falling = 0x400,
    Dead = 0x8000
}

public enum CombatMotionKind
{
    Idle = 0,
    Walk = 1,
    Run = 2,
    Attack = 6
}

public enum CombatDirection
{
    None = 0,
    DownBack = 1,
    Down = 2,
    DownForward = 3,
    Back = 4,
    Forward = 6,
    UpBack = 7,
    Up = 8,
    UpForward = 9
}

public readonly record struct CombatActionState(CombatActionFlags Flags, CombatMotionKind Motion, int JumpStage,
    int Health, bool Active, int FrameInterval)
{
    public bool GroundedAndAlive => JumpStage == 0 && Health > 0 && Active;
    public bool CanStartGroundAttack => GroundedAndAlive && Flags is CombatActionFlags.Ready or CombatActionFlags.Moving;
}
