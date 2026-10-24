namespace Atria.Game.Combat;

/// <summary>Real40d100 effects bound to recovered reaction/raw-damage engine; no ordinary-hit reroll.</summary>
public sealed class CombatParticleImpactAdapter : ICombatParticleImpactEffects
{
    private readonly CombatSceneRuntime runtime;
    private readonly CombatParticleSystem particles;
    private readonly CombatHitExecutor hits;
    private readonly Func<uint> milliseconds;
    private readonly Func<int> visualDetail;
    private readonly Action<int> playSound;
    private readonly Action<CombatParticleState, int> transform;
    public CombatParticleImpactAdapter(
        CombatSceneRuntime runtime,
        CombatParticleSystem particles,
        CombatHitExecutor hits,
        Func<uint> milliseconds,
        Func<int> visualDetail,
        Action<int> playSound,
        Action<CombatParticleState, int> transformAtExpiry
    )
    {
        this.runtime = runtime;
        this.particles = particles;
        this.hits = hits;
        this.milliseconds = milliseconds;
        this.visualDetail = visualDetail;
        this.playSound = playSound;
        transform = transformAtExpiry;
    }

    public void Reaction(CombatActorImage owner, CombatActorImage target, int reaction) => CombatHitReaction.ParticleReaction(owner, target, reaction, milliseconds(), playSound);
    public void Launch(CombatActorImage owner, CombatActorImage target, CombatDeathReaction force) => hits.LaunchReaction(Index(owner), Index(target), force, milliseconds());
    public void Damage(CombatActorImage owner, CombatActorImage target, int amount) => hits.ApplyRawDamage(Index(owner), Index(target), amount, milliseconds());
    public void Block(CombatActorImage owner, CombatActorImage target) => CombatGuard.KnockBack(owner, target, milliseconds(), Spawn, playSound);
    public void Spawn(CombatHitParticle value) => particles.SpawnPositional(value, milliseconds(), visualDetail(), playSound);
    public void Expire(CombatParticleState particle) => CombatParticleSystem.Expire(particle, transform);
    private int Index(CombatActorImage value)
    {
        for (int i = 0; i < runtime.Actors.Count; i++)
        {
            if (ReferenceEquals(runtime.Actors[i], value))
            {
                return i;
            }
        }

        throw new InvalidDataException("Particle actor does not belong to current battle.");
    }
}
