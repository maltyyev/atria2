using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>40b4c0/40b610/40b400/40cfc0/40d030/40d070/40d410, fixed native50-slot overwrite ring.</summary>
public sealed class CombatParticleSystem(CombatAnimationAssets effectAssets)
{
    public const int Capacity = 50;
    private readonly CombatParticleState[] particles = Enumerable.Range(0, Capacity).Select(_ => new CombatParticleState()).ToArray();
    public IReadOnlyList<CombatParticleState> Particles => particles;
    public int NextSlot
    {
        get; private set;
    }

    public CombatParticleState ReserveRetained()
    {
        var value = particles[NextSlot];
        if (++NextSlot >= Capacity)
        {
            NextSlot = 0;
        }

        return value;
    }

    public static CombatParticleSystem Load(string root) => new(CombatAnimationAssets.Load(root, "EFFECT"u8));
    //4077f0 camera translation deliberately leaves particle depth unchanged.
    public void Translate(int dx, int dy)
    {
        foreach (var particle in particles)
        {
            if (particle.Facing == 0)
            {
                continue;
            }

            particle.X = unchecked(particle.X + dx);
            particle.SpriteX = unchecked(particle.SpriteX + dx);
            particle.Y = unchecked(particle.Y + dy);
            particle.SpriteY = unchecked(particle.SpriteY + dy);
        }
    }

    public CombatParticleState SpawnProjectile(
        CombatActorImage owner,
        CombatAnimationAssets actualMagic,
        int x,
        int y,
        int depth,
        int facing,
        uint now,
        Action<int> playSound,
        Action<uint> applyScreenEffect
    )
    {
        if (actualMagic.Animation.Sequences.Length < 1)
        {
            throw new InvalidDataException("Magic requires actual first sequence.");
        }
        //40c920 deliberately does not zero the overwritten pool slot.
        var particle = ReserveRetained();
        particle.Assets = particle.ContainerAssets = actualMagic;
        particle.SequenceIndex = 0;
        particle.Owner = owner;
        particle.Kind = 11;
        particle.FrameIndex = 0;
        particle.Facing = facing;
        var first = particle.FirstFrame;
        playSound(first.Sound);
        applyScreenEffect(first.FinalDword);
        particle.FrameInterval = first.ParticleFrameInterval;
        particle.HitDelay = first.HitDelay;
        particle.AccelerationY = first.VerticalAcceleration;


        particle.DepthTolerance = first.DepthTolerance == 0 ? 20 : first.DepthTolerance;
        particle.CompletedCycles = 0;
        particle.X = particle.SpriteX = x;
        particle.Y = particle.SpriteY = y;
        particle.Depth = depth;
        particle.ExpiryRule = first.ParticleExpiryRule;
        particle.VelocityX = unchecked(first.HorizontalStep * facing);
        particle.VelocityY = first.VerticalSpeed;
        particle.AdvanceFrame(now, playSound);
        return particle;
    }

    public CombatParticleState? SpawnActor(CombatActorImage actor, int id, uint now, int visualDetail, Action<int> playSound)
    {
        if (visualDetail >= 3)
        {
            return null;
        }

        var sprite = (actor.Animation ?? throw new InvalidOperationException("Actor particle requires bound current sprite.")).CurrentSprite;
        CombatActorState data = actor.State;
        return Spawn(
            unchecked(sprite.Metadata2 + data.SpriteX),
            unchecked(data.Depth - 5),
            unchecked(data.Depth + 1),
            id,
            unchecked((sbyte)data.Facing),
            2,
            now,
            playSound
        );
    }

    public CombatParticleState? SpawnPositional(CombatHitParticle value, uint now, int visualDetail, Action<int> playSound) => visualDetail >= 3 ? null : Spawn(value.X, value.Y, value.Depth, value.Id, value.Facing, 1, now, playSound);
    private CombatParticleState Spawn(int x, int y, int depth, int id, int facing, int category, uint now, Action<int> playSound)
    {
        int sequence = id - 1;
        if ((uint)sequence >= (uint)effectAssets.Animation.Sequences.Length)
        {
            throw new InvalidDataException("Effect ID is outside native1-based ANI sequences.");
        }

        var particle = ReserveRetained();
        particle.Clear();
        particle.Assets = particle.ContainerAssets = effectAssets;
        particle.SequenceIndex = sequence;
        particle.X = x;
        particle.Y = y;
        particle.Depth = depth;
        particle.Facing = facing;
        playSound(particle.FirstFrame.Sound);
        particle.FrameIndex = 0;
        particle.FrameInterval = 25;
        particle.ExpiryRule = 5;
        particle.Kind = category;
        particle.AdvanceFrame(now, playSound);
        return particle;
    }

    // Call once at native40d410 logical render stage, after actors enter the common depth queue.
    // Draw packets intentionally capture PRE-update frame/position. Callbacks run synchronously in native slot order.
    public CombatParticlePass Advance(uint now, Action<int> playSound, Action<CombatParticleState, int> transformAtExpiry, Action<CombatParticleState> collideWithActors)
    {
        List<CombatParticleDraw> draws = [];
        bool active = false;
        for (int slot = 0; slot < Capacity; slot++)
        {
            var particle = particles[slot];
            if (particle.Facing == 0)
            {
                continue;
            }

            active = true;
            if (particle.Facing == 999)
            {
                draws.Add(new(slot, 0, 0, 0, 999, null));
                particle.Facing = 0;
                continue;
            }

            if (!KeepAlive(particle, transformAtExpiry))
            {
                continue;
            }

            int code = particle.Kind == 0 ? unchecked(particle.Facing << 3) : unchecked(particle.Facing * (particle.Kind == 11 ? 11 : 10));
            draws.Add(new(slot, particle.SpriteX, particle.SpriteY, particle.Depth, code, particle.CurrentSprite));
            AdvanceMotion(particle, now, playSound);
            if (unchecked((short)particle.FirstFrame.Damage) != 0)
            {
                collideWithActors(particle);
            }
        }

        return new(active, draws.ToArray());
    }

    internal static bool KeepAlive(CombatParticleState particle, Action<CombatParticleState, int> transform)
    {
        bool expire = particle.ExpiryRule switch
        {
            0 or 4 => particle.X < -20 || particle.X > 660,
            1 => particle.Depth <= unchecked(particle.CurrentSprite.Height - 1 + particle.SpriteY),
            5 => particle.CompletedCycles != 0,
            _ => false
        };
        if (!expire)
        {
            return true;
        }

        Expire(particle, transform);
        return false;
    }

    public static void Expire(CombatParticleState particle, Action<CombatParticleState, int> transform)
    {
        int effect = particle.FirstFrame.SecondaryEffect;
        if (effect != 0)
        {
            transform(particle, effect);
        }
        else
        {
            particle.Clear();
        }
    }

    internal static void AdvanceMotion(CombatParticleState particle, uint now, Action<int> playSound)
    {
        if (particle.NextFrameTime > unchecked((int)now))
        {
            return;
        }

        unchecked
        {
            particle.X += particle.VelocityX;
            int acceleration = particle.AccelerationY, speed = particle.VelocityY;
            if (acceleration < 1)
            {
                particle.Y += speed;
                particle.VelocityY = speed - acceleration;
            }
            else
            {
                particle.Y -= speed;
                particle.VelocityY = speed - acceleration;
                if (particle.VelocityY < 1)
                {
                    particle.VelocityY = 1;
                    particle.AccelerationY = -acceleration;
                    particle.AdvanceFrame(now, playSound);
                    return;
                }
            }
        }

        particle.AdvanceFrame(now, playSound);
    }

}
