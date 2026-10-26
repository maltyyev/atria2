using System.Buffers.Binary;
using Atria.Game.State;

namespace Atria.Game.Combat;

public interface ICombatParticleImpactEffects
{
    void Reaction(CombatActorImage owner, CombatActorImage target, int reaction);
    void Launch(CombatActorImage owner, CombatActorImage target, CombatDeathReaction force);
    void Damage(CombatActorImage owner, CombatActorImage target, int amount);
    void Block(CombatActorImage owner, CombatActorImage target);
    void Spawn(CombatHitParticle particle);
    void Expire(CombatParticleState particle);
}

/// <summary>40d100 exact projectile eligibility and damage path, separate from ordinary406d40 attacks.</summary>
public static class CombatParticleCollision
{
    internal static bool CanHit(CombatParticleState particle, CombatActorImage target)
    {
        var t = target.State;
        var sprite = particle.CurrentSprite;
        return t.SpawnCondition == 0 && t.Rank != 0 && t.Team != particle.Owner!.State.Team
            && !ReferenceEquals(target, particle.Owner) && t.Invulnerable == 0
            && t.HitPriority < particle.FirstFrame.HitPriority
            && Math.Abs((long)t.Depth - particle.Depth) <= particle.DepthTolerance
            && t.HurtLeft <= (long)particle.SpriteX + sprite.Width && t.HurtRight >= particle.SpriteX
            && t.HurtTop <= (long)particle.SpriteY + sprite.Height && t.HurtBottom >= particle.SpriteY;
    }
    public static int Apply(
        CombatParticleState particle,
        IReadOnlyList<CombatActorImage> actors,
        GameState state,
        Func<bool> magicActive,
        Func<int> nextNativeRandom,
        ICombatParticleImpactEffects effects
    )
    {
        var owner = particle.Owner ?? throw new InvalidOperationException("Damaging particle has no actual owner.");
        var first = particle.FirstFrame;
        var sprite = particle.CurrentSprite;
        int x = particle.SpriteX, y = particle.SpriteY, width = sprite.Width, height = sprite.Height, velocity = particle.VelocityX, hits = 0;
        foreach (var target in actors)
        {
            CombatActorState t = target.State;
            if (!CanHit(particle, target))
            {
                continue;
            }

            hits++;
            t.HitPriority = first.HitPriority;
            t.Invulnerable = 15;
            int velocityFacing = velocity >= 0 ? 1 : -1;
            if ((t.ActionFlags & (int)CombatActionFlags.Guarding) == 0 || unchecked((sbyte)t.Facing) == velocityFacing)
            {
                t.FrameInterval = CombatActorMaintenance.ModifyActionInterval(t, 30);
                int reaction = first.Reaction;
                if (reaction <= 7)
                {
                    effects.Reaction(owner, target, reaction);
                }
                else
                {
                    effects.Launch(owner, target, new(10, 10, 6, 3, unchecked((sbyte)owner.State.Facing)));
                }

                int amount;
                if (!magicActive())
                {
                    amount = first.Damage >> (state.AvailableItems[82] != 0 ? 1 : 0);
                    if (t.MagicBarrier != 0)
                    {
                        amount >>= 1;
                    }
                }
                else
                {
                    amount = unchecked(first.Damage / 3 + owner.State.MagicRank * 20);
                    int defense = t.MagicDefense;
                    if (defense != 0)
                    {
                        amount = unchecked(amount + (nextNativeRandom() % defense) * -2);
                    }

                    if (amount < 0)
                    {
                        amount = 1;
                    }

                    if (!magicActive() && t.MagicBarrier != 0)
                    {
                        amount >>= 1;
                    }
                }

                effects.Damage(owner, target, amount);
            }
            else
            {
                effects.Block(owner, target);
            }

            if (first.HitParticle != 0)
            {
                effects.Spawn(new(particle.Facing == 1 ? unchecked(particle.SpriteX + particle.CurrentSprite.Width) : particle.SpriteX, particle.SpriteY, particle.Depth, first.HitParticle, unchecked((sbyte)particle.Facing)));
            }

            if (first.HitPriority < 2)
            {
                effects.Expire(particle);
                return hits;
            }
        }

        return hits;
    }

}
