using Atria.Render.Codecs;

namespace Atria.Game.Combat;

/// <summary>438720/438fd0: black, quarter-height ground projection of actual sprite packets.</summary>
public static class CombatShadowRaster
{
    public static void Draw(SpriteFrame sprite, Span<ushort> target, int width, int height, int stride, int x, int shadowY, bool mirrored, RasterClip clip)
    {
        if (width < 0 || height < 0 || stride < width || (long)stride * height > target.Length || clip.Left < 0 || clip.Top < 0
|| clip.RightExclusive > width || clip.BottomExclusive > height || clip.Left > clip.RightExclusive || clip.Top > clip.BottomExclusive)
        {
            throw new ArgumentException("Invalid combat shadow raster bounds.");
        }

        int quarter = sprite.Height >> 2;
        foreach (SpriteRun run in sprite.Runs)
        {
            if (run.Alpha.Length != 0)
            {
                throw new NotSupportedException("Native ground shadows do not consume alpha-pair sprite packets.");
            }

            if ((run.Y & 3) != 0)
            {
                continue;
            }

            long row = (long)shadowY + quarter * 3 + run.Y / 4;
            if (row < clip.Top || row >= clip.BottomExclusive)
            {
                continue;
            }

            long origin = (long)x - quarter + run.Y / 4;
            long start = origin + run.X;
            // This branch drops a packet spanning both horizontal clip edges in438720.
            if (!mirrored && start < clip.Left && start + run.Colors.Length > clip.RightExclusive)
            {
                continue;
            }

            for (int i = 0; i < run.Colors.Length; i++)
            {
                long column = mirrored ? origin + sprite.Width - run.X - i : start + i;
                if (column >= clip.Left && column < clip.RightExclusive)
                {
                    target[(int)row * stride + (int)column] = 0;
                }
            }
        }
    }
}
