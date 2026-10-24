using Atria.Render.Codecs;

namespace Atria.Game.Combat;

/// <summary>40a820 codes ±5 and ±999. Mode2 is half-all blending, not geometric scaling.</summary>
public static class CombatSpecialRaster
{
    public static void DrawHalfAll(SpriteFrame sprite, Span<ushort> target, int stride, int x, int y, bool mirrored, RasterClip clip)
    {
        if (stride <= 0 || target.Length % stride != 0)
        {
            throw new ArgumentException("Invalid combat raster stride.");
        }

        SpriteRaster.Draw565(sprite, target, stride, target.Length / stride, stride, x, y, SpriteBlend.HalfAll, mirrored, clip, preserveLegacyClipping: true);
    }

    //435190/435210 are unclipped native line fills. Bounds checking changes only unsafe destinations;
    //the original fixed cross is entirely inside the battle viewport.
    public static void DrawCrosshair(Span<ushort> target, int stride, RasterClip clip)
    {
        if (stride <= 0 || clip.Left < 0 || clip.Top < 0 || clip.RightExclusive > stride || clip.RightExclusive < clip.Left
|| clip.BottomExclusive < clip.Top || (long)clip.BottomExclusive * stride > target.Length)
        {
            throw new ArgumentException("Invalid combat raster bounds.");
        }

        for (int x = 319; x < 322; x++)
        {
            Pixel(x, 240, target, stride, clip);
        }

        for (int y = 239; y < 242; y++)
        {
            Pixel(320, y, target, stride, clip);
        }
    }

    private static void Pixel(int x, int y, Span<ushort> target, int stride, RasterClip clip)
    {
        if (x >= clip.Left && x < clip.RightExclusive && y >= clip.Top && y < clip.BottomExclusive)
        {
            target[y * stride + x] = 0xf98c; //DAT_004cc3b2, initialized display RGB565 palette
        }
    }
}
