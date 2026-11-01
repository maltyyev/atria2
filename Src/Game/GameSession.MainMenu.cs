using Atria.Render;
using Atria.Render.Scenes;

namespace Atria.Game;

public sealed partial class GameSession
{
    private MainMenuView? mainMenuView;

    private bool PresentMainMenu()
    {
        if (blockingControls || state.Mode is not (GameMode.MainMenu or GameMode.QuitConfirmation))
            return false;
        var size = window.ClientSize;
        if (size.Width <= 0 || size.Height <= 0)
            return true;
        mainMenuView ??= new(hostServices.InterfaceRoot);
        bool quitting = state.Mode == GameMode.QuitConfirmation;
        string profile = GameText.Format(CanChangeProfile ? "ui.profiles.menuHint" : "ui.profiles.menuCurrent",
            ("name", playerProfiles.Active.Name));
        var frame = mainMenuView.Render(menu.Selection, saveDisabled, menu.Confirmation, quitting ? quit.Selection == 1 : menu.YesSelected, quitting, profile);
        presenter.Resize(size.Width, size.Height);
        presenter.SetFrameSize(MainMenuLayout.Width, MainMenuLayout.Height);
        presenter.Present(frame);
        return true;
    }
}
