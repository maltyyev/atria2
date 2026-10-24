using Atria.Render.Codecs;
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
            ReadOnlySpan<byte> text = frame.Labels[i].Span;
            if (i != frame.Selection)
            {
                font.DrawText(text, target, 640, 480, 640, x, y, 0x7bef);
                continue;
            }

            // Four diagonal black copies create the native outline; preserve draw order.
            font.DrawText(text, target, 640, 480, 640, x + 1, y + 1, 0);
            font.DrawText(text, target, 640, 480, 640, x + 1, y - 1, 0);
            font.DrawText(text, target, 640, 480, 640, x - 1, y + 1, 0);
            font.DrawText(text, target, 640, 480, 640, x - 1, y - 1, 0);
            if (frame.Flash == 0)
            {
                UiRaster.DrawStyledText(font, text, NativeUiPalette.Selected, 0, target, 640, 480, 640, x, y);
            }
            else
            {
                font.DrawText(text, target, 640, 480, 640, x, y, frame.Flash == 2 ? (ushort)0xffff : (ushort)0xffe0);
            }
        }
    }
}
