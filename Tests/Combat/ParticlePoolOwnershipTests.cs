using System.Text;
using Atria.Game.Combat;
using Atria.Resources;

namespace Atria.Tests.Combat;

public static class ParticlePoolOwnershipTests
{
    public static void Run(string root)
    {
        using var archive = BinderArchive.Open(Path.Combine(root, "INF.000"));
        var characters = archive.Entries.SelectMany(entry =>
            CombatInfoDefinition.Parse(archive.ReadMember(entry.Index)).Characters).ToArray();

        foreach (string resource in new[] { "GOBLIN2", "ZOMBI2" })
        foreach (byte facing in new byte[] { 1, 255 })
        foreach (int spell in new[] { 106, 119, 120, 145 })
        {
            var (runtime, state) = PartySpellTests.Fixture(root);
            var enemy = runtime.Actors[0];
            var caster = runtime.Actors[runtime.Scene.PartyStartIndex + 4];
            var info = characters.First(c => Encoding.ASCII.GetString(c.AnimationResource.Span)
                .StartsWith(resource, StringComparison.Ordinal));
            var art = CombatAnimationAssets.Load(root, info.AnimationResource.Span);
            enemy.Binding = new(0, 0, info, art);
            enemy.Animation = new(enemy.State, info, art);
            enemy.State.Facing = facing;

            var emitter = art.Animation.Sequences.SelectMany((sequence, index) =>
                sequence.Frames.Select((frame, position) => (Sequence: index, Position: position, Frame: frame)))
                .First(e => e.Frame.Effect > 0 && e.Frame.Effect < 101
                    && art.Animation.Sequences[e.Frame.Effect].Frames[0].SecondaryEffect > 0);
            enemy.Animation.BeginSequenceCommand(emitter.Sequence, 28, 1000);
            enemy.State.FrameIndex = checked((short)emitter.Position);
            enemy.Animation.RebindFromFrameCounter();

            var pool = CombatParticleSystem.Load(root);
            var hits = new CombatHitExecutor(runtime, state, () => 1, _ => { }, _ => { });
            var magic = new CombatParticleMagicRuntime(root, runtime, pool, hits, () => 1000, () => 1, _ => { }, _ => { });
            void Emit() => magic.ApplyFrameEffect(0, emitter.Frame.Effect, emitter.Frame.EffectArgument);

            Emit();
            var replaced = pool.Particles[0];
            for (int i = 1; i < CombatParticleSystem.Capacity - 1; i++)
            {
                pool.SpawnPositional(new(200, 200, 300, 1, 1), 1000, 0, _ => { });
            }
            Emit();
            var surviving = pool.Particles[^1];
            int secondary = surviving.FirstFrame.SecondaryEffect;
            Check(pool.NextSlot == 0, "live pool wraps to the first enemy projectile");

            magic.Apply(caster, null, spell, 0, false);
            Check(ReferenceEquals(replaced.Owner, caster)
                && ReferenceEquals(replaced.Assets, magic.ActiveMagic)
                && ReferenceEquals(replaced.ContainerAssets, replaced.Assets)
                && replaced.SequenceIndex == 0, "replacement has one consistent spell owner and animation bank");
            Check(ReferenceEquals(surviving.Owner, enemy) && ReferenceEquals(surviving.Assets, art)
                && ReferenceEquals(surviving.ContainerAssets, art), "unreplaced projectile retains its enemy animation bank");

            CombatParticleSystem.Expire(replaced, magic.Transform);
            CombatParticleSystem.Expire(surviving, magic.Transform);
            Check(surviving.SequenceIndex == secondary && ReferenceEquals(surviving.Assets, art)
                && ReferenceEquals(surviving.CurrentSprite, art.Sprites.Frames[surviving.CurrentFrame.SpriteFrameIndex]),
                "secondary animation remains in its original sprite bank for either facing");
        }
        Console.WriteLine("Particle ownership PASS: 16 real goblin/zombie, facing and spell combinations with a full live pool.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Particle ownership: " + message);
        }
    }
}
