using Atria.Game.Interiors;
using Atria.Game.State;
using Atria.Game.Town;
using Atria.Resources;
using Atria.Script;

namespace Atria.Tests.Town;

public static class TigerLibraryTests
{
    public static string Run(string root)
    {
        using var scenes = BinderArchive.Open(Path.Combine(root, "AT2SOD.000"));
        byte[] original = scenes.ReadMember(scenes.FindResource("32tiger.sod"u8));
        Check(SodV2.Parse(original).GetObject(4).GetString(1).Span.SequenceEqual("32_libar.scr"u8),
            "original doorway reproduces the misspelled reference");
        Check(ReferenceEquals(TownSceneRepairs.Normalize("OTHER", original), original),
            "the correction must not apply to another scene");

        var assets = TownSceneAssets.Load(root, "32tiger.sod"u8);
        var town = new TownController();
        var commands = new List<TownCommand>();
        town.SetSavedSelection(4);
        town.Enter(assets.Scene, assets.Objects, 32, 32, "32tiger.sod"u8.ToArray(), default,
            TownCatalog.Get(32).MusicTrack, commands.Add);
        commands.Clear();
        town.ChooseAction(0, commands.Add);
        var preEnter = commands.Single(command => command.Kind == TownEffectKind.RunPreEnter);

        // Use the same resource read as GameSession.HandleTown; helper-only tests missed this crash.
        byte[] script = GameResources.Read(root, "SCRIPT.000", preEnter.Bytes.Span);
        Check(preEnter.Bytes.Span.SequenceEqual("32_libra.scr"u8), "doorway selects the existing library script");
        var program = ScriptProgram.Parse("32_LIBRA", script);
        Check(program.LibraryRepairApplied, "the separate malformed-script repair remains active");
        var state = new GameState { Mode = 400, ScriptActive = 1 };
        new ScriptMachine(program, new ScriptStateHost(state)).Invoke("pre_enter");
        Check(state.EnterPermission == 1 && state.ScriptActive == 0, "entry script permits and completes entry");
        commands.Clear();
        town.CompletePreEnter(true, state.Mode, commands.Add);
        int mode = commands.Single(command => command.Kind == TownEffectKind.ChangeMode).Value;

        var doorway = assets.Scene.GetObject(town.Selection);
        var library = TownSceneAssets.Load(root, doorway.GetString(0).Span);
        var interiorCommands = new List<InteriorCommand>();
        new InteriorController().Enter(mode, doorway, library.Scene, interiorCommands.Add);
        var context = interiorCommands.Single(command => command.Kind == InteriorCommandKind.SetScriptContext).Bytes;
        Check(context.Span.SequenceEqual(preEnter.Bytes.Span), "town doorway and library interior share the script");
        Check(GameResources.Read(root, "SCRIPT.000", context.Span).AsSpan().SequenceEqual(script),
            "interior can load the same original script bytes");
        Check(scenes.ReadMember(scenes.FindResource("32tiger.sod"u8)).AsSpan().SequenceEqual(original),
            "original archive remains unchanged");
        return "Tiger library: real town loader, action menu entry, pre_enter, interior script and immutable archive passed.";
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Tiger library: " + message);
        }
    }
}
