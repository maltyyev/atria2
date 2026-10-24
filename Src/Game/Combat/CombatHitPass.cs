using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>407520 actor-order hit pass, including one outer DD latch gate and per-target updates.</summary>
public sealed class CombatHitPass(CombatSceneRuntime runtime, CombatHitExecutor executor)
{
    public void Run(uint milliseconds)
    {
        for (int a = 0; a < runtime.Actors.Count; a++)
        {
            CombatActorImage attacker = runtime.Actors[a];
            CombatActorState record = attacker.State;
            if (record.Active == 0 || record.HitLatch == 0 || (record.ActionFlags & (int)CombatActionFlags.Dead) != 0
|| record.AttackLeft == record.AttackRight)
            {
                continue;
            }

            if (attacker.Animation is null || attacker.Binding is null)
            {
                throw new InvalidOperationException("Active hit actor has no actual bindings.");
            }

            for (int d = 0; d < runtime.Actors.Count; d++)
            {
                // Original clears DD on every target iteration; it does not recheck DD inside this pass.
                record.HitLatch = 0;
                CombatActorImage target = runtime.Actors[d];
                if (d == a || target.State.Health <= 0)
                {
                    continue;
                }

                if (target.Animation is null)
                {
                    // The native unused slot has degenerate zero rectangles and cannot be hit.
                    if (target.State.HurtRight == target.State.HurtLeft)
                    {
                        continue;
                    }

                    throw new InvalidOperationException("Potential combat target has no actual animation binding.");
                }

                if (!CombatHitEligibility.CanHit(a, d, record, attacker.Binding.Info.Type, attacker.Animation.CurrentFrame, target.State, target.Animation.CurrentFrame, false))
                {
                    continue;
                }

                record.HitConnected = 1;
                target.State.JumpStage = 0;
                if (record.ControlledBy != 0)
                {
                    runtime.TargetIndices[a] = d;
                    record.TargetBarDeadline = unchecked((int)milliseconds + 7000);
                }

                executor.Execute(a, d, milliseconds);
                if (target.State.Motion != 14)
                {
                    int interval = attacker.Animation.CurrentFrame.HitDelay;
                    if (interval == 0)
                    {
                        interval = 80;
                    }

                    target.State.FrameInterval = CombatActorMaintenance.ModifyActionInterval(target.State, interval);
                }

                CombatActorMaintenance.CaptureSecondaryAction(record, attacker.Animation.CurrentFrame);
            }
        }
    }

}
