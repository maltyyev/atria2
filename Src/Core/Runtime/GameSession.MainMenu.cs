using Atria.Core.Contracts.Presentation;
using Atria.Core.Contracts.Host;
using Atria.Core.Game;
using Atria.Core.Game.Progression;
using Atria.Core.Presentation;
using Atria.Core.Presentation.Scenes;
using Atria.Core.Resources;
using Atria.Core.Resources.Localization;

namespace Atria.Core.Runtime;

public sealed partial class GameSession
{
    private MainMenuView? mainMenuView;
    private bool inputHintsDirty = true;

    private bool PresentMainMenu()
    {
        if (blockingControls || state.Mode is not (GameMode.MainMenu or GameMode.QuitConfirmation))
            return false;
        var size = window.ClientSize;
        if (size.Width <= 0 || size.Height <= 0)
            return true;
        mainMenuView ??= new(hostServices.InterfaceRoot);
        if (inputHintsDirty)
        {
            mainMenuView.InvalidateProfileHint();
        }
        bool quitting = state.Mode == GameMode.QuitConfirmation;
        string profile = window is IMobileUiChannel
            ? ""
            : GameText.Format(CanChangeProfile ? "ui.profiles.menuHintDynamic" : "ui.profiles.menuCurrent",
                ("name", playerProfiles.Active.Name), ("button", controls.ProfileMenuHint));
        var frame = mainMenuView.Render(menu.Selection, saveDisabled, menu.Confirmation, quitting ? quit.Selection == 1 : menu.YesSelected, quitting, profile);
        inputHintsDirty = false;
        presenter.Resize(size.Width, size.Height);
        presenter.SetFrameSize(MainMenuLayout.Width, MainMenuLayout.Height);
        presenter.Present(frame);
        return true;
    }
}
