using Atria.Render.Codecs;

namespace Atria.Game.Credits;

public static class CreditsRenderer
{
    public static void Draw(CreditsFrame frame, CreditsText text, SpriteFile sprites, BitmapFont smallFont, ReadOnlySpan<ushort> background, Span<ushort> target)
    {
        if (background.Length != 640 * 480 || target.Length < 640 * 480 || sprites.Frames.Length < 13 || smallFont.Height != 16)
        {
            throw new InvalidDataException("Invalid credits assets.");
        }

        background.CopyTo(target);
        foreach (var picture in frame.Pictures)
        {
            SpriteRaster.Draw565(
                sprites.Frames[picture.Frame],
                target,
                640,
                480,
                640,
                picture.X,
                picture.Y,
                picture.Half ? SpriteBlend.HalfAll : SpriteBlend.Copy,
                preserveLegacyClipping: true
            );
        }

        var clip = new RasterClip(0, 80, 640, 381); //425120(80,380), inclusive lower/upper font raster limits.
        for (int row = 0; row < text.Flow.Lines.Count; row++)
        {
            int y = frame.TextY + row * 24;
            if (y < 60)
            {
                continue;
            }

            if (y > 380)
            {
                break;
            }

            smallFont.DrawText(text.Flow.Lines[row], target, 640, 480, 640, 341, y + 1, 0, clip);
            smallFont.DrawText(text.Flow.Lines[row], target, 640, 480, 640, 340, y, 0xd69a, clip);
        }
    }
}
