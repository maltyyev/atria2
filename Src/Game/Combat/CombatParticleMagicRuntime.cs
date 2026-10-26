
namespace Atria.Game.Combat;

/// <summary>40b930/40caf0 exact magic/status branches over the shared native particle pool.</summary>
public sealed class CombatParticleMagicRuntime
{
    private readonly string root;
    private readonly CombatSceneRuntime runtime;
    private readonly CombatParticleSystem pool;
    private readonly Func<uint> now;
    private readonly Func<int> random;
    private readonly Action<int> sound;
    private readonly Action<uint> screenEffect;
    private readonly CombatHitExecutor hits;
    private readonly Dictionary<int, CombatAnimationAssets> magicAssets = [];
    public CombatStatusSlot[] GlobalStatus { get; } = new CombatStatusSlot[CombatStatusSlots.GlobalCount];
    public CombatAnimationAssets? ActiveMagic
    {
        get; private set;
    }
    public int GlobalDepth
    {
        get; set;
    } //45c39c, supplied from actual battle camera context.

    public CombatParticleMagicRuntime(
        string root,
        CombatSceneRuntime runtime,
        CombatParticleSystem pool,
        CombatHitExecutor hits,
        Func<uint> now,
        Func<int> nextNativeRandom,
        Action<int> sound,
        Action<uint> screenEffect
    )
    {
        this.root = root;
        this.runtime = runtime;
        this.pool = pool;
        this.hits = hits;
        this.now = now;
        random = nextNativeRandom;
        this.sound = sound;
        this.screenEffect = screenEffect;
    }

    public void ApplyFrameEffect(int ownerIndex, int id, int argument) => Apply(runtime.Actors[ownerIndex], null, id, argument, false);
    public void Transform(CombatParticleState value, int sequence) => Apply(value.Owner ?? throw new InvalidOperationException("Particle transform has no owner."), value, sequence, 10, true);
    public void ReleaseActiveMagic() => ActiveMagic = null; //40a820 only after40d410 reports no active entries.
    public void Apply(CombatActorImage owner, CombatParticleState? existing, int id, int argument, bool transformed)
    {
        if (id is 148 or 149 or 152 or 154)
        {
            Dispatch(owner, id);
            return;
        }

        if (id is 165 or 166)
        {
            sound(id == 165 ? 340 : 341);
            CombatParticleMagic.Resurrect(runtime.Actors, owner, id == 165 ? 1 : 0);
            return;
        }

        if (!transformed && id >= 101 && id != 999)
        {
            if (!magicAssets.TryGetValue(id, out var assets))
            {
                assets = CombatParticleMagic.Load(root, id);
                magicAssets.Add(id, assets);
            }
            ActiveMagic = assets;
        }

        // Dispatched spells allocate their own particles; do not partially overwrite another live effect.
        if (Dispatch(owner, id))
        {
            return;
        }

        var particle = existing ?? pool.ReserveRetained();
        if (existing is not null)
        {
            particle.X = particle.SpriteX;
            particle.Y = particle.SpriteY;
        }

        if (!transformed)
        {
            if (id == 999)
            {
                particle.Facing = 999;
                return;
            }

            if (id < 101)
            {
                particle.Kind = argument;
                particle.ContainerAssets = owner.Binding?.Assets ?? throw new InvalidOperationException("Frame effect owner has no actual ANI.");
            }
            else
            {
                particle.Kind = 11;
                particle.ContainerAssets = ActiveMagic;
            }
        }
        else if (particle.Facing == -1)
        {
            particle.X = unchecked(particle.X - particle.CurrentSprite.Width);
            particle.SpriteX = particle.X;
        }

        particle.Owner = owner;
        if (!transformed)
        {
            particle.Facing = unchecked((sbyte)owner.State.Facing);
        }

        particle.Assets = particle.ContainerAssets ?? throw new InvalidOperationException("Particle transform lost ANI owner.");
        particle.SequenceIndex = particle.Kind < 11 || transformed ? id : 0;
        if ((uint)particle.SequenceIndex >= (uint)particle.Assets.Animation.Sequences.Length)
        {
            throw new InvalidDataException("Frame effect sequence exceeds actual ANI.");
        }

        particle.FrameIndex = 0;
        var first = particle.FirstFrame;
        sound(first.Sound);
        screenEffect(first.FinalDword);
        particle.FrameInterval = first.ParticleFrameInterval;
        particle.HitDelay = first.HitDelay;
        particle.AccelerationY = first.VerticalAcceleration;


        particle.DepthTolerance = first.DepthTolerance == 0 ? 20 : first.DepthTolerance;
        particle.CompletedCycles = 0;
        if (existing is null)
        {
            var sprite = owner.Animation!.CurrentSprite;
            CombatActorState a = owner.State;
            int facing = unchecked((sbyte)a.Facing);
            if (particle.Kind is 0 or 1 or 2 or 3 or 4 or 11)
            {
                if (sprite.Points.Length < 1)
                {
                    throw new InvalidDataException("Frame effect requires native sprite anchor point.");
                }

                int anchor = sprite.Points[0].X;
                if (particle.Kind == 4)
                {
                    particle.Facing = -particle.Facing;
                }

                particle.X = unchecked(a.SpriteX + (facing == 1 ? anchor : particle.Kind is 2 or 3 ? anchor - sprite.Width : sprite.Width - anchor));
                particle.Y = unchecked(sprite.Points[0].Y + a.SpriteY);
                particle.Depth = unchecked(a.Depth + (particle.Kind is 2 or 3 ? -10 : particle.Kind == 11 ? 1 : -4));
            }
            else if (particle.Kind == 5)
            {
                int ownerIndex = Index(owner), targetIndex = runtime.TargetIndices[ownerIndex];
                if (targetIndex < 0)
                {
                    particle.Facing = 0;
                    return;
                }

                var target = runtime.Actors[targetIndex];
                particle.X = unchecked(target.Animation!.CurrentSprite.Metadata2 + target.State.SpriteX);
                particle.Y = unchecked(target.State.Depth - particle.CurrentSprite.Height);
                particle.Depth = target.State.Depth;
            }
        }

        particle.ExpiryRule = first.ParticleExpiryRule;
        particle.VelocityX = unchecked(first.HorizontalStep * particle.Facing);
        particle.VelocityY = first.VerticalSpeed;
        particle.AdvanceFrame(now(), sound);
    }

