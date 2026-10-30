namespace Atria.Game.Navigation;

internal enum LocationKind
{
    World = GameMode.World,
    Dungeon = GameMode.Dungeon,
    Town = GameMode.Town,
    Interior = GameMode.Interior,
    Inn = GameMode.Inn,
    Shop = GameMode.Shop
}

internal enum UiScreen
{
    MainMenu = GameMode.MainMenu,
    Save = GameMode.Save,
    Load = GameMode.Load,
    Credits = GameMode.Credits,
    Settings = GameMode.Settings,
    Status = GameMode.Status,
    Quit = GameMode.QuitConfirmation
}

internal readonly record struct ScreenReturn(int Mode, int MusicTrack);

/// <summary>UI history never owns or reinitializes the retained location.</summary>
internal sealed class SessionNavigation
{
    private readonly Stack<(UiScreen Screen, int ReturnMusic)> screens = new();
    private int worldPresentation;

    public LocationKind? Location { get; private set; }
    public int WorldMode => Location is { } location ? (int)location : throw new InvalidOperationException("No active world.");
    public int Mode => screens.TryPeek(out var screen) ? (int)screen.Screen : worldPresentation;
    public int ScreenDepth => screens.Count;

    public SessionNavigation() => Reset();

    public void Reset()
    {
        Location = null;
        worldPresentation = GameMode.MainMenu;
        screens.Clear();
        screens.Push((UiScreen.MainMenu, 1));
    }

    public void RestoreLocation(LocationKind location)
    {
        if (!Enum.IsDefined(location))
            throw new ArgumentOutOfRangeException(nameof(location));

        Location = location;
        worldPresentation = (int)location;
        screens.Clear();
    }

    public void SetWorldReveal(bool active)
    {
        if (Location != LocationKind.World || screens.Count != 0)
            throw new InvalidOperationException("Map reveal requires the active world map.");

        worldPresentation = active ? GameMode.WorldEntry : GameMode.World;
    }

    public void Open(UiScreen screen, int returnMusic)
    {
        if (!Enum.IsDefined(screen))
            throw new ArgumentOutOfRangeException(nameof(screen));

        screens.Push((screen, returnMusic));
    }

    public bool TryClose(out ScreenReturn result)
    {
        if (screens.Count == 0 || (screens.Count == 1 && Location is null))
        {
            result = default;
            return false;
        }

        var closed = screens.Pop();
        result = new(Mode, closed.ReturnMusic);
        return true;
    }
}
