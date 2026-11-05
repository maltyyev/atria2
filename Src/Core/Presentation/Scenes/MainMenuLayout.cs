using Atria.Core.Game.Progression;
using Atria.Core.Presentation.Sprites;
using Atria.Core.Resources;
using Atria.Core.Resources.Codecs;

namespace Atria.Core.Presentation.Scenes;

/// <summary>Controls which main-menu actions are available for the current presentation.</summary>
public readonly record struct MainMenuPresentationPolicy(bool CanContinue, bool ShowQuit)
{
    public static MainMenuPresentationPolicy Desktop { get; } = new(false, true);
    public static MainMenuPresentationPolicy Mobile(bool canContinue) => new(canContinue, false);
}

/// <summary>Button surfaces measured on the approved temple artwork, in presentation pixels.</summary>
public static class MainMenuLayout
{
    public const int Width = 2560, Height = 1440;
    public const int NewItem = 0, LoadItem = 1, SaveItem = 2, QuitItem = 3, CreditsItem = 4, SettingsItem = 5;
    private static readonly int[] mobileOrder = [NewItem, LoadItem, SaveItem, SettingsItem, CreditsItem];
    public static ReadOnlySpan<int> Order => [NewItem, LoadItem, SaveItem, SettingsItem, CreditsItem, QuitItem];
    public static ReadOnlySpan<int> OrderFor(MainMenuPresentationPolicy policy) => policy.ShowQuit ? Order : mobileOrder;
    public static RasterClip ConfirmationYes => new(960, 690, 1240, 798);
    public static RasterClip ConfirmationNo => new(1320, 690, 1640, 798);

    public static int HitTestConfirmation(int x, int y) =>
        Contains(ConfirmationYes, x, y) ? 1 : Contains(ConfirmationNo, x, y) ? 2 : -1;

    private static bool Contains(RasterClip box, int x, int y) =>
        x >= box.Left && x < box.RightExclusive && y >= box.Top && y < box.BottomExclusive;

    public static RasterClip Button(int item) => Button(item, MainMenuPresentationPolicy.Desktop);

    public static RasterClip Button(int item, MainMenuPresentationPolicy policy)
    {
        int row = OrderFor(policy).IndexOf(item);
        if (row < 0)
            throw new ArgumentOutOfRangeException(nameof(item));
        const int left = 466, top = 397, buttonWidth = 644, buttonHeight = 112, rowSpacing = 138;
        int y = top + row * rowSpacing;
        return new(left, y, left + buttonWidth, y + buttonHeight);
    }

    public static int HitTest(int x, int y) => HitTest(x, y, MainMenuPresentationPolicy.Desktop);

    public static int HitTest(int x, int y, MainMenuPresentationPolicy policy)
    {
        foreach (int item in OrderFor(policy))
        {
            var box = Button(item, policy);
            if (Contains(box, x, y))
                return item;
        }
        return -1;
    }

    public static int Navigate(int item, int direction, bool saveDisabled) =>
        Navigate(item, direction, saveDisabled, MainMenuPresentationPolicy.Desktop);

    public static int Navigate(int item, int direction, bool saveDisabled, MainMenuPresentationPolicy policy)
    {
        ReadOnlySpan<int> order = OrderFor(policy);
        int row = order.IndexOf(item);
        if (row < 0 || direction is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(item));
        do
        {
            row = (row + direction + order.Length) % order.Length;
        } while (saveDisabled && order[row] == SaveItem);
        return order[row];
    }
}