    public static int Relation(CombatActorImage owner, CombatActorImage target)
    {
        CombatActorState a = owner.State, t = target.State;
        if (t.SpawnCondition != 0 || t.Health < 1 || t.Active == 0)
        {
            return 2;
        }

        sbyte team = unchecked((sbyte)a.Team), other = unchecked((sbyte)t.Team);
        if (team >= 100)
        {
            return other < 100 ? 1 : 0;
        }

        return team != other && t.CanTurn != 0 ? 1 : 0;
    }

    private void Status(CombatActorImage owner, int code, int bank, int field3)
    {
        int deadline = unchecked((owner.State.Rank + 5) * 4000 + (int)now());
        CombatStatusSlots.Insert(
            bank == 0 ? owner.State.StatusBank.AsSpan() : GlobalStatus,
            code,
            bank,
            field3,
            deadline,
            owner.Binding!.Info.Type,
            unchecked((int)now())
        );
    }

    private CombatParticleState Spawn(CombatActorImage owner, int x, int y, int depth, int facing = 1) => pool.SpawnProjectile(
        owner,
        ActiveMagic ?? throw new InvalidOperationException("Magic dispatch requires loaded ANI."),
        x,
        y,
        depth,
        facing,
        now(),
        sound,
        screenEffect
    );
    private void Launch(CombatActorImage owner, CombatActorImage target) => hits.LaunchReaction(Index(owner), Index(target), new(10, 10, 6, 3, unchecked((sbyte)owner.State.Facing)), now());
    private int Index(CombatActorImage value)
    {
        for (int i = 0; i < runtime.Actors.Count; i++)
        {
            if (ReferenceEquals(runtime.Actors[i], value))
            {
                return i;
            }
        }

        throw new InvalidDataException("Magic actor outside current battle.");
    }

