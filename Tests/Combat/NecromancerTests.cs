using System.Text;
using System.Text.Json;
using Atria.Audio;
using Atria.Game.Combat;
using Atria.Game.Dialogue;
using Atria.Game.Exploration;
using Atria.Game.Persistence;
using Atria.Game.Progression;
using Atria.Game.State;
using Atria.Game.World;
using Atria.Localization;
using Atria.Resources;
using Atria.Script;

namespace Atria.Tests.Combat;

public static class NecromancerTests
{
    private sealed class Voice : IBufferedSoundEffect
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
    private sealed class StoryEffects : IScriptEffects
    {
        public int Battles
        {
            get; private set;
        }
        public void Execute(ScriptEffect effect, GameState state)
        {
            if (effect.Kind == ScriptEffectKind.ForcedBattleWithPostAction)
            {
                Battles++;
                state.PendingBattle = state.EndingResetFlag = 0;
            }
            else if (effect.Call.Function == "dialogue_text")
            {
                Check(GameText.Get(Encoding.ASCII.GetString(effect.Call.Arguments[2].Bytes.Span)).Length > 0, "localized scene text");
            }
            else
            {
                throw new InvalidOperationException("Unexpected story effect.");
            }
        }
    }

    public static string Run(string root)
    {
        Console.WriteLine(NarrativeRevisionTests.Run(root));
        Console.WriteLine(NarrativeReturnTests.Run(root));
        var items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT")));
        var messages = LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat")));
        var state = InitialGameState.Create(items, messages);
        foreach (var hero in state.Heroes.Where(hero => hero.MemberStatus != 0))
        {
            hero.Level = 8;
        }

        state.Events[441] = 1;
        var effects = new StoryEffects();
        using var archive = BinderArchive.Open(Path.Combine(root, "SCRIPT.000"));
        foreach (var scene in NarrativeScenes.All.Where(scene => scene.AfterMember != "WORLD"))
        {
            var source = GameResources.Read(root, "SCRIPT.000", Encoding.ASCII.GetBytes(scene.AfterMember));
            var original = ScriptProgram.Parse(scene.AfterMember, source);
            Check(original.Tokens.Any(token => token.Text == scene.AfterFunction), "hook exists in an original scenario: " + scene.AfterMember);
        }
        var objectives = new QuestMapIndex(root);
        foreach (var scene in NarrativeScenes.All.Take(8))
        {
            int? worldArea = null;
            int? active = Enumerable.Range(1020, 4).Cast<int?>().FirstOrDefault(id => state.Journal.CanTrack(state, id!.Value));
            if (active is { } quest)
            {
                state.Journal.RestoreTracking(quest);
                var target = objectives.Objective(state) ?? throw new InvalidOperationException("Missing Eiren objective.");
                var map = WorldMapContext.Parse(GameResources.Read(root, "AT2AMF.000", Encoding.ASCII.GetBytes($"ground{target.Area + 1}.amf")));
                worldArea = map.AreaIndex;
                if (scene.AfterMember != "WORLD")
                {
                    var destination = map.SelectScene(target.X, target.Y);
                    string script = Path.GetFileNameWithoutExtension(Encoding.ASCII.GetString(destination.ScriptMember.Span)).ToUpperInvariant();
                    Check(script == scene.AfterMember, "quest marker reaches actual scenario hook: " + scene.Id);
                }
                Console.WriteLine($"Narrative marker: {scene.Id}, quest={quest}, area={target.Area}, tile={target.X},{target.Y}, hook={scene.AfterMember}/{scene.AfterFunction}.");
            }
            if (scene.Conditions.TryGetProperty("world", out var position))
            {
                Check(worldArea == position[0].GetInt32(), "marker and trigger use loaded area");
                state.WorldX = position[1].GetInt32();
                state.WorldY = position[2].GetInt32();
            }
            Check(NarrativeScenes.After(state, scene.AfterMember, scene.AfterFunction, worldArea)?.Id == scene.Id, $"reachable stage {scene.Id}");
            var program = ScriptProgram.Parse(scene.Id, scene.Source);
            var checkpoints = new List<(byte[] State, ScriptCallSnapshot[] Calls)>();
            var recording = new CheckpointScriptHost(new ScriptStateHost(state, effects));
            recording.AfterCall = call =>
            {
                if (call.Function is "battle_start3" or "recruit_hero")
                {
                    checkpoints.Add((NativeGameStateCodec.Encode(state), recording.Capture()));
                }
            };
            new ScriptMachine(program, recording).Invoke("run");
            byte[] expected = NativeGameStateCodec.Encode(state);
            foreach (var checkpoint in checkpoints)
            {
                var resumed = NativeGameStateCodec.Decode(checkpoint.State);
                var replay = new CheckpointScriptHost(new ScriptStateHost(resumed, new StoryEffects()), checkpoint.Calls);
                new ScriptMachine(program, replay).Invoke("run");
                replay.VerifyReplayComplete();
                Check(expected.AsSpan().SequenceEqual(NativeGameStateCodec.Encode(resumed)), "battle/recruitment checkpoints resume exactly once");
            }
            state = NativeGameStateCodec.Decode(NativeGameStateCodec.Encode(state));
            Check(NarrativeScenes.After(state, scene.AfterMember, scene.AfterFunction, worldArea)?.Id != scene.Id, $"no repeated quest stage {scene.Id}");
        }
        var optional = NarrativeScenes.All[8];
        Check(NarrativeScenes.After(state, optional.AfterMember, optional.AfterFunction)?.Id == optional.Id, "optional recruited-party dialogue appears");
        state.NarrativeHistory.Add(optional.Id);
        state = NativeGameStateCodec.Decode(NativeGameStateCodec.Encode(state));
        Check(NarrativeScenes.After(state, optional.AfterMember, optional.AfterFunction) is null, "optional dialogue history survives reload without repeated apology");
        Check(effects.Battles == 7 && state.HasHero("eiren_wald"), "seven quest combats and recruitment");
        Check(state.Journal.Progress(state, 1023).HasFlag(QuestMilestones.Completed), "quest chain completion persists");
        Check(NewCharacterRuntime.ProcessPending(state, items, 0), "joining finalizes stats and equipment");
        int eiren = state.HeroCatalog.Find("eiren_wald");
        Check(state.Heroes[eiren].Level == 8 && state.Heroes[eiren].Health > 0, "recruit starts at current party median");
        int owned = state.OwnedItems.Sum();
        HeroRecruitment.Join(state, "eiren_wald");
        Check(state.NewCharacterPending == 0 && state.OwnedItems.Sum() == owned, "idempotent recruitment");
        using (var condition = JsonDocument.Parse("{\"all\":[\"roland\"],\"any\":[\"mist\",\"emerald\"],\"none\":[\"tiger\"]}"))
        {
            Check(NarrativeScenes.Matches(condition.RootElement, state), "generic all/any/none party predicate");
            state.Heroes[1].MemberStatus = 0;
            Check(!NarrativeScenes.Matches(condition.RootElement, state), "absent members do not speak");
            state.Heroes[1].MemberStatus = 1;
        }
        using (var faces = new FaceSpriteSource(Path.Combine(root, "FACE.SPR")))
        {
            Check(faces.Load(157).Width > 0 && faces.Load(158).Height > 0, "both authored portraits decode");
        }
        foreach (string language in new[] { "ru-RU", "be-BY" })
        {
            var catalog = new LocalizationCatalog(Path.GetFullPath(Path.Combine(root, "..", "Localization")), language);
            Check(catalog.Issues.Count == 0 && catalog.Get("ui.characters.eiren").Length > 0, "localized content validates: " + language);
        }

        CheckStorehouseEncounters();
        for (int encounter = 900; encounter <= 907; encounter++)
        {
            foreach (var hero in state.Heroes.Where(hero => hero.MemberStatus != 0))
            {
                hero.Health = hero.MaximumHealth;
                hero.Mana = hero.MaximumMana;
                hero.SelectedForBattle = 1;
            }
            var random = new LegacyRandom(123);
            using var entry = new CombatEntryPreparation(root, new(FixedEncounterKind.Forced, encounter), random, () => 123, 0, _ => new Voice(), _ => { });
            var selection = new CombatPartySelection(state, 1);
            selection.ApplyInput((_, key) => key == CombatSelectionInput.Finish, _ => { });
            Check(selection.Completed, "selection confirms authored encounter");
            selection.Commit(state);
            entry.BindSelectedParty(selection, 6, 0);
            var runtime = entry.Runtime;
            var caster = runtime.Actors[runtime.Scene.PartyStartIndex + eiren];
            var execution = new CombatExecutionRuntime(root, entry, state, random,
                ControlBindings.Parse(File.ReadAllBytes(Path.Combine(root, "BATTLE.CFG"))), new CombatScanInput(_ => false), () => -10000, (_, _) => { }, (_, _) => { });
            var info = caster.Binding!.Info;
            foreach (var sequence in caster.Binding.Assets.Animation.Sequences)
            {
                foreach (var frame in sequence.Frames)
                {
                    Check(frame.SpriteFrameIndex < caster.Binding.Assets.Sprites.Frames.Length, "all authored frames own their sprite indexes");
                }
            }

            for (int count = 1; count <= 3; count++)
            {
                uint now = (uint)(count * 40_000);
                runtime.Summons.RemoveDead();
                runtime.Summons.Cast(caster, CombatSummons.FirstEffect + count - 1, now);
                var servants = runtime.Actors.Where(actor => actor.IsSummon && actor.State.Active != 0).ToArray();
                Check(servants.Length == count && servants.All(actor => actor.State.MaximumHealth == 288 / count), "summon choice and 1.5x HP budget");
                Check(!runtime.Summons.CanSummon(caster), "no accumulated groups");
                int mana = caster.State.Mana;
                caster.State.CommandCategory = 1;
                caster.State.CommandSelection = count - 1;
                caster.State.CommandScroll = 0;
                CombatManualControl.ExecuteCommand(runtime, caster, state, now, false, _ => { }, _ => { }, _ => { });
                Check(caster.State.Mana == mana, "occupied summon group does not consume mana");
                servants[0].State.Health = 0;
                runtime.Summons.RemoveDead();
                Check(servants[0].State.Active == 0 && servants.Skip(1).All(actor => actor.State.Active != 0), "only killed servants disappear");
                Check(runtime.Summons.CanSummon(caster) == (count == 1), "surviving servants still prevent another group");
                foreach (var servant in servants)
                {
                    servant.State.Health = 0;
                }
                runtime.Summons.RemoveDead();
                Check(runtime.Summons.CanSummon(caster), "all servants dead frees the group");
                Check(CombatParticleMagic.Resurrect(runtime.Actors, caster, 0) == 0, "dead servants cannot be resurrected");
            }
            for (uint now = 200_000; now < 205_000; now += 10)
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
            Check(execution.HasCachedFrame, "real actor, magic, hit and HUD pipeline renders");
        }
        NecromancerPresentationTests.Run(root, state);
        NecromancerExecutionTests.Run(root);
        return "Necromancer PASS: four staged quests/reload, generic membership, recruitment, RU/BE, portraits, seven current encounters plus legacy storehouse, three summon choices and death-only cleanup.";
    }

