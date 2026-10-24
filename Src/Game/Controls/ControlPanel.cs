using Atria.Render.Codecs;
using Atria.Render.UI;

namespace Atria.Game.Controls;

public sealed record ControlRow(string Label, string Value, Action? Activate = null, Action<int>? Adjust = null, int Icon = -1);
/// <summary>Original panel tiles with measured, Unicode-capable text rows.</summary>
public sealed class ControlPanel(SpriteFile tiles, BitmapFont font)
{
    public int Selection
    {
        get; set;
    }
    public int Scroll
    {
        get; private set;
    }

    public const int RowsPerPage = 12;
    private int lastX = -1, lastY = -1;
    public void Reset()
    {
        Selection = Scroll = 0;
        lastX = lastY = -1;
    }

    public bool Input(InputRouter input, IReadOnlyList<ControlRow> rows, bool back = true, int player = -1)
    {
        bool Menu(GameAction action, bool repeat = false) => player < 0 ? input.Menu(action, repeat) : input.PlayerMenu(player, action, repeat);
        void Clear()
        {
            if (player < 0)
            {
                input.Clear();
            }
            else
            {
                input.ClearPlayer(player);
            }
        }

        if (rows.Count == 0)
        {
            return back && input.Menu(GameAction.Back);
        }

        Selection = Math.Clamp(Selection, 0, rows.Count - 1);
        if (Menu(GameAction.MenuUp, true))
        {
            Selection = Math.Max(0, Selection - 1);
        }

        if (Menu(GameAction.MenuDown, true))
        {
            Selection = Math.Min(rows.Count - 1, Selection + 1);
        }

        if (input.MouseX != lastX || input.MouseY != lastY)
        {
            if (input.MouseX is >= 40 and < 600 && input.MouseY is >= 78 and < 342)
            {
                int selected = Scroll + (input.MouseY - 78) / 22;
                if (selected < rows.Count)
                {
                    Selection = selected;
                }
            }

            lastX = input.MouseX;
            lastY = input.MouseY;
        }

        if (player < 0 || input.Profile.Players[player].Keyboard)
        {
            Selection = Math.Clamp(Selection - input.TakeWheel() * 3, 0, rows.Count - 1);
        }

        if (Selection < Scroll)
        {
            Scroll = Selection;
        }

        if (Selection >= Scroll + RowsPerPage)
        {
            Scroll = Selection - RowsPerPage + 1;
        }

        if (Menu(GameAction.MenuLeft, true))
        {
            rows[Selection].Adjust?.Invoke(-1);
        }
        else if (Menu(GameAction.MenuRight, true))
        {
            rows[Selection].Adjust?.Invoke(1);
        }

        bool click = (player < 0 || input.Profile.Players[player].Keyboard) && input.Take(256);
        if (click && input.MouseY is >= 400 and < 424 && input.MouseX is >= 520 and < 600)
        {
            rows[Selection].Adjust?.Invoke(input.MouseX < 560 ? -1 : 1);
            return false;
        }

        if (click && input.MouseX is >= 40 and < 600 && input.MouseY is >= 78 and < 342)
        {
            if (Scroll + (input.MouseY - 78) / 22 >= rows.Count)
            {
                return false;
            }

            var row = rows[Selection];
            if (row.Activate is not null)
            {
                row.Activate();
            }
            else
            {
                row.Adjust?.Invoke(input.MouseX < 490 ? -1 : 1);
            }

            Clear();
        }
        else if (Menu(GameAction.Confirm))
        {
            if (rows[Selection].Activate is { } activate)
            {
                activate();
            }
            else
            {
                rows[Selection].Adjust?.Invoke(1);
            }

            Clear();
        }

        return back && Menu(GameAction.Back);
    }

    public void Draw(Span<ushort> pixels, string title, IReadOnlyList<ControlRow> rows, string footer, string notice = "", SpriteFile? icons = null)
    {
        for (int shade = 0; shade < 3; shade++)
        {
            UiRaster.TintHalf(pixels, 640, 480, 640, 24, 24, 592, 428, 0);
        }

        UiRaster.DrawPanel(tiles, 16, 16, 17, 12, pixels, arrows: false);
        Text(pixels, title, 46, 42, 0xffe0, 68);
        Selection = Math.Clamp(Selection, 0, Math.Max(0, rows.Count - 1));
        Scroll = Math.Clamp(Scroll, 0, Math.Max(0, rows.Count - 1));
        for (int r = 0; r < RowsPerPage && Scroll + r < rows.Count; r++)
        {
            int index = Scroll + r, y = 80 + r * 22;
            var row = rows[index];
            ushort color = index == Selection ? (ushort)0xffff : (ushort)0xbdf7;
            if (index == Selection)
            {
                for (int yy = y - 2; yy < y + 18; yy++)
                {
                    pixels.Slice(yy * 640 + 38, 564).Fill(0x2104);
                }

                Text(pixels, ">", 42, y, 0xffe0);
            }

            int textX = 58;
            // Original command icons are taller than a list row. Show the selected icon in the footer.
            Text(pixels, row.Label, textX, y, color, row.Value.Length == 0 ? (600 - textX) / 8 : 39);
            if (row.Value.Length > 0)
            {
                Text(pixels, row.Value, 392, y, 0xffe0, 25);
            }
        }

        Text(pixels, GameText.Format("ui.controls.rowSelection", ("selected", Selection + 1), ("count", rows.Count)), 48, 352, 0x9cf3, 68);
        if (icons is not null && rows.Count > 0 && rows[Selection].Icon is int icon && icon >= 0 && icon < icons.Frames.Length)
        {
            SpriteRaster.Draw565(icons.Frames[icon], pixels, 640, 480, 640, 568, 346);
        }

        Text(pixels, notice, 48, 378, 0xffe0, 68);
        Text(pixels, footer, 48, 402, 0xbdf7, 56);
        if (rows.Count > 0 && rows[Selection].Adjust is not null)
        {
            for (int y = 400; y < 423; y++)
            {
                pixels.Slice(y * 640 + 520, 34).Fill(0x2104);
                pixels.Slice(y * 640 + 560, 34).Fill(0x2104);
            }

            Text(pixels, "<", 532, 402, 0xffe0);
            Text(pixels, ">", 572, 402, 0xffe0);
        }

        Text(pixels, GameText.Get("ui.controls.acceptBack"), 48, 426, 0xbdf7, 68);
    }

    public static void Text(BitmapFont font, Span<ushort> pixels, string text, int x, int y, ushort color, int max = 72)
    {
        text = UnicodeText.Fit(text, Math.Min(max * font.Advance, 640 - x), font.MeasureText);
        font.DrawText(text, pixels, 640, 480, 640, x + 1, y + 1, 0);
        font.DrawText(text, pixels, 640, 480, 640, x, y, color);
    }

    public void Text(Span<ushort> pixels, string text, int x, int y, ushort color, int max = 72) => Text(font, pixels, text, x, y, color, max);
}
