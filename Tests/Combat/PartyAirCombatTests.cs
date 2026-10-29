using System.Text;
using Atria.Game.Combat;
using Atria.Game.Controls;

namespace Atria.Tests.Combat;

public static class PartyAirCombatTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Air combat: " + message);
        }
    }

    public static string Run(string root)
    {
        var (runtime, state) = PartySpellTests.Fixture(root);
        var enemy = runtime.Actors[0];
        int jumps = 0, upwardMoves = 0, hits = 0;
        var catalog = new MoveCatalog(root);
        foreach (var (type, resource) in new[] { (19, "21.inf"), (45, "47.inf") })
        {
            var enemyInfo = CombatInfoDefinition.Load(root, Encoding.ASCII.GetBytes(resource)).Characters[^1];
            var enemyAssets = CombatAnimationAssets.Load(root, enemyInfo.AnimationResource.Span);
            enemy.Binding = new(0, 0, enemyInfo, enemyAssets);
            enemy.Animation = new(enemy.State, enemyInfo, enemyAssets);
            for (int party = 0; party < state.HeroCatalog.Count; party++)
            {
                foreach (int form in new[] { 1, 5 })
                {
                    foreach (byte facing in new byte[] { 1, 255 })
                    {
                        foreach (uint cadence in new uint[] { 10, 20, 33 })
                        {
                            var ally = runtime.Actors[runtime.Scene.PartyStartIndex + party];
                            var info = catalog.Info(party, form);
                            var art = CombatAnimationAssets.Load(root, info.AnimationResource.Span);
                            ally.Binding = new(0, 0, info, art);
                            ally.Animation = new(ally.State, info, art);
                            var a = ally.State;
                            var t = enemy.State;
                            a.ClearTransientEffects();
                            a.JumpStage = a.JumpSequenceIndex = a.Reaction = 0;
                            a.Health = 100;
                            a.Active = 1;
                            a.ControlledBy = 0;
                            a.Facing = facing;
                            a.AnchorX = facing == 1 ? 220 : 400;
                            a.Depth = 350;
                            ally.Animation.BeginMotion(0, 0);
                            a.ActionFlags = 0;
                            t.Facing = 1;
                            t.AnchorX = 300;
                            t.Depth = 350;
                            t.Health = 1000;
                            t.Active = 1;
                            t.SpawnCondition = 0;
                            t.HitPriority = t.Invulnerable = 0;
                            enemy.Animation.BeginMotion(0, 0);
                            Project(ally);
                            Project(enemy);
                            Check(PartyAirCombat.IsAirborne(enemy), $"actual flying type detected: requested={type}, info={enemyInfo.Type}, motion={t.Motion}");
                            // Put the opponent at a fixed close horizontal distance while preserving real sprite/hurt geometry.
                            int desired = a.TrackingX + (facing == 1 ? 55 : -55);
                            t.AnchorX += desired - t.TrackingX;
                            Project(enemy);
                            Check(PartyAirCombat.TryAct(ally, enemy, 10, cadence), $"engages flying target {type}/{party}/{form}/{facing}");
                            bool jumped = a.Motion == 15 || a.JumpStage != 0;
                            if (jumped)
                            {
                                jumps++;
                            }
                            else
                            {
                                upwardMoves++;
                            }

                            Check(jumped || a.ActionFlags == (int)CombatActionFlags.Moving || info.Attacks.Skip(info.FirstVariation).Take(info.VariationEnd - info.FirstVariation)
                    .Any(v => v.Direction == (int)CombatDirection.Up && v.Animation == ally.Animation.SequenceIndex), "no grounded ordinary attack");
                            var shake = new CombatCameraShake();
                            bool reachable = false;
                            for (uint now = 10 + cadence; now <= 4000; now += cadence)
                            {
                                PartyAirCombat.TryAct(ally, enemy, now, cadence);
                                ally.Animation.RebindFromFrameCounter();
                                CombatSpriteProjection.Update(a, info.Type, ally.Animation.CurrentFrame, ally.Animation.CurrentSprite);
                                CombatAnimationRuntime.Advance(ally, state, now, 3, () => 1, _ => { }, _ => { }, (_, _) => { }, _ => { }, () => { }, shake);
                                CombatSpriteProjection.UpdateTrackingX(a, ally.Animation.CurrentSprite);
                                CombatHitEligibility.RebuildRectangles(a, ally.Animation.CurrentSprite);
                                if (ally.Animation.CurrentFrame.Damage > 0 && CombatHitEligibility.CanHit(1, 0, a, info.Type,
                                    ally.Animation.CurrentFrame, t, enemy.Animation.CurrentFrame, testHitLatch: false))
                                {
                                    reachable = true;
                                    hits++;
                                    break;
                                }
                            }
                            Check(reachable, $"original attack reaches flying hurtbox {type}/{party}/{form}/{facing}, jumped={jumped}");
                        }
                    }
                }
            }
        }
        Check(jumps > 0 && upwardMoves > 0, "both jumping and upward attacks exercised");
        var actor = runtime.Actors[runtime.Scene.PartyStartIndex];
        enemy.State.Motion = 11;
        enemy.State.JumpStage = 0;
        Check(!PartyAirCombat.IsAirborne(enemy), "grounded reaction is not treated as flying");
        Check(!PartyAirCombat.TryAct(actor, enemy, 5000, 20), "grounded targets keep ordinary AI");
        return $"Air combat PASS: {hits} actual flying-target hitbox scenarios; {jumps} jumps, {upwardMoves} upward moves, both facings, forms1/5, all playable allies,10/20/33ms opportunities.";
    }

    private static void Project(CombatActorImage image)
    {
        CombatSpriteProjection.Update(image.State, image.Binding!.Info.Type, image.Animation!.CurrentFrame, image.Animation.CurrentSprite);
        CombatSpriteProjection.UpdateTrackingX(image.State, image.Animation.CurrentSprite);
        CombatHitEligibility.RebuildRectangles(image.State, image.Animation.CurrentSprite);
    }
}
