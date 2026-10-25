using Atria.Render.Scenes;

namespace Atria.Game;

public sealed partial class GameSession
{
    private int pointerX = -1, pointerY = -1;
    private void UpdatePointerFocus()
    {
        int x = controls.MouseX, y = controls.MouseY;
        if (state.Mode is GameMode.MainMenu or GameMode.QuitConfirmation)
        {
            var size = window.ClientSize;
            var viewport = Atria.Render.Viewport.Fit(size.Width, size.Height, MainMenuLayout.Width, MainMenuLayout.Height);
            if (!viewport.TryMapInput(window.MouseX, window.MouseY, out x, out y))
                x = y = -1;
            int pixelX = x * MainMenuLayout.Width / 640, pixelY = y * MainMenuLayout.Height / 480;
            bool confirming = menu.Confirmation || state.Mode == GameMode.QuitConfirmation;
            int item = confirming ? MainMenuLayout.HitTestConfirmation(pixelX, pixelY) : MainMenuLayout.HitTest(pixelX, pixelY);
            if (item < 0 || !confirming && saveDisabled && item == MainMenuLayout.SaveItem)
            {
                controls.DiscardPointerConfirmation();
            }
        }
        if (x == pointerX && y == pointerY)
        {
            return;
        }

        pointerX = x;
        pointerY = y;
        if (x < 0 || dialogueActive || battleActive)
        {
            return;
        }

        if (actions.Depth >= 0 && actions.RenderEnabled)
        {
            actions.PointerSelect(x, y);
            return;
        }

        if (state.Mode == GameMode.MainMenu && menu.Confirmation)
        {
            menu.PointerConfirm(x, y);
        }
        else if (state.Mode == GameMode.MainMenu && !menu.Confirmation)
        {
            int item = MainMenuLayout.HitTest(x * MainMenuLayout.Width / 640, y * MainMenuLayout.Height / 480);
            if (item >= 0)
                menu.PointerSelect(item, saveDisabled);
        }
        else if (state.Mode == GameMode.QuitConfirmation)
        {
            quit.PointerSelect(MainMenuLayout.HitTestConfirmation(x * MainMenuLayout.Width / 640, y * MainMenuLayout.Height / 480));
        }
        else if (state.Mode is GameMode.Save or GameMode.Load)
        {
            persistenceMenu.PointerSelect(x, y);
        }
        else if (state.Mode == GameMode.Town && !town.InputDeferred && !town.LeaveConfirmation && townAssets?.Objects is { } sprites)
        {
            for (int i = SodScenePresentation.SelectableCount(town.Scene) - 1; i >= 0; i--)
            {
                var obj = town.Scene.GetObject(i);
                int index = obj.GetInteger(2);
                if ((uint)index >= (uint)sprites.Frames.Length)
                {
                    continue;
                }

                var sprite = sprites.Frames[index];
                int sx = obj.GetInteger(0), sy = obj.GetInteger(1);
                if (x >= sx && x < sx + sprite.Width && y >= sy && y < sy + sprite.Height)
                {
                    town.PointerSelect(i);
                    return;
                }
            }
        }
        else if (state.Mode == GameMode.Shop && trade is { } current)
        {
            current.PointerSelect(x, y);
        }
        else if (state.Mode == GameMode.Status && !statusRuntime.Menu.DetailActive && x is >= 20 and < 148 && y is >= 240 and < 430)
        {
            statusRuntime.Menu.PointerPage((y - 240) / 38);
        }
    }
}
