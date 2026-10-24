namespace Atria.Game.Combat;

/// <summary>Live combat state. Binary layout is confined to CombatActorCodec.</summary>
public sealed class CombatActorState
{
    public CombatActionState ActionState => new(
        (CombatActionFlags)ActionFlags, (CombatMotionKind)Motion, JumpStage, Health, Active != 0, FrameInterval);

    public CombatAttackStats AttackStats => new(
        Attack, AttackBonus, Defense, DefenseBonus, CriticalChance, CriticalBonus, Rank, Facing);

    public int DisplayedHealth
    {
        get; set;
    }
    public int RecoverableHealth
    {
        get; set;
    }
    public int HealthRecoveryDeadline
    {
        get; set;
    }
    public int HealthRecoveryInterval
    {
        get; set;
    }
    public int HealthBarMaximum
    {
        get; set;
    }
    public int DisplayedMana
    {
        get; set;
    }
    public int RecoverableMana
    {
        get; set;
    }
    public int ManaBarMaximum
    {
        get; set;
    }
    public int CommandCategory
    {
        get; set;
    }
    public int CommandScroll
    {
        get; set;
    }
    public int CommandSelection
    {
        get; set;
    }
    public int SecondaryEffect
    {
        get; set;
    }
    public int SecondaryEffectDuration
    {
        get; set;
    }
    public int SpawnCondition
    {
        get; set;
    }
    public int SpawnPlacement
    {
        get; set;
    }
    public int SpawnEncounterThreshold
    {
        get; set;
    }
    public int SpawnStartTime
    {
        get; set;
    }
    public int AnchorX
    {
        get; set;
    }
    public int AnchorY
    {
        get; set;
    }
    public int Depth
    {
        get; set;
    }
    public int SpriteX
    {
        get; set;
    }
    public int SpriteY
    {
        get; set;
    }
    public int ActionSpriteHeight
    {
        get; set;
    }
    public int JumpY
    {
        get; set;
    }
    public int JumpX
    {
        get; set;
    }
    public int SequenceOriginY
    {
        get; set;
    }
    public int SequenceOriginDepth
    {
        get; set;
    }
    public int TrackingX
    {
        get; set;
    }
    public int MovementStepX
    {
        get; set;
    }
    public int MovementStepY
    {
        get; set;
    }
    public int TargetBarDeadline
    {
        get; set;
    }
    public int ActionFlags
    {
        get; set;
    }
    public int Motion
    {
        get; set;
    }
    public int JumpOriginMotion
    {
        get; set;
    }
    public int JumpStage
    {
        get; set;
    }
    public int AnimationDeadline
    {
        get; set;
    }
    public int FrameInterval
    {
        get; set;
    }
    public int MovementInterval
    {
        get; set;
    }
    public int MovementDeadline
    {
        get; set;
    }
    public int ChainDeadline
    {
        get; set;
    }
    public int VerticalVelocity
    {
        get; set;
    }
    public int HorizontalVelocity
    {
        get; set;
    }
    public int VelocityDecay
    {
        get; set;
    }
    public int LaunchTicks
    {
        get; set;
    }
    public int Reaction
    {
        get; set;
    }
    public int HitPriority
    {
        get; set;
    }
    public int FacingDecisionTime
    {
        get; set;
    }
    public int MemberStatus
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
    public int BaseRecoveryInterval
    {
        get; set;
    }
    public int MagicDefense
    {
        get; set;
    }
    public int CriticalChance
    {
        get; set;
    }
    public int Rank
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
    public int Active
    {
        get; set;
    }
    public int ControlRank
    {
        get; set;
    }
    public int ControlRole
    {
        get; set;
    }
    public int FlashTicks
    {
        get; set;
    }
    public int Invulnerable
    {
        get; set;
    }
    public int DeathFadeTicks
    {
        get; set;
    }
    public int FlashPhase
    {
        get; set;
    }
    public int FlashColor
    {
        get; set;
    }
    public int CriticalBonus
    {
        get; set;
    }
    public int DefenseBonus
    {
        get; set;
    }
    public int AttackBonus
    {
        get; set;
    }
    public int GuardBonus
    {
        get; set;
    }
    public int Slow
    {
        get; set;
    }
    public int Haste
    {
        get; set;
    }
    public int Poison
    {
        get; set;
    }
    public int UndeadAttackBonus
    {
        get; set;
    }
    public int MagicBarrier
    {
        get; set;
    }
    public int Translucent
    {
        get; set;
    }
    public int AttackDecisionDeadline
    {
        get; set;
    }
    public int AttackVariant
    {
        get; set;
    }
    public int JumpSequenceIndex
    {
        get; set;
    }
    public int JumpAnimationDeadline
    {
        get; set;
    }
    public int JumpFrameInterval
    {
        get; set;
    }
    public int JumpRiseTicks
    {
        get; set;
    }
    public int HitConnected
    {
        get; set;
    }
    public int PreviousDirectionScan
    {
        get; set;
    }
    public int RunTapDeadline
    {
        get; set;
    }
    public int CurrentDirectionScan
    {
        get; set;
    }
    public int AiStep
    {
        get; set;
    }
    public int SpecialAction
    {
        get; set;
    }
    public int SpecialActionDeadline
    {
        get; set;
    }
    public int SpecialActionInterval
    {
        get; set;
    }
    public int ComboCount
    {
        get; set;
    }
    public int ComboDeadline
    {
        get; set;
    }
    public int TargetStrategy
    {
        get; set;
    }
    public int TargetStrategyDeadline
    {
        get; set;
    }
    public int ManaRecoveryDeadline
    {
        get; set;
    }

    public short FrameIndex
    {
        get; set;
    }
    public short JumpFrameIndex
    {
        get; set;
    }
    public short AttackIndex
    {
        get; set;
    }
    public short MovementTicksX
    {
        get; set;
    }
    public short MovementTicksY
    {
        get; set;
    }
    public short AttackLeft
    {
        get; set;
    }
    public short AttackTop
    {
        get; set;
    }
    public short AttackRight
    {
        get; set;
    }
    public short AttackBottom
    {
        get; set;
    }
    public short HurtLeft
    {
        get; set;
    }
    public short HurtTop
    {
        get; set;
    }
    public short HurtRight
    {
        get; set;
    }
    public short HurtBottom
    {
        get; set;
    }

    public byte SavedControl
    {
        get; set;
    }
    public byte ControlledBy
    {
        get; set;
    }
    public byte Team
    {
        get; set;
    }
    public byte CanTurn
    {
        get; set;
    }
    public byte ChainMode
    {
        get; set;
    }
    public byte HitLatch
    {
        get; set;
    }
    public byte Facing
    {
        get; set;
    }
    public byte HasDeathEffect
    {
        get; set;
    }

    public int[] Equipment { get; } = new int[6];
    public CombatStatusSlot[] StatusBank { get; } = new CombatStatusSlot[CombatStatusSlots.PersonalCount];
    public byte[][] SceneTexts { get; } = [new byte[20], new byte[20], new byte[20]];
    internal byte[]? UnmappedLegacyData
    {
        get; set;
    }

    public void ClearTransientEffects()
    {
        Array.Clear(StatusBank);
        FlashTicks = 0;
        Invulnerable = 0;
        DeathFadeTicks = 0;
        FlashPhase = 0;
        FlashColor = 0;
        CriticalBonus = 0;
        DefenseBonus = 0;
        AttackBonus = 0;
        GuardBonus = 0;
        Slow = 0;
        Haste = 0;
        Poison = 0;
        UndeadAttackBonus = 0;
        MagicBarrier = 0;
        Translucent = 0;
    }
}
