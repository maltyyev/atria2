using System.Text.Json;
using Atria.Game.Navigation;
using Atria.Game.Persistence;

namespace Atria.Game;

public sealed partial class GameSession
{
    private void VerifyUiPreservesLocation()
    {
        int mode = navigation.WorldMode;
        var world = exploration;
        var dungeonController = dungeon;
        var currentMap = map;
        var townResources = townAssets;
        var insideResources = interiorAssets;
        string pendingSave = autosavePending;
        var before = CaptureSaveImage(mode, true, "navigation verification");

        foreach (UiScreen screen in new[] { UiScreen.Status, UiScreen.Settings, UiScreen.Save, UiScreen.Load, UiScreen.Credits })
        {
            OpenScreen(UiScreen.MainMenu);
            OpenScreen(screen);
            ComposeScene(state, host.FrameBuffer);
            host.Present();
            window.Pump();
            if (screen == UiScreen.Save)
            {
                SaveSessionSlot(91);
                if (PersistenceStore.ReadSlot(91)!.Native!.State.Mode != mode)
                    throw new InvalidOperationException("Save menu lost its underlying location.");
            }

            if (!CloseScreen() || state.Mode != GameMode.MainMenu || !CloseScreen() || state.Mode != mode)
                throw new InvalidOperationException("Nested UI did not return to its retained world.");

            var after = CaptureSaveImage(mode, true, "navigation verification");
            if (!NativeGameStateCodec.Encode(before.State).AsSpan().SequenceEqual(NativeGameStateCodec.Encode(after.State))
                || !ReferenceEquals(world, exploration) || !ReferenceEquals(dungeonController, dungeon)
                || !ReferenceEquals(currentMap, map) || !ReferenceEquals(townResources, townAssets)
                || !ReferenceEquals(insideResources, interiorAssets) || pendingSave != autosavePending)
                throw new InvalidOperationException($"UI {screen} mutated/reloaded world {mode}.");

            using var prior = JsonDocument.Parse(before.Context.Payload);
            using var next = JsonDocument.Parse(after.Context.Payload);
            foreach (string name in new[] { "Random", "CurrentMap", "Town", "Exploration", "Dungeon", "Interior", "Location", "Script", "ScriptMember", "PendingHandler", "AdmissionTarget" })
            {
                if (prior.RootElement.GetProperty(name).GetRawText() != next.RootElement.GetProperty(name).GetRawText())
                {
                    Directory.CreateDirectory(sessionOutputDirectory);
                    File.WriteAllText(Path.Combine(sessionOutputDirectory, "ui-before.json"), prior.RootElement.GetRawText());
                    File.WriteAllText(Path.Combine(sessionOutputDirectory, "ui-after.json"), next.RootElement.GetRawText());
                    throw new InvalidOperationException($"UI {screen} mutated retained {name} in world {mode}.");
                }
            }
        }

        Console.WriteLine($"UI isolation passed: location {mode}, five nested screen routes, state/RNG/controllers/resources/pending scripts preserved.");
    }
}