    public bool Dispatch(CombatActorImage owner, int id)
    {
        CombatActorState a = owner.State;
        int level = a.Rank, deadline = unchecked((level + 5) * 4000 + (int)now());
        bool Roll(CombatActorImage target) => target.State.MagicRank < random() % 11;
        void Affect(int relation, Action<CombatActorImage> apply)
        {
            foreach (var target in runtime.Actors)
            {
                if (Relation(owner, target) == relation)
                {
                    apply(target);
                }
            }
        }

        switch (id)
        {
            case 106:
                for (int i = 0; i < unchecked(level + 2); i++)
                {
                    Spawn(
                        owner,
                        unchecked(owner.Animation!.CurrentSprite.Metadata2 + a.SpriteX),
                        unchecked(a.Depth - 30),
                        unchecked(a.Depth + 1),
                        unchecked((sbyte)a.Facing)
                    );
                }

                return true;
            case 107:
                Status(owner, 7, 0, 0);
                a.CriticalBonus = unchecked(a.CriticalBonus + (40));
                return false;
            case 108:
                Status(owner, 8, 0, 0);
                a.DefenseBonus = unchecked(a.DefenseBonus + (5));
                return false;
            case 111:
                Status(owner, 11, 0, 0);
                a.Haste = 1;
                return false;
            case 112:
                Status(owner, 12, 0, 0);
                a.Invulnerable = unchecked((level + 10) * 30);
                a.AttackBonus = unchecked(a.AttackBonus + (6));
                a.CriticalBonus = unchecked(a.CriticalBonus + (6));
                return false;
            case 114:
                int x = 0;
                for (int i = 0; i < unchecked(level + 3); i++)
                {
                    x += 30;
                    if (x > 500)
                    {
                        x = 20;
                    }

                    int depth = unchecked(random() % 100 + GlobalDepth), y = -(random() % 100);
                    var p = Spawn(owner, x, y, depth);
                    if (p.VelocityX < 0)
                    {
                        p.VelocityX = -p.VelocityX;
                    }
                }

                return true;
            case 116:
                Status(owner, 16, 1, 1);
                Affect(1, t =>
                {
                    if (Roll(t))
                    {
                        t.State.Slow = deadline;
                    }
                });
                break;
            case 117:
                Status(owner, 17, 1, 0);
                Affect(0, t => t.State.AttackBonus = unchecked(t.State.AttackBonus + (4)));
                break;
            case 119:
                foreach (var actor in runtime.Actors)
                {
                    if (actor.Binding is not null)
                    {
                        actor.State.ClearTransientEffects();
                    }
                }

                Array.Clear(GlobalStatus);
                Spawn(owner, 320, 180, 400);
                return true;
            case 121:
                {
                    var p = Spawn(owner, 0, 0, 350);
                    if (p.VelocityX < 0)
                    {
                        p.VelocityX = -p.VelocityX;
                    }

                    p.Facing = 1;
                    return true;
                }

            case 126:
                Status(owner, 26, 1, 1);
                Affect(1, t =>
                {
                    if (Roll(t))
                    {
                        t.State.AttackBonus = unchecked(t.State.AttackBonus + (-4));
                    }
                });
                Spawn(owner, 250, 180, 400);
                return true;
            case 127:
                Status(owner, 27, 1, 1);
                Affect(1, t => t.State.DefenseBonus = unchecked(t.State.DefenseBonus + (-3)));
                Spawn(owner, 250, 180, 400);
                return true;
            case 128:
                Status(owner, 28, 0, 0);
                a.Translucent = 1;
                a.FlashTicks = deadline;
                a.Invulnerable = deadline;
                Spawn(owner, 300, 180, 400).Facing = 1;
                return true;
            case 130:
                Status(owner, 30, 1, 0);
                Affect(0, t =>
                {
                    t.State.MagicBarrier = 1;
                    t.State.DefenseBonus = unchecked(t.State.DefenseBonus + (3));
                });
                Spawn(owner, 300, 180, 400);
                return true;
            case 131:
                Status(owner, 31, 1, 1);
                Affect(1, t =>
                {
                    t.State.Poison = 1;
                    t.State.DefenseBonus = unchecked(t.State.DefenseBonus + (-3));
                    t.State.AttackBonus = unchecked(t.State.AttackBonus + (-3));
                });
                break;
            case 132:
                Status(owner, 32, 1, 1);
                Affect(1, t =>
                {
                    if (Roll(t))
                    {
                        t.State.Poison = 1;
                    }
                });
                Spawn(owner, 320, 180, 400);
                return true;
            case 135:
                Status(owner, 35, 1, 0);
                Affect(0, t =>
                {
                    t.State.DefenseBonus = unchecked(t.State.DefenseBonus + (1));
                    t.State.AttackBonus = unchecked(t.State.AttackBonus + (2));
                    t.State.CriticalBonus = unchecked(t.State.CriticalBonus + (10));
                    t.State.GuardBonus = unchecked(t.State.GuardBonus + (2));
                    Spawn(owner, t.State.SpriteX, t.State.Depth, unchecked(t.State.Depth + 1));
                });
                return true;
            case 139:
                Spawn(owner, 280, 150, 400);
                CombatParticleMagic.Resurrect(runtime.Actors, owner, 0);
                return true;
            case 140:
                Status(owner, 40, 1, 0);
                Affect(0, t =>
                {
                    t.State.AttackBonus = unchecked(t.State.AttackBonus + (3));
                    t.State.UndeadAttackBonus = unchecked(t.State.UndeadAttackBonus + (5));
                });
                Spawn(owner, 300, 180, 400);
                return true;
            case 141:
                foreach (var t in runtime.Actors)
                {
                    if (Relation(owner, t) != 2 && Roll(t))
                    {
                        t.State.Health = Math.Max(0, unchecked(t.State.Health + (-5 - a.MagicRank) * 20));
                        Launch(owner, t);
                    }
                }

                Spawn(owner, 280, 180, 400);
                return true;
            case 143:
                for (int i = 0; i < GlobalStatus.Length; i++)
                {
                    if (GlobalStatus[i].Code == 32)
                    {
                        GlobalStatus[i].Deadline = 0;
                        break;
                    }
                }

                Affect(0, t =>
                {
                    t.State.Poison = 0;
                    Spawn(owner, t.State.SpriteX, unchecked(t.Animation!.CurrentSprite.Height - 40 + t.State.SpriteY), unchecked(t.State.Depth + 1));
                });
                return true;
            case 144:
                Affect(1, t =>
                {
                    if (t.Binding!.Info.Type is 12 or 21 or 22 or 23 or 37 or 41)
                    {
                        Launch(owner, t);
                        t.State.Health = Math.Max(0, unchecked(t.State.Health + (-5 - level) * 10));
                    }
                });
                Spawn(owner, 320, 180, 400);
                return true;
            case 145:
                Affect(0, t =>
                {
                    Spawn(owner, t.State.SpriteX, unchecked(t.State.Depth - 10), unchecked(t.State.Depth + 1));
                    t.State.Health = Math.Min(t.State.MaximumHealth, unchecked(t.State.Health + (level + 2) * 10));
                });
                return true;
            case 146:
                Affect(1, t =>
                {
                    Launch(owner, t);
                    t.State.FrameInterval = CombatActorMaintenance.ModifyActionInterval(t.State, 100);
                    t.State.Health = Math.Max(0, unchecked(t.State.Health + (-20 - level) * 10));
                });
                Spawn(owner, 0, 0, 400);
                return true;
            case 148:
                a.Health = Math.Min(a.MaximumHealth, unchecked(a.Health + level * 30));
                return false;
            case 149:
                Status(owner, 49, 0, 0);
                a.GuardBonus = unchecked(a.GuardBonus + ((level > 5 ? 1 : 0) + 2));
                return false;
            case 152:
                Status(owner, 52, 0, 0);
                a.AttackBonus = unchecked(a.AttackBonus + (5));
                return false;
            case 154:
                Status(owner, 54, 0, 0);
                a.AttackBonus = unchecked(a.AttackBonus + (a.Defense));
                a.DefenseBonus = unchecked(a.DefenseBonus + (-a.Defense));
                return false;
            case 156:
                Status(owner, 30, 1, 0);
                Affect(0, t => t.State.DefenseBonus = unchecked(t.State.DefenseBonus + (3)));
                Spawn(owner, 300, 180, 400);
                return true;
            case 157:
                Affect(0, t =>
                {
                    t.State.Health = t.State.MaximumHealth;
                    Spawn(owner, t.State.SpriteX, unchecked(t.Animation!.CurrentSprite.Height - 70 + t.State.SpriteY), unchecked(t.State.Depth + 1));
                });
                return true;
            default:
                return false; //Native explicitdefault0; caller continues generic animation setup.
        }

        Spawn(owner, 280, 180, 400);
        return true;
    }

}
