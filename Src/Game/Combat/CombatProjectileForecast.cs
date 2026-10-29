using Atria.Game.Exploration;

namespace Atria.Game.Combat;

/// <summary>Read-only forecast using the same projectile spawning, motion and collision rules as battle.</summary>
internal sealed class CombatProjectileForecast
{
    private const int MaximumForecastSteps = 512;
    private readonly CombatSceneRuntime runtime;
    private readonly CombatParticleSystem pool;
    private readonly CombatParticleMagicRuntime magic;
    private readonly LegacyRandom forecastRandom = new(0);
    private readonly LegacyRandom battleRandom;
    private readonly CombatActorImage caster = new();
    private readonly bool[] struck;
    private uint clock;
    private static readonly Action<int> Silence = _ => { };

    public CombatProjectileForecast(string root, CombatSceneRuntime runtime, CombatHitExecutor hits, LegacyRandom random)
    {
        this.runtime = runtime;
        battleRandom = random;
        struck = new bool[runtime.Actors.Count];
        pool = CombatParticleSystem.Load(root);
        magic = new(root, runtime, pool, hits, () => clock, forecastRandom.Next, Silence, _ => { })
        {
            GlobalDepth = runtime.Scene.Header73
        };
    }

    internal static bool IsProjectile(int effect) => (CombatSpellEffect)effect is
        CombatSpellEffect.Waves or CombatSpellEffect.Lightning or CombatSpellEffect.Fireball
        or CombatSpellEffect.Meteors or CombatSpellEffect.Ice or CombatSpellEffect.Comet
        or CombatSpellEffect.Explosion or CombatSpellEffect.Missile or CombatSpellEffect.Energy
        or CombatSpellEffect.EnergyCharge or CombatSpellEffect.HolyMissile or CombatSpellEffect.ChargedEnergy;

    public int Score(CombatActorImage owner, CombatCommand command, byte facing)
    {
        Array.Clear(struck);
        forecastRandom.Seed(battleRandom.State);
        caster.Binding = owner.Binding;
        caster.Animation = new(caster.State, owner.Binding!.Info, owner.Binding.Assets);
        caster.State.AnchorX = owner.State.AnchorX;
        caster.State.Depth = owner.State.Depth;
        caster.State.Team = owner.State.Team;
        caster.State.Facing = owner.State.Facing;
        BanditAiDecision.Turn(caster.State, unchecked((sbyte)facing), owner.Animation!.CurrentSprite.Width);
        caster.State.Rank = owner.State.Rank;
        caster.State.MagicRank = owner.State.MagicRank;
        caster.Animation.BeginSequenceCommand(command.Animation, 28, 0);
        var frames = owner.Binding.Assets.Animation.Sequences[command.Animation].Frames;
        for (int frameIndex = 0; frameIndex < frames.Length; frameIndex++)
        {
            var frame = frames[frameIndex];
            if (!IsProjectile(frame.Effect))
            {
                continue;
            }

            foreach (var particle in pool.Particles)
            {
                particle.Clear();
            }
            clock = 0;
            caster.State.FrameIndex = checked((short)frameIndex);
            caster.Animation.RebindFromFrameCounter();
            CombatSpriteProjection.Update(caster.State, caster.Binding!.Info.Type, frame, caster.Animation.CurrentSprite);
            // Only projectile effects enter this isolated pool; no status/damage dispatch is allowed here.
            magic.Apply(caster, null, frame.Effect, frame.EffectArgument, false);
            Trace();
        }

        int count = 0;
        for (int i = 0; i < struck.Length; i++)
        {
            if (struck[i])
            {
                count++;
            }
        }
        return count;
    }

    private void Trace()
    {
        for (int step = 0; step < MaximumForecastSteps; step++)
        {
            bool active = false;
            int nextTime = int.MaxValue;
            foreach (var p in pool.Particles)
            {
                if (p.Facing == 0)
                {
                    continue;
                }
                active = true;
                if (CombatParticleSystem.KeepAlive(p, magic.Transform))
                {
                    CombatParticleSystem.AdvanceMotion(p, clock, Silence);
                    if (p.FirstFrame.Damage != 0)
                    {
                        for (int i = 0; i < runtime.Actors.Count; i++)
                        {
                            var target = runtime.Actors[i];
                            if (target.State.Team >= 100 || target.State.Health <= 0 || target.State.Active == 0
                                || !CombatParticleCollision.CanHit(p, target))
                            {
                                continue;
                            }
                            struck[i] = true;
                            if (p.FirstFrame.HitPriority < 2)
                            {
                                CombatParticleSystem.Expire(p, magic.Transform);
                                break;
                            }
                        }
                    }
                }
                if (p.Facing != 0)
                {
                    nextTime = Math.Min(nextTime, p.NextFrameTime);
                }
            }
            if (!active || nextTime == int.MaxValue)
            {
                return;
            }
            clock = (uint)Math.Max((long)clock + 1, nextTime);
        }
        // A malformed/nonterminating effect must not block an AI decision or justify spending mana.
        Array.Clear(struck);
    }
}
