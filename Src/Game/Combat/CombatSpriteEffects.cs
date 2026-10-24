using Atria.Render.Codecs;

namespace Atria.Game.Combat;

/// <summary>4397c0/439a40 tint,438140/438c90 fade,437880/437aa0 packet-color effects in RGB565.</summary>
public static class CombatSpriteEffects
{
    public static void DrawTint(SpriteFrame sprite, Span<ushort> target, int stride, int x, int y, ushort tint, bool mirrored, RasterClip clip) => Draw(sprite, target, stride, x, y, mirrored, clip, 0, tint);
    public static void DrawFade(SpriteFrame sprite, Span<ushort> target, int stride, int x, int y, int divisor, bool mirrored, RasterClip clip)
    {
        if (divisor < 1)
        {
            throw new InvalidDataException("Native combat fade divisor is not positive.");
        }

        Draw(sprite, target, stride, x, y, mirrored, clip, divisor, 0);
    }

    public static void DrawPacketColor(SpriteFrame sprite, Span<ushort> target, int stride, int x, int y, ushort color, bool mirrored, RasterClip clip) => Draw(sprite, target, stride, x, y, mirrored, clip, -1, color);
    public static void DrawAlpha(SpriteFrame sprite, Span<ushort> target, int stride, int x, int y, bool mirrored, RasterClip clip)
    {
        Draw(sprite, target, stride, x, y, mirrored, clip, -2, 0);
    }

    private static void Draw(SpriteFrame sprite, Span<ushort> target, int stride, int x, int y, bool mirrored, RasterClip clip, int mode, ushort tint)
    {
        if (stride <= 0 || clip.Left < 0 || clip.Top < 0 || clip.RightExclusive > stride || clip.BottomExclusive < clip.Top
|| clip.RightExclusive < clip.Left || (long)clip.BottomExclusive * stride > target.Length)
        {
            throw new ArgumentException("Invalid combat raster bounds.");
        }

        foreach (SpriteRun run in sprite.Runs)
        {
            if (run.Alpha.Length != 0 && mode != -2)
            {
                throw new NotSupportedException("Combat effect requires single-color sprite packets.");
            }

            long row = (long)y + run.Y;
            if (row < clip.Top || row >= clip.BottomExclusive)
            {
                continue;
            }

            for (int i = 0; i < run.Colors.Length; i++)
            {
                //43ace0 uses width-1; other mirrored sprite effects use width.
                long column = mirrored ? (long)x + sprite.Width - (mode == -2 ? 1 : 0) - run.X - i : (long)x + run.X + i;
                if (column < clip.Left || column >= clip.RightExclusive)
                {
                    continue;
                }

                int at = (int)row * stride + (int)column;
                ushort source = run.Colors[i], old = target[at];
                if (mode == 0)
                {
                    target[at] = SpriteRaster.AverageComponents565(source, tint);
                }
                else if (mode == -1)
                {
                    target[at] = run.Opcode == 3 ? tint : source;
                }
                else if (mode == -2)
                {
                    if (run.Opcode == 0)
                    {
                        target[at] = source;
                    }
                    else if (run.Alpha.Length != 0)
                    {
                        target[at] = SpriteRaster.Alpha565(source, old, run.Alpha[i]);
                    }
                    else
                    {
                        throw new InvalidDataException("Combat alpha sprite packet has no actual alpha data.");
                    }
                }
                else
                {
                    target[at] = (ushort)(((((source >> 11) / mode + (old >> 11)) >> 1) << 11) | (((((source >> 5) & 63) / mode + ((old >> 5) & 63)) >> 1) << 5) | (((source & 31) / mode + (old & 31)) >> 1));
                }
            }
        }
    }
}
