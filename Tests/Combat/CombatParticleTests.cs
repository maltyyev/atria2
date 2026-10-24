using System.Buffers.Binary;
using Atria.Game.Combat;
using Atria.Game.State;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Tests.Combat;

public static class CombatParticleTests
{
    public static string Run(string root)
    {
        AnimationFrame Frame(int sound, short x = 0, short y = 0)
        {
            ushort[] words = new ushort[28];
            words[1] = unchecked((ushort)x);
            words[2] = unchecked((ushort)y);
            words[7] = (ushort)sound;
            return new(words, 0, x, y);
        }

        var first = Frame(3);
        var frame1 = Frame(5, 2, 4);
        var frame2 = Frame(6, 3, 5);
        var sentinel = Frame(7);
        var sprite = new SpriteFrame(0, 0, 0, [], [], 10, 20, []);
        var assets = new CombatAnimationAssets(new("fixture"u8.ToArray(), [new(0, [first, frame1, frame2, sentinel])]), new(false, [sprite]));
        var system = new CombatParticleSystem(assets);
        List<int> sounds = [];
        var particle = system.SpawnPositional(new(100, 200, 300, 1, -1), 0, 0, sounds.Add)!;
        Check(sounds.SequenceEqual(new[] { 3, 5 }) && particle.FrameIndex == 1 && particle.SpriteX == 88 && particle.SpriteY == 204, "spawn binds first sound then frame1 and exact mirrored origin");
        Action<CombatParticleState, int> noTransform = (_, _) => throw new InvalidOperationException("Unexpected transform");
        Action<CombatParticleState> noCollision = _ => throw new InvalidOperationException("Unexpected damage");
        var pass = system.Advance(24, sounds.Add, noTransform, noCollision);
        Check(pass.Draws.Single().Code == -10 && particle.FrameIndex == 1, "deadline24 keeps frame1 and literal queuecode");
        pass = system.Advance(25, sounds.Add, noTransform, noCollision);
        Check(pass.Draws.Single().X == 88 && particle.FrameIndex == 2 && particle.CompletedCycles == 1, "deadline equality advances after old packet capture");
        pass = system.Advance(26, sounds.Add, noTransform, noCollision);
        Check(pass.HadActiveParticles && pass.Draws.Length == 0 && particle.Facing == 0, "penultimate loop expires on following pool scan");
        Check(!system.Advance(27, sounds.Add, noTransform, noCollision).HadActiveParticles, "empty scan reports native noactive");
        int cursor = system.NextSlot;
        Check(system.SpawnPositional(new(0, 0, 0, 1, 1), 0, 3, sounds.Add) is null && system.NextSlot == cursor, "detail3 suppresses effect spawning before ring consumption");
        system = new(assets);
        for (int i = 0; i < 51; i++)
        {
            system.SpawnPositional(new(i, 0, 0, 1, 1), 0, 0, _ =>
            {
            });
        }

        Check(system.NextSlot == 1 && system.Particles[0].X == 50, "50slot native ring overwrites living slot0");
        int depth = system.Particles[0].Depth;
        system.Translate(-7, 9);
        Check(system.Particles[0].X == 43 && system.Particles[0].Y == 9 && system.Particles[0].Depth == depth, "camera translates both coordinates but preserves particle depth");
        // Actual resources: this tests the same first-frame/sequence/sprite contract used at runtime.
        var actual = CombatParticleSystem.Load(root);
        var actualParticle = actual.SpawnPositional(new(200, 200, 220, 1, 1), 100, 0, _ =>
        {
        })!;
        Check(actualParticle.FrameIndex == 1 && actualParticle.CurrentSprite.Width > 0, "actual EFFECT.ANI and SPR bindings");
        var magic = CombatParticleMagic.Load(root, 106);
        var owner = new CombatActorImage();
        owner.State.Team = 1;
        W(owner.State, 0x17c, 3);
        var alphaFrame = magic.Sprites.Frames[0];
        var alphaRun = alphaFrame.Runs.First(r => r.Alpha.Length != 0);
        Check(magic.Sprites.PerPixelAlpha && alphaFrame.Width == 41 && alphaFrame.Height == 34 && alphaRun.Colors.Length == 5, "authentic MAGIC06 first defective native cursor case is a five-pixel alpha pair run");
        ushort[] alphaTarget = Enumerable.Repeat((ushort)0xffff, 640 * 480).ToArray();
        CombatSpriteEffects.DrawAlpha(alphaFrame, alphaTarget, 640, 100, 100, true, new(0, 0, 640, 401));
        int alphaX = 100 + alphaFrame.Width - 1 - alphaRun.X, alphaY = 100 + alphaRun.Y;
        Check(alphaTarget[alphaY * 640 + alphaX] == SpriteRaster.Alpha565(alphaRun.Colors[0], 0xffff, alphaRun.Alpha[0]), "actual decoded pair uses native width-1 mirroredalpha origin and exact component blend");
        using (var animationArchive = BinderArchive.Open(Path.Combine(root, "ANI.000")))
        {
            HashSet<string> alphaSources = new(StringComparer.OrdinalIgnoreCase);
            int renderedFrames = 0;
            foreach (var entry in animationArchive.Entries)
            {
                if (!entry.Name.StartsWith("MAGIC", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var actualMagic = CombatAnimationAssets.Load(root, entry.RawName.Span);
                string spriteName = System.Text.Encoding.ASCII.GetString(actualMagic.Animation.SpriteReference);
                if (!actualMagic.Sprites.PerPixelAlpha || !alphaSources.Add(spriteName))
                {
                    continue;
                }

                foreach (var affectedFrame in actualMagic.Sprites.Frames)
                {
                    CombatSpriteEffects.DrawAlpha(affectedFrame, alphaTarget, 640, 100, 100, true, new(0, 0, 640, 401));
                    renderedFrames++;
                }
            }

            Check(alphaSources.Count == 28 && renderedFrames > 28, "every actual alpha MAGIC sprite renders all frames through corrected bounded mirror path");
        }

        var magicParticle = actual.SpawnProjectile(owner, magic, 300, 100, 150, 1, 100, _ =>
        {
        }, _ =>
        {
        });
        Check(ReferenceEquals(magicParticle.Owner, owner) && magicParticle.Kind == 11, "actual MAGIC06 asset and owner binding");
        var scene = CombatSceneDefinition.Load(root, new(FixedEncounterKind.Forced, 154));
        var runtime = new CombatSceneRuntime(root, scene, 0);
        foreach (var actor in runtime.Actors)
        {
            actor.State.Team = 1;
            W(actor.State, 0x170, 0);
            W(actor.State, 0x16c, 100);
        }

        Check(CombatParticleMagic.Resurrect(runtime.Actors, owner, 1) == 0, "specialresurrection rejects actual EBAT154 classes26/30/31");
        Check(CombatParticleMagic.Resurrect(runtime.Actors, owner, 0) == 5
&& runtime.Actors.Take(5).All(a => LegacyActorFixture.ReadInt32LittleEndian(a.State.AsSpan(0x170, 4)) == 100), "massresurrection caps five with actual INF classes and maxHP");
        first.Words[8] = 90;
        first.Words[9] = 2;
        first.Words[23] = 3;
        first.Words[24] = 20;
        first.Words[25] = 0;
        var target = new CombatActorImage();
        target.State.Team = 2;
        W(target.State, 0x1ec, 1);
        W(target.State, 0x78, 40);
        W(target.State, 0x2bc, 1);
        WS(target.State, 0x107, -1000);
        WS(target.State, 0x109, -1000);
        WS(target.State, 0x10b, 1000);
        WS(target.State, 0x10d, 1000);
        var state = new GameState();
        state.AvailableItems[82] = 1;
        var impact = new Impact();
        system = new(assets);
        particle = system.SpawnProjectile(owner, assets, 20, 20, 40, 1, 0, _ =>
        {
        }, _ =>
        {
        });
        Check(CombatParticleCollision.Apply(particle, [owner, target], state, () => false, () => throw new InvalidOperationException(), impact) == 1
&& impact.Amount == 22 && impact.ReactionId == 3, "projectile two-stage nonmagic halving and reaction gates");
        W(target.State, 0x240, 0);
        W(target.State, 0x11c, 0);
        W(target.State, 0x1c0, 5);
        impact = new();
        CombatParticleCollision.Apply(particle, [owner, target], state, () => true, () => 7, impact);
        Check(impact.Amount == 86, "magic amount90/3+level3*20-(7mod5)*2 ignores nonmagic half flag");
        var shake = new CombatParticleScreenShake();
        shake.Apply(0x300);
        Check(shake.X == 5 && shake.Duration == 10, "lowest shake flag takes priority");
        Check(Enumerable.Range(0, 4).Select(_ => shake.AdvanceBackground().X).SequenceEqual(new[] { 5, 0, 0, 5 }), "native shake counter increments before two-of-four phase");
        runtime.BindDeclaredAnimations(100);
        var caster = runtime.Actors[0];
        W(caster.State, 0x1ec, 2);
        W(caster.State, 0x170, 50);
        W(caster.State, 0x16c, 100);
        W(caster.State, 0x17c, 3);
        var magicPool = CombatParticleSystem.Load(root);
        int randomCalls = 0;
        var hitExecutor = new CombatHitExecutor(runtime, state, () =>
        {
            randomCalls++;
            return 0;
        }, _ => throw new InvalidOperationException(), _ =>
        {
        });
        var spells = new CombatParticleMagicRuntime(root, runtime, magicPool, hitExecutor, () => 100, () =>
        {
            randomCalls++;
            return 0;
        }, _ =>
        {
        }, _ =>
        {
        });
        spells.Apply(caster, null, 148, 0, false);
        Check(LegacyActorFixture.ReadInt32LittleEndian(caster.State.AsSpan(0x170, 4)) == 100 && magicPool.NextSlot == 0, "directheal148 bypasses pool reservation");
        spells.Apply(caster, null, 149, 0, false);
        Check(LegacyActorFixture.ReadInt32LittleEndian(caster.State.AsSpan(0x244, 4)) == 28100
&& LegacyActorFixture.ReadInt32LittleEndian(caster.State.AsSpan(0x248, 4)) == 48, "directbuff149 exact status code/deadline");
        spells.Apply(caster, null, 106, 0, false);
        Check(magicPool.NextSlot == 5 && magicPool.Particles.Count(p => p.Facing != 0) == 4 && spells.ActiveMagic is not null && randomCalls == 0, "actualmagic106 reserves one slot then emits level+2 projectiles without RNG");
        var enemy = runtime.Actors[1];
        enemy.State.Team = 2;
        enemy.State.CanTurn = 1;
        W(enemy.State, 0x170, 100);
        W(enemy.State, 0x224, 1);
        W(enemy.State, 0x17c, 3);
        int curseRolls = 0;
        var curse = new CombatParticleMagicRuntime(root, runtime, magicPool, hitExecutor, () => 100, () =>
        {
            curseRolls++;
            return 10;
        }, _ =>
        {
        }, _ =>
        {
        });
        curse.Apply(caster, null, 116, 0, false);
        Check(curseRolls == 1 && LegacyActorFixture.ReadInt32LittleEndian(enemy.State.AsSpan(0x2ac, 4)) == 28100
&& curse.GlobalStatus[0].Code == 15, "actualmagic116 uses one eligible-enemy RNG and shared status15");
        W(caster.State, 0x170, 0);
        Check(CombatParticleMagicRuntime.Relation(owner, caster) == 2, "invalid relation2 preserved beyond decompiler bool");
        return "Combat particles: native ring/frame/sound/mirror/deadline/order, actual EFFECT/MAGIC06 resources, projectile damage gates";
    }

    private sealed class Impact : ICombatParticleImpactEffects
    {
        public int Amount, ReactionId;
        public void Reaction(CombatActorImage owner, CombatActorImage target, int reaction) => ReactionId = reaction;
        public void Damage(CombatActorImage owner, CombatActorImage target, int amount) => Amount = amount;
        public void Launch(CombatActorImage owner, CombatActorImage target, CombatDeathReaction force) => throw new InvalidOperationException();
        public void Block(CombatActorImage owner, CombatActorImage target) => throw new InvalidOperationException();
        public void Spawn(CombatHitParticle particle) => throw new InvalidOperationException();
        public void Expire(CombatParticleState particle) => throw new InvalidOperationException();
    }

    private static void W(object bytes, int offset, int value) => LegacyActorFixture.WriteInt32LittleEndian(bytes.FixtureSpan(offset, 4), value);
    private static void WS(object bytes, int offset, short value) => LegacyActorFixture.WriteInt16LittleEndian(bytes.FixtureSpan(offset, 2), value);
    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
