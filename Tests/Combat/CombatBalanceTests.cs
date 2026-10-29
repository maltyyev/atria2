using Atria.Game.Combat;
using Atria.Game.State;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Tests.Combat;

public static class CombatBalanceTests
{
    private sealed class NoActions : ICombatActorActions
    {
        public void Process(int index, uint now) { }
    }

    private sealed class NoEffects : ICombatFrameEffects
    {
        public void PlaySound(int id) { }
        public void SpawnActorParticle(int actor, int id, uint now) { }
        public void ApplyFrameEffect(int actor, int id, int argument, uint now) { }
        public void ShowConsumedItemNotice(int id, uint now) { }
    }

    public static string Run(string root)
    {
        var request = new CombatEntryRequest(FixedEncounterKind.Forced, 161);
        var scene = CombatSceneDefinition.Load(root, request);
        using var archive = BinderArchive.Open(Path.Combine(root, "SOD.000"));
        var original = SodV2.Parse(archive.ReadMember(archive.FindResource("EBAT161"u8)));
        var oldScene = new CombatSceneDefinition(request, original);
        var unchanged = oldScene.Actors.Where(actor => actor.Type is not (32 or 36)).ToArray();
        var retained = scene.Actors.Where(actor => actor.Type is not (32 or 36)).ToArray();
        Check(unchanged.Length == retained.Length && unchanged.Zip(retained).All(pair =>
            pair.First.Source.Raw.Span.SequenceEqual(pair.Second.Source.Raw.Span)), "all other actors retain their exact declarations");
        foreach (var actor in scene.Actors)
        {
            Check(oldScene.Actors.Any(old => old.Source.Raw.Span.SequenceEqual(actor.Source.Raw.Span)),
                "retained actor stats, wave conditions and placements are unchanged");
        }
        Check(scene.PartySpawns.Select(spawn => (spawn.Actor, spawn.X, spawn.Y, spawn.Facing))
            .SequenceEqual(oldScene.PartySpawns.Select(spawn => (spawn.Actor, spawn.X, spawn.Y, spawn.Facing))),
            "party spawn geometry survives scene compaction");
        foreach (var entry in archive.Entries.Where(entry => entry.Name.StartsWith("EBAT") || entry.Name.StartsWith("SBATTL")))
        {
            if (entry.Name == request.ResourceName) continue;
            var other = SodV2.Parse(archive.ReadMember(entry.Index));
            Check(ReferenceEquals(other, CombatEncounterBalance.Apply(entry.Name, other)), "unrelated encounter is unchanged: " + entry.Name);
        }

        foreach (int count in new[] { 5, 6 })
        {
            var state = CreateParty(root, count);
            var selection = new CombatPartySelection(state, 1);
            selection.ApplyInput((_, input) => input == CombatSelectionInput.Finish, _ => { });
            Check(selection.Completed && selection.ErrorCode == 0, "full recruited party confirms through normal selection");
            selection.Commit(state);
            var restored = new GameState();
            PartyCodec.Copy(state.Party, restored.Party);
            Check(restored.Party.Take(count).All(member => member.SelectedForBattle == 1), "all participation flags survive party serialization");
            var runtime = Bind(root, scene, selection);
            Check(runtime.Actors.Skip(scene.PartyStartIndex).Count(actor => actor.State.Active != 0) == count,
                "all selected heroes enter the battle");
            int current = scene.PartyStartIndex;
            var visited = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                visited.Add(current);
                current = CombatPartyControl.Cycle(runtime.Actors, scene.PartyStartIndex, current);
            }
            Check(visited.Count == count && current == scene.PartyStartIndex, "control cycles through the whole party");
            CheckWaves(root, runtime, state);
        }

