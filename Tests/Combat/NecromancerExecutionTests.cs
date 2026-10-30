using Atria.Audio;
using Atria.Game.Combat;
using Atria.Game.Exploration;
using Atria.Game.Progression;
using Atria.Game.State;
using Atria.Resources;
using Atria.Tests.Render;

namespace Atria.Tests.Combat;

internal static class NecromancerExecutionTests
{
    private sealed class SilentVoice : IBufferedSoundEffect
    {
        public void Play()
        {
        }
        public void StopAll()
        {
        }
        public void Dispose()
        {
        }
        public void SetCurrentVolume(int value)
        {
        }
    }

    public static void Run(string root)
    {
        foreach (int count in Enumerable.Range(1, 3))
        {
            var state = Party(root, 10);
            var random = new LegacyRandom(123);
            using var entry = Entry(root, state, random);
            var runtime = entry.Runtime;
            var execution = Execution(root, entry, state, random);
            var caster = runtime.Actors[runtime.Scene.PartyStartIndex + state.HeroCatalog.Find("eiren_wald")];
            var target = runtime.Actors[0];
            foreach (var image in runtime.Actors)
            {
                image.State.Active = image.State.Health = 0;
                image.State.SpawnCondition = 0;
                image.State.ControlRole = 0;
            }
            caster.State.Active = 1;
            caster.State.Health = caster.State.MaximumHealth;
            caster.State.Mana = 1000;
            caster.State.ControlledBy = 1;
            caster.State.ControlRole = 1;
            caster.ControlPlayer = 0;
            caster.State.AnchorX = caster.State.TrackingX = 300;
            caster.State.Depth = target.State.Depth;
            caster.State.Facing = 1;
            target.State.Active = 1;
            target.State.Health = target.State.MaximumHealth = 100_000;
            target.IsRitualAnchor = true; // Stationary damage dummy: leave real collisions and reactions active.
            target.State.AnchorX = target.State.TrackingX = 380;
            target.State.Defense = 6;
            target.State.GuardBonus = -100;
            caster.State.CommandCategory = 1;
            caster.State.CommandSelection = count - 1;
            caster.State.CommandScroll = 0;
            CombatManualControl.ExecuteCommand(runtime, caster, state, 0, false, _ => { }, _ => { }, _ => { });
            Check(caster.State.Mana == 910, "manual spell pays its configured cost once");
            bool summoned = false;
            for (uint now = 10; now <= 15_000; now += 10)
            {
                Step(execution, now);
                summoned |= runtime.Actors.Any(image => image.IsSummon && image.State.Active != 0);

            }
            var servants = runtime.Actors.Where(image => image.IsSummon && image.State.Active != 0).ToArray();
            Check(summoned && servants.Length == count, "actual command animation creates the chosen group");
            Check(servants.All(image => image.State.MaximumHealth == 360 / count), "level ten HP budget");
            int damage = 100_000 - target.State.Health;
            Check(damage > 0, "servants autonomously reach and damage a hostile target");
            Console.WriteLine($"Summon calibration L10 count={count}: HP={360 / count}, ATK={servants[0].State.Attack}, DEF={servants[0].State.Defense}, damage in15s against stationary DEF6={damage}.");
            string capture = Path.GetFullPath(Path.Combine(root, "..", "audit", "necromancer-0150"));
            Directory.CreateDirectory(capture);
            UnicodeTextTests.SaveBitmap(Path.Combine(capture, $"servants-{count}.bmp"), execution.CachedFrame.ToArray());
            for (uint now = 15_010; now <= 65_000; now += 10)
            {
                Step(execution, now);
            }
            Check(servants.All(image => image.State.Active != 0 && image.State.Health > 0), "real combat keeps servants alive beyond the former thirty-second lifetime");
            caster.State.Health = 0;
            runtime.Summons.RemoveDead();
            Check(servants.All(image => image.State.Active != 0), "owner death does not dismiss living servants");
            caster.State.Health = caster.State.MaximumHealth;
            runtime.Summons.Cast(caster, CombatSummons.FirstEffect, 66_000);
            Check(runtime.Actors.Count(image => image.IsSummon && image.State.Active != 0) == count, "owner revival cannot replace surviving servants");
            foreach (var servant in servants)
            {
                servant.State.Health = 0;
            }
            runtime.Summons.RemoveDead();
            runtime.Summons.Cast(caster, CombatSummons.FirstEffect, 67_000);
            Check(runtime.Actors.Count(image => image.IsSummon && image.State.Active != 0) == 1, "a fresh group reuses slots after the previous group dies");
        }

        CheckAiAndWaves(root);
        CheckQuestWaves(root);
    }

