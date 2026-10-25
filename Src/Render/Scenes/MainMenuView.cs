using Atria.Localization;
using Atria.Render.Codecs;
using Atria.Render.UI;

namespace Atria.Render.Scenes;

/// <summary>Cached full-resolution menu; drawing never advances game or menu state.</summary>
public sealed class MainMenuView(string assetRoot)
{
    private readonly ushort[] background = MainMenuArtwork.LoadNative(assetRoot);
    private readonly ushort[] pixels = new ushort[MainMenuLayout.Width * MainMenuLayout.Height];
    private readonly uint[] presentation = new uint[MainMenuLayout.Width * MainMenuLayout.Height];
    private (int Selection, bool SaveDisabled, bool Confirmation, bool Yes, bool Quit, LocalizationCatalog Catalog)? cached;

    public ReadOnlySpan<uint> Render(int selection, bool saveDisabled, bool confirmation, bool yes, bool quit)
    {
        var key = (selection, saveDisabled, confirmation, yes, quit, GameText.Current);
        if (cached == key)
            return presentation;
        background.CopyTo(pixels, 0);
        MainMenuArtwork.DrawLabels(selection, pixels, MainMenuLayout.Width, MainMenuLayout.Height, MainMenuLayout.Width, null, saveDisabled);
        if (confirmation || quit)
            DrawConfirmation(yes, quit);
        for (int i = 0; i < pixels.Length; i++)
            presentation[i] = SpriteRaster.ToBgra32(pixels[i]);
        cached = key;
        return presentation;
    }

    private void DrawConfirmation(bool yes, bool quit)
    {
        const int width = MainMenuLayout.Width, height = MainMenuLayout.Height;
        var panel = new RasterClip(680, 510, 1880, 840);
        UiRaster.TintHalf(pixels, width, height, width, 0, 0, width, height, 0);
        for (int y = panel.Top; y < panel.BottomExclusive; y++)
            pixels.AsSpan(y * width + panel.Left, panel.RightExclusive - panel.Left).Fill(0x18a2);
        MainMenuArtwork.Border(pixels, width, height, width, panel, 0xbccc, 3);
        MainMenuArtwork.DrawCentered(GameText.Get(quit ? "messages.306" : "messages.268"), pixels, width, height, width,
            new(720, 555, 1840, 660), 46, 0xe653);
        // These centers correspond to the retained logical confirmation hit regions (250/350, 236).
        MainMenuArtwork.DrawCentered(GameText.Get(quit ? "messages.307" : "messages.269"), pixels, width, height, width,
            MainMenuLayout.ConfirmationYes, 48, yes ? (ushort)0xff79 : (ushort)0xbccc);
        MainMenuArtwork.DrawCentered(GameText.Get(quit ? "messages.308" : "messages.270"), pixels, width, height, width,
            MainMenuLayout.ConfirmationNo, 48, yes ? (ushort)0xbccc : (ushort)0xff79);
    }
}
