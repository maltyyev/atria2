using Atria.Render.Codecs;
using Atria.Resources;
using Atria.Render.UI;

namespace Atria.Game.ActionMenu;

public sealed class ActionMenuRenderer
{
    private readonly SpriteFile tiles;
    private readonly BitmapFont font;
    public ActionMenuRenderer(SpriteFile etc, BitmapFont activeFont)
    {
        tiles = etc;
        font = activeFont;
    }

    //0x428d70 +428b50. Draw over a freshly recomposed town/interior frame, not over the previous shaded result.
    public void Draw(ActionMenuFrame frame, Span<ushort> target)
    {
        if (target.Length != 640 * 480 || (uint)frame.Selection >= (uint)frame.Labels.Length)
        {
            throw new ArgumentException("Invalid action menu frame.");
        }

        UiRaster.TintHalf(
            target,
            640,
            480,
            640,
            checked(frame.X + 14),
            checked(frame.Y + 14),
            checked(frame.HorizontalTiles * 32 + 36),
            checked(frame.VerticalTiles * 32 + 36),
            0
        );
        UiRaster.DrawPanel(tiles, frame.X, frame.Y, frame.HorizontalTiles, frame.VerticalTiles, target, arrows: true);
        int x = checked(frame.X + 45);
        // Native spaces text according to ROW COUNT parity, independent of font or tile count.
        int startY = checked(frame.Y + ((frame.Labels.Length & 1) == 0 ? 52 : 38));
        for (int i = 0; i < frame.Labels.Length; i++)
        {
            int y = checked(startY + i * 32);
            string text = GameText.Decode(frame.Labels[i].Span).Trim();
            int textWidth = (frame.HorizontalTiles + 2) * 32 - 67;
            if (i != frame.Selection)
            {
                TextBoxRaster.Draw(font, text, target, 640, 480, 640, new(x, y, x + textWidth, y + 32), 0x7bef, wrap: true);
                continue;
            }

            // Four diagonal black copies create the native outline; preserve draw order.
            TextBoxRaster.Draw(font, text, target, 640, 480, 640, new(x + 1, y + 1, x + 1 + textWidth, y + 1 + 32), 0, wrap: true);
            TextBoxRaster.Draw(font, text, target, 640, 480, 640, new(x + 1, y - 1, x + 1 + textWidth, y - 1 + 32), 0, wrap: true);
            TextBoxRaster.Draw(font, text, target, 640, 480, 640, new(x - 1, y + 1, x - 1 + textWidth, y + 1 + 32), 0, wrap: true);
            TextBoxRaster.Draw(font, text, target, 640, 480, 640, new(x - 1, y - 1, x - 1 + textWidth, y - 1 + 32), 0, wrap: true);
            if (frame.Flash == 0)
            {
                TextBoxRaster.Draw(font, text, target, 640, 480, 640, new(x, y, x + textWidth, y + 32), 0xffff, NativeUiPalette.Selected, wrap: true);
            }
            else
            {
                TextBoxRaster.Draw(font, text, target, 640, 480, 640, new(x, y, x + textWidth, y + 32), frame.Flash == 2 ? (ushort)0xffff : (ushort)0xffe0, wrap: true);
            }
        }
    }
}
