using Atria.Platform.Input;
using Atria.Game.Controls;
using Atria.Game.Menu;
using Atria.Platform.Win32;
using Atria.Render;
using Atria.Render.Scenes;
using Atria.Resources;

namespace Atria.Tests.Menu;

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
            var navigation = new MenuNavigation(bindings, scan => input.LegacyMenu(scan, true));
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
            menu.Input(new(Accept: input.LegacyMenu(bindings.Confirm, true)), false, commands.Add);
            Check(commands.Any(c => c.Effect == MenuEffect.ChangeMode && c.Value == 250), "Enter/A activates pointed settings immediately");
            Array.Clear(pressed);
            pad.Sample = pad.Sample with
            {
                Buttons = 0
            };
            input.Poll(keys, pressed, mouse, true, 102);
            input.Poll(keys, pressed, mouse, true, 103, [true, false, false]);
            input.DiscardPointerConfirmation();
            Check(!input.Menu(GameAction.Confirm), "click outside a button cannot activate the previously selected row");
            pressed[0x1c] = true;
            input.Poll(keys, pressed, mouse, true, 104);
            input.DiscardPointerConfirmation();
            Check(input.Menu(GameAction.Confirm), "pointer rejection preserves keyboard confirmation");
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
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            view.Render(0, true, false, true, false);
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "unchanged presentation reuses frame without allocation");
        return "Vertical menu: keyboard/XInput routing, immediate activation, six targets at four aspect ratios, save skip, confirmation restore and cached full-resolution rendering passed.";
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Vertical menu: " + message);
    }
}