    private static void CheckQuestWaves(string root)
    {
        foreach (int encounter in Enumerable.Range(906, 9))
        {
            var state = Party(root, 10);
            var random = new LegacyRandom(321);
            using var entry = Entry(root, state, random, encounter);
            var runtime = entry.Runtime;
            var execution = Execution(root, entry, state, random);
            var enemies = runtime.Actors.Take(runtime.Scene.Actors.Count).ToArray();
            foreach (var image in runtime.Actors) image.State.Invulnerable = 1;
            var seen = new HashSet<int>();
            uint waveStarted = 0;
            CombatFrameResult result = default;
            for (uint now = 10; now <= 30_000; now += 10)
            {
                Step(execution, now);
                var active = enemies.Where(image => image.State.Active != 0 && image.State.SpawnCondition == 0).ToArray();
                if (active.Length > 0 && now - waveStarted < 3_000)
                {
                    continue;
                }
                if (encounter == 906 && seen.Count == 0)
                {
                    Check(active.Length == 5 && active.All(image => image.Binding!.Info.Type != 40), "opening wave has five guards and no Eiren");
                }
                if (active.Length > 0)
                {
                    execution.AdvanceLogicalVisual(now);
                    string output = Path.GetFullPath(Path.Combine(root, "..", "audit", "eiren-expedition-0170"));
                    Directory.CreateDirectory(output);
                    UnicodeTextTests.SaveBitmap(Path.Combine(output, $"battle-{encounter}-after-{seen.Count}.bmp"), execution.CachedFrame.ToArray());
                }
                foreach (var enemy in active)
                {
                    seen.Add(Array.IndexOf(enemies, enemy));
                    enemy.State.Active = enemy.State.Health = 0;
                }
                if (active.Length > 0)
                {
                    waveStarted = now;
                }
                result = execution.AdvanceActors(now, true);
                if (result.ReturnCode != 0)
                {
                    break;
                }
            }
            Check(seen.Count == enemies.Length && result.ReturnCode == 1, "every quest wave spawns before victory: " + encounter);
            Console.WriteLine($"Quest EBAT{encounter}: {seen.Count} actors activated; victory returns to the dialogue.");
        }
    }

    private static void CheckAiAndWaves(string root)
    {
        var state = Party(root, 10);
        var random = new LegacyRandom(321);
        using var entry = Entry(root, state, random);
        var runtime = entry.Runtime;
        var execution = Execution(root, entry, state, random);
        var caster = runtime.Actors[runtime.Scene.PartyStartIndex + state.HeroCatalog.Find("eiren_wald")];
        caster.State.ControlledBy = 0;
        caster.State.Mana = caster.State.MaximumMana = 1000;
        foreach (var image in runtime.Actors.Where(image => image.State.Active != 0))
        {
            image.State.Invulnerable = 1;
        }

        bool summoned = false;
        for (uint now = 10; now <= 10_000; now += 10)
        {
            Step(execution, now);
            summoned |= runtime.Actors.Any(image => image.IsSummon && image.State.Active != 0);
        }
        Check(summoned, "uncontrolled companion chooses and casts a summon through the real AI");
        var completed = new HashSet<int>();
        CombatFrameResult result = default;
        for (uint now = 10_010; now <= 15_000; now += 10)
        {
            foreach (var image in runtime.Actors.Where(image => image.Binding is not null && image.State.Team < 100 && image.State.SpawnCondition == 0))
            {
                completed.Add(runtime.Actors.ToList().IndexOf(image));
                image.State.Active = image.State.Health = 0;
            }
            result = execution.AdvanceActors(now, true);
            if (result.ReturnCode != 0)
            {
                break;
            }
        }
        Check(completed.Count == runtime.Scene.Actors.Count && result.ReturnCode == 1, "every authored boss wave activates and victory returns without a pending-wave softlock");
    }

    private static GameState Party(string root, int level)
    {
        var items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT")));
        var state = InitialGameState.Create(items, LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat"))));
        foreach (var hero in state.Heroes.Where(hero => hero.MemberStatus != 0))
        {
            hero.Level = level;
        }

        HeroRecruitment.Join(state, "eiren_wald");
        NewCharacterRuntime.ProcessPending(state, items, 0);
        foreach (var hero in state.Heroes.Where(hero => hero.MemberStatus != 0))
        {
            hero.SelectedForBattle = 1;
        }

        return state;
    }

    private static CombatEntryPreparation Entry(string root, GameState state, LegacyRandom random, int encounter = 905)
    {
        var entry = new CombatEntryPreparation(root, new(FixedEncounterKind.Forced, encounter), random, () => 123, 0, _ => new SilentVoice(), _ => { });
        var selection = new CombatPartySelection(state, 1);
        selection.ApplyInput((_, key) => key == CombatSelectionInput.Finish, _ => { });
        selection.Commit(state);
        entry.BindSelectedParty(selection, 6, 0);
        return entry;
    }

    private static CombatExecutionRuntime Execution(string root, CombatEntryPreparation entry, GameState state, LegacyRandom random) =>
        new(root, entry, state, random, ControlBindings.Parse(File.ReadAllBytes(Path.Combine(root, "BATTLE.CFG"))),
            new CombatScanInput(_ => false), () => -10000, (_, _) => { }, (_, _) => { });

    private static void Step(CombatExecutionRuntime execution, uint now)
    {
        if (!execution.GlobalEffectBusy)
        {
            execution.AdvanceActors(now, false);
            execution.AdvanceHits(now);
        }
        execution.AdvanceStatuses(now);
        if (now % 40 == 0)
        {
            execution.AdvanceLogicalVisual(now);
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException("Necromancer execution: " + message);
        }
    }
}
