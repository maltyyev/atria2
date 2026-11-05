using Atria.Core.Game.Menu;
using Atria.Core.Presentation.Menu;
using Atria.Core.Presentation.Scenes;
using Tests.Core.Persistence;

namespace Tests.Core.Menu;

public static class MainMenuTests
{
    public static string Run()
    {
        var menu = new MainMenuController();
        var commands = new List<MenuCommand>();
        void Input(MenuInput input, bool disabled = false) => menu.Input(input, disabled, commands.Add);
        void Stable()
        {
            for (int i = 0; i < 20; i++)
            {
                menu.AdvanceLogicalFrame();
            }

            Check(menu.VisualState == MenuVisualState.Stable, "reveal did not settle");
        }

        Check(menu.AdvanceLogicalFrame().LoadResources, "entry loads before rendering");
        Stable();
        Input(new(Next: true));
        Check(menu.Selection == 1 && menu.PanelItem == 1, "vertical navigation selects immediately");
        Input(new(Next: true), true);
        Check(menu.Selection == 5, "disabled save skips to settings");
        Input(new(Previous: true), true);
        Check(menu.Selection == 1, "disabled save backward skip");
        Input(new(Previous: true));
        Stable();
        Input(new(Accept: true));
        Check(menu.Confirmation && menu.YesSelected, "new-game default yes");
        Input(new(Next: true));
        Check(!menu.YesSelected && menu.AdvanceLogicalFrame().ConfirmationFlash == 2, "modal no toggle/flash");
        Input(new(Accept: true));
        Check(!menu.Confirmation && menu.VisualState == MenuVisualState.Stable, "no returns to menu");
        Input(new(Accept: true));
        commands.Clear();
        Input(new(Accept: true));
        Check(commands.Select(c => c.Effect).SequenceEqual([MenuEffect.ReleaseMenuSprite, MenuEffect.StopAudio, MenuEffect.PrepareMovieDisplay, MenuEffect.PlayMovie, MenuEffect.RestoreMovieDisplay, MenuEffect.ResetGame, MenuEffect.SetNewGameGlobals, MenuEffect.LoadGroundMap, MenuEffect.InitializeGroundMap, MenuEffect.CopyInitialGroundLocation, MenuEffect.ChangeMode]), "new game ordered effects");
        Check(commands[3].Resource == "jamie1.jad" && commands[^1].Value == 400, "new game destination");

        MainMenuPresentationPolicy mobileStart = MainMenuPresentationPolicy.Mobile(false);
        MainMenuPresentationPolicy mobileContinue = MainMenuPresentationPolicy.Mobile(true);
        Check(MainMenuLayout.OrderFor(mobileStart).SequenceEqual([0, 1, 2, 5, 4])
            && MainMenuLayout.OrderFor(mobileContinue).SequenceEqual([0, 1, 2, 5, 4]),
            "mobile main menu always omits Quit while retaining the five original row positions");
        var mobileInitial = new MainMenuController();
        commands.Clear();
        mobileInitial.Input(new(Accept: true), false, commands.Add, mobileStart);
        Check(mobileInitial.Confirmation && mobileInitial.YesSelected
            && !commands.Any(command => command.Effect == MenuEffect.TryReturnToPreviousMode),
            "the initial mobile menu keeps New Game and its existing confirmation");

        var staleQuit = new MainMenuController();
        staleQuit.PointerSelect(MainMenuLayout.QuitItem, false);
        commands.Clear();
        staleQuit.Input(new(Accept: true), false, commands.Add, mobileContinue);
        Check(staleQuit.Selection == MainMenuLayout.NewItem && !staleQuit.Confirmation
            && commands.Select(command => command.Effect).SequenceEqual([MenuEffect.TryReturnToPreviousMode]),
            "a stale hidden Quit selection is normalized and Continue reuses the existing return path");
        Check(MainMenuLayout.Navigate(MainMenuLayout.CreditsItem, 1, false, mobileStart) == MainMenuLayout.NewItem
            && MainMenuLayout.Navigate(MainMenuLayout.NewItem, -1, false, mobileStart) == MainMenuLayout.CreditsItem,
            "mobile navigation wraps across only visible rows");

        foreach (var (item, mode) in new[]
        {
            (1, 230),
            (2, 220),
            (3, 9999),
            (4, 240),
            (5, 250)
        }
        )
        {
            var other = new MainMenuController();
            other.AdvanceLogicalFrame();
            for (int i = 0; i < 20; i++)
            {
                other.AdvanceLogicalFrame();
            }

            other.PointerSelect(item, false);

            commands.Clear();
            other.Input(new(Accept: true), false, commands.Add);
            Check(commands[^1] == new MenuCommand(MenuEffect.ChangeMode, mode), "mode dispatch " + item);
            Check(commands[1].Effect == (item == 3 ? MenuEffect.InitializeQuitDialog : MenuEffect.ReleaseMenuSprite), "dispatch preparation " + item);
            for (int i = 0; i < 100; i++)
            {
                Check(other.AdvanceLogicalFrame().Flames.All(f => f >= 0 && f <= 10), "flame wrap");
            }
        }

        var yesFlash = MainMenuModalPlan.Build(true, 2);
        var noFlash = MainMenuModalPlan.Build(false, 2);
        Check(!yesFlash.Any(p => p.Kind == ModalPrimitive.ForegroundText && p.DataAddress == 0x456b98), "yes flash suppresses foreground");
        Check(noFlash.Any(p => p.Kind == ModalPrimitive.ForegroundText && p.DataAddress == 0x456be8), "no flash retains foreground");
        return "Main menu: immediate vertical selection, disabled save, confirmation, retained cursors, all destinations and ordered movie/reset passed.";
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception("Menu trace: " + message);
        }
    }
}
