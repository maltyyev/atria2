using System.Text;
using Atria.Game.Combat;
using Atria.Game.Controls;
using Atria.Game.Exploration;
using Atria.Game.State;
using Atria.Resources;

namespace Atria.Tests.Combat;

public static class PartySpellTests
{
    private sealed class Effects : ICombatControlEffects
    {
        public bool GlobalEffectBusy => false;
        public void PlaySound(int id) { }
        public void SpawnActorParticle(int actor, int id, uint now) { }
        public void ApplyFrameEffect(int actor, int id, int argument, uint now) { }
        public void ShowConsumedItemNotice(int id, uint now) { }
        public void ShowRankNotice(int rank, uint now) { }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Party spells: " + message);
    }

    internal static (CombatSceneRuntime Runtime, GameState State) Fixture(string root)
    {
        var runtime = new CombatSceneRuntime(root, CombatSceneDefinition.Load(root, new(FixedEncounterKind.Scripted, 2)), 0);
        var state = InitialGameState.Create(ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT"))),
            LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat"))));
        runtime.BindDeclaredAnimations(0);
        string[] names = ["ROLAND", "MIST", "SWALLOW", "BRAIUN", "EMERALD", "TIGER"];
        for (int i = 0; i < runtime.Actors.Count; i++)
        {
            var image = runtime.Actors[i];
            int party = i - runtime.Scene.PartyStartIndex;
            if (party >= 0) Bind(image, root, names[party] + "5");
            if (image.Binding is null) continue;
            var a = image.State;
            a.Team = party >= 0 ? (byte)100 : (byte)1;
            a.Health = a.MaximumHealth = party >= 0 ? 100 : 79;
            a.Active = 1;
            a.Rank = a.MagicRank = 5;
            a.Mana = a.MaximumMana = 1000;
            a.SpawnCondition = 0;
            a.AnchorX = a.TrackingX = 100;
            a.Depth = 350;
            a.Facing = 1;
            image.Animation!.BeginMotion(0, 0);
            a.ActionFlags = 0;
        }
        runtime.CaptureEncounterStrength();
        return (runtime, state);
    }

    private static void Bind(CombatActorImage image, string root, string name)
    {
        var info = CombatInfoDefinition.Load(root, Encoding.ASCII.GetBytes(name)).Characters[^1];
        var art = CombatAnimationAssets.Load(root, info.AnimationResource.Span);
        image.Binding = new(0, 0, info, art);
        image.Animation = new(image.State, info, art);
    }

    public static string Run(string root)
    {
        var (runtime, state) = Fixture(root);
        var random = new LegacyRandom(123);
        var hits = new CombatHitExecutor(runtime, state, random.Next, _ => { }, _ => { });
        var magic = new CombatParticleMagicRuntime(root, runtime, CombatParticleSystem.Load(root), hits, () => 1000, random.Next, _ => { }, _ => { });
        var policy = new PartySpellDecision(root, runtime, magic, hits, random);
        var healer = runtime.Actors[runtime.Scene.PartyStartIndex + 4];
        var enemy = runtime.Actors[0];
        Check(!enemy.IsStrongEnemy, "79% is not strong");
        enemy.State.Health = 80;
        runtime.CaptureEncounterStrength();
        Check(enemy.IsStrongEnemy, "80% is strong");
        enemy.State.Health = 1;
        Check(enemy.IsStrongEnemy && enemy.InitialHealth == 80, "strength never changes when wounded");

        int Score(CombatSpellEffect effect) => policy.EffectScore(healer, effect, 1000);
        Check(Score(CombatSpellEffect.PartyHeal) == 0 && Score(CombatSpellEffect.FullHeal) == 0, "healthy party does not heal");
        healer.State.Health = 50;
        Check(Score(CombatSpellEffect.PartyHeal) == 0, "50% boundary does not heal");
        healer.State.Health = 49;
        Check(Score(CombatSpellEffect.PartyHeal) > 0 && Score(CombatSpellEffect.FullHeal) > 0, "below 50% heals");
        healer.State.Health = 0;
        Check(Score(CombatSpellEffect.PartyHeal) == 0 && Score(CombatSpellEffect.Resurrection) > 0, "dead ally needs resurrection, not healing");
        healer.State.Health = 100;
        Check(Score(CombatSpellEffect.CurePoison) == 0, "no poison means no cure");
        healer.State.Poison = 1;
        Check(Score(CombatSpellEffect.CurePoison) > 0, "poison permits cure");
        healer.State.Poison = 0;
        Check(Score(CombatSpellEffect.PartyDefense) > 0, "defense is useful in a real fight");
        CombatStatusSlots.Insert(magic.GlobalStatus, 30, 1, 0, 9000, healer.Binding!.Info.Type, 1000);
        Check(Score(CombatSpellEffect.PartyDefense) == 0, "existing group defense is not recast");
        Array.Clear(magic.GlobalStatus);
        foreach (var t in runtime.Actors.Where(t => t.State.Team < 100)) t.State.Health = 30;
        runtime.CaptureEncounterStrength();
        Check(runtime.IsWeakEncounter, "30% inclusive weak encounter");
        Check(Score(CombatSpellEffect.PartyDefense) == 0 && Score(CombatSpellEffect.WeakenAttack) == 0, "no defensive/debuff waste in weak fight");
        healer.State.Health = 49;
        healer.State.Mana = healer.State.MaximumMana = 300;
        Check(policy.Select(runtime.Scene.PartyStartIndex + 4, 1000, out _) < 0, "healing costing over 30% mana rejected in weak fight");
        healer.State.Mana = healer.State.MaximumMana = 1000;
        int healing = policy.Select(runtime.Scene.PartyStartIndex + 4, 2000, out _);
        Check(healing >= 0 && healer.Binding.Info.Commands[healing].ManaCost == 100, "affordable healing selected");
        policy.Cast(runtime.Scene.PartyStartIndex + 4, 2000);
        Check(policy.Select(runtime.Scene.PartyStartIndex + 4, 2001, out _) < 0, "cast cooldown");
        var bindings = ControlBindings.Parse(File.ReadAllBytes(Path.Combine(root, "BATTLE.CFG")));
        var controller = new CombatActorController(runtime, state, bindings, new(_ => false), new Effects(), random.Next)
        {
            Spells = policy
        };
        Check(policy.Select(runtime.Scene.PartyStartIndex + 4, 9000, out _) < 0, "old six-second interval no longer permits a cast");
        Check(policy.Select(runtime.Scene.PartyStartIndex + 4, 32000, out _) < 0, "strict enemy-style cooldown boundary");
        controller.Process(runtime.Scene.PartyStartIndex + 4, 32001);
        Check(healer.State.Mana == 900 && healer.State.ActionFlags == (int)CombatActionFlags.Command
            && healer.CurrentCommandIndex == healing, "automatic control starts real spell and charges mana once");
        healer.State.ActionFlags = 0;

        // Actual fireball geometry: strong enemy in front, then behind, then outside its lane.
        var caster = runtime.Actors[runtime.Scene.PartyStartIndex];
        var fireball = caster.Binding!.Info.Commands.First(c => caster.Binding.Assets.Animation.Sequences[c.Animation].Frames.Any(f => f.Effect == (int)CombatSpellEffect.Fireball));
        foreach (var t in runtime.Actors.Where(t => t.State.Team < 100)) t.State.Active = 0;
        enemy.State.Active = 1;
        enemy.State.Health = 100;
        healer.State.Health = 100;
        runtime.CaptureEncounterStrength();
        var forecast = new CombatProjectileForecast(root, runtime, hits, random);
        enemy.State.HurtLeft = 240; enemy.State.HurtRight = 300;
        enemy.State.HurtTop = 230; enemy.State.HurtBottom = 350;
        enemy.State.Depth = 350;
        ulong calls = random.Calls;
        Check(forecast.Score(caster, fireball, 1) == 1, "strong enemy on actual projectile path");
        Check(forecast.Score(caster, fireball, 255) == 0, "enemy behind projectile is not counted");
        enemy.State.Depth = 450;
        Check(forecast.Score(caster, fireball, 1) == 0, "other depth lane is excluded");
        Check(random.Calls == calls && enemy.State.Health == 100 && magic.ActiveMagic is null, "forecast cannot damage, consume RNG, or activate real magic");
        enemy.State.Depth = 350;
        enemy.State.Health = 79;
        runtime.CaptureEncounterStrength();
        Check(forecast.Score(caster, fireball, 1) == 0, "one weak target does not justify projectile");
        for (int i = 1; i < 3; i++)
        {
            var t = runtime.Actors[i];
            t.Binding = enemy.Binding;
            t.State.Team = 1;
            t.State.Health = 79;
            t.State.Active = 1;
            t.State.Rank = 1;
            t.State.SpawnCondition = 0;
            t.State.Depth = 350;
            t.State.HurtLeft = checked((short)(240 + i * 70));
            t.State.HurtRight = checked((short)(300 + i * 70));
            t.State.HurtTop = 230;
            t.State.HurtBottom = 350;
            runtime.CaptureEncounterStrength();
            Check(forecast.Score(caster, fireball, 1) == (i == 1 ? 0 : 3), "three weak targets required for piercing projectile");
        }

        // Every original form/command must be recognized and safe to inspect.
        int count = 0;
        foreach (string name in new[] { "ROLAND", "MIST", "SWALLOW", "BRAIUN", "EMERALD", "TIGER" })
        for (int form = 1; form <= 5; form++)
        {
            Bind(caster, root, name + form);
            caster.Animation!.BeginMotion(0, 0);
            foreach (var command in caster.Binding!.Info.Commands.Where(c => c.Category > 0))
            {
                foreach (var frame in caster.Binding.Assets.Animation.Sequences[command.Animation].Frames)
                    Check(frame.Effect == 0 || Enum.IsDefined((CombatSpellEffect)frame.Effect), "known spell effect " + frame.Effect);
                policy.Score(caster, command, 1000, out _);
                count++;
            }
        }
        CheckAlliedCastCadence(root);
        return $"Party spells PASS: strength snapshots, healing/cure/resurrection, weak-fight costs, active buffs, enemy mage cadence, real trajectories, {count} commands across 30 forms.";
    }

    private static void CheckAlliedCastCadence(string root)
    {
        var (runtime, state) = Fixture(root);
        var enemy = runtime.Actors[0];
        enemy.State.Health = enemy.State.MaximumHealth = 100;
        enemy.State.HurtLeft = 0;
        enemy.State.HurtRight = 640;
        enemy.State.HurtTop = 0;
        enemy.State.HurtBottom = 350;
        runtime.CaptureEncounterStrength();
        var random = new LegacyRandom(123);
        var hits = new CombatHitExecutor(runtime, state, random.Next, _ => { }, _ => { });
        var magic = new CombatParticleMagicRuntime(root, runtime, CombatParticleSystem.Load(root), hits,
            () => 1000, random.Next, _ => { }, _ => { });
        var policy = new PartySpellDecision(root, runtime, magic, hits, random);
        var bindings = ControlBindings.Parse(File.ReadAllBytes(Path.Combine(root, "BATTLE.CFG")));
        var controller = new CombatActorController(runtime, state, bindings, new(_ => false), new Effects(), random.Next)
        {
            Spells = policy
        };

        // Read the actual enemy mage definition and its initialized special-action timing.
        var mageInfo = CombatInfoDefinition.Load(root, "44"u8).Characters.Single();
        var mage = new CombatActorState();
        int award = 0;
        CombatEnemyAttributes.Initialize(mage, mageInfo, 1, 0, () => 1, ref award);
        Check(mageInfo.Type == 42 && mage.SpecialActionInterval == 30_000, "actual MAGE enemy uses thirty-second special actions");

        for (int party = 0; party < 6; party++)
        {
            int index = runtime.Scene.PartyStartIndex + party;
            var member = runtime.Actors[index];
            member.State.Health = 40;
            member.State.Poison = 1;
            if (!member.Binding!.Info.Commands.Any(c => c.Category > 0 && c.ManaCost > 0))
            {
                Check(policy.Select(index, 1000, out _) < 0, "classes without mana spells do not acquire invented abilities");
                continue;
            }
            Check(policy.Select(index, 1000, out _) >= 0, $"class {party} has a useful spell before cooldown");
            int manaBefore = member.State.Mana;
            controller.Process(index, 1601);
            Check(member.State.Mana < manaBefore && member.State.ActionFlags == (int)CombatActionFlags.Command,
                "automatic cast succeeds for every party class");
            member.State.ActionFlags = 0;
            int manaAfter = member.State.Mana;
            uint deadline = 1601u + (uint)mage.SpecialActionInterval;
            foreach (uint time in new[] { 1602u, 7602u, deadline - 1, deadline })
            {
                Check(policy.Select(index, time, out _) < 0 && member.State.Mana == manaAfter,
                    "no automatic repeat or mana charge before the enemy mage deadline");
            }
            int command = policy.Select(index, deadline + 1, out _);
            Check(command >= 0, "every class may cast again after the enemy mage deadline");

            policy.Cast(index, deadline + 1);
            Check(controller.ExecuteMove(index, $"p:{member.Binding!.Info.Commands[command].Icon}", deadline + 2),
                "manual spell execution is independent of the automatic cooldown");
            member.State.ActionFlags = 0;
        }
    }
}



