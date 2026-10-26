using System.Text;
using Atria.Game.Combat;
using Atria.Resources;

namespace Atria.Tests.Combat;

public static class ProjectileReactionTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Projectile reaction: " + message);
    }

    public static string Run(string root)
    {
        var (runtime, state) = PartySpellTests.Fixture(root);
        var caster = runtime.Actors[runtime.Scene.PartyStartIndex + 1];
        var target = runtime.Actors[0];
        var spell = caster.Binding!.Info.Commands.Where(c => c.Category > 0)
            .SelectMany(c => caster.Binding.Assets.Animation.Sequences[c.Animation].Frames
                .Select((frame, index) => (Command: c, Frame: frame, Index: index)))
            .Where(p => CombatProjectileForecast.IsProjectile(p.Frame.Effect)
                && p.Frame.SpriteFrameIndex >= target.Binding!.Assets.Sprites.Frames.Length)
            .First(p =>
            {
                var first = CombatParticleMagic.Load(root, p.Frame.Effect).Animation.Sequences[0].Frames[0];
                return first.Damage > 0 && first.Reaction <= 7;
            });
        var command = spell.Command;
        int emittingFrame = spell.Index;
        caster.Animation!.BeginSequenceCommand(command.Animation, 28, 1000);
        caster.State.FrameIndex = checked((short)emittingFrame);
        caster.Animation.RebindFromFrameCounter();
        CombatSpriteProjection.Update(caster.State, caster.Binding.Info.Type, caster.Animation.CurrentFrame, caster.Animation.CurrentSprite);
        Check(caster.Animation.CurrentFrame.SpriteFrameIndex >= target.Binding!.Assets.Sprites.Frames.Length,
            "real Mist emitter ordinal exceeds the real enemy sprite bank, reproducing the reported failure");
        Console.WriteLine($"Mist effect={spell.Frame.Effect}, emitter sprite={caster.Animation.CurrentFrame.SpriteFrameIndex}; target sprite count={target.Binding.Assets.Sprites.Frames.Length}.");

        var pool = CombatParticleSystem.Load(root);
        var hits = new CombatHitExecutor(runtime, state, () => 1, _ => { }, _ => { });
        var magic = new CombatParticleMagicRuntime(root, runtime, pool, hits, () => 1000, () => 1, _ => { }, _ => { });
        magic.ApplyFrameEffect(runtime.Scene.PartyStartIndex + 1, spell.Frame.Effect, spell.Frame.EffectArgument);
        var projectile = pool.Particles.First(p => p.Facing != 0 && p.FirstFrame.Damage != 0);
        var t = target.State;
        t.Reaction = 0;
        t.ActionFlags = 0;
        t.Invulnerable = t.HitPriority = 0;
        t.MagicDefense = 0;
        t.Health = t.MaximumHealth = 10_000;
        t.Depth = projectile.Depth;
        t.HurtLeft = checked((short)projectile.SpriteX);
        t.HurtRight = checked((short)(projectile.SpriteX + projectile.CurrentSprite.Width));
        t.HurtTop = checked((short)projectile.SpriteY);
        t.HurtBottom = checked((short)(projectile.SpriteY + projectile.CurrentSprite.Height));
        var impacts = new CombatParticleImpactAdapter(runtime, pool, hits, () => 1000, () => 3, _ => { }, magic.Transform);
        Check(CombatParticleCollision.Apply(projectile, [caster, target], state, () => true, () => 1, impacts) == 1,
            "real projectile goes through production collision/reaction/damage adapter");
        Check(t.Health < 10_000 && t.Health > 0 && t.Motion == 7, "hit causes actual damage and hit reaction");
        CheckTargetSprite(target);

        // The caster and victim have independent sprite banks, even when an ordinal fits both banks.
        using var archive = BinderArchive.Open(Path.Combine(root, "INF.000"));
        var bindings = new List<CombatInfoBinding>();
        var assets = new Dictionary<string, CombatAnimationAssets>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            foreach (var info in CombatInfoDefinition.Parse(archive.ReadMember(entry.Index)).Characters)
            {
                string name = Encoding.ASCII.GetString(info.AnimationResource.Span);
                if (!assets.TryGetValue(name, out var art))
                {
                    art = CombatAnimationAssets.Load(root, info.AnimationResource.Span);
                    assets.Add(name, art);
                }
                bindings.Add(new(0, 0, info, art));
            }
        }
        int cases = 0;
        foreach (var source in bindings.DistinctBy(b => b.Info.ReactionStartFrame))
        foreach (var destination in bindings)
        foreach (int previousReaction in new[] { 0, 4 })
        for (int reaction = 0; reaction <= 7; reaction++)
        {
            var owner = Actor(source);
            var victim = Actor(destination);
            victim.State.Reaction = previousReaction;
            try
            {
                CombatHitReaction.ParticleReaction(owner, victim, reaction, 1000, _ => { });
            }
            catch (InvalidDataException failure)
            {
                throw new InvalidDataException($"sourceType={source.Info.Type}, start={source.Info.ReactionStartFrame}, targetType={destination.Info.Type}, targetStart={destination.Info.ReactionStartFrame}, previous={previousReaction}, reaction={reaction}, frames={destination.Assets.Animation.Sequences[destination.Info.Reactions[reaction].Animation].Frames.Length}", failure);
            }
            CheckTargetSprite(victim);
            Check(victim.Animation!.SequenceIndex == destination.Info.Reactions[reaction].Animation,
                "selected reaction belongs to the victim");
            Check(victim.State.SpriteY == victim.State.Depth - victim.Animation.CurrentSprite.Height,
                "vertical placement uses victim reaction height");
            int requestedFrame = previousReaction < 4 ? source.Info.ReactionStartFrame : 0;
            int count = destination.Assets.Animation.Sequences[destination.Info.Reactions[reaction].Animation].Frames.Length;
            int expectedFrame = requestedFrame < count ? requestedFrame : 0;
            Check(victim.State.FrameIndex == expectedFrame, "valid reaction entry is preserved; absent entry restarts at frame zero");
            victim.State.FrameIndex = checked((short)requestedFrame);
            victim.Animation.BindReactionSequence(false);
            victim.Animation.RebindFromFrameCounter();
            Check(victim.State.FrameIndex == expectedFrame, "ordinary reaction binding also keeps the next frame update in bounds");
            CheckTargetSprite(victim);
            cases++;
        }
        return $"Projectile reaction PASS: real Mist projectile impact and damage; {cases} reaction cases across {bindings.Count} character definitions, all starting-frame values and old-reaction branches.";
    }

    private static CombatActorImage Actor(CombatInfoBinding binding)
    {
        var image = new CombatActorImage { Binding = binding };
        image.State.Active = 1;
        image.State.Health = 100;
        image.State.Depth = 350;
        image.State.Facing = 1;
        image.Animation = new(image.State, binding.Info, binding.Assets);
        image.Animation.BeginMotion(0, 0);
        return image;
    }

    private static void CheckTargetSprite(CombatActorImage target)
    {
        var animation = target.Animation!;
        Check(ReferenceEquals(animation.CurrentSprite, target.Binding!.Assets.Sprites.Frames[animation.CurrentFrame.SpriteFrameIndex]),
            "displayed frame is owned by the victim reaction animation");
    }
}
