using Atria.Core.Contracts.Input;
using Atria.Core.Contracts.Presentation;
using Atria.Core.Game.Combat;
using Atria.Core.Game.Menu;
using Atria.Core.Persistence.Controls;
using Atria.Core.Presentation;
using Atria.Core.Presentation.Controls;
using Atria.Core.Presentation.Menu;
using Atria.Core.Presentation.Scenes;
using Atria.Core.Presentation.Sprites;
using Atria.Core.Presentation.UI;
using Atria.Core.Resources;
using Atria.Core.Resources.Localization;
using Atria.Core.Runtime.Input;
using Atria.Platforms.Windows.Win32;
using Tests.Core.Render;

namespace Tests.Windows.Menu;

public static class VerticalMenuTests
{
    private sealed class Pad : IGamepadDevice
    {
        public PadSample Sample;
        public PadSample Read(int slot) => slot == 0 ? Sample : default;
        public void Vibrate(int slot, ushort left, ushort right)
        {
        }
    }

    public static string Run(string root)
    {
        int[] expectedOrder = [0, 1, 2, 5, 4, 3];
        var bindings = ControlBindings.Parse(File.ReadAllBytes(Path.Combine(root, "BATTLE.CFG")));
        foreach (bool gamepad in new[] { false, true })
        {
            var pad = new Pad();
            using var input = new InputRouter(ControlProfile.Defaults(bindings), bindings, pad);
            var menu = new MainMenuController();
            var navigation = new MenuNavigation(bindings, input.MenuInput);
            bool[] keys = new bool[256], pressed = new bool[256], mouse = new bool[3];
            const int downArrowScan = 0xd0;
            const ushort dpadDown = 0x0002, confirmA = 0x1000;
            for (int i = 1; i <= expectedOrder.Length; i++)
            {
                pad.Sample = new(true, 0, 0, 0, 0, 0, 0, 0);
                input.Poll(keys, pressed, mouse, true, i * 2);
                if (gamepad)
                    pad.Sample = pad.Sample with
                    {
                        Buttons = dpadDown
                    };
                else
                    pressed[downArrowScan] = true;
                input.Poll(keys, pressed, mouse, true, i * 2 + 1);
                Array.Clear(pressed);
                menu.Input(new(Next: navigation.Next()), false, _ => { });
                Check(menu.Selection == expectedOrder[i % expectedOrder.Length], $"keyboard/pad follows visible order without intervening renders (pad={gamepad}, step={i}, item={menu.Selection})");
            }
            menu.PointerSelect(5, false);
            pad.Sample = new(true, 0, 0, 0, 0, 0, 0, 0);
            input.Poll(keys, pressed, mouse, true, 100);
            if (gamepad)
                pad.Sample = pad.Sample with
                {
                    Buttons = confirmA
                };
            else
                pressed[0x1c] = true;
            input.Poll(keys, pressed, mouse, true, 101);
            var commands = new List<MenuCommand>();
            menu.Input(new(Accept: input.MenuInput.QueryLegacyMenuScan(bindings.Confirm)), false, commands.Add);
            Check(commands.Any(c => c.Effect == MenuEffect.ChangeMode && c.Value == 250), "Enter/A activates pointed settings immediately");
            Array.Clear(pressed);
            pad.Sample = pad.Sample with
            {
                Buttons = 0
            };
            input.Poll(keys, pressed, mouse, true, 102);
            input.Poll(keys, pressed, mouse, true, 103, [true, false, false]);
            input.DiscardPointerConfirmation();
            Check(!input.MenuInput.Menu(MenuAction.Confirm), "click outside a button cannot activate the previously selected row");
            pressed[0x1c] = true;
            input.Poll(keys, pressed, mouse, true, 104);
            input.DiscardPointerConfirmation();
            Check(input.MenuInput.Menu(MenuAction.Confirm), "pointer rejection preserves keyboard confirmation");
        }

        var resumePad = new Pad();
        using (var resumeInput = new InputRouter(ControlProfile.Defaults(bindings), bindings, resumePad))
        {
            var resumeMenu = new MainMenuController();
            resumeMenu.PointerSelect(MainMenuLayout.QuitItem, false);
            resumePad.Sample = new(true, 0, 0, 0, 0, 0, 0, 0);
            resumeInput.Poll(new bool[256], new bool[256], new bool[3], true, 2000);
            resumePad.Sample = resumePad.Sample with { Buttons = 0x1000 };
            resumeInput.Poll(new bool[256], new bool[256], new bool[3], true, 2001);
            var commands = new List<MenuCommand>();
            resumeMenu.Input(new(Accept: resumeInput.MenuInput.QueryLegacyMenuScan(bindings.Confirm)), false,
                commands.Add, MainMenuPresentationPolicy.Mobile(canContinue: true));
            Check(commands.Select(command => command.Effect).SequenceEqual([MenuEffect.TryReturnToPreviousMode])
                && !resumeMenu.Confirmation,
                "a physical gamepad confirm dispatches Continue under the mobile policy, even from a stale hidden Quit selection");
        }

        foreach (var size in new[] { (2560, 1440), (1920, 1080), (1024, 768), (3440, 1440) })
        {
            var viewport = Viewport.Fit(size.Item1, size.Item2, MainMenuLayout.Width, MainMenuLayout.Height);
            foreach (int item in expectedOrder)
            {
                var box = MainMenuLayout.Button(item);
                int x = viewport.X + (box.Left + box.RightExclusive) * viewport.Width / (MainMenuLayout.Width * 2);
                int y = viewport.Y + (box.Top + box.BottomExclusive) * viewport.Height / (MainMenuLayout.Height * 2);
                Check(viewport.TryMapInput(x, y, out int logicalX, out int logicalY), "button maps through letterboxing");
                Check(MainMenuLayout.HitTest(logicalX * 4, logicalY * 3) == item, "pointer targets displayed row at each aspect ratio");
            }
            Check(!viewport.TryMapInput(-1, 0, out _, out _), "outside viewport rejected");
        }
        Check(MainMenuLayout.HitTest(1200, 500) == -1 && MainMenuLayout.HitTest(500, 520) == -1, "background and row gap are not buttons");
        Check(MainMenuLayout.HitTestConfirmation(1100, 740) == 1 && MainMenuLayout.HitTestConfirmation(1450, 740) == 2
            && MainMenuLayout.HitTestConfirmation(1280, 740) == -1, "confirmation buttons share render and pointer geometry");
        Check(MainMenuLayout.Navigate(1, 1, true) == 5 && MainMenuLayout.Navigate(5, -1, true) == 1, "disabled save skipped both ways");

        ushort[] full = new ushort[640 * 480], clipped = new ushort[640 * 480];
        MainMenuArtwork.DrawLabels(0, full, 640, 480, 640, null);
        MainMenuArtwork.DrawLabels(0, clipped, 640, 480, 640, new(140, 165, 220, 300));
        for (int i = 0; i < full.Length; i++)
        {
            bool inside = i % 640 >= 140 && i % 640 < 220 && i / 640 >= 165 && i / 640 < 300;
            Check(clipped[i] == (inside ? full[i] : 0), "clipping preserves label placement and confines highlight");
        }

        var view = new MainMenuView(Path.Combine(AppContext.BaseDirectory, "assets"));
        var initial = view.Render(0, true, false, true, false).ToArray();
        var selected = view.Render(5, true, false, true, false);
        Check(!initial.AsSpan().SequenceEqual(selected), "selection visibly changes");
        Check(initial.AsSpan(100 * MainMenuLayout.Width, 200 * MainMenuLayout.Width).SequenceEqual(selected.Slice(100 * MainMenuLayout.Width, 200 * MainMenuLayout.Width)), "selection leaves title artwork intact");
        Check(!initial.AsSpan().SequenceEqual(view.Render(0, true, true, true, false)), "confirmation overlays menu");
        Check(initial.AsSpan().SequenceEqual(view.Render(0, true, false, true, false)), "cancel restores menu without stale overlay");
        VerifyProfileHintCompositing(Path.Combine(AppContext.BaseDirectory, "assets"));
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            view.Render(0, true, false, true, false);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, $"unchanged presentation reuses frame without allocation (allocated {allocated} bytes)");
        VerifyMobileMainMenu(Path.Combine(AppContext.BaseDirectory, "assets"));
        return "Vertical menu: keyboard/XInput routing, mobile continue policy, hidden Quit hit/navigation, RU/BE captures, Windows baseline and cached full-resolution rendering passed.";
    }

    private static void VerifyMobileMainMenu(string assetRoot)
    {
        string localizationRoot = Path.Combine(assetRoot, "localization");
        string output = Path.GetFullPath(Path.Combine(assetRoot, "..", "audit", "mobile-main-menu"));
        Directory.CreateDirectory(output);
        LocalizationCatalog previous = GameText.Current;
        try
        {
            var mobileStart = MainMenuPresentationPolicy.Mobile(false);
            var mobileContinue = MainMenuPresentationPolicy.Mobile(true);
            foreach (string language in new[] { LocalizationCatalog.BaseLanguage, LocalizationCatalog.BelarusianLanguage })
            {
                GameText.Use(new LocalizationCatalog(localizationRoot, language));
                var mobileView = new MainMenuView(assetRoot, mobile: true);
                uint[] initial = mobileView.Render(0, false, false, true, false, "", mobileStart).ToArray();
                BitmapTestWriter.Save(Path.Combine(output, $"mobile-{language}-new-game.bmp"), initial,
                    MainMenuLayout.Width, MainMenuLayout.Height);
                uint[] continuing = mobileView.Render(0, false, false, true, false, "", mobileContinue).ToArray();
                BitmapTestWriter.Save(Path.Combine(output, $"mobile-{language}-continue.bmp"), continuing,
                    MainMenuLayout.Width, MainMenuLayout.Height);
                Check(!initial.AsSpan().SequenceEqual(continuing), $"{language} policy change redraws New Game as Continue");
                Check(mobileView.Render(0, false, false, true, false, "", mobileStart).SequenceEqual(initial),
                    $"{language} policy flip back restores the cached initial mobile menu");

                foreach (int item in MainMenuLayout.OrderFor(mobileStart))
                    Check(MainMenuLayout.Button(item, mobileStart) == MainMenuLayout.Button(item),
                        $"{language} mobile row {item} retains its approved artwork position");

                RasterClip hiddenQuit = MainMenuLayout.Button(MainMenuLayout.QuitItem);
                Check(MainMenuLayout.HitTest((hiddenQuit.Left + hiddenQuit.RightExclusive) / 2,
                    (hiddenQuit.Top + hiddenQuit.BottomExclusive) / 2, mobileStart) == -1,
                    $"{language} mobile hit testing ignores the removed Quit plaque");
                var hiddenPointer = new MainMenuController();
                hiddenPointer.PointerSelect(MainMenuLayout.QuitItem, false, mobileStart);
                Check(hiddenPointer.Selection == MainMenuLayout.NewItem,
                    $"{language} pointer cannot select hidden Quit");
            }

            GameText.Use(new LocalizationCatalog(localizationRoot, LocalizationCatalog.BaseLanguage));
            var windowsView = new MainMenuView(assetRoot);
            uint[] windows = windowsView.Render(0, false, false, true, false).ToArray();
            BitmapTestWriter.Save(Path.Combine(output, "windows-baseline.bmp"), windows,
                MainMenuLayout.Width, MainMenuLayout.Height);
            RasterClip quitButton = MainMenuLayout.Button(MainMenuLayout.QuitItem);
            Check(MainMenuLayout.HitTest((quitButton.Left + quitButton.RightExclusive) / 2,
                (quitButton.Top + quitButton.BottomExclusive) / 2) == MainMenuLayout.QuitItem,
                "Windows baseline retains the Quit pointer target");
        }
        finally
        {
            GameText.Use(previous);
        }
    }

    private static void VerifyProfileHintCompositing(string assetRoot)
    {
        const string hint = "Profile F2";
        var view = new MainMenuView(assetRoot);
        var confirmation = view.Render(0, true, true, true, false, hint).ToArray();
        Check(confirmation.AsSpan().SequenceEqual(RenderPriorComposition(assetRoot, 0, true, true, true, false, hint)),
            "confirmation frame preserves the original dimmed profile strip composition");
        var quitting = view.Render(0, true, false, true, true, hint).ToArray();
        Check(quitting.AsSpan().SequenceEqual(RenderPriorComposition(assetRoot, 0, true, false, true, true, hint)),
            "quit frame preserves the original dimmed profile strip composition");
    }

    private static uint[] RenderPriorComposition(string assetRoot, int selection, bool saveDisabled, bool confirmation, bool yes, bool quit, string profile)
    {
        const int width = MainMenuLayout.Width, height = MainMenuLayout.Height;
        ushort[] pixels = MainMenuArtwork.LoadNative(assetRoot);
        MainMenuArtwork.DrawLabels(selection, pixels, width, height, width, null, saveDisabled);
        if (profile.Length > 0)
            MainMenuArtwork.DrawCentered(profile, pixels, width, height, width, new(320, 1280, 2240, 1360), 32, 0xd69a);
        if (confirmation || quit)
        {
            var panel = new RasterClip(680, 510, 1880, 840);
            UiRaster.TintHalf(pixels, width, height, width, 0, 0, width, height, 0);
            for (int y = panel.Top; y < panel.BottomExclusive; y++)
                pixels.AsSpan(y * width + panel.Left, panel.RightExclusive - panel.Left).Fill(0x18a2);
            MainMenuArtwork.Border(pixels, width, height, width, panel, 0xbccc, 3);
            MainMenuArtwork.DrawCentered(GameText.Get(quit ? "messages.306" : "messages.268"), pixels, width, height, width,
                new(720, 555, 1840, 660), 46, 0xe653);
            MainMenuArtwork.DrawCentered(GameText.Get(quit ? "messages.307" : "messages.269"), pixels, width, height, width,
                MainMenuLayout.ConfirmationYes, 48, yes ? (ushort)0xff79 : (ushort)0xbccc);
            MainMenuArtwork.DrawCentered(GameText.Get(quit ? "messages.308" : "messages.270"), pixels, width, height, width,
                MainMenuLayout.ConfirmationNo, 48, yes ? (ushort)0xbccc : (ushort)0xff79);
        }

        uint[] result = new uint[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
            result[i] = SpriteRaster.ToBgra32(pixels[i]);
        return result;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Vertical menu: " + message);
    }
}
