using System.Buffers.Binary;
using Atria.Game.Combat;

namespace Atria.Resources;

/// <summary>Original actor layout, used for party import and independent parity fixtures, never for simulation.</summary>
public static class CombatActorCodec
{
    public const int ImageSize = 0x379;
    private const int PartyStart = 0x124;
    private const int PartySize = 280;
    private const int EquipmentStart = 0x208;
    private const int StatusStart = 0x244;
    private const int TextStart = 0x2e9;

    public static CombatActorState Decode(ReadOnlySpan<byte> image)
    {
        var actor = new CombatActorState();
        DecodeInto(image, actor);
        return actor;
    }

    public static void ImportParty(ReadOnlySpan<byte> party, CombatActorState actor)
    {
        if (party.Length != PartySize)
        {
            throw new ArgumentException("Incomplete party record.");
        }

        byte[] image = Encode(actor);
        party.CopyTo(image.AsSpan(PartyStart, PartySize));
        DecodeInto(image, actor);
    }

    public static void DecodeInto(ReadOnlySpan<byte> image, CombatActorState actor)
    {
        if (image.Length != ImageSize)
        {
            throw new ArgumentException("Incomplete combat actor image.");
        }

        actor.UnmappedLegacyData = image.ToArray();
        actor.DisplayedHealth = BinaryPrimitives.ReadInt32LittleEndian(image[0x4..]);
        actor.RecoverableHealth = BinaryPrimitives.ReadInt32LittleEndian(image[0x8..]);
        actor.HealthRecoveryDeadline = BinaryPrimitives.ReadInt32LittleEndian(image[0xc..]);
        actor.HealthRecoveryInterval = BinaryPrimitives.ReadInt32LittleEndian(image[0x10..]);
        actor.HealthBarMaximum = BinaryPrimitives.ReadInt32LittleEndian(image[0x14..]);
        actor.DisplayedMana = BinaryPrimitives.ReadInt32LittleEndian(image[0x18..]);
        actor.RecoverableMana = BinaryPrimitives.ReadInt32LittleEndian(image[0x1c..]);
        actor.ManaBarMaximum = BinaryPrimitives.ReadInt32LittleEndian(image[0x28..]);
        actor.CommandCategory = BinaryPrimitives.ReadInt32LittleEndian(image[0x2c..]);
        actor.CommandScroll = BinaryPrimitives.ReadInt32LittleEndian(image[0x30..]);
        actor.CommandSelection = BinaryPrimitives.ReadInt32LittleEndian(image[0x34..]);
        actor.SecondaryEffect = BinaryPrimitives.ReadInt32LittleEndian(image[0x54..]);
        actor.SecondaryEffectDuration = BinaryPrimitives.ReadInt32LittleEndian(image[0x58..]);
        actor.SpawnCondition = BinaryPrimitives.ReadInt32LittleEndian(image[0x60..]);
        actor.SpawnPlacement = BinaryPrimitives.ReadInt32LittleEndian(image[0x64..]);
        actor.SpawnEncounterThreshold = BinaryPrimitives.ReadInt32LittleEndian(image[0x68..]);
        actor.SpawnStartTime = BinaryPrimitives.ReadInt32LittleEndian(image[0x6c..]);
        actor.AnchorX = BinaryPrimitives.ReadInt32LittleEndian(image[0x70..]);
        actor.AnchorY = BinaryPrimitives.ReadInt32LittleEndian(image[0x74..]);
        actor.Depth = BinaryPrimitives.ReadInt32LittleEndian(image[0x78..]);
        actor.SpriteX = BinaryPrimitives.ReadInt32LittleEndian(image[0x7c..]);
        actor.SpriteY = BinaryPrimitives.ReadInt32LittleEndian(image[0x80..]);
        actor.ActionSpriteHeight = BinaryPrimitives.ReadInt32LittleEndian(image[0x84..]);
        actor.JumpY = BinaryPrimitives.ReadInt32LittleEndian(image[0x88..]);
        actor.JumpX = BinaryPrimitives.ReadInt32LittleEndian(image[0x8c..]);
        actor.SequenceOriginY = BinaryPrimitives.ReadInt32LittleEndian(image[0x90..]);
        actor.SequenceOriginDepth = BinaryPrimitives.ReadInt32LittleEndian(image[0x94..]);
        actor.TrackingX = BinaryPrimitives.ReadInt32LittleEndian(image[0x98..]);
        actor.MovementStepX = BinaryPrimitives.ReadInt32LittleEndian(image[0x9c..]);
        actor.MovementStepY = BinaryPrimitives.ReadInt32LittleEndian(image[0xa0..]);
        actor.TargetBarDeadline = BinaryPrimitives.ReadInt32LittleEndian(image[0xa9..]);
        actor.ActionFlags = BinaryPrimitives.ReadInt32LittleEndian(image[0xad..]);
        actor.Motion = BinaryPrimitives.ReadInt32LittleEndian(image[0xb1..]);
        actor.JumpOriginMotion = BinaryPrimitives.ReadInt32LittleEndian(image[0xb5..]);
        actor.JumpStage = BinaryPrimitives.ReadInt32LittleEndian(image[0xb9..]);
        actor.AnimationDeadline = BinaryPrimitives.ReadInt32LittleEndian(image[0xbd..]);
        actor.FrameInterval = BinaryPrimitives.ReadInt32LittleEndian(image[0xc1..]);
        actor.MovementInterval = BinaryPrimitives.ReadInt32LittleEndian(image[0xc5..]);
        actor.MovementDeadline = BinaryPrimitives.ReadInt32LittleEndian(image[0xc9..]);
        actor.ChainDeadline = BinaryPrimitives.ReadInt32LittleEndian(image[0xcd..]);
        actor.VerticalVelocity = BinaryPrimitives.ReadInt32LittleEndian(image[0xe2..]);
        actor.HorizontalVelocity = BinaryPrimitives.ReadInt32LittleEndian(image[0xe6..]);
        actor.VelocityDecay = BinaryPrimitives.ReadInt32LittleEndian(image[0xea..]);
        actor.LaunchTicks = BinaryPrimitives.ReadInt32LittleEndian(image[0xf2..]);
        actor.Reaction = BinaryPrimitives.ReadInt32LittleEndian(image[0xf6..]);
        actor.HitPriority = BinaryPrimitives.ReadInt32LittleEndian(image[0x11c..]);
        actor.FacingDecisionTime = BinaryPrimitives.ReadInt32LittleEndian(image[0x120..]);
        actor.MemberStatus = BinaryPrimitives.ReadInt32LittleEndian(image[0x160..]);
        actor.MaximumHealth = BinaryPrimitives.ReadInt32LittleEndian(image[0x16c..]);
        actor.Health = BinaryPrimitives.ReadInt32LittleEndian(image[0x170..]);
        actor.MaximumMana = BinaryPrimitives.ReadInt32LittleEndian(image[0x174..]);
        actor.Mana = BinaryPrimitives.ReadInt32LittleEndian(image[0x178..]);
        actor.MagicRank = BinaryPrimitives.ReadInt32LittleEndian(image[0x17c..]);
        actor.BaseRecoveryInterval = BinaryPrimitives.ReadInt32LittleEndian(image[0x180..]);
        actor.MagicDefense = BinaryPrimitives.ReadInt32LittleEndian(image[0x1c0..]);
        actor.CriticalChance = BinaryPrimitives.ReadInt32LittleEndian(image[0x1e0..]);
        actor.Rank = BinaryPrimitives.ReadInt32LittleEndian(image[0x1ec..]);
        actor.Defense = BinaryPrimitives.ReadInt32LittleEndian(image[0x1f0..]);
        actor.Attack = BinaryPrimitives.ReadInt32LittleEndian(image[0x1f4..]);
        actor.Active = BinaryPrimitives.ReadInt32LittleEndian(image[0x224..]);
        actor.ControlRank = BinaryPrimitives.ReadInt32LittleEndian(image[0x22c..]);
        actor.ControlRole = BinaryPrimitives.ReadInt32LittleEndian(image[0x230..]);
        actor.FlashTicks = BinaryPrimitives.ReadInt32LittleEndian(image[0x23c..]);
        actor.Invulnerable = BinaryPrimitives.ReadInt32LittleEndian(image[0x240..]);
        actor.DeathFadeTicks = BinaryPrimitives.ReadInt32LittleEndian(image[0x28c..]);
        actor.FlashPhase = BinaryPrimitives.ReadInt32LittleEndian(image[0x290..]);
        actor.FlashColor = BinaryPrimitives.ReadInt32LittleEndian(image[0x294..]);
        actor.CriticalBonus = BinaryPrimitives.ReadInt32LittleEndian(image[0x29c..]);
        actor.DefenseBonus = BinaryPrimitives.ReadInt32LittleEndian(image[0x2a0..]);
        actor.AttackBonus = BinaryPrimitives.ReadInt32LittleEndian(image[0x2a4..]);
        actor.GuardBonus = BinaryPrimitives.ReadInt32LittleEndian(image[0x2a8..]);
        actor.Slow = BinaryPrimitives.ReadInt32LittleEndian(image[0x2ac..]);
        actor.Haste = BinaryPrimitives.ReadInt32LittleEndian(image[0x2b0..]);
        actor.Poison = BinaryPrimitives.ReadInt32LittleEndian(image[0x2b4..]);
        actor.UndeadAttackBonus = BinaryPrimitives.ReadInt32LittleEndian(image[0x2b8..]);
        actor.MagicBarrier = BinaryPrimitives.ReadInt32LittleEndian(image[0x2bc..]);
        actor.Translucent = BinaryPrimitives.ReadInt32LittleEndian(image[0x2c0..]);
        actor.AttackDecisionDeadline = BinaryPrimitives.ReadInt32LittleEndian(image[0x329..]);
        actor.AttackVariant = BinaryPrimitives.ReadInt32LittleEndian(image[0x32d..]);
        actor.JumpSequenceIndex = BinaryPrimitives.ReadInt32LittleEndian(image[0x331..]);
        actor.JumpAnimationDeadline = BinaryPrimitives.ReadInt32LittleEndian(image[0x335..]);
        actor.JumpFrameInterval = BinaryPrimitives.ReadInt32LittleEndian(image[0x339..]);
        actor.JumpRiseTicks = BinaryPrimitives.ReadInt32LittleEndian(image[0x33d..]);
        actor.HitConnected = BinaryPrimitives.ReadInt32LittleEndian(image[0x341..]);
        actor.PreviousDirectionScan = BinaryPrimitives.ReadInt32LittleEndian(image[0x345..]);
        actor.RunTapDeadline = BinaryPrimitives.ReadInt32LittleEndian(image[0x349..]);
        actor.CurrentDirectionScan = BinaryPrimitives.ReadInt32LittleEndian(image[0x34d..]);
        actor.AiStep = BinaryPrimitives.ReadInt32LittleEndian(image[0x351..]);
        actor.SpecialAction = BinaryPrimitives.ReadInt32LittleEndian(image[0x355..]);
        actor.SpecialActionDeadline = BinaryPrimitives.ReadInt32LittleEndian(image[0x359..]);
        actor.SpecialActionInterval = BinaryPrimitives.ReadInt32LittleEndian(image[0x35d..]);
        actor.ComboCount = BinaryPrimitives.ReadInt32LittleEndian(image[0x365..]);
        actor.ComboDeadline = BinaryPrimitives.ReadInt32LittleEndian(image[0x369..]);
        actor.TargetStrategy = BinaryPrimitives.ReadInt32LittleEndian(image[0x36d..]);
        actor.TargetStrategyDeadline = BinaryPrimitives.ReadInt32LittleEndian(image[0x371..]);
        actor.ManaRecoveryDeadline = BinaryPrimitives.ReadInt32LittleEndian(image[0x375..]);
        actor.FrameIndex = BinaryPrimitives.ReadInt16LittleEndian(image[0xd5..]);
        actor.JumpFrameIndex = BinaryPrimitives.ReadInt16LittleEndian(image[0xd9..]);
        actor.AttackIndex = BinaryPrimitives.ReadInt16LittleEndian(image[0xdb..]);
        actor.MovementTicksX = BinaryPrimitives.ReadInt16LittleEndian(image[0xde..]);
        actor.MovementTicksY = BinaryPrimitives.ReadInt16LittleEndian(image[0xe0..]);
        actor.AttackLeft = BinaryPrimitives.ReadInt16LittleEndian(image[0xff..]);
        actor.AttackTop = BinaryPrimitives.ReadInt16LittleEndian(image[0x101..]);
        actor.AttackRight = BinaryPrimitives.ReadInt16LittleEndian(image[0x103..]);
        actor.AttackBottom = BinaryPrimitives.ReadInt16LittleEndian(image[0x105..]);
        actor.HurtLeft = BinaryPrimitives.ReadInt16LittleEndian(image[0x107..]);
        actor.HurtTop = BinaryPrimitives.ReadInt16LittleEndian(image[0x109..]);
        actor.HurtRight = BinaryPrimitives.ReadInt16LittleEndian(image[0x10b..]);
        actor.HurtBottom = BinaryPrimitives.ReadInt16LittleEndian(image[0x10d..]);
        actor.SavedControl = image[0xa5];
        actor.ControlledBy = image[0xa4];
        actor.Team = image[0xa6];
        actor.CanTurn = image[0xa7];
        actor.ChainMode = image[0xa8];
        actor.HitLatch = image[0xdd];
        actor.Facing = image[0xfa];
        actor.HasDeathEffect = image[0x11b];
        for (int i = 0; i < actor.Equipment.Length; i++)
        {
            actor.Equipment[i] = BinaryPrimitives.ReadInt32LittleEndian(image[(EquipmentStart + i * sizeof(int))..]);
        }
        CombatStatusCodec.Decode(image.Slice(StatusStart, actor.StatusBank.Length * CombatStatusCodec.RecordSize), actor.StatusBank);
        for (int i = 0; i < actor.SceneTexts.Length; i++)
        {
            image.Slice(TextStart + i * actor.SceneTexts[i].Length, actor.SceneTexts[i].Length).CopyTo(actor.SceneTexts[i]);
        }
    }

