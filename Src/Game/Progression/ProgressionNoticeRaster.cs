using Atria.Render.Codecs;
using Atria.Render.UI;

namespace Atria.Game.Progression;

public static class ProgressionNoticeRaster
{
    //424d40 uses the literal "%d LevelUp", small ENG font, palettecc266 thencc228.
    // Draw the current award screen first; this is its exact notice overlay only.
    public static void Draw(LevelUpNoticeFrame frame, BitmapFont font, Span<ushort> target, int width = 640, int height = 480, int stride = 640)
    {
        if (frame.Level is < 1 or > 13)
        {
            throw new InvalidDataException("Unsupported level-up notice rank.");
        }

        if (font.Width != 8 || font.Height != 16)
        {
            throw new ArgumentException("Level notice requires ENG1 small font.");
        }

        string label = GameText.Format("ui.progression.levelUp", ("level", frame.Level));
        font.DrawText(label, target, width, height, stride, frame.ShadowX, frame.ShadowY, NativeUiPalette.Normal[31]);
        font.DrawText(label, target, width, height, stride, frame.TextX, frame.TextY, NativeUiPalette.Normal[0]);
    }
}
