using Atria.Game.Combat;
using Atria.Game.Controls;
using Atria.Game.Persistence;
using Atria.Game.Progression;
using Atria.Game.State;
using Atria.Game.Status;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Tests.State;

public static class HeroRosterTests
{
    private sealed class Effects : ICombatControlEffects
    {
        public bool GlobalEffectBusy => false;
        public void ShowRankNotice(int rank, uint milliseconds) { }
        public void PlaySound(int id) { }
        public void SpawnActorParticle(int actor, int id, uint now) { }
        public void ApplyFrameEffect(int actor, int id, int argument, uint now) { }
        public void ShowConsumedItemNotice(int id, uint now) { }
    }

    public static string Run(string root)
    {
        var originals = Enumerable.Range(0, HeroCatalog.Default.Count).Select(i => HeroCatalog.Default[i]).ToArray();
        const int total = 20;
        var catalog = new HeroCatalog(originals.Concat(Enumerable.Range(originals.Length, total - originals.Length)
            .Select(i => originals[1] with { Id = $"test-{i}", StorageIndex = GameState.LegacyPartyRecordCount + i - originals.Length })));
        var items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT")));
        var messages = LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat")));
        var state = InitialGameState.Create(items, messages, catalog);
        Check(state.Heroes.Length == total && state.Party.Length == total + GameState.LegacyPartyRecordCount - originals.Length, "separate playable roster and legacy escorts");
        foreach (var member in state.Heroes)
        {
            member.MemberStatus = 1;
            member.SelectedForBattle = 1;
            member.CombatForm = 5;
        }
        for (int i = originals.Length; i < GameState.LegacyPartyRecordCount; i++)
        {
            state.Party[i].MemberStatus = 1;
            state.Party[i].Health = 1234 + i;
            Check(!state.Heroes.Contains(state.Party[i]), "escorts never become playable heroes");
        }
        Check(state.HasHero("test-19") && !state.HasHero("missing"), "stable membership predicate");
        byte[] escorts = PartyCodec.Encode(state.Party.Skip(originals.Length).Take(GameState.LegacyPartyRecordCount - originals.Length).ToArray());

        var selection = new CombatPartySelection(state, 2);
        Check(selection.PointerSelect(0, total - 1), "select last hero");
        Check(selection.FirstVisible <= total - 1 && selection.FirstVisible + CombatPartySelection.VisibleRows > total - 1, "selection scroll follows last hero");
        selection.PointerSelect(1, 0);
        selection.ApplyInput((_, action) => action == CombatSelectionInput.Finish, _ => { });
        Check(selection.Completed && selection.Selected.Length == total, "all heroes confirm with two distinct leaders");
        selection.Commit(state);
        var scene = CombatSceneDefinition.Load(root, new(FixedEncounterKind.Forced, 161), catalog);
        var runtime = new CombatSceneRuntime(root, scene, 0);
        runtime.BindDeclaredAnimations(0);
        runtime.PopulateSelectedParty(selection);
        runtime.BindAllAttributes(3, 1, 0, () => 1);
        runtime.BindPlayerRoles(selection);
        Check(runtime.Actors.Skip(scene.PartyStartIndex).Count(actor => actor.State.Active != 0) == total, "twenty heroes bind animations and enter battle");
        int current = scene.PartyStartIndex + total - 1;
        var visited = new HashSet<int>();
        for (int i = 0; i < total; i++)
        {
            visited.Add(current);
            current = CombatPartyControl.Cycle(runtime.Actors, scene.PartyStartIndex, current);
        }
        Check(visited.Count == total - 1, "player one cycles every hero except player two's leader");
        CombatOutcomeRewards.CopyEquipmentBack(state, runtime.Actors, scene.PartyStartIndex);
        InventoryMaintenance.ReconcileBattleSupplies(state);
        Check(escorts.AsSpan().SequenceEqual(PartyCodec.Encode(state.Party.Skip(originals.Length).Take(GameState.LegacyPartyRecordCount - originals.Length).ToArray())), "battle/inventory leave escort records unchanged");

        var xp = new ExperienceAllocationSession(PartyCodec.Encode(state.Party), total * 100, catalog);
        for (int i = 1; i < total; i++) xp.ApplyInput(action => action == ExperienceInput.NextMember);
        Check(xp.SelectedMember == total - 1 && xp.Allocation.ToArray().All(award => award == 100), "all heroes receive XP and last is navigable");
        xp.ApplyInput(action => action == ExperienceInput.CycleMode);
        Check(xp.Allocation.ToArray().Sum() == total * 100, "waterfill preserves XP for expanded roster");
        var assets = CombatSelectionAssets.Load(root);
        var pixels = new ushort[640 * 480];
        var battleAssets = CombatBattleAssets.Load(root, scene);
        var camera = battleAssets.CreateCamera(runtime, (_, _) => { });
        var input = new CombatScanInput(_ => false);
        var actions = new CombatActorController(runtime, state,
            ControlBindings.Parse(File.ReadAllBytes(Path.Combine(root, "BATTLE.CFG"))), input, new Effects(), () => 1);
        actions.SelectedMoves[0][total - 1] = "v:4";
        Check(actions.SelectedMoves.All(moves => moves.Length == total), "manual action banks include every hero");
        var battleFrames = new CombatFrameRuntime(runtime, state, camera, actions, new Effects(), () => 1);
        Check(battleFrames.Step(1).ReturnCode == 0, "expanded roster executes the real battle frame");
        var battleRenderer = new CombatSceneRenderer(runtime, battleAssets, camera, new CombatCameraShake());
        battleRenderer.PrepareActors(2, false);
        battleRenderer.AppendParticles([]);
        battleRenderer.DrawWorldAndMenu(pixels);
        new CombatSelectionRaster(assets).Draw(selection, pixels);
        new ExperienceAllocationRaster(assets.Etc, assets.Font, assets.Labels, X87Precision.Bits53)
            .Draw(xp.Party, xp.Allocation, xp.SelectedMember, xp.Mode, 0, pixels, heroCatalog: catalog);
        var menu = new StatusMenuRuntime();
        menu.Menu.SetSelectedActor(total - 1);
        Check(menu.Menu.SelectedActor == total - 1, "status accepts last hero");
        using (var archive = BinderArchive.Open(Path.Combine(root, "AT2SPR.000")))
        {
            SpriteFile Load(ReadOnlySpan<byte> key) => SpriteFile.Decode(archive.ReadMember(archive.FindResource(key)));
            var renderer = new StatusMenuRenderer(Load("item.spr"u8), Load("etc.spr"u8), Load("charinfo.spr"u8),
                assets.Font, BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "eng.fnt")), false), items,
                messages, File.ReadAllBytes(Path.Combine(root, "CHARINTR.DAT")));
            var plan = renderer.BuildRootPlan(menu, state, new());
            Check(plan.Any(command => command.Primitive == StatusPrimitive.Portrait && command.A == catalog[total - 1].Portrait),
                "status portrait uses catalog mapping, not hero ordinal");
            renderer.DrawPlan(plan, new(), pixels);
        }

        state.Heroes[^1].Health = 77;
        state.Heroes[^1].Experience = 4321;
        byte[] saved = NativeGameStateCodec.Encode(state);
        var restored = NativeGameStateCodec.Decode(saved, catalog);
        Check(PartyCodec.Encode(state.Party).AsSpan().SequenceEqual(PartyCodec.Encode(restored.Party)), "all hero and escort bytes survive save/load");
        var reordered = new HeroCatalog(originals.Concat(Enumerable.Range(originals.Length, total - originals.Length).Reverse().Select(i => catalog[i])));
        var remapped = NativeGameStateCodec.Decode(saved, reordered);
        Check(remapped.Heroes[remapped.HeroCatalog.Find("test-19")].Experience == 4321, "save identifies additions by stable ID");
        Reject(() => NativeGameStateCodec.Decode(saved), "missing hero definitions reject instead of losing data");
        Reject(() => NativeGameStateCodec.Decode(saved.AsSpan(0, saved.Length - 1), catalog), "truncated additions reject");
        var old = InitialGameState.Create(items, messages);
        restored = NativeGameStateCodec.Decode(NativeGameStateCodec.Encode(old), catalog);
        Check(restored.Heroes.Skip(originals.Length).All(hero => hero.MemberStatus == 0 && hero.Level > 0), "old saves initialize unrecruited additions");
        Check(PartyCodec.Encode(old.Party).AsSpan().SequenceEqual(PartyCodec.Encode(restored.Party.Take(GameState.LegacyPartyRecordCount).ToArray())), "old save records stay byte-identical");

        var controls = new ControlProfile();
        controls.Players[0].EnsureHeroCount(total);
        controls.Players[0].Quick[^1][0] = "p:28";
        var loadedControls = ControlProfileStore.Decode(ControlProfileStore.Encode(controls));
        Check(loadedControls.Players[0].Quick[total - 1][0] == "p:28", "additional hero shortcuts persist");
        Check(ControlProfileStore.Decode(ControlProfileStore.Encode(new ControlProfile())).Players[0].Quick.Length >= originals.Length, "old control profiles remain valid");
        return "Hero roster PASS: twenty heroes, escort isolation, selection/scroll/control/XP/raster, stable save IDs, old-save migration and shortcuts.";
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException(message);
    }
}