    public static byte[] Encode(CombatActorState actor)
    {
        byte[] image = actor.UnmappedLegacyData?.ToArray() ?? new byte[ImageSize];
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x4), actor.DisplayedHealth);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x8), actor.RecoverableHealth);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0xc), actor.HealthRecoveryDeadline);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x10), actor.HealthRecoveryInterval);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x14), actor.HealthBarMaximum);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x18), actor.DisplayedMana);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x1c), actor.RecoverableMana);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x28), actor.ManaBarMaximum);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x2c), actor.CommandCategory);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x30), actor.CommandScroll);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x34), actor.CommandSelection);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x54), actor.SecondaryEffect);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x58), actor.SecondaryEffectDuration);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x60), actor.SpawnCondition);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x64), actor.SpawnPlacement);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x68), actor.SpawnEncounterThreshold);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x6c), actor.SpawnStartTime);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x70), actor.AnchorX);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x74), actor.AnchorY);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x78), actor.Depth);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x7c), actor.SpriteX);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x80), actor.SpriteY);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x84), actor.ActionSpriteHeight);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x88), actor.JumpY);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x8c), actor.JumpX);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x90), actor.SequenceOriginY);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x94), actor.SequenceOriginDepth);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x98), actor.TrackingX);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x9c), actor.MovementStepX);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0xa0), actor.MovementStepY);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0xa9), actor.TargetBarDeadline);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0xad), actor.ActionFlags);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0xb1), actor.Motion);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0xb5), actor.JumpOriginMotion);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0xb9), actor.JumpStage);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0xbd), actor.AnimationDeadline);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0xc1), actor.FrameInterval);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0xc5), actor.MovementInterval);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0xc9), actor.MovementDeadline);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0xcd), actor.ChainDeadline);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0xe2), actor.VerticalVelocity);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0xe6), actor.HorizontalVelocity);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0xea), actor.VelocityDecay);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0xf2), actor.LaunchTicks);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0xf6), actor.Reaction);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x11c), actor.HitPriority);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x120), actor.FacingDecisionTime);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x160), actor.MemberStatus);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x16c), actor.MaximumHealth);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x170), actor.Health);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x174), actor.MaximumMana);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x178), actor.Mana);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x17c), actor.MagicRank);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x180), actor.BaseRecoveryInterval);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x1c0), actor.MagicDefense);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x1e0), actor.CriticalChance);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x1ec), actor.Rank);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x1f0), actor.Defense);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x1f4), actor.Attack);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x224), actor.Active);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x22c), actor.ControlRank);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x230), actor.ControlRole);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x23c), actor.FlashTicks);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x240), actor.Invulnerable);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x28c), actor.DeathFadeTicks);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x290), actor.FlashPhase);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x294), actor.FlashColor);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x29c), actor.CriticalBonus);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x2a0), actor.DefenseBonus);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x2a4), actor.AttackBonus);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x2a8), actor.GuardBonus);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x2ac), actor.Slow);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x2b0), actor.Haste);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x2b4), actor.Poison);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x2b8), actor.UndeadAttackBonus);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x2bc), actor.MagicBarrier);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x2c0), actor.Translucent);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x329), actor.AttackDecisionDeadline);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x32d), actor.AttackVariant);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x331), actor.JumpSequenceIndex);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x335), actor.JumpAnimationDeadline);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x339), actor.JumpFrameInterval);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x33d), actor.JumpRiseTicks);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x341), actor.HitConnected);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x345), actor.PreviousDirectionScan);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x349), actor.RunTapDeadline);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x34d), actor.CurrentDirectionScan);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x351), actor.AiStep);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x355), actor.SpecialAction);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x359), actor.SpecialActionDeadline);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x35d), actor.SpecialActionInterval);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x365), actor.ComboCount);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x369), actor.ComboDeadline);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x36d), actor.TargetStrategy);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x371), actor.TargetStrategyDeadline);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x375), actor.ManaRecoveryDeadline);
        BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(0xd5), actor.FrameIndex);
        BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(0xd9), actor.JumpFrameIndex);
        BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(0xdb), actor.AttackIndex);
        BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(0xde), actor.MovementTicksX);
        BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(0xe0), actor.MovementTicksY);
        BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(0xff), actor.AttackLeft);
        BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(0x101), actor.AttackTop);
        BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(0x103), actor.AttackRight);
        BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(0x105), actor.AttackBottom);
        BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(0x107), actor.HurtLeft);
        BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(0x109), actor.HurtTop);
        BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(0x10b), actor.HurtRight);
        BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(0x10d), actor.HurtBottom);
        image[0xa5] = actor.SavedControl;
        image[0xa4] = actor.ControlledBy;
        image[0xa6] = actor.Team;
        image[0xa7] = actor.CanTurn;
        image[0xa8] = actor.ChainMode;
        image[0xdd] = actor.HitLatch;
        image[0xfa] = actor.Facing;
        image[0x11b] = actor.HasDeathEffect;
        for (int i = 0; i < actor.Equipment.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(EquipmentStart + i * sizeof(int)), actor.Equipment[i]);
        }
        CombatStatusCodec.Encode(actor.StatusBank).CopyTo(image, StatusStart);
        for (int i = 0; i < actor.SceneTexts.Length; i++)
        {
            actor.SceneTexts[i].CopyTo(image, TextStart + i * actor.SceneTexts[i].Length);
        }
        return image;
    }
}
