using Atria.Game.Navigation;
using Atria.Game.Town;

namespace Atria.Game;

public sealed partial class GameSession
{
    private readonly SessionNavigation navigation = new();
    private uint worldScreenOpenedAt;

    private void OpenScreen(UiScreen screen)
    {
        if (navigation.ScreenDepth == 0)
            worldScreenOpenedAt = gate.LastFrameMilliseconds;

        navigation.Open(screen, musicId);
        actions.SetRenderEnabled(false);
        SetVisibleMode(navigation.Mode);
        InitializeScreen(screen);
    }

    private void InitializeScreen(UiScreen screen)
    {
        switch (screen)
        {
            case UiScreen.MainMenu:
                menuSpriteActive = true;
                menu.Enter();
                break;
            case UiScreen.Status: EnterStatus(); break;
            case UiScreen.Settings: EnterSettings(); break;
            case UiScreen.Credits: EnterCredits(); break;
            case UiScreen.Save:
            case UiScreen.Load: EnterPersistence((int)screen); break;
            case UiScreen.Quit: EnterQuitConfirmation(); break;
            default: throw new ArgumentOutOfRangeException(nameof(screen));
        }
    }

    private bool CloseScreen()
    {
        if (!navigation.TryClose(out var resumed))
            return false;

        SetVisibleMode(resumed.Mode);
        actions.SetRenderEnabled(navigation.ScreenDepth == 0);
        if (navigation.ScreenDepth == 0)
        {
            uint elapsed = unchecked(gate.LastFrameMilliseconds - worldScreenOpenedAt);
            if (navigation.Location == LocationKind.World)
                exploration?.DelayMovement(elapsed);
            else if (navigation.Location == LocationKind.Dungeon)
                dungeon?.Motion.DelayMovement(elapsed);
        }
        if (resumed.Mode == GameMode.MainMenu)
            InitializeScreen(UiScreen.MainMenu);

        if (musicId != resumed.MusicTrack)
        {
            musicId = resumed.MusicTrack;
            music.Update(musicId);
        }

        return true;
    }

    private void SetVisibleMode(int mode)
    {
        controls.Clear();
        state.Mode = mode;
        state.ModeStackDepth = navigation.ScreenDepth;
        hasFrame = false;
    }

    private void CommitLocation(LocationKind destination, bool townArrival = true)
    {
        TownSceneAssets? prepared = destination switch
        {
            LocationKind.Town => TownSceneAssets.Load(root,
                (location ?? throw new InvalidOperationException("No current town reference.")).SceneMember.Span),
            LocationKind.Interior or LocationKind.Inn or LocationKind.Shop => TownSceneAssets.Load(root,
                town.Scene.GetObject(town.Selection).GetString(0).Span, loadObjects: destination != LocationKind.Inn),
            LocationKind.World or LocationKind.Dungeon => null,
            _ => throw new ArgumentOutOfRangeException(nameof(destination))
        };
        bool hadLocation = navigation.Location is not null;
        navigation.RestoreLocation(destination);
        SetVisibleMode(navigation.Mode);
        switch (destination)
        {
            case LocationKind.Town:
                InitializeTown(prepared!, townArrival);
                break;
            case LocationKind.World: EnterExploration(GameMode.World); break;
            case LocationKind.Dungeon: EnterDungeon(); break;
            case LocationKind.Interior:
            case LocationKind.Inn:
            case LocationKind.Shop: EnterInterior((int)destination, prepared!); break;
            default: throw new ArgumentOutOfRangeException(nameof(destination));
        }

        if (hadLocation)
            RequestAutosave(Persistence.AutosaveReason.Location);
    }

    private void InitializeTown(TownSceneAssets prepared, bool arrival)
    {
        var selected = location ?? throw new InvalidOperationException("No current town reference.");
        townAssets = prepared;
        town.Initialize(townAssets.Scene, townAssets.Objects, selected.SceneIndex, selected.SceneMember,
            TownCatalog.Name(messages, selected.SceneIndex), TownCatalog.Get(selected.SceneIndex).MusicTrack, HandleTown);
        if (arrival)
            town.QueueArrival(HandleTown);
    }

    private void ShowTermination(int mode)
    {
        navigation.Reset();
        SetVisibleMode(mode);
        if (mode == GameMode.Defeat)
            EnterDefeat();
        else if (mode == GameMode.Ending)
            EnterEnding();
        else
            throw new ArgumentOutOfRangeException(nameof(mode));
    }
}