        var twoPlayers = new CombatPartySelection(CreateParty(root, 6), 2);
        twoPlayers.ApplyInput((_, input) => input == CombatSelectionInput.Finish, _ => { });
        Check(twoPlayers.Completed && twoPlayers.Leaders[0] != twoPlayers.Leaders[1], "two leaders may confirm the entire party");
        CheckRenderQueue(root, Bind(root, scene, twoPlayers));
        return "Combat balance PASS: EBAT161 9/7/7 enemies, unchanged HP/NPCs/other encounters, full party selection/control, all waves and dynamic depth queue.";
    }

    private static GameState CreateParty(string root, int count)
    {
        var state = InitialGameState.Create(ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT"))),
            LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat"))));
        byte[] template = PartyCodec.EncodeMember(state.Party[0]);
        for (int i = 0; i < 6; i++)
        {
            PartyCodec.DecodeMember(template, state.Party[i]);
            state.Party[i].CombatForm = 5;
            state.Party[i].MemberStatus = i < count ? 1 : 0;
            state.Party[i].SelectedForBattle = i < count ? 1 : 0;
        }
        return state;
    }

    private static CombatSceneRuntime Bind(string root, CombatSceneDefinition scene, CombatPartySelection selection)
    {
        var runtime = new CombatSceneRuntime(root, scene, 0);
        runtime.BindDeclaredAnimations(0);
        runtime.PopulateSelectedParty(selection);
        runtime.BindAllAttributes(3, (int)scene.Request.Kind, 0, () => 1);
        runtime.BindPlayerRoles(selection);
        return runtime;
    }

    private static void CheckWaves(string root, CombatSceneRuntime runtime, GameState state)
    {
        var enemies = runtime.Actors.Where(actor => actor.Binding is not null && actor.State.Team < 100).ToArray();
        int[] generals = [3, 3, 3], archers = [2, 3, 2], mages = [1, 1, 2], totals = [9, 7, 7];
        for (int wave = 0; wave < totals.Length; wave++)
        {
            var group = enemies.Where(actor => actor.State.SpawnEncounterThreshold == wave).ToArray();
            Check(group.Length == totals[wave] && group.Count(actor => actor.Binding!.Info.Type == 30) == generals[wave]
                && group.Count(actor => actor.Binding!.Info.Type == 34) == archers[wave]
                && group.Count(actor => actor.Binding!.Info.Type == 42) == mages[wave], "correct group sizes in wave " + wave);
        }
        Check(enemies.Where(actor => actor.Binding!.Info.Type == 42).All(actor => actor.State.Health == 475), "all four mage HP values unchanged");
        Check(enemies.Where(actor => actor.Binding!.Info.Type == 30).All(actor => actor.State.Health == 262), "general HP unchanged");
        Check(enemies.Where(actor => actor.Binding!.Info.Type == 34).All(actor => actor.State.Health == 122), "archer HP unchanged");
        Check(runtime.Actors.Take(runtime.Scene.PartyStartIndex).Count(actor => actor.Binding is not null
            && actor.State.Team == 100 && actor.State.Health == 400) == 6, "six friendly NPCs unchanged");

        var assets = CombatBattleAssets.Load(root, runtime.Scene);
        var frames = new CombatFrameRuntime(runtime, state, assets.CreateCamera(runtime, (_, _) => { }), new NoActions(), new NoEffects(), () => 1);
        foreach (var actor in runtime.Actors) actor.State.AnimationDeadline = int.MaxValue;
        uint now = 1;
        foreach (int expected in totals)
        {
            for (int attempt = 0; attempt < 20 && !enemies.Any(actor => actor.State.Active != 0 && actor.State.SpawnCondition == 0); attempt++)
            {
                Check(frames.Step(now++).ReturnCode == 0, "pending wave prevents premature victory");
            }
            var active = enemies.Where(actor => actor.State.Active != 0 && actor.State.SpawnCondition == 0).ToArray();
            Check(active.Length == expected, "entire next wave activates without deadlock");
            foreach (var actor in active) actor.State.Active = actor.State.Health = 0;
            Check(frames.Step(now++).ReturnCode is 0 or 1, "defeating the wave preserves the party");
        }
        Check(frames.Step(now++).ReturnCode == 1, "victory follows the last wave");
    }

    private static void CheckRenderQueue(string root, CombatSceneRuntime runtime)
    {
        var assets = CombatBattleAssets.Load(root, runtime.Scene);
        var renderer = new CombatSceneRenderer(runtime, assets, assets.CreateCamera(runtime, (_, _) => { }), new CombatCameraShake());
        ushort[] baseline = new ushort[640 * 480], pixels = new ushort[640 * 480], repeated = new ushort[640 * 480];
        var marker = new SpriteFrame(0, 0, 0, [], [], 1, 1, [new(0, 0, 0, [0x1234], [])]);
        var nearer = marker with { Runs = [new(0, 0, 0, [0xabcd], [])] };
        renderer.PrepareActors(2, false);
        renderer.AppendParticles([]);
        renderer.DrawWorldAndMenu(baseline);
        foreach (int count in new[] { 79, 80, 81, 160, 300 })
        {
            renderer.PrepareActors(2, false);
            var draws = Enumerable.Range(0, count).Select(i => new CombatParticleDraw(i, 10 + i, 50, 1000 + count - i, 10, marker)).ToList();
            draws.Add(new(0, 10, 70, 2000, 10, nearer));
            draws.Add(new(0, 10, 70, 1000, 10, marker));
            renderer.AppendParticles(draws.ToArray());
            renderer.DrawWorldAndMenu(pixels);
            renderer.DrawWorldAndMenu(repeated);
            Check(pixels.AsSpan().SequenceEqual(repeated), "repeated presentation does not mutate the queue");
            for (int i = 0; i < count; i++) Check(pixels[50 * 640 + 10 + i] == 0x1234, "every queued object is drawn");
            Check(pixels[70 * 640 + 10] == 0xabcd, "near object covers far object regardless of insertion order");
            Check(pixels.AsSpan(200 * 640).SequenceEqual(baseline.AsSpan(200 * 640)), "actors and HUD survive particle overflow");
        }
        renderer.PrepareActors(2, false);
        renderer.AppendParticles([]);
        renderer.DrawWorldAndMenu(pixels);
        Check(pixels.AsSpan().SequenceEqual(baseline), "new frame clears previous objects");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Combat balance: " + message);
    }
}
