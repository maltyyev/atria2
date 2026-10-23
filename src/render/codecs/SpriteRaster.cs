namespace Atria.Render.Codecs;

public enum SpriteBlend { Copy, HalfType3, HalfAll, ComponentAverageAll, AlphaPercent }
public readonly record struct RasterClip(int Left, int Top, int RightExclusive, int BottomExclusive);

public static class SpriteRaster
{
    // Sources: 0x4375f0, 0x437bd0, 0x4383d0, 0x438140, 0x439c50.
    // RGB565 is the unconverted asset/native display mode (0x434da0 / 0x4354a0).
    public static void Draw565(SpriteFrame frame, Span<ushort> destination, int width, int height,
        int stride, int x, int y, SpriteBlend blend = SpriteBlend.Copy, bool mirrored = false,
        RasterClip? clip = null, bool preserveLegacyClipping = false)
    {
        if (width < 0 || height < 0 || stride < width || (long)stride * height > destination.Length)
            throw new ArgumentException("Invalid destination raster.");
        RasterClip bounds = clip ?? new(0, 0, width, height);
        if (bounds.Left < 0 || bounds.Top < 0 || bounds.RightExclusive > width || bounds.BottomExclusive > height ||
            bounds.RightExclusive < bounds.Left || bounds.BottomExclusive < bounds.Top)
            throw new ArgumentException("Clip is outside destination.");
        if (mirrored && blend == SpriteBlend.AlphaPercent)
            throw new NotSupportedException("A mirrored alpha-pair raster contract has not been recovered.");
        foreach (var run in frame.Runs)
        {
            long row = (long)y + run.Y;
            if (row < bounds.Top || row >= bounds.BottomExclusive) continue;
            long start = (long)x + run.X, end = start + run.Colors.Length;
            bool paired = run.Alpha.Length != 0;
            if (paired && blend != SpriteBlend.AlphaPercent)
                throw new InvalidOperationException("Alpha-pair sprite requires its recovered alpha raster mode.");
            // Native forward clipping drops runs spanning BOTH edges. Native type3 half/alpha
            // paths also drop a run clipped on the left. Preserve explicitly for baseline comparisons.
            if (!mirrored && preserveLegacyClipping && start < bounds.Left &&
                (end > bounds.RightExclusive || (run.Opcode == 3 && blend is SpriteBlend.HalfType3 or SpriteBlend.AlphaPercent)))
                continue;
            if (!mirrored && preserveLegacyClipping && blend == SpriteBlend.AlphaPercent && run.Opcode == 0 &&
                start < bounds.Left && end > bounds.Left)
                throw new NotSupportedException("Legacy alpha raster has a misaligned scratch-buffer write when clipped on the left.");
            for (int i = 0; i < run.Colors.Length; i++)
            {
                // Native mirrored origin is x+width, not x+width-1 (0x438a50).
                long column = mirrored ? (long)x + frame.Width - run.X - i : start + i;
                if (column < bounds.Left || column >= bounds.RightExclusive) continue;
                int target = (int)row * stride + (int)column;
                ushort source = run.Colors[i], old = destination[target];
                destination[target] = blend switch
                {
                    SpriteBlend.Copy => source,
                    SpriteBlend.HalfType3 when run.Opcode == 0 => source,
                    SpriteBlend.HalfType3 or SpriteBlend.HalfAll => Half565(source, old),
                    SpriteBlend.ComponentAverageAll => AverageComponents565(source, old),
                    SpriteBlend.AlphaPercent when run.Opcode == 0 => source,
                    SpriteBlend.AlphaPercent when paired => Alpha565(source, old, run.Alpha[i]),
                    SpriteBlend.AlphaPercent => throw new InvalidOperationException("Missing per-pixel alpha."),
                    _ => throw new ArgumentOutOfRangeException(nameof(blend))
                };
            }
        }
    }

    public static ushort Half565(ushort source, ushort destination) =>
        (ushort)(((source & 0xF7DE) >> 1) + ((destination & 0xF7DE) >> 1));

    public static ushort AverageComponents565(ushort source, ushort destination) =>
        (ushort)(((((source >> 11) + (destination >> 11)) >> 1) << 11) |
                 (((((source >> 5) & 63) + ((destination >> 5) & 63)) >> 1) << 5) |
                 (((source & 31) + (destination & 31)) >> 1));

    public static ushort Alpha565(ushort source, ushort destination, int percent)
    {
        if ((uint)percent > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        int inverse = 100 - percent;
        // Two divisions per component deliberately retain native truncation.
        return (ushort)((((source >> 11) * percent / 100 + (destination >> 11) * inverse / 100) << 11) |
            ((((source >> 5) & 63) * percent / 100 + ((destination >> 5) & 63) * inverse / 100) << 5) |
            ((source & 31) * percent / 100 + (destination & 31) * inverse / 100));
    }

    // Display expansion is a new presentation boundary. Blend in 565 before expanding.
    public static uint ToBgra32(ushort pixel)
    {
        uint r = (uint)pixel >> 11, g = ((uint)pixel >> 5) & 63, b = (uint)pixel & 31;
        return 0xFF000000u | ((r << 3 | r >> 2) << 16) | ((g << 2 | g >> 4) << 8) | (b << 3 | b >> 2);
    }
}


