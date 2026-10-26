using Atria.Render.Codecs;

namespace Atria.Render.UI;

/// <summary>Wraps UI text using the selected game font without substituting or resizing it.</summary>
public static class TextBoxRaster
{
    public static string[] Layout(string text, BitmapFont font, int width, int height, bool wrap = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int overhang = Math.Max(0, font.Width - font.Advance);
        return wrap
            ? TextFlow.Wrap(text, Math.Max(1, width - overhang), font.MeasureText)
            : text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
    }

    public static void Draw(BitmapFont font, string text, Span<ushort> target, int width, int height, int stride,
        RasterClip box, ushort color, ReadOnlySpan<ushort> palette = default, int phase = 0, bool wrap = false, bool centered = false)
    {
        int availableWidth = box.RightExclusive - box.Left, availableHeight = box.BottomExclusive - box.Top;
        var lines = Layout(text, font, availableWidth, availableHeight, wrap);
        for (int line = 0; line < lines.Length; line++)
        {
            int measuredWidth = font.MeasureText(lines[line]) + Math.Max(0, font.Width - font.Advance);
            int x = box.Left + (centered ? Math.Max(0, (availableWidth - measuredWidth) / 2) : 0);
            int y = box.Top + line * font.Height;
            if (palette.IsEmpty)
            {
                font.DrawText(lines[line], target, width, height, stride, x, y, color, box);
            }
            else
            {
                UiRaster.DrawStyledText(font, lines[line], palette, phase, target, width, height, stride, x, y, box);
            }
        }
    }
}