    private static void CheckStorehouseEncounters()
    {
        using var document = GameData.Open("AuthoredEncounters");
        var first = document.RootElement.GetProperty("EBAT906").GetProperty("actors").EnumerateArray().ToArray();
        var second = document.RootElement.GetProperty("EBAT907").GetProperty("actors").EnumerateArray().ToArray();
        var openingWave = first.Where(actor => actor.GetProperty("wave").GetInt32() == 0).ToArray();
        var eirenWave = first.Where(actor => actor.GetProperty("wave").GetInt32() == 1).ToArray();
        const int necromancerSceneType = 42;
        Check(first.Select(actor => actor.GetProperty("wave").GetInt32()).Distinct().Order().SequenceEqual([0, 1]), "first storehouse battle has exactly two waves");
        Check(openingWave.All(actor => actor.GetProperty("type").GetInt32() != necromancerSceneType)
            && eirenWave.Count(actor => actor.GetProperty("type").GetInt32() == necromancerSceneType) == 1,
            "Eiren appears only in the second wave");
        Check(second.All(actor => actor.GetProperty("wave").GetInt32() == 0)
            && second.GroupBy(actor => actor.GetProperty("type").GetInt32()).All(group => group.Count() == 2)
            && second.Count(actor => actor.GetProperty("type").GetInt32() == necromancerSceneType) == 2,
            "copying battle has one wave with two necromancers and paired servants");
        foreach (string statistic in new[] { "health", "attack", "defense" })
        {
            Check(openingWave.Sum(actor => actor.GetProperty(statistic).GetInt32()) < eirenWave.Sum(actor => actor.GetProperty(statistic).GetInt32())
                && eirenWave.Sum(actor => actor.GetProperty(statistic).GetInt32()) < second.Sum(actor => actor.GetProperty(statistic).GetInt32()),
                "successive storehouse waves increase total " + statistic);
        }
        foreach (var pair in second.GroupBy(actor => actor.GetProperty("type").GetInt32()))
        {
            foreach (string statistic in new[] { "health", "attack", "defense" })
            {
                Check(pair.Select(actor => actor.GetProperty(statistic).GetInt32()).Distinct().Count() == 1, "copies have the same combat statistics");
            }
        }
    }
    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException("Necromancer: " + message);
        }
    }
}
