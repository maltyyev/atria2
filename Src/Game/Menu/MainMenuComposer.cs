using Atria.Render.Scenes;

namespace Atria.Game.Menu;

public delegate void MenuConfirmationRenderer(MenuFrame frame, Span<ushort> target);

/// <summary>Legacy-size snapshots for scene transitions and save previews.</summary>
public static class MainMenuComposer
{
    public static void Draw(MenuFrame frame, ReadOnlySpan<ushort> background, Span<ushort> target, MenuConfirmationRenderer? confirmationRenderer = null)
    {
        if (background.Length != 640 * 480 || target.Length != 640 * 480)
            throw new ArgumentException("Menu snapshot requires a 640x480 surface.");
        background.CopyTo(target);
        MainMenuPresentation.DrawSelection(frame.Selection, target, 640, 480, 640);
        if (frame.Confirmation)
        {
            if (confirmationRenderer is null)
                throw new ArgumentNullException(nameof(confirmationRenderer));
            confirmationRenderer(frame, target);
        }
    }
}
