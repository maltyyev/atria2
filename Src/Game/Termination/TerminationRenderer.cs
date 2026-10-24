using Atria.Render.Codecs;
using Atria.Render.UI;

namespace Atria.Game.Termination;

public static class TerminationRenderer
{
    public static void DrawQuit(QuitController controller, SpriteFile etc, BitmapFont largeFont, IReadOnlyList<ReadOnlyMemory<byte>> messages, Span<ushort> target)
    {
        UiRaster.TintHalf(target, 640, 480, 640, 206, 174, 228, 100, 0x000f);
        UiRaster.DrawPanel(etc, 192, 160, 6, 2, target);
        Text(largeFont, messages[306].Span, 252, 200, NativeUiPalette.Title, target);
        Text(largeFont, messages[307].Span, 260, 236, controller.Selection == 1 ? NativeUiPalette.Selected : NativeUiPalette.Normal, target);
        Text(largeFont, messages[308].Span, 360, 236, controller.Selection == 2 ? NativeUiPalette.Selected : NativeUiPalette.Normal, target);
    }

    private static void Text(BitmapFont font, ReadOnlySpan<byte> text, int x, int y, ReadOnlySpan<ushort> palette, Span<ushort> target)
    {
        font.DrawText(text, target, 640, 480, 640, x + 1, y + 1, 0);
        UiRaster.DrawStyledText(font, text, palette, 0, target, 640, 480, 640, x, y);
    }

    public static void DrawEnding(EndingText text, int textY, BitmapFont font, Span<ushort> target)
    {
        var clip = new RasterClip(0, 150, 640, 351);
        for (int row = 0; row < text.Flow.Lines.Count; row++)
        {
            int y = textY + row * 24;
            if (y < 130)
            {
                continue;
            }

            if (y > 350)
            {
                break;
            }

            font.DrawText(text.Flow.Lines[row], target, 640, 480, 640, 101, y + 1, 0, clip);
            font.DrawText(text.Flow.Lines[row], target, 640, 480, 640, 100, y, 0xffff, clip);
        }
    }
}
