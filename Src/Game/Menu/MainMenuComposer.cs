using Atria.Render.Codecs;
using Atria.Render.Scenes;

namespace Atria.Game.Menu;

public delegate void MenuConfirmationRenderer(MenuFrame frame, Span<ushort> target);
public static class MainMenuComposer
{
    // Localized captions are drawn over the text-free artwork.
    // Modal renderer is mandatory on modal frames until 0x423660 text/box helpers are integrated.
    public static void Draw(
        MenuFrame frame,
        ReadOnlySpan<ushort> background,
        AnimationFile fireAnimation,
        SpriteFile fireSprites,
        Span<ushort> target,
        MenuConfirmationRenderer? confirmationRenderer = null
    )
    {
        if (background.Length != 640 * 480 || target.Length != 640 * 480)
        {
            throw new ArgumentException("Menu requires the native 640x480 surface.");
        }

        if (frame.Confirmation && confirmationRenderer is null)
        {
            throw new NotSupportedException("Native new-game confirmation composition requires a recovered modal renderer.");
        }

        background.CopyTo(target);
        if (frame.LoadResources)
        {
            return;
        }

        MainMenuPresentation.DrawSelection(frame.Selection, target, 640, 480, 640);
        if (frame.PanelItem >= 0)
        {
            MainMenuPresentation.DrawPanel(frame.PanelItem, frame.PanelReveal, target, 640, 480, 640);
        }

        if (frame.Confirmation)
        {
            MainMenuPresentation.DrawPanel(frame.PanelItem < 0 ? frame.Selection : frame.PanelItem, frame.ConfirmationPanelReveal, target, 640, 480, 640);
            confirmationRenderer!(frame, target);
        }

        MainMenuPresentation.DrawFlames(fireAnimation, fireSprites, frame.Flames, target, 640, 480, 640);
    }
}
