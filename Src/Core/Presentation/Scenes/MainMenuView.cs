using Atria.Core.Contracts.Presentation;
using Atria.Core.Presentation.Sprites;
using Atria.Core.Presentation.UI;
using Atria.Core.Resources.Codecs;
using Atria.Core.Resources.Localization;

namespace Atria.Core.Presentation.Scenes;

/// <summary>Cached full-resolution menu; drawing never advances game or menu state.</summary>
public sealed class MainMenuView(string assetRoot, bool mobile = false)
{
    private static readonly RasterClip profileHintBounds = new(320, 1280, 2240, 1360);
    private readonly ushort[] background = MainMenuArtwork.LoadNative(assetRoot, mobile);
    private readonly ushort[] pixels = new ushort[MainMenuLayout.Width * MainMenuLayout.Height];
    private readonly uint[] presentation = new uint[MainMenuLayout.Width * MainMenuLayout.Height];
    private (int Selection, bool SaveDisabled, bool Confirmation, bool Yes, bool Quit, LocalizationCatalog Catalog)? cached;
    private string? cachedProfile;
    private bool cachedCanContinue, cachedShowQuit;
    private bool profileHintDirty = true;

    /// <summary>Forces the cached profile hint strip to redraw on the next frame.</summary>
    internal void InvalidateProfileHint() => profileHintDirty = true;

    public ReadOnlySpan<uint> Render(int selection, bool saveDisabled, bool confirmation, bool yes, bool quit, string profile = "") =>
        Render(selection, saveDisabled, confirmation, yes, quit, profile, MainMenuPresentationPolicy.Desktop);

    public ReadOnlySpan<uint> Render(int selection, bool saveDisabled, bool confirmation, bool yes, bool quit,
        string profile, MainMenuPresentationPolicy policy)
    {
        LocalizationCatalog catalog = GameText.Current;
        if (cached is not { } previous
            || previous.Selection != selection
            || previous.SaveDisabled != saveDisabled
            || previous.Confirmation != confirmation
            || previous.Yes != yes
            || previous.Quit != quit
            || cachedCanContinue != policy.CanContinue
            || cachedShowQuit != policy.ShowQuit
            || !ReferenceEquals(previous.Catalog, catalog))
        {
            background.CopyTo(pixels, 0);
            MainMenuArtwork.DrawLabels(selection, pixels, MainMenuLayout.Width, MainMenuLayout.Height, MainMenuLayout.Width, null, saveDisabled, policy);
            if (confirmation || quit)
                DrawConfirmation(yes, quit);
            for (int i = 0; i < pixels.Length; i++)
                presentation[i] = SpriteRaster.ToBgra32(pixels[i]);
            cached = (selection, saveDisabled, confirmation, yes, quit, catalog);
            cachedCanContinue = policy.CanContinue;
            cachedShowQuit = policy.ShowQuit;
            cachedProfile = null;
            profileHintDirty = true;
        }

        if (profileHintDirty || cachedProfile != profile)
            DrawProfileHint(profile, confirmation || quit);
        return presentation;
    }

    private void DrawProfileHint(string profile, bool dimmed)
    {
        const int width = MainMenuLayout.Width;
        int hintWidth = profileHintBounds.RightExclusive - profileHintBounds.Left;
        for (int y = profileHintBounds.Top; y < profileHintBounds.BottomExclusive; y++)
        {
            int start = y * width + profileHintBounds.Left;
            background.AsSpan(start, hintWidth).CopyTo(pixels.AsSpan(start, hintWidth));
        }

        if (profile.Length > 0)
            MainMenuArtwork.DrawCentered(profile, pixels, width, MainMenuLayout.Height, width, profileHintBounds, 32, 0xd69a);
        if (dimmed)
            UiRaster.TintHalf(pixels, width, MainMenuLayout.Height, width, profileHintBounds.Left, profileHintBounds.Top,
                hintWidth, profileHintBounds.BottomExclusive - profileHintBounds.Top, 0);

        for (int y = profileHintBounds.Top; y < profileHintBounds.BottomExclusive; y++)
        {
            int start = y * width + profileHintBounds.Left;
            for (int x = 0; x < hintWidth; x++)
                presentation[start + x] = SpriteRaster.ToBgra32(pixels[start + x]);
        }

        cachedProfile = profile;
        profileHintDirty = false;
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
