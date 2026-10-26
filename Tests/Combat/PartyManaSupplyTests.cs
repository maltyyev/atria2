using Atria.Game.Combat;
using Atria.Game.Exploration;
using Atria.Resources;

namespace Atria.Tests.Combat;

public static class PartyManaSupplyTests
{
    private sealed class Effects : ICombatControlEffects
    {
        public bool GlobalEffectBusy => false;
        public List<int> Consumed { get; } = [];
        public void PlaySound(int id) { }
        public void SpawnActorParticle(int actor, int id, uint now) { }
        public void ApplyFrameEffect(int actor, int id, int argument, uint now) { }
        public void ShowConsumedItemNotice(int id, uint now) => Consumed.Add(id);
        public void ShowRankNotice(int rank, uint now) { }
    }

    public static void Run(string root)
    {
        foreach (var scenario in new[]
        {
            (Mana: 0, Crystal: true, Potion: true, Hurt: true, Expected: ItemIds.MagicCrystal),
            (Mana: 49, Crystal: true, Potion: false, Hurt: true, Expected: ItemIds.MagicCrystal),
            (Mana: 0, Crystal: false, Potion: true, Hurt: true, Expected: ItemIds.Mana),
            (Mana: 50, Crystal: true, Potion: true, Hurt: true, Expected: -1),
            (Mana: 0, Crystal: false, Potion: false, Hurt: true, Expected: -1),
            (Mana: 0, Crystal: true, Potion: true, Hurt: false, Expected: -1)
        })
        {
            var (runtime, state) = PartySpellTests.Fixture(root);
            int index = runtime.Scene.PartyStartIndex + 4;
            var healer = runtime.Actors[index];
            healer.State.Mana = scenario.Mana;
            healer.State.Health = scenario.Hurt ? 40 : 100;
            Array.Fill(healer.State.Equipment, -1);
            if (scenario.Crystal) healer.State.Equipment[0] = ItemIds.MagicCrystal;
            if (scenario.Potion) healer.State.Equipment[1] = ItemIds.Mana;
            state.OwnedItems[ItemIds.MagicCrystal] = state.OwnedItems[ItemIds.Mana] = 2;
            state.AvailableItems[ItemIds.MagicCrystal] = state.AvailableItems[ItemIds.Mana] = 1;
            foreach (var enemy in runtime.Actors.Where(a => a.State.Team < 100)) enemy.State.Health = 20;
            runtime.CaptureEncounterStrength();

            var random = new LegacyRandom(123);
            var hits = new CombatHitExecutor(runtime, state, random.Next, _ => { }, _ => { });
            var magic = new CombatParticleMagicRuntime(root, runtime, CombatParticleSystem.Load(root), hits,
                () => 1000, random.Next, _ => { }, _ => { });
            var policy = new PartySpellDecision(root, runtime, magic, hits, random);
            var effects = new Effects();
            var controls = ControlBindings.Parse(File.ReadAllBytes(Path.Combine(root, "BATTLE.CFG")));
            var controller = new CombatActorController(runtime, state, controls, new(_ => false), effects, random.Next)
            {
                Spells = policy
            };

            policy.Select(index, 1000, out _);
            Check(healer.State.Mana == scenario.Mana && effects.Consumed.Count == 0
                && state.OwnedItems[ItemIds.MagicCrystal] == 2 && state.OwnedItems[ItemIds.Mana] == 2,
                "prediction does not consume supplies or mutate mana");
            controller.Process(index, 1601);
            if (scenario.Expected < 0)
            {
                Check(effects.Consumed.Count == 0 && healer.State.Mana == scenario.Mana
                    && state.OwnedItems[ItemIds.MagicCrystal] == 2 && state.OwnedItems[ItemIds.Mana] == 2,
                    "affordable arsenal, absent equipment, or no useful spell prevents consumption");
                continue;
            }

            int restored = scenario.Expected == ItemIds.MagicCrystal ? 100 : 200;
            int slot = scenario.Expected == ItemIds.MagicCrystal ? 0 : 1;
            int notice = scenario.Expected == ItemIds.MagicCrystal ? 4 : 5;
            var command = healer.Binding!.Info.Commands[healer.CurrentCommandIndex];
            Check(command.ManaCost == 100 && healer.State.ActionFlags == (int)CombatActionFlags.Command,
                "actual useful party-heal command starts");
            Check(healer.State.Mana == scenario.Mana + restored - command.ManaCost
                && healer.State.DisplayedMana == healer.State.Mana && healer.State.Equipment[slot] == -1
                && state.OwnedItems[scenario.Expected] == 1 && state.AvailableItems[scenario.Expected] == 1
                && effects.Consumed.SequenceEqual(new[] { notice }), "one equipped dose, one notice and exact mana cost");
            healer.State.ActionFlags = 0;
            healer.State.Mana = 0;
            Check(policy.Select(index, 7602, out _) < 0 && effects.Consumed.Count == 1,
                "restoration does not bypass the thirty-second automatic cast cooldown");
        }

        var actor = new CombatActorState();
        Array.Fill(actor.Equipment, -1);
        actor.Equipment[0] = ItemIds.MagicCrystal;
        actor.Equipment[1] = ItemIds.Mana;
        Check(CombatManualControl.FindManaSupplySlot(actor, 100) == 0, "smallest sufficient supply wins");
        Check(CombatManualControl.FindManaSupplySlot(actor, 150) == 1, "larger supply when the crystal cannot cover the cost");
        Check(CombatManualControl.FindManaSupplySlot(actor, 201) == -1, "never waste a dose that cannot fund a cast");
        Console.WriteLine("Party mana supplies PASS: exhausted arsenal, affordability boundary, equipped-only use, no waste, exact consumption and cooldown.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Party mana supplies: " + message);
    }
}
