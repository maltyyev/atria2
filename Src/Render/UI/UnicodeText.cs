using System.Globalization;
using Atria.Render.Codecs;

namespace Atria.Render.UI;

public static class UnicodeText
{
    private const int MaximumCachedRuns = 512;
    private const int MaximumCacheBytes = 16 * 1024 * 1024;
    private static readonly object sync = new();
    private static readonly Dictionary<(string Text, float Size, string Family, bool Bold, int Wrap), TextMask> masks = new();
    private static ITextRasterizer? rasterizer;

    /// <summary>Replaces the required platform text backend and drops masks made by its predecessor.</summary>
    public static void UseRasterizer(ITextRasterizer replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        lock (sync)
        {
            if (ReferenceEquals(rasterizer, replacement))
            {
                return;
            }
            var previous = rasterizer;
            rasterizer = replacement;
            masks.Clear();
            cacheBytes = 0;
            previous?.Dispose();
        }
    }

    /// <summary>Clears cached masks and disposes the backend when its owning session ends.</summary>
    public static void ReleaseRasterizer(ITextRasterizer owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (sync)
        {
            if (!ReferenceEquals(rasterizer, owner))
            {
                return;
            }

            rasterizer = null;
            masks.Clear();
            cacheBytes = 0;
            owner.Dispose();
        }
    }

    private static int cacheBytes;

    static UnicodeText() => AppDomain.CurrentDomain.ProcessExit += (_, _) => rasterizer?.Dispose();

    /// <summary>Layouts text through the host's configured rasterizer; throws when no backend is active.</summary>
    public static TextMask Layout(string text, float size, string family = "Segoe UI", bool bold = false, int wrapWidth = 4096)
    {
        lock (sync)
        {
            var key = (text, size, family, bold, wrapWidth);
            if (masks.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var backend = rasterizer ?? throw new InvalidOperationException("A text rasterizer must be configured by the host.");
            var result = backend.Render(text, size, family, bold, wrapWidth);
            if (masks.Count >= MaximumCachedRuns || cacheBytes + result.Coverage.Length > MaximumCacheBytes)
            {
                masks.Clear();
                cacheBytes = 0;
            }
            masks.Add(key, result);
            cacheBytes += result.Coverage.Length;
            return result;
        }
    }

    public static void Draw(TextMask mask, Span<ushort> target, int width, int height, int stride, int x, int y, ushort color, RasterClip? clip = null, ReadOnlySpan<ushort> palette = default, int phase = 0)
    {
        if (width < 0 || height < 0 || stride < width || (long)stride * height > target.Length)
        {
            throw new ArgumentException("Invalid text raster dimensions.");
        }
        RasterClip bounds = clip ?? new(0, 0, width, height);
        if (bounds.Left < 0 || bounds.Top < 0 || bounds.RightExclusive < bounds.Left || bounds.BottomExclusive < bounds.Top || bounds.RightExclusive > width || bounds.BottomExclusive > height)
        {
            throw new ArgumentException("Text clip is outside the destination.");
        }

        int left = x + mask.OriginX, top = y + mask.OriginY;
        for (int row = 0; row < mask.Height; row++)
        {
            int destinationY = top + row;
            if (destinationY < bounds.Top || destinationY >= bounds.BottomExclusive)
            {
                continue;
            }
            ushort foreground = palette.IsEmpty ? color : palette[(phase + Math.Max(0, row + mask.OriginY - 2) * 2) % palette.Length];
            for (int column = 0; column < mask.Width; column++)
            {
                int destinationX = left + column;
                if (destinationX < bounds.Left || destinationX >= bounds.RightExclusive)
                {
                    continue;
                }
                byte alpha = mask.Coverage[row * mask.Width + column];
                if (alpha != 0)
                {
                    ref ushort pixel = ref target[destinationY * stride + destinationX];
                    pixel = Blend(foreground, pixel, alpha);
                }
            }
        }
    }

    public static string Fit(string text, int maximumWidth, Func<string, int> measure)
    {
        if (maximumWidth <= 0)
        {
            return "";
        }
        if (measure(text) <= maximumWidth)
        {
            return text;
        }

        const string ellipsis = "…";
        if (measure(ellipsis) > maximumWidth)
        {
            return "";
        }
        int[] boundaries = StringInfo.ParseCombiningCharacters(text);
        int low = 0, high = boundaries.Length;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            int end = middle == boundaries.Length ? text.Length : boundaries[middle];
            if (measure(text[..end] + ellipsis) <= maximumWidth)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }
        return text[..(low == boundaries.Length ? text.Length : boundaries[low])] + ellipsis;
    }

    private static ushort Blend(ushort foreground, ushort background, int alpha)
    {
        int inverse = byte.MaxValue - alpha;
        int red = (((foreground >> 11) * alpha + (background >> 11) * inverse) + 127) / byte.MaxValue;
        int green = ((((foreground >> 5) & 63) * alpha + ((background >> 5) & 63) * inverse) + 127) / byte.MaxValue;
        int blue = (((foreground & 31) * alpha + (background & 31) * inverse) + 127) / byte.MaxValue;
        return (ushort)((red << 11) | (green << 5) | blue);
    }
}
