using Atria.Platform.Win32;
using Atria.Render.Codecs;
using Atria.Render.UI;
using Atria.Resources;

namespace Atria.Render.Scenes;

public static class MainMenuArtwork
{
    private const int Width = 640, Height = 480;
    private const int MaximumCachedLabels = 64;
    private const string LabelFont = "Bahnschrift", TitleFont = "Palatino Linotype";
    private static readonly string[] keys = ["ui.mainMenu.new", "ui.mainMenu.load", "ui.mainMenu.save", "ui.mainMenu.quit", "ui.mainMenu.credits", "ui.mainMenu.settings"];
    private static readonly (int X, int Y, int Angle, int Width, int Height)[] anchors =
    [
        (181, 233, 0, 60, 15), (248, 315, -48, 58, 15), (394, 315, 48, 58, 15),
        (465, 233, 0, 62, 15), (386, 167, -48, 32, 12), (250, 168, 48, 34, 12)
    ];
    private static readonly Dictionary<(string Text, int Item), TextMask> labels = new();

    public static ushort[] Load(string assetRoot)
    {
        string path = Path.Combine(assetRoot, "interface", "ru-RU", "MainMenu.bmp");
        var image = BmpImage.Decode(File.ReadAllBytes(path));
        if ((long)image.Width * Height != (long)image.Height * Width)
        {
            throw new InvalidDataException("Main menu artwork must have a 4:3 aspect ratio.");
        }

        ushort[] source = image.ToRgb565(), result = new ushort[Width * Height];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                result[y * Width + x] = source[(y * image.Height / Height) * image.Width + x * image.Width / Width];
            }
        }
        return result;
    }

    public static void DrawLabels(int selected, Span<ushort> target, int width, int height, int stride, RasterClip? clip)
    {
        for (int item = 0; item < keys.Length; item++)
        {
            var mask = Label(item);
            var anchor = anchors[item];
            int x = anchor.X - mask.Width / 2 + DirectWriteRasterizer.MaskPadding;
            int y = anchor.Y - mask.Height / 2 + DirectWriteRasterizer.MaskPadding;
            DrawEmbossed(mask, target, width, height, stride, x, y, item == selected, clip);
        }
    }

    public static void DrawTitle(int item, int reveal, Span<ushort> target, int width, int height, int stride, RasterClip? clip)
    {
        const int revealSteps = 8;
        string text = Caption(item);
        TextMask mask;
        lock (labels)
        {
            if (!labels.TryGetValue((text, -1), out mask!))
            {
                mask = Fit(text, TitleFont, 40, 168, 42);
                if (labels.Count >= MaximumCachedLabels) labels.Clear();
                labels[(text, -1)] = mask;
            }
        }

        RasterClip bounds = clip ?? new(0, 0, width, height);
        bounds = new(bounds.Left, bounds.Top, Math.Min(bounds.RightExclusive, 232 + 176 * (reveal + 1) / revealSteps), bounds.BottomExclusive);
        if (bounds.RightExclusive <= bounds.Left)
        {
            return;
        }
        int x = 320 - mask.Advance / 2;
        int y = 224 - (mask.Height - DirectWriteRasterizer.MaskPadding * 2) / 2;
        DrawEmbossed(mask, target, width, height, stride, x, y, true, bounds);
    }

    private static string Caption(int item) => GameText.Get(keys[item]);

    private static TextMask Label(int item)
    {
        string text = Caption(item);
        lock (labels)
        {
            if (labels.TryGetValue((text, item), out var cached))
            {
                return cached;
            }

            var anchor = anchors[item];
            var mask = Fit(text, LabelFont, 18, anchor.Width, anchor.Height);
            var result = Rotate(mask, anchors[item].Angle);
            if (labels.Count >= MaximumCachedLabels)
            {
                labels.Clear();
            }
            labels.Add((Caption(item), item), result);
            return result;
        }
    }

    private static void DrawEmbossed(TextMask mask, Span<ushort> target, int width, int height, int stride,
        int x, int y, bool selected, RasterClip? clip)
    {
        const ushort bronze = 0x9387, litGold = 0xfed0, bronzeEdge = 0xbccc, goldEdge = 0xff79, recess = 0x2080;
        UnicodeText.Draw(mask, target, width, height, stride, x + 1, y + 2, 0, clip);
        UnicodeText.Draw(mask, target, width, height, stride, x + 1, y + 1, recess, clip);
        UnicodeText.Draw(mask, target, width, height, stride, x - 1, y - 1, selected ? goldEdge : bronzeEdge, clip);
        UnicodeText.Draw(mask, target, width, height, stride, x, y, selected ? litGold : bronze, clip);
    }

    private static TextMask Fit(string text, string family, float size, int maximumWidth, int maximumHeight)
    {
        var mask = Tighten(UnicodeText.Layout(text, size, family, bold: true));
        while ((mask.Advance > maximumWidth || mask.Height - DirectWriteRasterizer.MaskPadding * 2 > maximumHeight) && size > 8)
        {
            mask = Tighten(UnicodeText.Layout(text, --size, family, bold: true));
        }
        if (mask.Advance > maximumWidth)
        {
            text = UnicodeText.Fit(text, maximumWidth, value => Tighten(UnicodeText.Layout(value, size, family, bold: true)).Advance);
            mask = Tighten(UnicodeText.Layout(text, size, family, bold: true));
        }
        return mask;
    }

    // Font line boxes include unused ascender/descender space; center the visible ink on each plaque.
    private static TextMask Tighten(TextMask source)
    {
        int left = source.Width, top = source.Height, right = -1, bottom = -1;
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                if (source.Coverage[y * source.Width + x] == 0) continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }
        if (right < left) return source;
        int padding = DirectWriteRasterizer.MaskPadding;
        int inkWidth = right - left + 1, inkHeight = bottom - top + 1;
        int width = inkWidth + padding * 2, height = inkHeight + padding * 2;
        byte[] coverage = new byte[width * height];
        for (int y = 0; y < inkHeight; y++)
        {
            source.Coverage.AsSpan((top + y) * source.Width + left, inkWidth).CopyTo(coverage.AsSpan((y + padding) * width + padding));
        }
        return new(width, height, inkWidth, coverage, source.MissingGlyphs, source.Lines);
    }

    private static TextMask Rotate(TextMask source, int degrees)
    {
        if (degrees == 0)
        {
            return source;
        }

        double angle = degrees * Math.PI / 180, cosine = Math.Cos(angle), sine = Math.Sin(angle);
        int width = (int)Math.Ceiling(Math.Abs(source.Width * cosine) + Math.Abs(source.Height * sine));
        int height = (int)Math.Ceiling(Math.Abs(source.Width * sine) + Math.Abs(source.Height * cosine));
        byte[] coverage = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double dx = x - width / 2.0, dy = y - height / 2.0;
                int sx = (int)Math.Round(dx * cosine + dy * sine + source.Width / 2.0);
                int sy = (int)Math.Round(-dx * sine + dy * cosine + source.Height / 2.0);
                if ((uint)sx < source.Width && (uint)sy < source.Height)
                {
                    coverage[y * width + x] = source.Coverage[sy * source.Width + sx];
                }
            }
        }
        return new(width, height, source.Advance, coverage, source.MissingGlyphs, source.Lines);
    }
}
