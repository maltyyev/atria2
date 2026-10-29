namespace Atria.Game.Combat;

internal static class PartyAirCombat
{
    private const int EngagementDistance = 120;
    private const int PartyDepthTolerance = 21;
    private const int MinimumJumpHeight = 16;

    public static bool IsAirborne(CombatActorImage target)
    {
        var a = target.State;
        return a.JumpStage is 16 or 17
            || CombatSpriteProjection.FlightHeight(target.Binding!.Info.Type, a.Motion) > 0
            || ((a.ActionFlags & (int)(CombatActionFlags.Launched | CombatActionFlags.Falling)) != 0
                && a.SpriteY + target.Animation!.CurrentSprite.Height < a.Depth);
    }

    public static bool TryAct(CombatActorImage image, CombatActorImage target, uint now, uint opportunityInterval)
    {
        var a = image.State;
        var t = target.State;
        if (a.Team < 100 || a.ControlledBy != 0 || a.Health <= 0 || a.Active == 0
            || t.Health <= 0 || t.Active == 0 || t.SpawnCondition != 0 || t.Team >= 100
            || !IsAirborne(target))
        {
            return false;
        }
        var info = image.Binding!.Info;
        if (a.JumpStage is 16 or 17)
        {
            if (a.Motion == (int)CombatMotionKind.Idle && a.Depth - a.SpriteY - image.Animation!.CurrentSprite.Height >= MinimumJumpHeight)
            {
                int flags = a.ActionFlags, selected = -1;
                for (int i = info.FirstVariation; i < info.VariationEnd; i++)
                {
                    if ((flags & (int)CombatActionFlags.CanChain) != 0 && info.Attacks[i].RequiredFlags == flags)
                    {
                        selected = i;
                        flags = info.Attacks[i].ResultFlags;
                    }
                }
                if (selected >= 0 && CanReachInJump(image, target, selected, now, opportunityInterval))
                {
                    CombatSemanticControl.AirAttack(image, now);
                }
            }
            return true;
        }
        if (!a.ActionState.CanStartGroundAttack || Math.Abs(t.TrackingX - a.TrackingX) >= EngagementDistance
            || Math.Abs(t.Depth - a.Depth) >= PartyDepthTolerance)
        {
            return false;
        }

        int direction = t.TrackingX < a.TrackingX ? -1 : 1;
        if (unchecked((sbyte)a.Facing) != direction)
        {
            BanditAiDecision.Turn(a, direction, image.Animation!.CurrentSprite.Width);
        }
        if (NecromancerCombatAssets.IsInfo(info) && Math.Abs(t.TrackingX - a.TrackingX) > NecromancerCombatAssets.AirEngagementDistance)
        {
            if (a.Motion != (int)CombatMotionKind.Walk || a.ActionFlags != (int)CombatActionFlags.Moving)
            {
                image.Animation!.BeginAutomaticMovement((int)CombatMotionKind.Walk, t.TrackingX - a.TrackingX, 0, now);
            }

            BanditAiDecision.AdvanceMovement(a, t, image.Animation!, now);
            return true;
        }
        for (int i = info.FirstVariation; i < info.VariationEnd; i++)
        {
            if (info.Attacks[i].Direction == (int)CombatDirection.Up && CanReach(image, target, i))
            {
                image.Animation!.BeginVariation(i, now);
                return true;
            }
        }

        // Stop at melee range: a running jump would carry the ally past a hovering enemy.
        image.Animation!.BeginMotion((int)CombatMotionKind.Idle, now);
        a.ActionFlags = (int)CombatActionFlags.Ready;
        return CombatJumpRuntime.Begin(a, info, image.Animation, now);
    }

    private static bool CanReachInJump(CombatActorImage image, CombatActorImage target, int attackIndex, uint now, uint opportunityInterval)
    {
        var a = image.State;
        var preview = new CombatActorState
        {
            Active = a.Active,
            Health = a.Health,
            Team = a.Team,
            Facing = a.Facing,
            AnchorX = a.AnchorX,
            AnchorY = a.AnchorY,
            SpriteX = a.SpriteX,
            SpriteY = a.SpriteY,
            Depth = a.Depth,
            JumpStage = a.JumpStage,
            JumpOriginMotion = a.JumpOriginMotion,
            JumpSequenceIndex = a.JumpSequenceIndex,
            JumpX = a.JumpX,
            JumpY = a.JumpY,
            JumpRiseTicks = a.JumpRiseTicks,
            HorizontalVelocity = a.HorizontalVelocity,
            VerticalVelocity = a.VerticalVelocity,
            MovementInterval = a.MovementInterval,
            MovementDeadline = a.MovementDeadline,
            JumpFrameInterval = a.JumpFrameInterval,
            JumpAnimationDeadline = a.JumpAnimationDeadline,
            Haste = a.Haste,
            Slow = a.Slow
        };
        var binding = image.Binding!;
        var cursor = new CombatAnimationCursor(preview, binding.Info, binding.Assets);
        cursor.BindJumpSequence(a.JumpSequenceIndex, false);
        preview.JumpFrameIndex = a.JumpFrameIndex;
        cursor.BeginVariation(attackIndex, now);
        const int MaximumJumpForecastSteps = 128;
        for (int step = 0; step < MaximumJumpForecastSteps && preview.JumpStage != 0; step++)
        {
            cursor.RebindFromFrameCounter();
            CombatSpriteProjection.Update(preview, binding.Info.Type, cursor.CurrentFrame, cursor.CurrentSprite);
            CombatJumpRuntime.Step(preview, binding.Info, cursor, now);
            CombatHitEligibility.RebuildRectangles(preview, cursor.CurrentSprite);
            if (cursor.CurrentFrame.Damage > 0 && CombatHitEligibility.CanHit(0, 1, preview, binding.Info.Type,
                cursor.CurrentFrame, target.State, target.Animation!.CurrentFrame, testHitLatch: false))
            {
                return true;
            }
            if (preview.Motion != (int)CombatMotionKind.Attack)
            {
                return false;
            }
            now += Math.Max(1, opportunityInterval);
        }
        return false;
    }

    private static bool CanReach(CombatActorImage image, CombatActorImage target, int attackIndex)
    {
        var a = image.State;
        var t = target.State;
        var binding = image.Binding!;
        var attack = binding.Info.Attacks[attackIndex];
        var frames = binding.Assets.Animation.Sequences[attack.Animation].Frames;
        int firstHeight = binding.Assets.Sprites.Frames[frames[0].SpriteFrameIndex].Height;
        var preview = new CombatActorState
        {
            Active = a.Active,
            Health = a.Health,
            Team = a.Team,
            Facing = a.Facing,
            Depth = a.Depth,
            ActionFlags = attack.ResultFlags
        };
        foreach (var frame in frames)
        {
            if (frame.Damage == 0)
            {
                continue;
            }
            var sprite = binding.Assets.Sprites.Frames[frame.SpriteFrameIndex];
            int x = a.Facing == 1 ? a.AnchorX + frame.OffsetX : a.AnchorX - sprite.Width - frame.OffsetX;
            int y = (attack.ResultFlags & (int)CombatActionFlags.OffsetMotion) != 0
                ? a.Depth - firstHeight + frame.OffsetY : a.Depth - sprite.Height;
            preview.SpriteX = x;
            preview.SpriteY = y;
            CombatHitEligibility.RebuildRectangles(preview, sprite);
            if (CombatHitEligibility.CanHit(0, 1, preview, binding.Info.Type, frame, t,
                target.Animation!.CurrentFrame, testHitLatch: false))
            {
                return true;
            }
        }
        return false;
    }
}
